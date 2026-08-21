using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 回话动作（reply）：把助手算出来的一段文字回进原来那个会话。
    ///
    /// 与 <see cref="CardSender"/> 的区别是收件人从哪来：卡片发给配置里的「测试收件人」
    /// （出站通知，收件人是配置的），回话发回**消息来的那个会话**（收件人跟着事件走）。
    /// 所以这里收 receive_id_type=chat_id，而不是 open_id。
    ///
    /// 干跑默认开着：调文案不该靠反复真发（决策 92）。
    /// </summary>
    public static class MessageReplier
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>回话端点：按会话标识发，receive_id_type=chat_id。</summary>
        private const string ImMessagesByChatEndpoint =
            "https://open.feishu.cn/open-apis/im/v1/messages?receive_id_type=chat_id";

        /// <summary>
        /// 执行 reply 动作：干跑返回要发的消息体；真跑发一条文本消息。
        /// </summary>
        /// <param name="request">请求信封：配置含 应用标识 / 飞书应用密钥 / 超时秒，
        /// 载荷含 干跑（缺省 true）、会话标识、文本。</param>
        public static BridgeResponse Reply(BridgeRequest request)
        {
            var appId = ReadConfigurationString(request, "应用标识", "");
            var secretKey = ReadConfigurationString(request, "飞书应用密钥", "");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);
            var conversationIdentifier = ReadPayloadString(request, "会话标识");
            var text = ReadPayloadString(request, "文本");

            if (appId.Length == 0)
            {
                return Failure("凭据无效", "应用标识未配置（配置键「应用标识」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return Failure("凭据无效", "飞书应用密钥未配置（配置键「飞书应用密钥」为空）", retryable: false);
            }

            if (conversationIdentifier.Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「会话标识」，回话没有去处", retryable: false);
            }

            if (text.Trim().Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「文本」或它是空的——不许发空消息", retryable: false);
            }

            // 飞书的文本消息 content 是一个 JSON 字符串，里面再包一层 {"text": …}。
            var content = JsonSerializer.Serialize(new JsonObject { ["text"] = text }.ToJsonString());
            var body = "{\"receive_id\":" + JsonSerializer.Serialize(conversationIdentifier)
                + ",\"msg_type\":\"text\""
                + ",\"content\":" + content + "}";

            if (isDryRun)
            {
                var dryPayload = new JsonObject
                {
                    ["干跑"] = true,
                    ["要发的消息体"] = body,
                    ["字数"] = text.Length
                };
                return Success(JsonSerializer.SerializeToElement(dryPayload));
            }

            var call = FeishuClient.Send("POST", ImMessagesByChatEndpoint, body, appId, secretKey, timeoutSeconds);
            if (!call.Succeeded)
            {
                return call.Response;
            }

            var messageIdentifier = ReadResponseString(call.ResponseBody, "data", "message_id");
            if (messageIdentifier.Length == 0)
            {
                return Failure("下游报错", "回话请求下游回了成功，但响应里没有 message_id——没法证明真发出去了", retryable: false);
            }

            var payload = new JsonObject
            {
                ["干跑"] = false,
                ["消息标识"] = messageIdentifier,
                ["字数"] = text.Length
            };
            return Success(JsonSerializer.SerializeToElement(payload));
        }

        /// <summary>成功响应。</summary>
        private static BridgeResponse Success(JsonElement payload)
        {
            return BridgeResponse.Success(ContractVersion, payload);
        }

        /// <summary>失败响应。</summary>
        private static BridgeResponse Failure(string code, string humanText, bool retryable)
        {
            return BridgeResponse.Failure(ContractVersion, code, humanText, retryable);
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

        /// <summary>读载荷里的字符串键；缺失给空串。</summary>
        private static string ReadPayloadString(BridgeRequest request, string key)
        {
            if (request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读载荷里的布尔键；缺失给缺省值。</summary>
        private static bool ReadPayloadBool(BridgeRequest request, string key, bool defaultValue)
        {
            if (request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var element))
            {
                if (element.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (element.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
            }

            return defaultValue;
        }

        /// <summary>从响应体里按两级键读字符串；取不到给空串。</summary>
        private static string ReadResponseString(JsonElement body, string first, string second)
        {
            if (body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty(first, out var level)
                && level.ValueKind == JsonValueKind.Object
                && level.TryGetProperty(second, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }
    }
}
