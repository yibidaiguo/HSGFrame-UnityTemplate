using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Oaiimage
{
    /// <summary>
    /// 两个动作的编排：caps（查下游模型清单，不产图不花钱）与 generate（真出图 + 落变体与溯源边车）。
    /// 下游失败一律照 <see cref="ImageBridgeException"/> 的错误码与人话原样转成协议失败响应。
    /// </summary>
    public static class ImageRunner
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>桥自己的 driver 名。桥住在 Bridges/（下游边界门禁的白名单），可以写自己的名字。</summary>
        private const string DriverName = "oaiimage";

        /// <summary>缺省超时秒数：线上出图比对话慢得多，120 秒常常不够。</summary>
        private const int DefaultTimeoutSeconds = 180;

        /// <summary>边车「机检结果」里那句种子说明——本接口不收种子，这件事必须写在边车上。</summary>
        private const string SeedNotSupportedText = "本接口不收种子，同样提示词不保证复现";

        /// <summary>
        /// caps：GET /models，把 {"节点":[],"模型":[{名,版本,hash}],"lora":[]} 写进载荷「输出路径」指的文件，
        /// 同一份对象也作为响应载荷返回。这是面板「试跑一次」的落点，不产图、不花钱。
        /// 「节点」与「lora」恒空数组：线上服务没有自定义节点、也不暴露 lora 清单，
        /// 空数组是实话；去掉这两个键会让读探测结果的那一侧多一条分支。
        /// </summary>
        /// <param name="request">请求信封，载荷 {"输出路径":"…"}。</param>
        public static BridgeResponse RunCaps(BridgeRequest request)
        {
            if (!TryGetPayloadString(request, "输出路径", out var outputPath, out var reason))
            {
                return InvalidRequest("载荷缺「输出路径」或它不是字符串：" + reason);
            }

            var endpoint = ReadConfigurationString(request, "地址", "");
            var secretKey = ReadConfigurationString(request, "生图密钥", "");
            if (endpoint.Length == 0)
            {
                return BridgeResponse.Failure(ContractVersion, "下游不可达", "生图服务地址未配置（配置键「地址」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return BridgeResponse.Failure(ContractVersion, "凭据无效", "生图密钥未配置（配置键「生图密钥」为空）", retryable: false);
            }

            using var client = new ImageClient(endpoint, secretKey, ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds));
            try
            {
                var modelNames = client.ListModelNames();
                var root = new JsonObject
                {
                    ["节点"] = new JsonArray(),
                    ["模型"] = ToProbeArray(modelNames),
                    ["lora"] = new JsonArray()
                };

                try
                {
                    var directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(outputPath, root.ToJsonString(), new UTF8Encoding(false));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    return InvalidRequest($"探测输出写盘失败：{exception.Message}");
                }

                Console.Error.WriteLine($"BridgeOaiimage 探测到 {modelNames.Count} 个模型");
                return BridgeResponse.Success(ContractVersion, JsonSerializer.SerializeToElement(root));
            }
            catch (ImageBridgeException exception)
            {
                return BridgeResponse.Failure(ContractVersion, exception.ErrorCode, exception.Message, exception.Retryable);
            }
        }

        /// <summary>
        /// generate：读预设 → 拼提示词 → 调 generations 或 edits → 全部取回后才落盘
        /// （变体 + 溯源边车），任何失败都不留半张图或空边车。
        /// </summary>
        /// <param name="request">请求信封，载荷 {asset-requests, 配方名, 输出目录, 可选「种子」, 可选「参考图路径」}。</param>
        public static BridgeResponse RunGenerate(BridgeRequest request)
        {
            if (!TryGetPayloadObject(request, "asset-requests", out var assetRequest, out var reason))
            {
                return InvalidRequest("载荷缺「asset-requests」或它不是对象：" + reason);
            }

            if (!TryGetPayloadString(request, "配方名", out var presetName, out reason))
            {
                return InvalidRequest("载荷缺「配方名」或它不是字符串：" + reason);
            }

            if (!TryGetPayloadString(request, "输出目录", out var outputDirectory, out reason))
            {
                return InvalidRequest("载荷缺「输出目录」或它不是字符串：" + reason);
            }

            var repositoryRoot = Environment.CurrentDirectory;
            ImagePreset preset;
            try
            {
                preset = ImagePreset.Load(repositoryRoot, DriverName, presetName);
            }
            catch (InvalidOperationException exception)
            {
                return InvalidRequest($"读预设失败：{exception.Message}");
            }

            var endpoint = ReadConfigurationString(request, "地址", "");
            var secretKey = ReadConfigurationString(request, "生图密钥", "");
            if (endpoint.Length == 0)
            {
                return BridgeResponse.Failure(ContractVersion, "下游不可达", "生图服务地址未配置（配置键「地址」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return BridgeResponse.Failure(ContractVersion, "凭据无效", "生图密钥未配置（配置键「生图密钥」为空）", retryable: false);
            }

            var modelName = preset.ModelName.Length > 0 ? preset.ModelName : ReadConfigurationString(request, "模型", "");

            // 「自动」是配置层的哨兵，正常路径上调用方已经把它换成了真模型名；这里再挡一道，
            // 是防着有人手改 local.json 之后直接调桥——哨兵绝不许当成模型名发给下游。
            if (string.Equals(modelName.Trim(), ModelSelection.AutoSentinel, StringComparison.Ordinal))
            {
                modelName = "";
            }

            if (modelName.Length == 0)
            {
                return InvalidRequest($"模型未配置：预设的「模型」为空，本机配置的「模型」也为空。配成「自动」但还没探过时也是这一句——先跑一次 bridge.probe --Driver {DriverName}，或这次调用带 --Model");
            }

            if (!TryResolveSize(preset, request, assetRequest, out var size, out var sizeReason))
            {
                return InvalidRequest(sizeReason);
            }

            // 吸附到下游真能出的档位。**吸附了要说出来**：出来的图不是你要的尺寸这件事，
            // 不写进溯源就没人知道——人只会看到一张图，以为它就是 1920×1080。
            var requestedSize = size;
            size = SnapToOption(size, preset.SizeOptions);
            if (!string.Equals(size, requestedSize, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"BridgeOaiimage 尺寸吸附：{requestedSize} → {size}（下游只出这几档）");
            }

            if (!TryResolveVariantCount(assetRequest, out var variantCount, out var countReason))
            {
                return InvalidRequest(countReason);
            }

            if (!TryBuildPrompt(preset, assetRequest, out var prompt, out var promptReason))
            {
                return InvalidRequest(promptReason);
            }

            var referenceImagePath = ReadOptionalPayloadString(request, "参考图路径");
            if (preset.WantsReferenceImage && referenceImagePath.Length == 0)
            {
                // 不拦的话请求会带着一个空 image 字段发到下游，报出来的是下游那句
                // 「image is a required parameter」，看的人根本对不上「我忘了给参考图」这件事。
                return InvalidRequest($"配方「{preset.Name}」有「参考图」锚点槽，必须给参考图路径（载荷键「参考图路径」）");
            }

            if (!preset.WantsReferenceImage && referenceImagePath.Length > 0)
            {
                // 给了值却找不到同名槽 → 报错，不静默忽略：那说明调用方以为这份预设能收参考图，
                // 而它走的是 generations，根本不能。
                return InvalidRequest($"配方「{preset.Name}」没有「参考图」锚点槽（接口是 {preset.ApiName}），参考图填不进去");
            }

            if (referenceImagePath.Length > 0 && !File.Exists(referenceImagePath))
            {
                return InvalidRequest($"参考图不存在：{referenceImagePath}");
            }

            // 载荷里的「种子」：OpenAI 图像接口**不收 seed**，所以这里既不发它、也不假装能复现。
            // 给了就在 stderr 与边车的「机检结果」里点名说清没发出去（决策 26 的反面：
            // 边车看上去齐全而实际重现不出来，是最糟的一种缺）。
            var providedSeedText = ReadOptionalPayloadString(request, "种子");
            if (providedSeedText.Length > 0)
            {
                Console.Error.WriteLine($"BridgeOaiimage 知会：收到种子「{providedSeedText}」，但{SeedNotSupportedText}，本次没有发给下游");
            }

            using var client = new ImageClient(endpoint, secretKey, ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds));
            try
            {
                Console.Error.WriteLine($"BridgeOaiimage 开始出图：接口={preset.ApiName} 模型={modelName} 尺寸={(size.Length == 0 ? "（下游默认）" : size)} 张数={variantCount}");

                var images = string.Equals(preset.ApiName, ImagePreset.EditsApiName, StringComparison.Ordinal)
                    ? client.Edit(modelName, prompt, variantCount, size, referenceImagePath)
                    : client.Generate(modelName, prompt, variantCount, size);

                // 本接口不回 prompt id，这个是本地现编的，只为把同一次调用的几张图串起来。
                var promptIdentifier = DriverName + "-" + Guid.NewGuid().ToString("N");

                var variants = LandVariants(images, preset, assetRequest, outputDirectory, prompt, promptIdentifier, modelName, size, requestedSize, providedSeedText, referenceImagePath);
                var payload = new JsonObject
                {
                    ["prompt_id"] = promptIdentifier,
                    ["variants"] = new JsonArray(variants.ToArray())
                };
                return BridgeResponse.Success(ContractVersion, JsonSerializer.SerializeToElement(payload));
            }
            catch (ImageBridgeException exception)
            {
                return BridgeResponse.Failure(ContractVersion, exception.ErrorCode, exception.Message, exception.Retryable);
            }
        }

        /// <summary>先在临时目录写盘，全部成功才移进变体目录并写边车；任何失败都不落最终盘。</summary>
        private static List<JsonObject> LandVariants(
            IReadOnlyList<GeneratedImage> images,
            ImagePreset preset,
            JsonElement assetRequest,
            string outputDirectory,
            string prompt,
            string promptIdentifier,
            string modelName,
            string size,
            string requestedSize,
            string providedSeedText,
            string referenceImagePath)
        {
            // 出图文件名必须 ASCII（gate.pathascii 是 block 级），而「命名」字段常常是中文。
            var stem = AsciiFileNaming.ToAsciiStem(ReadString(assetRequest, "命名"));
            var tempDirectory = Path.Combine(Path.GetTempPath(), "bridge-oaiimage-" + Guid.NewGuid().ToString("N"));
            var stagedFiles = new List<(string SourcePath, string FileName)>();

            try
            {
                Directory.CreateDirectory(tempDirectory);
                var stagedIndex = 0;
                foreach (var image in images)
                {
                    if (image.Bytes.Length == 0)
                    {
                        throw new ImageBridgeException("下游报错", "下游回来的图是空的", retryable: false);
                    }

                    stagedIndex++;
                    var fileName = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}-{1:D2}{2}",
                        stem,
                        stagedIndex,
                        ImageClient.GuessExtension(image.Bytes));
                    var sourcePath = Path.Combine(tempDirectory, fileName);
                    File.WriteAllBytes(sourcePath, image.Bytes);
                    stagedFiles.Add((sourcePath, fileName));
                }

                var variantDirectory = Path.Combine(outputDirectory, "variants");
                Directory.CreateDirectory(variantDirectory);

                var variants = new List<JsonObject>();
                var variantIndex = 0;
                foreach (var (sourcePath, fileName) in stagedFiles)
                {
                    var destinationPath = UniqueDestinationPath(variantDirectory, fileName);
                    File.Move(sourcePath, destinationPath);

                    var sidecar = BuildSidecar(
                        assetRequest,
                        variantIndex + 1,
                        preset,
                        destinationPath,
                        prompt,
                        promptIdentifier,
                        modelName,
                        size,
                        requestedSize,
                        images[variantIndex].SourceFieldName,
                        providedSeedText,
                        referenceImagePath);
                    sidecar.WriteTo(destinationPath + ".provenance.json");

                    var (width, height) = ImageDimensionReader.Read(images[variantIndex].Bytes);
                    variants.Add(new JsonObject
                    {
                        ["文件"] = destinationPath,
                        ["字节数"] = new FileInfo(destinationPath).Length,
                        ["宽"] = width,
                        ["高"] = height
                    });

                    variantIndex++;
                }

                return variants;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                TryDeleteDirectory(tempDirectory);
                throw new ImageBridgeException("内部错误", $"变体落盘失败：{exception.Message}", retryable: false);
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }

        /// <summary>
        /// 拼一份溯源边车。
        /// 「随机种」**只能留空**：本接口不收 seed，写一个进去就是骗人——
        /// 边车的用处是「照着它能把这张图再生成一遍」（决策 26），
        /// 边车看上去齐全而实际重现不出来，是最糟的一种缺。
        /// 这件事写在「机检结果」里，读边车的人第一眼就该看见。
        /// </summary>
        private static ProvenanceSidecar BuildSidecar(
            JsonElement assetRequest,
            int variantIndex,
            ImagePreset preset,
            string filePath,
            string prompt,
            string promptIdentifier,
            string modelName,
            string size,
            string requestedSize,
            string sourceFieldName,
            string providedSeedText,
            string referenceImagePath)
        {
            var inspectionResults = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["prompt_id"] = JsonSerializer.Serialize(promptIdentifier ?? ""),
                ["接口"] = JsonSerializer.Serialize(preset.ApiName),
                ["模型"] = JsonSerializer.Serialize(modelName ?? ""),
                ["尺寸"] = JsonSerializer.Serialize(size ?? ""),
                ["要的尺寸"] = JsonSerializer.Serialize(requestedSize ?? ""),
                ["取图字段"] = JsonSerializer.Serialize(sourceFieldName ?? ""),
                ["种子说明"] = JsonSerializer.Serialize(SeedNotSupportedText)
            };

            if (!string.IsNullOrEmpty(providedSeedText))
            {
                inspectionResults["未发出的种子"] = JsonSerializer.Serialize(
                    $"{providedSeedText}（调用方给了种子，但{SeedNotSupportedText}，本次没有发给下游）");
            }

            var anchors = ReadObjectAsRawText(assetRequest, "风格锚点");
            if (!string.IsNullOrEmpty(referenceImagePath))
            {
                // 参考图是决定成品长相的最大变量，边车里不记它，拿着边车也重生成不出来。
                anchors[ImagePreset.ReferenceImageSlotName] = JsonSerializer.Serialize(referenceImagePath);
            }

            return new ProvenanceSidecar(
                ReadString(assetRequest, "id"),
                variantIndex,
                "生成",
                DriverName,
                preset.Name,
                "",
                new[] { prompt ?? "" },
                anchors,
                DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ProvenanceSidecar.ComputeFileHash(filePath),
                inspectionResults,
                false,
                ContractVersion);
        }

        /// <summary>
        /// 算这次要发的尺寸，四级回落，**每一级都可以是「不指定」**：
        ///
        /// 1. 预设的「尺寸」写「规格」→ 从资产请求的「规格.宽 / 规格.高」现算（这是缺省做法：
        ///    尺寸是资产请求说了算的东西，不该焊在预设里）；
        /// 2. 预设写了别的非空值 → 就用它（要把某份预设钉死在一档上时用）；
        /// 3. 都没有 → 用本机配置的「尺寸」；
        /// 4. 连它也空 → <b>一个 size 参数都不发</b>，由下游按它自己的默认来。
        ///
        /// 第 4 级是关键：桥里**没有**写死的缺省尺寸。写一个进去就等于替下游决定了
        /// 「这个模型该出多大的图」，而各家模型的档位根本不一样
        /// （同一个中转后面挂什么模型也不由我们决定）。不发这个参数，下游自己会挑它支持的。
        ///
        /// 写「规格」但资产请求里没有规格时**不报错**，顺着往下回落——
        /// 资产请求不带规格是常事（试跑、临时出一张图），为它整条失败不合算。
        /// </summary>
        private static bool TryResolveSize(ImagePreset preset, BridgeRequest request, JsonElement assetRequest, out string size, out string reason)
        {
            size = "";
            reason = "";

            if (string.Equals(preset.Size, ImagePreset.SizeFromSpecification, StringComparison.Ordinal))
            {
                if (TryReadSpecificationLength(assetRequest, "宽", out var width, out reason)
                    && TryReadSpecificationLength(assetRequest, "高", out var height, out reason))
                {
                    size = string.Format(CultureInfo.InvariantCulture, "{0}x{1}", width, height);
                    return true;
                }

                // 规格里的宽高是**写了但写坏了**（负数、不是整数）时才算错；
                // 压根没写规格只是「没指定」，回落到配置。
                if (reason.Length > 0 && HasSpecificationSize(assetRequest))
                {
                    return false;
                }

                reason = "";
            }
            else if (preset.Size.Length > 0)
            {
                size = preset.Size;
                return true;
            }

            size = ReadConfigurationString(request, "尺寸", "");
            return true;
        }

        /// <summary>
        /// 把要的尺寸吸附到下游真能出的那几档：**先挑长宽比最接近的**，比例一样时挑面积最接近的。
        ///
        /// 为什么按比例挑而不按面积：比例错了画面会被压扁或拉长，那是毁掉整张图；
        /// 面积差一点只是清晰度差一点，缩放能补。
        /// 档位为空、或要的尺寸本来就在档位里，就原样返回。
        /// </summary>
        /// <param name="size">要的尺寸，形如 1920x1080。</param>
        /// <param name="options">下游能出的档位。</param>
        private static string SnapToOption(string size, IReadOnlyList<string> options)
        {
            if (options == null || options.Count == 0 || !TryParseSize(size, out var width, out var height))
            {
                return size;
            }

            foreach (var option in options)
            {
                if (string.Equals(option, size, StringComparison.Ordinal))
                {
                    return size;
                }
            }

            var wantedRatio = (double)width / height;
            var wantedArea = (double)width * height;
            var best = size;
            var bestRatioGap = double.MaxValue;
            var bestAreaGap = double.MaxValue;

            foreach (var option in options)
            {
                if (!TryParseSize(option, out var optionWidth, out var optionHeight))
                {
                    continue;
                }

                var ratioGap = Math.Abs(((double)optionWidth / optionHeight) - wantedRatio);
                var areaGap = Math.Abs(((double)optionWidth * optionHeight) - wantedArea);

                if (ratioGap < bestRatioGap - 0.001
                    || (Math.Abs(ratioGap - bestRatioGap) <= 0.001 && areaGap < bestAreaGap))
                {
                    best = option;
                    bestRatioGap = ratioGap;
                    bestAreaGap = areaGap;
                }
            }

            return best;
        }

        /// <summary>把 1920x1080 这样的文本拆成宽高；拆不动给 false。</summary>
        /// <param name="size">尺寸文本。</param>
        /// <param name="width">宽。</param>
        /// <param name="height">高。</param>
        private static bool TryParseSize(string size, out int width, out int height)
        {
            width = 0;
            height = 0;
            var parts = (size ?? "").Split('x', 'X');
            return parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
                && width > 0
                && height > 0;
        }

        /// <summary>资产请求里到底有没有写「规格.宽」或「规格.高」（不管写得对不对）。</summary>
        private static bool HasSpecificationSize(JsonElement assetRequest)
        {
            return assetRequest.ValueKind == JsonValueKind.Object
                && assetRequest.TryGetProperty("规格", out var specification)
                && specification.ValueKind == JsonValueKind.Object
                && (specification.TryGetProperty("宽", out _) || specification.TryGetProperty("高", out _));
        }

        /// <summary>读资产请求的「规格.宽」或「规格.高」；缺失或不是正整数时给出可读原因。</summary>
        private static bool TryReadSpecificationLength(JsonElement assetRequest, string propertyName, out int value, out string reason)
        {
            value = 0;
            reason = "";
            if (assetRequest.ValueKind != JsonValueKind.Object
                || !assetRequest.TryGetProperty("规格", out var specification)
                || specification.ValueKind != JsonValueKind.Object
                || !specification.TryGetProperty(propertyName, out var element)
                || element.ValueKind != JsonValueKind.Number)
            {
                reason = $"预设的「尺寸」写的是「{ImagePreset.SizeFromSpecification}」，但资产请求里缺「规格.{propertyName}」或它不是数字";
                return false;
            }

            try
            {
                value = element.GetInt32();
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
            {
                reason = $"资产请求的「规格.{propertyName}」不是合法整数";
                return false;
            }

            if (value <= 0)
            {
                reason = $"资产请求的「规格.{propertyName}」必须大于 0";
                return false;
            }

            return true;
        }

        /// <summary>读资产请求的「变体数」；缺失按 1 算，非正数报错。</summary>
        private static bool TryResolveVariantCount(JsonElement assetRequest, out int variantCount, out string reason)
        {
            variantCount = 1;
            reason = "";
            if (assetRequest.ValueKind != JsonValueKind.Object
                || !assetRequest.TryGetProperty("变体数", out var element)
                || element.ValueKind != JsonValueKind.Number)
            {
                return true;
            }

            try
            {
                variantCount = element.GetInt32();
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
            {
                reason = "资产请求的「变体数」不是合法整数";
                return false;
            }

            if (variantCount <= 0)
            {
                reason = "资产请求的「变体数」必须大于 0";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 按预设的提示词模板拼这次的提示词：<c>{字段}</c> 从资产请求里取值，支持「规格.宽」这样的点路径。
        /// 模板为空时直接用资产请求的「描述」。引用了取不到的字段就报错，不静默留下一个 {字段} 发给下游。
        /// </summary>
        private static bool TryBuildPrompt(ImagePreset preset, JsonElement assetRequest, out string prompt, out string reason)
        {
            prompt = "";
            reason = "";

            if (preset.PromptTemplate.Length == 0)
            {
                prompt = ReadString(assetRequest, "描述");
                if (prompt.Length == 0)
                {
                    reason = "预设没写「提示词模板」，而资产请求里的「描述」也是空的，拼不出提示词";
                    return false;
                }

                return true;
            }

            var builder = new StringBuilder();
            var template = preset.PromptTemplate;
            var index = 0;
            while (index < template.Length)
            {
                var open = template.IndexOf('{', index);
                if (open < 0)
                {
                    builder.Append(template, index, template.Length - index);
                    break;
                }

                var close = template.IndexOf('}', open + 1);
                if (close < 0)
                {
                    reason = $"预设的「提示词模板」里有没闭合的大括号（第 {open + 1} 个字符起）";
                    return false;
                }

                builder.Append(template, index, open - index);
                var fieldPath = template.Substring(open + 1, close - open - 1);
                if (!TryResolveRequestField(assetRequest, fieldPath, out var value))
                {
                    reason = $"资产请求里找不到提示词模板引用的字段「{fieldPath}」";
                    return false;
                }

                builder.Append(value);
                index = close + 1;
            }

            prompt = builder.ToString().Trim();
            if (prompt.Length == 0)
            {
                reason = "按预设模板拼出来的提示词是空的";
                return false;
            }

            return true;
        }

        /// <summary>从资产请求按字段路径取值（支持「规格.宽」这样的点路径）；取不到返回 false。字符串取原文，其余取 JSON 原样文本。</summary>
        private static bool TryResolveRequestField(JsonElement assetRequest, string fieldPath, out string value)
        {
            value = "";
            if (assetRequest.ValueKind != JsonValueKind.Object || string.IsNullOrEmpty(fieldPath))
            {
                return false;
            }

            var current = assetRequest;
            foreach (var segment in fieldPath.Split('.'))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                {
                    return false;
                }

                current = next;
            }

            value = current.ValueKind == JsonValueKind.String ? (current.GetString() ?? "") : current.GetRawText();
            return true;
        }

        /// <summary>探测数组：{名, 版本, hash}，版本与 hash 一律空串（决策 31：线上服务不报版本）。</summary>
        private static JsonArray ToProbeArray(IReadOnlyList<string> names)
        {
            var array = new JsonArray();
            foreach (var name in names)
            {
                array.Add(new JsonObject
                {
                    ["名"] = name,
                    ["版本"] = "",
                    ["hash"] = ""
                });
            }

            return array;
        }

        /// <summary>目标路径已存在时追加序号，不覆盖既有变体（落地产物只追加）。</summary>
        private static string UniqueDestinationPath(string directory, string fileName)
        {
            var candidate = Path.Combine(directory, fileName);
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var index = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(directory, $"{stem}-{index}{extension}");
                index++;
            }

            return candidate;
        }

        /// <summary>递归删临时目录；删不掉就放着，不影响结果。</summary>
        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>失败响应：参数与预设的问题一律归「请求不合协议」。</summary>
        private static BridgeResponse InvalidRequest(string humanText)
        {
            return BridgeResponse.Failure(ContractVersion, "请求不合协议", humanText, retryable: false);
        }

        /// <summary>读请求配置里的字符串键；缺失给缺省值。</summary>
        private static string ReadConfigurationString(BridgeRequest request, string key, string fallback)
        {
            if (request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return (element.GetString() ?? fallback).Trim();
            }

            return fallback;
        }

        /// <summary>读请求配置里的整数键；缺失、类型不对给缺省值。</summary>
        private static int ReadConfigurationInt(BridgeRequest request, string key, int fallback)
        {
            if (request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return element.GetInt32();
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                }
            }

            return fallback;
        }

        /// <summary>读载荷里的字符串键。</summary>
        private static bool TryGetPayloadString(BridgeRequest request, string key, out string value, out string reason)
        {
            value = "";
            reason = "";
            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty(key, out var element)
                || element.ValueKind != JsonValueKind.String)
            {
                reason = "缺「" + key + "」或它不是字符串";
                return false;
            }

            value = element.GetString() ?? "";
            return true;
        }

        /// <summary>读载荷里的对象键。</summary>
        private static bool TryGetPayloadObject(BridgeRequest request, string key, out JsonElement value, out string reason)
        {
            value = default;
            reason = "";
            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty(key, out var element)
                || element.ValueKind != JsonValueKind.Object)
            {
                reason = "缺「" + key + "」或它不是对象";
                return false;
            }

            value = element;
            return true;
        }

        /// <summary>读载荷里的可选字符串键；缺失或类型不对给空串（不算错）。</summary>
        private static string ReadOptionalPayloadString(BridgeRequest request, string key)
        {
            if (request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return (element.GetString() ?? "").Trim();
            }

            return "";
        }

        /// <summary>读对象里的字符串键；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读对象字段成「字段名 → 原始 JSON 文本」映射；缺失、类型不对给空字典。</summary>
        private static Dictionary<string, string> ReadObjectAsRawText(JsonElement element, string propertyName)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out var container)
                || container.ValueKind != JsonValueKind.Object)
            {
                return values;
            }

            foreach (var property in container.EnumerateObject())
            {
                values[property.Name] = property.Value.GetRawText();
            }

            return values;
        }
    }
}
