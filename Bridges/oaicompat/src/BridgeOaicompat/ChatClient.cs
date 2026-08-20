using System;
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
        /// <param name="request">请求信封，载荷 {"提示":"…","上下文":"…"}，配置含 地址/模型/超时秒/执行后端密钥。</param>
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

            var requestBody = BuildRequestBody(modelName, context, prompt);
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
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
                httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

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

        /// <summary>把配置与载荷拼成 chat/completions 请求体 JSON 文本。</summary>
        private static string BuildRequestBody(string modelName, string context, string prompt)
        {
            var builder = new StringBuilder();
            builder.Append("{\"model\":");
            builder.Append(JsonSerializer.Serialize(modelName));
            builder.Append(",\"messages\":[{\"role\":\"system\",\"content\":");
            builder.Append(JsonSerializer.Serialize(context));
            builder.Append("},{\"role\":\"user\",\"content\":");
            builder.Append(JsonSerializer.Serialize(prompt));
            builder.Append("}]}");
            return builder.ToString();
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
