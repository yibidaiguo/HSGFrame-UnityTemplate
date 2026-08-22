using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Tripo
{
    /// <summary>
    /// 执行后端桥：stdin 收一份协议请求 JSON，stdout 出一份协议响应 JSON，退出码 0/非 0。
    /// 与 BridgeOaicompat / BridgeBlender 同构，只是把「起子进程」「发 chat 请求」换成
    /// 「向下游提交生成任务 + 轮询 + 下载模型」。
    /// 铁律：stdout 上只许有那一份 JSON，一个字节都不许多——日志、进度、警告一律走 stderr。
    /// 密钥只进 Authorization 头（决策 5、78），任何日志、异常、返回都不许带上它。
    /// </summary>
    public static class Program
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省下游地址（driver.json 配置 schema 的默认值）：v3 的主机与版本，实证过。</summary>
        private const string DefaultBaseUrl = "https://openapi.tripo3d.ai/v3";

        /// <summary>缺省超时秒数（driver.json 配置 schema 的默认值）。</summary>
        private const int DefaultTimeoutSeconds = 600;

        /// <summary>
        /// 入口：读 stdin 到 EOF → 解析请求 → 按动作分发 → 响应写 stdout → 按成功与否给退出码。
        /// 未知动作返回错误码「未知动作」的失败响应，不是崩溃；整个入口用 try/catch 兜住，
        /// 任何异常都转成失败响应（否则调用方拿到的是空 stdout）。
        /// </summary>
        /// <param name="args">命令行参数，本桥不消费。</param>
        public static int Main(string[] args)
        {
            // 三条流先钉成 UTF-8，再碰 stdin 一个字节——协议 JSON 的键是中文，
            // 编码没对上时收回来就是乱码，而报错完全指不到编码上。
            BridgeProtocolConsole.PinUtf8();

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
                    case "generate":
                        response = RunGenerate(request);
                        break;
                    case "caps":
                        response = RunCaps(request);
                        break;
                    case "balance":
                        response = RunBalance(request);
                        break;
                    default:
                        response = BridgeResponse.Failure(ContractVersion, "未知动作", $"不认识动作「{request.Action}」，本桥支持 generate / balance", retryable: false);
                        break;
                }

                WriteResponse(response);
                return response.Succeeded ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("BridgeTripo 内部错误：" + exception.Message);
                WriteResponse(BridgeResponse.Failure(ContractVersion, "内部错误", exception.Message, retryable: false));
                return 1;
            }
        }

        /// <summary>把响应写 stdout（唯一允许出现在 stdout 上的内容），日志走 stderr。</summary>
        private static void WriteResponse(BridgeResponse response)
        {
            Console.Out.WriteLine(response.ToJson());
            Console.Error.WriteLine("BridgeTripo 处理完成，成功=" + response.Succeeded);
        }

        /// <summary>
        /// 跑 generate 动作：提交 text_to_model → 轮询到终态 → 成功则下载模型落盘。
        /// 载荷 {"提示词":"…","输出目录":"…"}，返回 {"模型文件":"…","task_id":"…","状态":"…"}。
        /// 失败映射：连不上 → 下游不可达；401/403 → 凭据无效；余额/配额 → 额度不足；
        /// 429 → 限流；超时 → 超时；其余 → 下游报错带服务端 message。
        /// </summary>
        /// <param name="request">请求信封，配置含 地址/超时秒/模型生成密钥。</param>
        private static BridgeResponse RunGenerate(BridgeRequest request)
        {
            // 参考图地址给了就走 image-to-model，没给就走 text-to-model。
            // 两条路的提交体形状都实证过（见 Bridges/tripo/endpoints-verified.md）。
            var referenceImageUrl = ReadOptionalPayloadString(request, "参考图地址");
            var referenceImageType = ReadOptionalPayloadString(request, "参考图类型");
            var usesImage = referenceImageUrl.Length > 0;

            var prompt = "";
            string reason;
            if (!usesImage)
            {
                if (!TryGetPayloadString(request, "提示词", out prompt, out reason))
                {
                    return Failure("请求不合协议", reason, retryable: false);
                }

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    return Failure("请求不合协议", "载荷「提示词」是空的", retryable: false);
                }
            }

            if (!TryGetPayloadString(request, "输出目录", out var outputDirectory, out reason))
            {
                return Failure("请求不合协议", reason, retryable: false);
            }

            var baseUrl = ReadConfigurationString(request, "地址", DefaultBaseUrl);
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var secretKey = ReadConfigurationString(request, "模型生成密钥", "");
            var modelVersion = ReadConfigurationString(request, "模型版本", "");

            // 「自动」是配置层的哨兵，正常路径上调用方已经把它换成了真模型版本；这里再挡一道，
            // 是防着有人手改 local.json 之后直接调桥——哨兵绝不许当成模型版本发给下游。
            if (string.Equals(modelVersion.Trim(), ModelSelection.AutoSentinel, StringComparison.Ordinal))
            {
                modelVersion = "";
            }


            if (baseUrl.Length == 0)
            {
                return Failure("下游不可达", "下游地址未配置（配置键「地址」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return Failure("凭据无效", "模型生成密钥未配置（配置键「模型生成密钥」为空）", retryable: false);
            }

            string modelFilePath;
            string taskId;
            string statusText;
            TripoClient client;
            try
            {
                client = new TripoClient(baseUrl, secretKey, timeoutSeconds, modelVersion);
            }
            catch (TripoClientException exception)
            {
                return Failure(exception.ErrorCode, exception.Message, exception.Retryable);
            }

            using (client)
            {
                try
                {
                    taskId = usesImage
                        ? client.SubmitImageTask(referenceImageUrl, referenceImageType)
                        : client.SubmitTask(prompt);

                    var query = client.PollUntilFinal(taskId);
                    statusText = query.State.StatusText;
                    if (!query.State.Succeeded)
                    {
                        return Failure("下游报错", query.State.HumanText + "（task_id=" + taskId + "）", retryable: false);
                    }

                    var bytes = client.DownloadModel(query.ModelUrl);
                    modelFilePath = LandModel(bytes, query.ModelUrl, outputDirectory);
                }
                catch (TripoClientException exception)
                {
                    return Failure(exception.ErrorCode, exception.Message, exception.Retryable);
                }
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["模型文件"] = modelFilePath,
                ["task_id"] = taskId,
                ["状态"] = statusText,
                ["提交方式"] = usesImage ? "image-to-model" : "text-to-model"
            });

            Console.Error.WriteLine("BridgeTripo 模型已落盘：" + modelFilePath);
            return BridgeResponse.Success(ContractVersion, payload);
        }

        /// <summary>
        /// caps：探下游允许的模型版本清单，写进载荷「输出路径」指的文件，同一份对象作为响应载荷返回。
        /// tripo 没有 list-models 接口，清单是从参数校验的报错里读回来的（见 TripoClient.ProbeAllowedModelVersions）：
        /// **不产模型、不花积分**。「节点」与「lora」恒空数组——线上服务没这两样，空数组是实话。
        /// </summary>
        /// <param name="request">请求信封，载荷 {"输出路径":"…"}。</param>
        private static BridgeResponse RunCaps(BridgeRequest request)
        {
            if (!TryGetPayloadString(request, "输出路径", out var outputPath, out var reason))
            {
                return Failure("请求不合协议", "载荷缺「输出路径」或它不是字符串：" + reason, retryable: false);
            }

            var baseUrl = ReadConfigurationString(request, "地址", DefaultBaseUrl);
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var secretKey = ReadConfigurationString(request, "模型生成密钥", "");

            if (baseUrl.Length == 0)
            {
                return Failure("下游不可达", "下游地址未配置（配置键「地址」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return Failure("凭据无效", "模型生成密钥未配置（配置键「模型生成密钥」为空）", retryable: false);
            }

            try
            {
                // 构造时模型版本传空串走它的缺省：哨兵只出现在探测请求体里，不进客户端状态。
                using var client = new TripoClient(baseUrl, secretKey, timeoutSeconds, "");
                var versions = client.ProbeAllowedModelVersions();

                var models = new JsonArray();
                foreach (var version in versions)
                {
                    // tripo 的「模型」本来就是一个版本号，「名」与「版本」同值是实话，不是偷懒。
                    models.Add(new JsonObject
                    {
                        ["名"] = version,
                        ["版本"] = version,
                        ["hash"] = ""
                    });
                }

                var root = new JsonObject
                {
                    ["节点"] = new JsonArray(),
                    ["模型"] = models,
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
                    return Failure("请求不合协议", $"探测输出写盘失败：{exception.Message}", retryable: false);
                }

                Console.Error.WriteLine($"BridgeTripo 探测到 {versions.Count} 个模型版本");
                return BridgeResponse.Success(ContractVersion, JsonSerializer.SerializeToElement(root));
            }
            catch (TripoClientException exception)
            {
                return Failure(exception.ErrorCode, exception.Message, exception.Retryable);
            }
        }

        /// <summary>
        /// 跑 balance 动作：查一次账号余额，返回 {"可用积分":…,"冻结积分":…}。
        /// 这是**诊断**用的，不是就绪判据——决策 91 说得很死：能不能用只有真提交一次任务才算数。
        /// 它的用处是把「2010 到底是不是真没钱」这件事一次问清楚，省得去换 key、查权限。
        /// </summary>
        /// <param name="request">请求信封，配置含 地址/超时秒/模型生成密钥。</param>
        private static BridgeResponse RunBalance(BridgeRequest request)
        {
            var baseUrl = ReadConfigurationString(request, "地址", DefaultBaseUrl);
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var secretKey = ReadConfigurationString(request, "模型生成密钥", "");

            if (baseUrl.Length == 0)
            {
                return Failure("下游不可达", "下游地址未配置（配置键「地址」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return Failure("凭据无效", "模型生成密钥未配置（配置键「模型生成密钥」为空）", retryable: false);
            }

            using var client = new TripoClient(baseUrl, secretKey, timeoutSeconds);
            try
            {
                var reading = client.QueryBalance();
                var payload = JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["可用积分"] = reading.Balance,
                    ["冻结积分"] = reading.Frozen,
                    ["提醒"] = "余额不是就绪判据（决策 91）：能不能用只有真提交一次任务才算数"
                });

                return BridgeResponse.Success(ContractVersion, payload);
            }
            catch (TripoClientException exception)
            {
                return Failure(exception.ErrorCode, exception.Message, exception.Retryable);
            }
        }

        /// <summary>
        /// 把模型字节落到输出目录：先写临时目录、成功后移进目标目录；任何失败都不落空/半个模型文件。
        /// 目标路径已存在时追加序号，不覆盖既有产物（落地产物只追加，决策 64 同源）。
        /// </summary>
        /// <param name="bytes">模型字节。</param>
        /// <param name="modelUrl">模型下载地址，取文件名用。</param>
        /// <param name="outputDirectory">输出目录。</param>
        private static string LandModel(byte[] bytes, string modelUrl, string outputDirectory)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "bridge-tripo-" + Guid.NewGuid().ToString("N"));
            var fileName = SanitizeFileName(FileNameFromUrl(modelUrl));
            var sourcePath = Path.Combine(tempDirectory, fileName);
            try
            {
                Directory.CreateDirectory(tempDirectory);
                File.WriteAllBytes(sourcePath, bytes);

                Directory.CreateDirectory(outputDirectory);
                var destinationPath = UniqueDestinationPath(outputDirectory, fileName);
                File.Move(sourcePath, destinationPath);
                return destinationPath;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new TripoClientException("内部错误", $"模型落盘失败：{exception.Message}", retryable: false);
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }

        /// <summary>从下载 URL 取文件名；取不到给缺省名。</summary>
        private static string FileNameFromUrl(string modelUrl)
        {
            try
            {
                if (Uri.TryCreate(modelUrl, UriKind.Absolute, out var uri))
                {
                    var name = Path.GetFileName(uri.AbsolutePath);
                    if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, "/", StringComparison.Ordinal))
                    {
                        return name;
                    }
                }
            }
            catch (Exception exception) when (exception is UriFormatException || exception is ArgumentException)
            {
            }

            return "tripo_model.glb";
        }

        /// <summary>把文件名里的路径分隔符与非法字符替换掉，防路径穿越。</summary>
        private static string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder();
            foreach (var ch in fileName ?? "")
            {
                builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }

            var result = builder.ToString().Trim();
            return result.Length == 0 ? "tripo_model.glb" : result;
        }

        /// <summary>目标路径已存在时追加序号，不覆盖既有产物。</summary>
        private static string UniqueDestinationPath(string directory, string fileName)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var candidate = Path.Combine(directory, fileName);
            var index = 1;
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

        /// <summary>读载荷里的字符串键。</summary>
        private static bool TryGetPayloadString(BridgeRequest request, string key, out string value, out string reason)
        {
            value = "";
            reason = "";
            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty(key, out var element)
                || element.ValueKind != JsonValueKind.String)
            {
                reason = "载荷缺「" + key + "」或它不是字符串";
                return false;
            }

            value = element.GetString() ?? "";
            return true;
        }

        /// <summary>失败响应。</summary>
        private static BridgeResponse Failure(string code, string humanText, bool retryable)
        {
            return BridgeResponse.Failure(ContractVersion, code, humanText, retryable);
        }
    }
}
