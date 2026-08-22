using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Oaicompat
{
    /// <summary>
    /// 执行后端桥的 complete 动作：把「提示 / 上下文」POST 到 OpenAI 兼容的 chat/completions，
    /// 把回传的文本、模型名与 token 数解析成协议响应载荷。
    /// 密钥红线（决策 5、78）：密钥只许出现在 Authorization 头里——不进日志、不进异常消息、
    /// 不进返回载荷，长度和前缀也不许。HTTP 出错打印的内容里不允许出现请求头。
    /// 模型名回填服务端返回的 model 字段（服务端会做模型别名，报告里记「回来的那个」）。
    /// </summary>
    public static class ChatClient
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 120;

        /// <summary>缺省模型名，配置里没有时用（正常情况配置一定填了，这里是兜底）。</summary>
        private const string DefaultModelName = "";

        /// <summary>执行 complete：发 HTTP 调 chat/completions，返回协议响应。</summary>
        /// <param name="request">请求信封，载荷 {"提示":"…","上下文":"…"}，可选「图片」（本地 PNG 路径数组，
        /// 带了就走多模态，图以 data: URL 内联发过去，不经任何第三方图床）；
        /// 配置含 地址/模型/超时秒/执行后端密钥。</param>
        public static BridgeResponse RunComplete(BridgeRequest request)
        {
            if (!TryGetPayloadString(request, "提示", out var prompt, out var reason))
            {
                return Failure("请求不合协议", reason, retryable: false);
            }

            if (!TryGetPayloadString(request, "上下文", out var context, out reason))
            {
                return Failure("请求不合协议", reason, retryable: false);
            }

            var endpoint = ReadConfigurationString(request, "地址", "");
            var modelName = ReadConfigurationString(request, "模型", DefaultModelName);

            // 「自动」是配置层的哨兵，正常路径上调用方已经把它换成了真模型名；这里再挡一道，
            // 是防着有人手改 local.json 之后直接调桥——哨兵绝不许当成模型名发给下游。
            if (string.Equals(modelName.Trim(), ModelSelection.AutoSentinel, StringComparison.Ordinal))
            {
                modelName = "";
            }

            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var secretKey = ReadConfigurationString(request, "执行后端密钥", "");

            if (endpoint.Length == 0)
            {
                return Failure("下游不可达", "执行后端地址未配置（配置键「地址」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return Failure("凭据无效", "执行后端密钥未配置（配置键「执行后端密钥」为空）", retryable: false);
            }

            var url = endpoint.TrimEnd('/') + "/chat/completions";

            var imagePaths = ReadPayloadStringList(request, "图片");
            var imageDataUrls = ReadImagesAsDataUrls(imagePaths, out var skippedImages);
            var requestBody = BuildRequestBody(modelName, context, prompt, imageDataUrls);
            var call = SendChatCompletion(url, secretKey, requestBody, timeoutSeconds);
            if (!call.Succeeded)
            {
                return call.Response;
            }

            if (!TryParseCompletion(call.ResponseJson, modelName, out var text, out var returnedModel, out var totalTokens, out var parseReason))
            {
                return Failure("下游报错", "服务端回传的不是合法的 chat/completions 响应：" + parseReason, retryable: false);
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["文本"] = text,
                ["模型"] = returnedModel,
                ["用了token"] = totalTokens
            });

            return BridgeResponse.Success(ContractVersion, payload);
        }

        /// <summary>
        /// caps：GET /models，把 {"节点":[],"模型":[{名,版本,hash}],"lora":[]} 写进载荷「输出路径」指的文件，
        /// 同一份对象也作为响应载荷返回。这是「模型那一格的下拉从哪来」的唯一来源——
        /// 清单**跟着地址走**：换个中转地址重探一次，清单就换一批。不产文本、不花 token。
        /// 「节点」与「lora」恒空数组：线上服务没有自定义节点、也不暴露 lora 清单，空数组是实话。
        /// </summary>
        /// <param name="request">请求信封，载荷 {"输出路径":"…"}。</param>
        public static BridgeResponse RunCaps(BridgeRequest request)
        {
            if (!TryGetPayloadString(request, "输出路径", out var outputPath, out var reason))
            {
                return Failure("请求不合协议", "载荷缺「输出路径」或它不是字符串：" + reason, retryable: false);
            }

            var endpoint = ReadConfigurationString(request, "地址", "");
            var secretKey = ReadConfigurationString(request, "执行后端密钥", "");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);

            if (endpoint.Length == 0)
            {
                return Failure("下游不可达", "执行后端地址未配置（配置键「地址」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return Failure("凭据无效", "执行后端密钥未配置（配置键「执行后端密钥」为空）", retryable: false);
            }

            var call = Send(HttpMethod.Get, endpoint.TrimEnd('/') + "/models", secretKey, null, timeoutSeconds);
            if (!call.Succeeded)
            {
                return call.Response;
            }

            if (!TryParseModelNames(call.ResponseJson, out var names, out var parseReason))
            {
                return Failure("下游报错", parseReason, retryable: false);
            }

            var root = new JsonObject
            {
                ["节点"] = new JsonArray(),
                ["模型"] = ToProbeArray(names),
                ["lora"] = new JsonArray()
            };

            try
            {
                var directory = System.IO.Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                System.IO.File.WriteAllText(outputPath, root.ToJsonString(), new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException)
            {
                return Failure("请求不合协议", $"探测输出写盘失败：{exception.Message}", retryable: false);
            }

            Console.Error.WriteLine($"BridgeOaicompat 探测到 {names.Count} 个模型");
            return BridgeResponse.Success(ContractVersion, JsonSerializer.SerializeToElement(root));
        }

        /// <summary>解析 /models 的响应：顶层 data 数组里逐项取字符串 id，去空、去重、按序数序排。</summary>
        /// <param name="responseJson">响应体文本。</param>
        /// <param name="names">解析出来的模型名。</param>
        /// <param name="reason">解析不了时的人话。</param>
        private static bool TryParseModelNames(string responseJson, out System.Collections.Generic.List<string> names, out string reason)
        {
            names = new System.Collections.Generic.List<string>();
            reason = "";

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(responseJson);
            }
            catch (JsonException exception)
            {
                reason = $"/models 回来的不是合法 JSON：{exception.Message}";
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                {
                    reason = "/models 回来的 JSON 里没有「data」数组";
                    return false;
                }

                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("id", out var identifier)
                        && identifier.ValueKind == JsonValueKind.String)
                    {
                        var name = identifier.GetString() ?? "";
                        if (name.Length > 0 && !names.Contains(name))
                        {
                            names.Add(name);
                        }
                    }
                }
            }

            names.Sort(StringComparer.Ordinal);
            return true;
        }

        /// <summary>把模型名列表拼成探测产出的「模型」数组：线上服务不报版本与 hash，两个键留空串是实话。</summary>
        /// <param name="names">模型名。</param>
        private static JsonArray ToProbeArray(System.Collections.Generic.IReadOnlyList<string> names)
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

        /// <summary>一次 HTTP 调用的结果：成功时带回传的响应体文本，失败时带协议响应。</summary>
        private sealed class HttpCall
        {
            public bool Succeeded;
            public BridgeResponse Response;
            public string ResponseJson;
        }

        /// <summary>
        /// 发 POST /chat/completions，读响应体。错误分类：
        /// 连不上 → 下游不可达；HTTP 401/403 → 凭据无效；429 → 限流（可重试）；
        /// 超时 → 超时；其余 → 下游报错，人话带服务端 message。
        /// 请求头（含密钥）绝不进任何错误文案。
        /// </summary>
        private static HttpCall SendChatCompletion(string url, string secretKey, string requestBody, int timeoutSeconds)
        {
            return Send(HttpMethod.Post, url, secretKey, requestBody, timeoutSeconds);
        }

        /// <summary>
        /// 发一次 HTTP 并读响应体。**密钥只进 Authorization 头，这是它唯一被允许出现的地方**——
        /// 所以这条路只许有这一处写法，POST 与 GET 共用它。错误分类见 <see cref="SendChatCompletion"/>。
        /// </summary>
        /// <param name="method">HTTP 方法。</param>
        /// <param name="url">完整 URL。</param>
        /// <param name="secretKey">执行后端密钥。</param>
        /// <param name="requestBody">请求体；null 表示不带体（GET）。</param>
        /// <param name="timeoutSeconds">超时秒数。</param>
        private static HttpCall Send(HttpMethod method, string url, string secretKey, string requestBody, int timeoutSeconds)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
                using var httpRequest = new HttpRequestMessage(method, url);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
                if (requestBody != null)
                {
                    httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                }

                using var httpResponse = client.SendAsync(httpRequest).GetAwaiter().GetResult();
                var responseText = httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                var statusCode = (int)httpResponse.StatusCode;
                if (statusCode >= 200 && statusCode < 300)
                {
                    return new HttpCall { Succeeded = true, ResponseJson = responseText };
                }

                if (statusCode == 401 || statusCode == 403)
                {
                    return Failed("凭据无效", $"执行后端返回 HTTP {statusCode}，密钥无效或无权访问", retryable: false);
                }

                if (statusCode == 429)
                {
                    return Failed("限流", "执行后端返回 HTTP 429，被限流，稍后重试", retryable: true);
                }

                var serverMessage = ReadErrorMessage(responseText);
                var retryable = statusCode >= 500;
                return Failed("下游报错", $"执行后端返回 HTTP {statusCode}：{serverMessage}", retryable: retryable);
            }
            catch (TaskCanceledException)
            {
                // HttpClient.Timeout 到期抛 TaskCanceledException；本进程没有其他取消源。
                return Failed("超时", $"执行后端超过 {timeoutSeconds} 秒未响应，已放弃本次调用", retryable: true);
            }
            catch (HttpRequestException exception)
            {
                // 连不上：DNS 失败、连接被拒、TLS 失败都落在这一支。异常消息不含请求头。
                return Failed("下游不可达", $"连不上执行后端：{exception.Message}", retryable: true);
            }
        }

        /// <summary>
        /// 把配置与载荷拼成 chat/completions 请求体 JSON 文本。
        ///
        /// 带图时用 OpenAI 兼容的多模态 content 数组（一段 text 加若干 image_url），
        /// 图以 data: URL 内联——**不上传到任何第三方图床**：那等于把项目里的美术稿
        /// 发到一个我们不控制的地方去。不带图时照旧发一个字符串 content，
        /// 免得给不支持多模态的模型塞一个它读不懂的形状。
        /// </summary>
        /// <param name="modelName">模型名。</param>
        /// <param name="context">系统上下文。</param>
        /// <param name="prompt">用户提示。</param>
        /// <param name="imageDataUrls">要一起发过去的图，已经是 data: URL；空表示不带图。</param>
        private static string BuildRequestBody(
            string modelName, string context, string prompt, IReadOnlyList<string> imageDataUrls)
        {
            var builder = new StringBuilder();
            builder.Append("{\"model\":");
            builder.Append(JsonSerializer.Serialize(modelName));
            builder.Append(",\"messages\":[{\"role\":\"system\",\"content\":");
            builder.Append(JsonSerializer.Serialize(context));
            builder.Append("},{\"role\":\"user\",\"content\":");

            if (imageDataUrls == null || imageDataUrls.Count == 0)
            {
                builder.Append(JsonSerializer.Serialize(prompt));
            }
            else
            {
                builder.Append("[{\"type\":\"text\",\"text\":");
                builder.Append(JsonSerializer.Serialize(prompt));
                builder.Append("}");
                foreach (var dataUrl in imageDataUrls)
                {
                    builder.Append(",{\"type\":\"image_url\",\"image_url\":{\"url\":");
                    builder.Append(JsonSerializer.Serialize(dataUrl));
                    builder.Append("}}");
                }

                builder.Append("]");
            }

            builder.Append("}]}");
            return builder.ToString();
        }

        /// <summary>读载荷里的字符串数组；缺失或类型不对给空表。</summary>
        /// <param name="request">请求信封。</param>
        /// <param name="key">键名。</param>
        private static IReadOnlyList<string> ReadPayloadStringList(BridgeRequest request, string key)
        {
            var values = new List<string>();
            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty(key, out var element)
                || element.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString() ?? "";
                    if (text.Length > 0)
                    {
                        values.Add(text);
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// 把本地图片读成 data: URL。读不动的**跳过并记一笔**，不让一张读不动的图
        /// 把整次调用打掉——少看一张图是少一份依据，而整次失败是什么都没有。
        /// </summary>
        /// <param name="paths">本地图片路径。</param>
        /// <param name="skipped">跳过的文件与原因。</param>
        private static IReadOnlyList<string> ReadImagesAsDataUrls(
            IReadOnlyList<string> paths, out IReadOnlyList<string> skipped)
        {
            var urls = new List<string>();
            var skippedList = new List<string>();
            skipped = skippedList;

            foreach (var path in paths ?? Array.Empty<string>())
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    urls.Add("data:image/png;base64," + Convert.ToBase64String(bytes));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    skippedList.Add(Path.GetFileName(path) + "：" + exception.Message);
                }
            }

            return urls;
        }

        /// <summary>
        /// 解析 chat/completions 响应：文本取 choices[0].message.content，模型取顶层的 model
        /// （服务端回的那个，不是配置里配的），token 数取 usage.total_tokens。
        /// 模型缺失时回落到调用方给的配置模型名（正常情况服务端一定回 model）。
        /// </summary>
        private static bool TryParseCompletion(string responseJson, string configuredModel, out string text, out string model, out int totalTokens, out string reason)
        {
            text = "";
            model = configuredModel;
            totalTokens = 0;
            reason = "";

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(responseJson);
            }
            catch (JsonException exception)
            {
                reason = exception.Message;
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "顶层不是对象";
                    return false;
                }

                if (root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String)
                {
                    model = modelElement.GetString() ?? configuredModel;
                }

                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                {
                    reason = "缺「choices」数组或它是空的";
                    return false;
                }

                var firstChoice = choices[0];
                if (firstChoice.ValueKind != JsonValueKind.Object
                    || !firstChoice.TryGetProperty("message", out var message)
                    || message.ValueKind != JsonValueKind.Object
                    || !message.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.String)
                {
                    reason = "choices[0].message.content 不是字符串";
                    return false;
                }

                text = content.GetString() ?? "";

                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
                    && usage.TryGetProperty("total_tokens", out var tokenElement)
                    && tokenElement.ValueKind == JsonValueKind.Number)
                {
                    try
                    {
                        totalTokens = tokenElement.GetInt32();
                    }
                    catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                    {
                        totalTokens = 0;
                    }
                }

                return true;
            }
        }

        /// <summary>从错误响应体里抠服务端 message：先试 {"error":{"message":"…"}}，取不到给状态码占位。</summary>
        private static string ReadErrorMessage(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return "（服务端没有返回错误说明）";
            }

            try
            {
                using var document = JsonDocument.Parse(responseText);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    var text = message.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
            catch (JsonException)
            {
            }

            return "（服务端返回的不是带 message 的 JSON 错误）";
        }

        /// <summary>失败响应。</summary>
        private static BridgeResponse Failure(string code, string humanText, bool retryable)
        {
            return BridgeResponse.Failure(ContractVersion, code, humanText, retryable);
        }

        /// <summary>失败运行结果。</summary>
        private static HttpCall Failed(string code, string humanText, bool retryable)
        {
            return new HttpCall { Succeeded = false, Response = Failure(code, humanText, retryable) };
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

    }
}
