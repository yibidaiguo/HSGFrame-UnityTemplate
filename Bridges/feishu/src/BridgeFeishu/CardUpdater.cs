using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 更新一条已经发出去的卡片（card-update）：把原来那张卡换成新的一张。
    ///
    /// 为什么必须有：卡片上的按钮**点完不会自己消失**。人点了「出图」，图要跑几十秒，
    /// 这期间那个按钮还亮着——连点几下就是连着出好几批图，真花钱。
    /// 所以点下去第一件事就是把原卡换成一张没有按钮的，跑完再换成结果，
    /// 失败了才把按钮换回来。
    ///
    /// 与 reply 的区别：reply 发一条新消息，这里改的是**原来那一条**，所以要消息标识。
    /// </summary>
    public static class CardUpdater
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>
        /// 执行 card-update 动作：干跑返回要换成的卡片 JSON；真跑改那条消息。
        /// </summary>
        /// <param name="request">请求信封：配置含 应用标识 / 飞书应用密钥 / 超时秒，
        /// 载荷含 干跑（缺省 true）、消息标识、卡片。</param>
        public static BridgeResponse Update(BridgeRequest request)
        {
            var appId = ReadConfigurationString(request, "应用标识", "");
            var secretKey = ReadConfigurationString(request, "飞书应用密钥", "");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);
            var messageIdentifier = ReadPayloadString(request, "消息标识");

            if (appId.Length == 0 || secretKey.Length == 0)
            {
                return Failure("凭据无效", "应用标识或飞书应用密钥未配置", retryable: false);
            }

            if (messageIdentifier.Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「消息标识」：不知道要改哪一条消息", retryable: false);
            }

            // **原样透传那一路优先**：调用方给了「卡片JSON」就直接用它，不重拼。
            // 撤按钮走的正是这条——把上一轮真发出去的那份 JSON 原样送回来，
            // 只少掉按钮那一个元素。重拼的话图会没（这条路不传图），
            // 而人要的是「按钮没了」，不是「聊天记录没了」。
            var rawCardJson = ReadPayloadString(request, "卡片JSON");
            string cardJson;

            if (rawCardJson.Length > 0)
            {
                cardJson = rawCardJson;
            }
            else
            {
                var card = ReadPayloadObject(request, "卡片");
                if (card == null)
                {
                    return Failure("请求不合协议", "载荷既没有「卡片JSON」也没有「卡片」：不知道要换成什么", retryable: false);
                }

                // 重拼这一路不带图：贴图要先上传拿 image_key，而它服务的是
                // 「点了立刻换掉按钮」那种争几百毫秒的场景；结果图走 reply 发新卡。
                cardJson = MessageReplier.BuildCardJson(card, "", appId, secretKey, timeoutSeconds, uploadImages: false);
            }
            var body = new JsonObject { ["content"] = cardJson }.ToJsonString();

            if (isDryRun)
            {
                return Success(JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["干跑"] = true,
                    ["消息标识"] = messageIdentifier,
                    ["要换成的卡片JSON"] = cardJson
                }));
            }

            var call = FeishuClient.Send(
                "PATCH",
                "https://open.feishu.cn/open-apis/im/v1/messages/" + Uri.EscapeDataString(messageIdentifier),
                body,
                appId,
                secretKey,
                timeoutSeconds);
            if (!call.Succeeded)
            {
                return call.Response;
            }

            return Success(JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = false,
                ["消息标识"] = messageIdentifier
            }));
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
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out var number))
            {
                return number;
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

        /// <summary>读载荷里的对象键，拷成可写的 JsonObject；缺失或类型不对给 null。</summary>
        private static JsonObject ReadPayloadObject(BridgeRequest request, string key)
        {
            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty(key, out var element)
                || element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(element.GetRawText()) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
