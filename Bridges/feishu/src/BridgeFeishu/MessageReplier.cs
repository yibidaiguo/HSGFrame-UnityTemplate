using System;
using System.Collections.Generic;
using System.Text;
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
        /// 执行 reply 动作：干跑返回要发的消息体；真跑发一条消息（带「卡片」发 interactive，否则发文本）。
        /// </summary>
        /// <param name="request">请求信封：配置含 应用标识 / 飞书应用密钥 / 超时秒，
        /// 载荷含 干跑（缺省 true）、会话标识、文本，可选「卡片」（归一卡片数据）。</param>
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

            // 载荷带「卡片」就发 interactive，否则发纯文本。
            // **文本永远要给**：卡片拼不出来时退回文本发，不许因为卡片发不成就什么都不回。
            var card = ReadPayloadObject(request, "卡片");
            var messageKind = card == null ? "text" : "interactive";

            // 飞书的消息 content 是一个 JSON 字符串：文本是 {"text": …}，卡片是整张卡的 JSON。
            var contentText = card == null
                ? new JsonObject { ["text"] = text }.ToJsonString()
                : BuildCardJson(card, text, appId, secretKey, timeoutSeconds, uploadImages: true);
            var content = JsonSerializer.Serialize(contentText);
            var body = "{\"receive_id\":" + JsonSerializer.Serialize(conversationIdentifier)
                + ",\"msg_type\":\"" + messageKind + "\""
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

            // 把**真发出去的那份卡 JSON** 带回去。引擎要留着它：
            // 下一轮撤按钮时，只有拿着这份原样的 JSON 才能做到「只去掉按钮、别的一个字不动」。
            // 尤其是图——图片在这份 JSON 里已经是 image_key 了，
            // 重新拼一遍的话 card-update 那条路不传图，图会当场消失。
            if (card != null)
            {
                payload["卡片JSON"] = contentText;
            }

            return Success(JsonSerializer.SerializeToElement(payload));
        }

        /// <summary>
        /// 把归一的卡片数据拼成飞书 interactive 卡片。
        ///
        /// 按钮带 <c>value</c> 才会触发回传交互（<c>card.action.trigger</c>），
        /// 旁路订的就是这个事件——**动作名一定要进 value**，否则点了回来引擎不知道点的是哪个键。
        /// 引擎侧的归一键（动作/携带）在这里翻成飞书的形状：卡片长什么样是下游知识（决策 93）。
        /// </summary>
        /// <param name="card">归一卡片数据：标题/正文/条目/待确认/按钮/图片。</param>
        /// <param name="fallbackText">卡片没有正文时兜底用的文本。</param>
        /// <param name="appId">飞书应用标识，贴图要用（先上传拿 image_key）。</param>
        /// <param name="secretKey">飞书应用密钥。</param>
        /// <param name="timeoutSeconds">单次调用超时秒数。</param>
        /// <param name="uploadImages">要不要把「图片」传上去；更新卡片那条路不传（争的是那几百毫秒）。</param>
        internal static string BuildCardJson(
            JsonObject card, string fallbackText, string appId, string secretKey, int timeoutSeconds,
            bool uploadImages = true)
        {
            var elements = new JsonArray();

            var bodyText = ReadString(card, "正文");
            if (bodyText.Length == 0)
            {
                bodyText = fallbackText ?? "";
            }

            if (bodyText.Length > 0)
            {
                elements.Add(new JsonObject
                {
                    ["tag"] = "div",
                    ["text"] = new JsonObject { ["tag"] = "lark_md", ["content"] = bodyText }
                });
            }

            if (card["条目"] is JsonArray entries && entries.Count > 0)
            {
                var fields = new JsonArray();
                foreach (var entry in entries)
                {
                    if (entry is not JsonObject item)
                    {
                        continue;
                    }

                    fields.Add(new JsonObject
                    {
                        ["is_short"] = false,
                        ["text"] = new JsonObject
                        {
                            ["tag"] = "lark_md",
                            ["content"] = "**" + ReadString(item, "名称") + "**\n" + ReadString(item, "值")
                        }
                    });
                }

                if (fields.Count > 0)
                {
                    elements.Add(new JsonObject { ["tag"] = "hr" });
                    elements.Add(new JsonObject { ["tag"] = "div", ["fields"] = fields });
                }
            }

            if (card["待确认"] is JsonArray questions && questions.Count > 0)
            {
                var builder = new StringBuilder("**想跟你确认**\n");
                foreach (var question in questions)
                {
                    builder.Append("· ").Append(question?.ToString() ?? "").Append('\n');
                }

                elements.Add(new JsonObject
                {
                    ["tag"] = "div",
                    ["text"] = new JsonObject { ["tag"] = "lark_md", ["content"] = builder.ToString().TrimEnd() }
                });
            }

            // 图片：本地文件先上传拿 image_key，卡片里的 img 只引用 key。
            // **传不上去的那张要说出来**，不许静默少一张——人对着少一张的九宫格根本不会发现。
            if (uploadImages && card["图片"] is JsonArray images && images.Count > 0)
            {
                var failures = new List<string>();
                foreach (var image in images)
                {
                    var path = image?.ToString() ?? "";
                    if (path.Length == 0)
                    {
                        continue;
                    }

                    var upload = FeishuClient.UploadImage(path, appId, secretKey, timeoutSeconds);
                    if (!upload.Succeeded)
                    {
                        failures.Add(System.IO.Path.GetFileName(path));
                        continue;
                    }

                    var imageKey = ReadResponseString(upload.ResponseBody, "data", "image_key");
                    if (imageKey.Length == 0)
                    {
                        failures.Add(System.IO.Path.GetFileName(path));
                        continue;
                    }

                    // 缩略图，不是满宽大图。一张卡上贴几张图时，满宽（fit_horizontal）
                    // 会把聊天框撑成长长一条，人要滑半天才划得完——真被这么抱怨过。
                    // compact_width 把宽度压到 278px，crop_center 对长图限高；
                    // 点击放大是 preview 的默认行为，看细节照样看得到。
                    elements.Add(new JsonObject
                    {
                        ["tag"] = "img",
                        ["img_key"] = imageKey,
                        ["alt"] = new JsonObject { ["tag"] = "plain_text", ["content"] = System.IO.Path.GetFileName(path) },
                        ["mode"] = "crop_center",
                        ["compact_width"] = true,
                        ["preview"] = true
                    });
                }

                if (failures.Count > 0)
                {
                    elements.Add(new JsonObject
                    {
                        ["tag"] = "div",
                        ["text"] = new JsonObject
                        {
                            ["tag"] = "lark_md",
                            ["content"] = "（这几张没贴上来：" + string.Join("、", failures) + "，本体还在仓库里）"
                        }
                    });
                }
            }

            if (card["按钮"] is JsonArray buttons && buttons.Count > 0)
            {
                var actions = new JsonArray();
                foreach (var button in buttons)
                {
                    if (button is not JsonObject item)
                    {
                        continue;
                    }

                    var value = item["携带"] is JsonObject carried
                        ? (JsonObject)carried.DeepClone()
                        : new JsonObject();
                    value["动作"] = ReadString(item, "动作");

                    actions.Add(new JsonObject
                    {
                        ["tag"] = "button",
                        ["text"] = new JsonObject { ["tag"] = "plain_text", ["content"] = ReadString(item, "文案") },
                        ["type"] = ReadBool(item, "主按钮") ? "primary" : "default",
                        ["value"] = value
                    });
                }

                if (actions.Count > 0)
                {
                    elements.Add(new JsonObject { ["tag"] = "action", ["actions"] = actions });
                }
            }

            var cardObject = new JsonObject
            {
                ["config"] = new JsonObject { ["wide_screen_mode"] = true },
                ["elements"] = elements
            };

            var title = ReadString(card, "标题");
            if (title.Length > 0)
            {
                cardObject["header"] = new JsonObject
                {
                    ["title"] = new JsonObject { ["tag"] = "plain_text", ["content"] = title },
                    ["template"] = "blue"
                };
            }

            return cardObject.ToJsonString();
        }

        /// <summary>读一个 JsonObject 里的字符串键；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonObject node, string key)
        {
            return node != null
                && node.TryGetPropertyValue(key, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var text)
                ? text
                : "";
        }

        /// <summary>读一个 JsonObject 里的布尔键；缺失或类型不对给 false。</summary>
        private static bool ReadBool(JsonObject node, string key)
        {
            return node != null
                && node.TryGetPropertyValue(key, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<bool>(out var flag)
                && flag;
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
