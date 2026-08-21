using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Template.Toolkit.AgentRunner
{
    /// <summary>一轮 chat/completions 的结果：助手消息（可能带 tool_calls）与本轮 token 总数。</summary>
    public sealed class ChatTurn
    {
        /// <summary>
        /// 构造一轮结果。
        /// </summary>
        /// <param name="assistantMessage">choices[0].message 的完整 JSON 对象。</param>
        /// <param name="totalTokens">usage.total_tokens；服务端没回时为 0。</param>
        /// <param name="modelName">服务端回的模型名。</param>
        public ChatTurn(JsonObject assistantMessage, int totalTokens, string modelName)
        {
            AssistantMessage = assistantMessage;
            TotalTokens = totalTokens;
            ModelName = modelName ?? "";
        }

        /// <summary>choices[0].message 的完整 JSON 对象。</summary>
        public JsonObject AssistantMessage { get; }

        /// <summary>usage.total_tokens；服务端没回时为 0。</summary>
        public int TotalTokens { get; }

        /// <summary>服务端回的模型名。</summary>
        public string ModelName { get; }
    }

    /// <summary>chat 传输接口：循环只依赖它，测试用假传输喂预置回合。</summary>
    public interface IChatTransport
    {
        /// <summary>发一轮 chat/completions。</summary>
        /// <param name="messages">完整消息数组。</param>
        /// <param name="tools">工具声明数组；null 表示本轮不带工具。</param>
        ChatTurn Complete(JsonArray messages, JsonArray tools);
    }

    /// <summary>
    /// OpenAI 兼容 chat/completions 的 HTTP 传输，支持函数调用（tools）。
    /// 密钥红线（决策 5、78）：密钥只进 Authorization 头，不进日志、异常消息与任何返回文本。
    /// 限流（429）、超时与 5xx 各重试一次；仍失败抛 <see cref="InvalidOperationException"/>，消息不含请求头。
    /// </summary>
    public sealed class HttpChatTransport : IChatTransport
    {
        private readonly string _url;
        private readonly string _modelName;
        private readonly string _secretKey;
        private readonly int _timeoutSeconds;

        /// <summary>
        /// 构造一个传输。
        /// </summary>
        /// <param name="endpoint">OpenAI 兼容 base URL（不带 /chat/completions）。</param>
        /// <param name="modelName">模型名。</param>
        /// <param name="secretKey">API 密钥。</param>
        /// <param name="timeoutSeconds">单次调用超时秒数。</param>
        public HttpChatTransport(string endpoint, string modelName, string secretKey, int timeoutSeconds)
        {
            _url = (endpoint ?? "").TrimEnd('/') + "/chat/completions";
            _modelName = modelName ?? "";
            _secretKey = secretKey ?? "";
            _timeoutSeconds = Math.Max(1, timeoutSeconds);
        }

        /// <summary>发一轮 chat/completions；可重试错误重试一次。</summary>
        /// <param name="messages">完整消息数组。</param>
        /// <param name="tools">工具声明数组；null 表示本轮不带工具。</param>
        public ChatTurn Complete(JsonArray messages, JsonArray tools)
        {
            var requestBody = BuildRequestBody(messages, tools);
            var firstAttempt = TrySend(requestBody, out var turn, out var reason, out var retryable);
            if (firstAttempt)
            {
                return turn;
            }

            if (retryable)
            {
                Thread.Sleep(TimeSpan.FromSeconds(5));
                if (TrySend(requestBody, out turn, out reason, out _))
                {
                    return turn;
                }
            }

            throw new InvalidOperationException(reason);
        }

        private string BuildRequestBody(JsonArray messages, JsonArray tools)
        {
            var body = new JsonObject
            {
                ["model"] = _modelName,
                ["messages"] = JsonNode.Parse(messages.ToJsonString())
            };
            if (tools != null)
            {
                body["tools"] = JsonNode.Parse(tools.ToJsonString());
                body["tool_choice"] = "auto";
            }

            return body.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        private bool TrySend(string requestBody, out ChatTurn turn, out string reason, out bool retryable)
        {
            turn = null;
            reason = "";
            retryable = false;

            string responseText;
            int statusCode;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(_timeoutSeconds) };
                using var request = new HttpRequestMessage(HttpMethod.Post, _url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                statusCode = (int)response.StatusCode;
            }
            catch (TaskCanceledException)
            {
                reason = $"执行后端超过 {_timeoutSeconds} 秒未响应";
                retryable = true;
                return false;
            }
            catch (HttpRequestException exception)
            {
                reason = $"连不上执行后端：{exception.Message}";
                retryable = true;
                return false;
            }

            if (statusCode == 401 || statusCode == 403)
            {
                reason = $"执行后端返回 HTTP {statusCode}，密钥无效或无权访问";
                return false;
            }

            if (statusCode == 429 || statusCode >= 500)
            {
                reason = $"执行后端返回 HTTP {statusCode}";
                retryable = true;
                return false;
            }

            if (statusCode < 200 || statusCode >= 300)
            {
                reason = $"执行后端返回 HTTP {statusCode}：{ReadErrorMessage(responseText)}";
                return false;
            }

            return TryParse(responseText, out turn, out reason);
        }

        private bool TryParse(string responseText, out ChatTurn turn, out string reason)
        {
            turn = null;
            reason = "";

            JsonObject root;
            try
            {
                root = JsonNode.Parse(responseText) as JsonObject;
            }
            catch (JsonException exception)
            {
                reason = "服务端回传的不是合法 JSON：" + exception.Message;
                return false;
            }

            if (root == null
                || root["choices"] is not JsonArray choices
                || choices.Count == 0
                || choices[0] is not JsonObject firstChoice
                || firstChoice["message"] is not JsonObject message)
            {
                reason = "服务端回传缺 choices[0].message";
                return false;
            }

            var totalTokens = 0;
            if (root["usage"] is JsonObject usage
                && usage["total_tokens"] is JsonValue tokenValue
                && tokenValue.TryGetValue<int>(out var parsedTokens))
            {
                totalTokens = parsedTokens;
            }

            var modelName = _modelName;
            if (root["model"] is JsonValue modelValue && modelValue.TryGetValue<string>(out var returnedModel))
            {
                modelName = returnedModel;
            }

            // 从父节点摘下来，让它能挂进消息数组继续用。
            firstChoice.Remove("message");
            turn = new ChatTurn(message, totalTokens, modelName);
            return true;
        }

        private static string ReadErrorMessage(string responseText)
        {
            try
            {
                var root = JsonNode.Parse(responseText ?? "") as JsonObject;
                if (root?["error"] is JsonObject error
                    && error["message"] is JsonValue message
                    && message.TryGetValue<string>(out var text)
                    && !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch (JsonException)
            {
            }

            return "（服务端没有返回错误说明）";
        }
    }
}
