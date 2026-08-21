using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Comfyui
{
    /// <summary>
    /// 生图桥：stdin 收一份协议请求 JSON，stdout 出一份协议响应 JSON，退出码 0/非 0。
    /// 铁律：stdout 上只许有那一份 JSON，一个字节都不许多——日志、进度、警告一律走 stderr。
    /// 两个动作：caps（真查下游的能力探测）与 generate（真出图 + 落变体与溯源边车）。
    /// </summary>
    public static class Program
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>桥自己的 driver 名。桥住在 Bridges/（下游边界门禁的白名单），可以写自己的名字。</summary>
        private const string DriverName = "comfyui";

        /// <summary>缺省下游地址（driver.json 配置 schema 的默认值）。</summary>
        private const string DefaultBaseUrl = "http://127.0.0.1:8188";

        /// <summary>缺省轮询超时秒数：首次出图要现编译 CUDA 内核，可能要好几分钟，300 秒不够。</summary>
        private const int DefaultTimeoutSeconds = 900;

        /// <summary>
        /// 入口：读 stdin 到 EOF → 解析请求 → 按动作分发 → 响应写 stdout → 按成功与否给退出码。
        /// 未知动作返回错误码「未知动作」的失败响应，不是崩溃；整个入口用 try/catch 兜住。
        /// </summary>
        /// <param name="args">命令行参数，本桥不消费。</param>
        public static int Main(string[] args)
        {
            try
            {
                var input = Console.In.ReadToEnd();
                if (!BridgeRequest.TryParse(input, out var request, out var reason))
                {
                    WriteResponse(BridgeResponse.Failure(ContractVersion, "请求不合协议", reason, retryable: false));
                    return 1;
                }

                BridgeResponse response;
                switch (request.Action)
                {
                    case "caps":
                        response = ComfyRunner.RunCaps(request);
                        break;
                    case "generate":
                        response = ComfyRunner.RunGenerate(request);
                        break;
                    default:
                        response = BridgeResponse.Failure(ContractVersion, "未知动作", $"不认识动作「{request.Action}」，本桥只支持 caps / generate", retryable: false);
                        break;
                }

                WriteResponse(response);
                return response.Succeeded ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("BridgeComfyui 内部错误：" + exception);
                WriteResponse(BridgeResponse.Failure(ContractVersion, "内部错误", exception.Message, retryable: false));
                return 1;
            }
        }

        /// <summary>把响应写 stdout（唯一允许出现在 stdout 上的内容），日志走 stderr。</summary>
        private static void WriteResponse(BridgeResponse response)
        {
            Console.Out.WriteLine(response.ToJson());
            Console.Error.WriteLine("BridgeComfyui 处理完成，成功=" + response.Succeeded);
        }

        /// <summary>读请求配置里的字符串键；缺失给缺省值。</summary>
        private static string ReadConfigurationString(BridgeRequest request, string key, string fallback)
        {
            if (request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? fallback;
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

        /// <summary>
        /// 纯函数：读资产请求 → 按配方映射填参数覆盖 → 解析种子（给了就用给的，没给才随机）→
        /// 翻译成下游 API 形状。测试与 RunGenerate 共用这一条路径，保证测的就是真跑的。
        /// </summary>
        /// <param name="workflow">workflow.json 的顶层对象。</param>
        /// <param name="recipe">配方定义。</param>
        /// <param name="assetRequest">资产请求。</param>
        /// <param name="providedSeedText">载荷里给的种子；空串 = 桥自己产随机种。</param>
        /// <param name="seedText">实际用的种子文本（边车「随机种」写的就是它）。</param>
        /// <param name="reason">失败原因；成功时空串。</param>
        /// <returns>下游 API 形状的翻译结果；失败返回 null。</returns>
        public static JsonObject BuildGenerateWorkflow(
            JsonObject workflow,
            RecipeDefinition recipe,
            JsonElement assetRequest,
            string providedSeedText,
            out string seedText,
            out string reason)
        {
            return BuildGenerateWorkflow(workflow, recipe, assetRequest, providedSeedText, null, out seedText, out reason);
        }

        /// <summary>
        /// 同上，外加**锚点槽取值**：槽名 → 值（图生图把上传后的图片名填进「参考图」那个槽）。
        ///
        /// 锚点槽与映射的区别：映射把**资产请求里的字段**填进节点，
        /// 锚点槽填的是**请求之外的东西**（一张图、一个文件名），由调用方在调用时给。
        /// 两者最后都落成同一种「参数覆盖」，所以翻译器只有一条路。
        ///
        /// 给了值却找不到同名槽 → **报错**，不静默忽略：
        /// 那说明调用方以为这份配方能收参考图，而它根本不能（P8 批次 4 那个孤立锚点槽就是这么骗人的）。
        /// </summary>
        /// <param name="workflow">workflow.json 的顶层对象。</param>
        /// <param name="recipe">配方定义。</param>
        /// <param name="assetRequest">资产请求。</param>
        /// <param name="providedSeedText">载荷里给的种子；空串 = 桥自己产随机种。</param>
        /// <param name="anchorValues">锚点槽取值：槽名 → 值；null 或空表示不填任何锚点。</param>
        /// <param name="seedText">实际用的种子文本。</param>
        /// <param name="reason">失败原因；成功时空串。</param>
        public static JsonObject BuildGenerateWorkflow(
            JsonObject workflow,
            RecipeDefinition recipe,
            JsonElement assetRequest,
            string providedSeedText,
            IReadOnlyDictionary<string, string> anchorValues,
            out string seedText,
            out string reason)
        {
            var overrides = BuildOverrides(recipe, workflow, assetRequest, providedSeedText, out reason, out seedText);
            if (overrides == null)
            {
                return null;
            }

            if (anchorValues != null)
            {
                foreach (var pair in anchorValues)
                {
                    var slot = recipe.AnchorSlots.FirstOrDefault(item => string.Equals(item.SlotName, pair.Key, StringComparison.Ordinal));
                    if (slot == null)
                    {
                        reason = $"配方「{recipe.Name}」没有名叫「{pair.Key}」的锚点槽，填不进去";
                        return null;
                    }

                    if (!overrides.TryGetValue(slot.NodeIdentifier, out var nodeOverrides))
                    {
                        nodeOverrides = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
                        overrides[slot.NodeIdentifier] = (IReadOnlyDictionary<string, JsonNode>)nodeOverrides;
                    }

                    ((Dictionary<string, JsonNode>)nodeOverrides)[slot.ParameterName] = JsonValue.Create(pair.Value);
                }
            }

            try
            {
                return WorkflowTranslator.Translate(workflow, overrides);
            }
            catch (InvalidOperationException exception)
            {
                reason = $"翻译配方失败：{exception.Message}";
                return null;
            }
        }

        /// <summary>按映射把资产请求的字段填成参数覆盖；缺 KSampler 的 seed 时补一个种子（给了就用给的，没给才随机）。</summary>
        private static Dictionary<string, IReadOnlyDictionary<string, JsonNode>> BuildOverrides(
            RecipeDefinition recipe,
            JsonObject workflow,
            JsonElement assetRequest,
            string providedSeedText,
            out string reason,
            out string seedText)
        {
            reason = "";
            seedText = "";

            var overrides = new Dictionary<string, IReadOnlyDictionary<string, JsonNode>>(StringComparer.Ordinal);
            foreach (var entry in recipe.MappingEntries)
            {
                if (!TryResolveRequestField(assetRequest, entry.RequestField, out var value))
                {
                    reason = $"资产请求里找不到映射字段「{entry.RequestField}」（映射到节点 {entry.NodeIdentifier} 的 {entry.ParameterName}）";
                    return null;
                }

                GetNodeOverrides(overrides, entry.NodeIdentifier)[entry.ParameterName] = value;
            }

            if (assetRequest.TryGetProperty("变体数", out var variantCountElement)
                && variantCountElement.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    if (variantCountElement.GetInt32() <= 0)
                    {
                        reason = "资产请求的「变体数」必须大于 0";
                        return null;
                    }
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                }
            }

            // 映射没覆盖 seed 时补种子：给了（非空）就用给的，没给才随机。
            // batch 多张共用一个 seed，边车要写它。给了种子时绝不加偏移（决策 26：重生成的前提）。
            seedText = string.IsNullOrEmpty(providedSeedText)
                ? Random.Shared.Next().ToString(CultureInfo.InvariantCulture)
                : providedSeedText;

            if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out var seedNumber))
            {
                reason = $"种子「{seedText}」不是合法的 64 位无符号整数";
                return null;
            }

            foreach (var nodeProperty in workflow)
            {
                if (nodeProperty.Value is not JsonObject nodeObject
                    || !nodeObject.TryGetPropertyValue("类型", out var typeNode)
                    || typeNode is not JsonValue typeValue
                    || !typeValue.TryGetValue<string>(out var typeName)
                    || !string.Equals(typeName, "KSampler", StringComparison.Ordinal))
                {
                    continue;
                }

                var nodeOverrides = GetNodeOverrides(overrides, nodeProperty.Key);
                if (!nodeOverrides.ContainsKey("seed"))
                {
                    nodeOverrides["seed"] = JsonValue.Create(seedNumber);
                }
            }

            return overrides;
        }

        /// <summary>从资产请求按请求字段取值（支持 规格. / 风格锚点. 的对象路径）；取不到返回 false。</summary>
        private static bool TryResolveRequestField(JsonElement assetRequest, string requestField, out JsonNode value)
        {
            value = null;
            var dotIndex = requestField.IndexOf('.');
            if (dotIndex > 0)
            {
                var containerName = requestField.Substring(0, dotIndex);
                var memberName = requestField.Substring(dotIndex + 1);
                if (assetRequest.ValueKind == JsonValueKind.Object
                    && assetRequest.TryGetProperty(containerName, out var container)
                    && container.ValueKind == JsonValueKind.Object
                    && container.TryGetProperty(memberName, out var member))
                {
                    value = JsonNode.Parse(member.GetRawText());
                    return true;
                }

                return false;
            }

            if (assetRequest.ValueKind != JsonValueKind.Object || !assetRequest.TryGetProperty(requestField, out var element))
            {
                return false;
            }

            value = JsonNode.Parse(element.GetRawText());
            return true;
        }

        /// <summary>取某节点的参数覆盖字典；没有就建一个。存进去的总是可变 Dictionary，取出来原样返回。</summary>
        private static Dictionary<string, JsonNode> GetNodeOverrides(Dictionary<string, IReadOnlyDictionary<string, JsonNode>> overrides, string nodeIdentifier)
        {
            if (!overrides.TryGetValue(nodeIdentifier, out var nodeOverrides))
            {
                nodeOverrides = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
                overrides[nodeIdentifier] = nodeOverrides;
            }

            return (Dictionary<string, JsonNode>)nodeOverrides;
        }

        /// <summary>
        /// 两个动作的编排：caps 与 generate。所有下游失败都转成协议失败响应，
        /// 错误码与人话照 <see cref="ComfyClientException"/> 原样带出。
        /// </summary>
        private static class ComfyRunner
        {
            /// <summary>探测输出文件里的一项：{名, 版本, hash}（版本一律空串，决策 31）。</summary>
            public static BridgeResponse RunCaps(BridgeRequest request)
            {
                if (!TryGetPayloadString(request, "输出路径", out var outputPath, out var reason))
                {
                    return FailureResponse("载荷缺「输出路径」或它不是字符串：" + reason);
                }

                using var client = new ComfyClient(ReadConfigurationString(request, "地址", DefaultBaseUrl));
                try
                {
                    var probe = client.Probe();

                    var root = new JsonObject
                    {
                        ["节点"] = ToProbeArray(probe.NodePackageNames),
                        ["模型"] = ToProbeArray(probe.ModelNames),
                        ["lora"] = ToProbeArray(probe.LoraNames)
                    };

                    try
                    {
                        var directory = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.WriteAllText(outputPath, root.ToJsonString(), new System.Text.UTF8Encoding(false));
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        return FailureResponse($"探测输出写盘失败：{exception.Message}");
                    }

                    return BridgeResponse.Success(ContractVersion, JsonSerializer.SerializeToElement(root));
                }
                catch (ComfyClientException exception)
                {
                    return BridgeResponse.Failure(ContractVersion, exception.ErrorCode, exception.Message, exception.Retryable);
                }
            }

            /// <summary>
            /// 真出图：读配方 → 按映射填参数 → 翻译成下游 API 形状 → 提交 → 轮询 →
            /// 下载全部图 → 全部成功后才落盘（变体 + 溯源边车），任何失败都不留半张图或空边车。
            /// </summary>
            public static BridgeResponse RunGenerate(BridgeRequest request)
            {
                if (!TryGetPayloadObject(request, "asset-requests", out var assetRequestElement, out var reason))
                {
                    return FailureResponse("载荷缺「资产请求」或它不是对象：" + reason);
                }

                if (!TryGetPayloadString(request, "配方名", out var recipeName, out reason))
                {
                    return FailureResponse("载荷缺「配方名」或它不是字符串：" + reason);
                }

                if (!TryGetPayloadString(request, "输出目录", out var outputDirectory, out reason))
                {
                    return FailureResponse("载荷缺「输出目录」或它不是字符串：" + reason);
                }

                var repositoryRoot = Environment.CurrentDirectory;
                RecipeDefinition recipe;
                JsonObject workflow;
                try
                {
                    recipe = RecipeDefinition.Load(repositoryRoot, DriverName, recipeName);
                    workflow = JsonNode.Parse(File.ReadAllText(RecipePaths.WorkflowFile(repositoryRoot, DriverName, recipeName))) as JsonObject
                        ?? throw new InvalidOperationException("workflow.json 顶层不是对象");
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    return FailureResponse($"读配方失败：{exception.Message}");
                }

                // 载荷里的可选「种子」：给了（非空）就用它，没给才随机（决策 26）。
                var providedSeedText = ReadOptionalSeed(request);

                // 载荷里的可选「参考图路径」：给了就先上传进下游的 input 目录，
                // 再把下游认的图片名填进配方的「参考图」锚点槽——图生图那条路就是这么接上的。
                var referenceImagePath = ReadOptionalPayloadString(request, "参考图路径");
                var timeoutSeconds = Math.Max(ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds), 1);
                using (var client = new ComfyClient(ReadConfigurationString(request, "地址", DefaultBaseUrl)))
                {
                    // 配方声明了「参考图」槽却没给图 → 当场拦下。
                    // 不拦的话请求会带着一个空槽发到下游，报出来的是下游的话
                    // （「LoadImage | Permission denied: …\input」），
                    // 看的人根本对不上「我忘了给参考图」这件事。
                    var wantsReference = recipe.AnchorSlots.Any(slot => string.Equals(slot.SlotName, "参考图", StringComparison.Ordinal));
                    if (wantsReference && referenceImagePath.Length == 0)
                    {
                        return FailureResponse($"配方「{recipe.Name}」有「参考图」锚点槽，必须给参考图路径（载荷键「参考图路径」）");
                    }

                    Dictionary<string, string> anchorValues = null;
                    if (referenceImagePath.Length > 0)
                    {
                        try
                        {
                            var uploadedName = client.UploadImage(referenceImagePath);
                            Console.Error.WriteLine("BridgeComfyui 参考图已上传，下游认的名字：" + uploadedName);
                            anchorValues = new Dictionary<string, string>(StringComparer.Ordinal) { ["参考图"] = uploadedName };
                        }
                        catch (ComfyClientException exception)
                        {
                            return BridgeResponse.Failure(ContractVersion, exception.ErrorCode, exception.Message, exception.Retryable);
                        }
                    }

                    var translated = BuildGenerateWorkflow(workflow, recipe, assetRequestElement, providedSeedText, anchorValues, out var seedText, out var generateReason);
                    if (translated == null)
                    {
                        return FailureResponse(generateReason);
                    }

                    try
                    {
                        var promptId = client.SubmitPrompt(translated);
                        Console.Error.WriteLine("BridgeComfyui 已提交 prompt：" + promptId);

                        var history = client.PollHistory(promptId, timeoutSeconds);
                        if (history.Images.Count == 0)
                        {
                            return BridgeResponse.Failure(ContractVersion, "下游报错", $"prompt {promptId} 跑完了但没有产出任何图", retryable: false);
                        }

                        var variants = LandVariants(client, history.Images, recipe, assetRequestElement, outputDirectory, seedText, promptId, anchorValues);
                        var payload = new JsonObject
                        {
                            ["prompt_id"] = promptId,
                            ["variants"] = new JsonArray(variants.ToArray())
                        };
                        return BridgeResponse.Success(ContractVersion, JsonSerializer.SerializeToElement(payload));
                    }
                    catch (ComfyClientException exception)
                    {
                        return BridgeResponse.Failure(ContractVersion, exception.ErrorCode, exception.Message, exception.Retryable);
                    }
                }
            }

            /// <summary>下载全部图 → 临时目录写盘 → 成功后移进变体目录并写边车；任何失败都不落最终盘。</summary>
            private static List<JsonObject> LandVariants(
                ComfyClient client,
                IReadOnlyList<ComfyOutputImage> images,
                RecipeDefinition recipe,
                JsonElement assetRequest,
                string outputDirectory,
                string seedText,
                string promptId,
                IReadOnlyDictionary<string, string> anchorValues)
            {
                // 先把图全下载到内存：任何一张下载失败就整体失败，不落盘。
                var downloaded = new List<(ComfyOutputImage Image, byte[] Bytes)>();
                foreach (var image in images)
                {
                    var bytes = client.DownloadImage(image);
                    if (bytes == null || bytes.Length == 0)
                    {
                        throw new ComfyClientException("下游报错", $"从下游下载输出图失败（{image.Filename} 是空的）", retryable: false);
                    }

                    downloaded.Add((image, bytes));
                }

                // 全下载成功后才建临时目录写盘；写盘也全成功才移动进变体目录。
                var tempDirectory = Path.Combine(Path.GetTempPath(), "bridge-comfyui-" + Guid.NewGuid().ToString("N"));
                var stagedFiles = new List<(string SourcePath, string FileName)>();
                try
                {
                    Directory.CreateDirectory(tempDirectory);
                    foreach (var (image, bytes) in downloaded)
                    {
                        var fileName = SanitizeFileName(image.Filename);
                        var sourcePath = Path.Combine(tempDirectory, fileName);
                        File.WriteAllBytes(sourcePath, bytes);
                        stagedFiles.Add((sourcePath, fileName));
                    }

                    var variantDirectory = Path.Combine(outputDirectory, "variants");
                    Directory.CreateDirectory(variantDirectory);

                    var variants = new List<JsonObject>();
                    var variantIndex = 0;
                    foreach (var (sourcePath, fileName) in stagedFiles)
                    {
                        variantIndex++;
                        var destinationPath = UniqueDestinationPath(variantDirectory, fileName);
                        File.Move(sourcePath, destinationPath);

                        var sidecar = BuildSidecar(assetRequest, variantIndex, recipe, destinationPath, seedText, promptId, anchorValues);
                        sidecar.WriteTo(destinationPath + ".provenance.json");

                        var (width, height) = ReadPngDimensions(File.ReadAllBytes(destinationPath));
                        variants.Add(new JsonObject
                        {
                            ["文件"] = destinationPath,
                            ["字节数"] = new FileInfo(destinationPath).Length,
                            ["宽"] = width,
                            ["高"] = height
                        });
                    }

                    return variants;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    // 落盘失败：清理本次临时产物，不留半张图。
                    TryDeleteDirectory(tempDirectory);
                    throw new ComfyClientException("内部错误", $"变体落盘失败：{exception.Message}", retryable: false);
                }
                finally
                {
                    TryDeleteDirectory(tempDirectory);
                }
            }

            /// <summary>按资产请求与配方拼一份溯源边车；prompt_id 没有专列字段，写进「机检结果」自由键值对（任务书要求边车写清哪个 prompt 出的）。</summary>
            private static ProvenanceSidecar BuildSidecar(JsonElement assetRequest, int variantIndex, RecipeDefinition recipe, string filePath, string seedText, string promptId, IReadOnlyDictionary<string, string> anchorValues)
            {
                var promptLines = ReadString(assetRequest, "描述");
                var promptLineList = string.IsNullOrEmpty(promptLines)
                    ? Array.Empty<string>()
                    : new[] { promptLines };

                var inspectionResults = new Dictionary<string, string>
                {
                    ["prompt_id"] = JsonSerializer.Serialize(promptId ?? "")
                };

                return new ProvenanceSidecar(
                    ReadString(assetRequest, "id"),
                    variantIndex,
                    "生成",
                    DriverName,
                    recipe.Name,
                    seedText,
                    promptLineList,
                    MergeAnchors(ReadObjectAsRawText(assetRequest, "风格锚点"), anchorValues),
                    DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    ProvenanceSidecar.ComputeFileHash(filePath),
                    inspectionResults,
                    false,
                    "1.0.0");
            }

            /// <summary>
            /// 把「资产请求里声明的风格锚点」与「这一次真正填进去的锚点取值」合并成一份。
            ///
            /// **为什么必须合并**：边车的用处是「照着它能把这张图再生成一遍」（决策 26）。
            /// 图生图那张参考图是决定成品长相的最大变量，边车里不记它，
            /// 拿着边车也重生成不出来——而边车看上去是齐全的，那是最糟的一种缺。
            /// </summary>
            /// <param name="declaredAnchors">资产请求里声明的风格锚点：字段名 → 原始 JSON 文本。</param>
            /// <param name="appliedAnchors">这一次真正填进去的锚点取值：槽名 → 值。</param>
            private static IReadOnlyDictionary<string, string> MergeAnchors(
                IReadOnlyDictionary<string, string> declaredAnchors,
                IReadOnlyDictionary<string, string> appliedAnchors)
            {
                var merged = new Dictionary<string, string>(StringComparer.Ordinal);
                if (declaredAnchors != null)
                {
                    foreach (var pair in declaredAnchors)
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }

                if (appliedAnchors != null)
                {
                    foreach (var pair in appliedAnchors)
                    {
                        merged[pair.Key] = JsonSerializer.Serialize(pair.Value);
                    }
                }

                return merged;
            }

            /// <summary>探测数组：{名, 版本, hash}，版本与 hash 一律空串。</summary>
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

            /// <summary>读载荷里的可选「种子」：缺失、空串、非字符串都当没给（返回空串，桥自己产随机种）。</summary>
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

        private static string ReadOptionalSeed(BridgeRequest request)
            {
                if (request.Payload.ValueKind == JsonValueKind.Object
                    && request.Payload.TryGetProperty("种子", out var element)
                    && element.ValueKind == JsonValueKind.String)
                {
                    return element.GetString() ?? "";
                }

                return "";
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
            private static IReadOnlyDictionary<string, string> ReadObjectAsRawText(JsonElement element, string propertyName)
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

            /// <summary>失败响应（不带下游信息）：参数/配方问题归「请求不合协议」。</summary>
            private static BridgeResponse FailureResponse(string humanText)
            {
                return BridgeResponse.Failure(ContractVersion, "请求不合协议", humanText, retryable: false);
            }

            /// <summary>把文件名里不允许出现在 Windows 路径里的字符换掉；空名给随机名。</summary>
            private static string SanitizeFileName(string fileName)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return "variant-" + Guid.NewGuid().ToString("N") + ".png";
                }

                var invalid = Path.GetInvalidFileNameChars();
                var builder = new System.Text.StringBuilder(fileName.Length);
                foreach (var character in fileName)
                {
                    builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
                }

                return builder.ToString();
            }

            /// <summary>目标路径已存在时追加序号，不覆盖既有变体（落地产物只追加，决策 64 同源）。</summary>
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

            /// <summary>从 PNG 头读像素尺寸（宽在字节 16、高在字节 20，大端）；不是 PNG 给 (0,0)。</summary>
            private static (int Width, int Height) ReadPngDimensions(byte[] bytes)
            {
                if (bytes == null || bytes.Length < 24
                    || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47
                    || bytes[12] != 0x49 || bytes[13] != 0x48 || bytes[14] != 0x44 || bytes[15] != 0x52)
                {
                    return (0, 0);
                }

                return (ReadBigEndianInt32(bytes, 16), ReadBigEndianInt32(bytes, 20));
            }

            /// <summary>读大端 4 字节整数。</summary>
            private static int ReadBigEndianInt32(byte[] bytes, int offset)
            {
                return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
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
        }
    }
}
