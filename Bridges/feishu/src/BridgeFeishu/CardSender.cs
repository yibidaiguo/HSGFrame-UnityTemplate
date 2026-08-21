using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 发卡片动作（card）：把一张选片卡（SelectionCard 出的数据）拼成飞书 interactive 卡片，
    /// 发给「测试收件人」。收件人只从配置的测试收件人取，不去通讯录捞人、不群发。
    /// 卡片上的九宫格预览图先经 image_key 上传，img 元素只引用 image_key，不直接塞图片字节。
    /// 调试卡片格式用干跑——干跑把要发的卡片 JSON 打出来，别靠反复真发去试（决策 92）。
    /// 整个验收过程只许真发一条。
    /// </summary>
    public static class CardSender
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>
        /// 执行 card 动作：干跑返回要发的卡片 JSON；真跑发一条 interactive 消息。
        /// </summary>
        /// <param name="request">请求信封：配置含 测试收件人/应用标识/飞书应用密钥/超时秒，
        /// 载荷含 干跑（缺省 true）与 卡片（需求id/资产id/轮次/合格变体/弃置数/按钮/提示）。</param>
        public static BridgeResponse SendCard(BridgeRequest request)
        {
            var appId = ReadConfigurationString(request, "应用标识", "");
            var secretKey = ReadConfigurationString(request, "飞书应用密钥", "");
            var recipient = ReadConfigurationString(request, "测试收件人", "");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);

            if (recipient.Length == 0)
            {
                return Failure("凭据无效", "测试收件人未配置（配置键「测试收件人」为空）", retryable: false);
            }

            if (appId.Length == 0)
            {
                return Failure("凭据无效", "应用标识未配置（配置键「应用标识」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return Failure("凭据无效", "飞书应用密钥未配置（配置键「飞书应用密钥」为空）", retryable: false);
            }

            if (!TryReadCardData(request, out var cardData, out var reason))
            {
                return Failure("请求不合协议", reason, retryable: false);
            }

            if (isDryRun)
            {
                var imageKey = cardData.SheetPath.Length > 0 ? "<待上传>" : "";
                var cardJson = BuildCardJson(cardData, imageKey);
                var payload = new JsonObject
                {
                    ["干跑"] = true,
                    ["要发的卡片JSON"] = cardJson
                };
                if (cardData.SheetPath.Length > 0)
                {
                    payload["拼图路径"] = cardData.SheetPath;
                }

                return Success(JsonSerializer.SerializeToElement(payload));
            }

            var effectiveImageKey = "";
            if (cardData.SheetPath.Length > 0)
            {
                var upload = FeishuClient.UploadImage(cardData.SheetPath, appId, secretKey, timeoutSeconds);
                if (!upload.Succeeded)
                {
                    return upload.Response;
                }

                effectiveImageKey = ReadString(upload.ResponseBody, "data", "image_key");
                if (effectiveImageKey.Length == 0)
                {
                    return Failure("下游报错", "飞书上传图片的响应里没有 image_key", retryable: false);
                }
            }

            var cardJsonReal = BuildCardJson(cardData, effectiveImageKey);

            var body = "{\"receive_id\":" + JsonSerializer.Serialize(recipient)
                + ",\"msg_type\":\"interactive\""
                + ",\"content\":" + JsonSerializer.Serialize(cardJsonReal) + "}";

            var call = FeishuClient.Send("POST", FeishuClient.ImMessagesUrl(), body, appId, secretKey, timeoutSeconds);
            if (!call.Succeeded)
            {
                return call.Response;
            }

            var messageId = ReadString(call.ResponseBody, "data", "message_id");
            if (messageId.Length == 0)
            {
                return Failure("下游报错", "飞书发消息的响应里没有 message_id", retryable: false);
            }

            var result = new JsonObject
            {
                ["message_id"] = messageId,
                ["收件人"] = recipient
            };
            return Success(JsonSerializer.SerializeToElement(result));
        }

        /// <summary>拼一张飞书 interactive 选片卡：标题、九宫格图（可选）、资产/轮次/数量摘要、变体清单与按钮。</summary>
        private static string BuildCardJson(CardData card, string imageKey)
        {
            var summaryLines = new List<string>
            {
                "资产：" + card.AssetIdentifier,
                "轮次：第 " + card.Round + " 轮",
                "合格变体：" + card.QualifiedVariants.Count + " 张，弃置：" + card.RejectedCount + " 张"
            };
            if (card.Hint.Length > 0)
            {
                summaryLines.Add(card.Hint);
            }

            var elements = new JsonArray();

            if (!string.IsNullOrEmpty(imageKey))
            {
                elements.Add(new JsonObject
                {
                    ["tag"] = "img",
                    ["img_key"] = imageKey,
                    ["alt"] = new JsonObject { ["tag"] = "plain_text", ["content"] = "选片九宫格" },
                    ["mode"] = "fit_horizontal",
                    ["preview"] = true
                });
            }

            elements.Add(new JsonObject
            {
                ["tag"] = "div",
                ["text"] = new JsonObject { ["tag"] = "lark_md", ["content"] = string.Join("\n", summaryLines) }
            });

            if (card.QualifiedVariants.Count > 0)
            {
                elements.Add(new JsonObject
                {
                    ["tag"] = "div",
                    ["text"] = new JsonObject
                    {
                        ["tag"] = "lark_md",
                        ["content"] = "变体：" + string.Join("、", card.QualifiedVariants)
                    }
                });
            }

            if (card.Buttons.Count > 0)
            {
                var actions = new JsonArray();
                foreach (var button in card.Buttons)
                {
                    actions.Add(new JsonObject
                    {
                        ["tag"] = "button",
                        ["text"] = new JsonObject { ["tag"] = "plain_text", ["content"] = button },
                        ["value"] = new JsonObject { ["选片"] = button }
                    });
                }

                elements.Add(new JsonObject { ["tag"] = "action", ["actions"] = actions });
            }

            var cardObject = new JsonObject
            {
                ["config"] = new JsonObject { ["wide_screen_mode"] = true },
                ["header"] = new JsonObject
                {
                    ["title"] = new JsonObject { ["tag"] = "plain_text", ["content"] = "选片：" + card.RequirementIdentifier }
                },
                ["elements"] = elements
            };

            return cardObject.ToJsonString();
        }

        /// <summary>卡片数据：从载荷的「卡片」对象读出。</summary>
        private sealed class CardData
        {
            public string RequirementIdentifier;
            public string AssetIdentifier;
            public int Round;
            public IReadOnlyList<string> QualifiedVariants;
            public int RejectedCount;
            public IReadOnlyList<string> Buttons;
            public string Hint;
            public string SheetPath;
        }

        /// <summary>从载荷读卡片数据；缺必填键或类型不对给可读原因。</summary>
        private static bool TryReadCardData(BridgeRequest request, out CardData cardData, out string reason)
        {
            cardData = null;
            reason = "";

            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty("卡片", out var cardElement)
                || cardElement.ValueKind != JsonValueKind.Object)
            {
                reason = "载荷缺「卡片」或它不是对象";
                return false;
            }

            var requirementIdentifier = ReadString(cardElement, "需求id");
            var assetIdentifier = ReadString(cardElement, "资产id");
            if (requirementIdentifier.Length == 0 || assetIdentifier.Length == 0)
            {
                reason = "卡片数据缺「需求id」或「资产id」";
                return false;
            }

            var round = ReadInt(cardElement, "轮次");
            if (round < 1)
            {
                round = 1;
            }

            var qualifiedVariants = ReadStringArray(cardElement, "合格变体");
            var buttons = ReadStringArray(cardElement, "按钮");
            if (buttons.Count == 0)
            {
                buttons = Array.Empty<string>();
            }

            cardData = new CardData
            {
                RequirementIdentifier = requirementIdentifier,
                AssetIdentifier = assetIdentifier,
                Round = round,
                QualifiedVariants = qualifiedVariants,
                RejectedCount = ReadInt(cardElement, "弃置数"),
                Buttons = buttons,
                Hint = ReadString(cardElement, "提示"),
                SheetPath = ReadString(cardElement, "拼图路径")
            };
            return true;
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

        /// <summary>读请求载荷里的布尔键；缺失给缺省值（缺省即不写，安全侧）。</summary>
        private static bool ReadPayloadBool(BridgeRequest request, string key, bool defaultValue)
        {
            if (request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var element)
                && (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
            {
                return element.ValueKind == JsonValueKind.True;
            }

            return defaultValue;
        }

        /// <summary>读 JSON 对象里的字符串键；缺失或类型不对给空串。</summary>
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

        /// <summary>读 JSON 对象里的整数键；缺失或类型不对给 0。</summary>
        private static int ReadInt(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return value.GetInt32();
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                }
            }

            return 0;
        }

        /// <summary>读 JSON 对象里的字符串数组；缺失或类型不对给空列表。</summary>
        private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
        {
            var values = new List<string>();
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    values.Add(item.GetString() ?? "");
                }
            }

            return values;
        }

        /// <summary>读嵌套路径上的字符串：先取 property1 的对象，再取 property2。</summary>
        private static string ReadString(JsonElement element, string property1, string property2)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(property1, out var outer)
                && outer.ValueKind == JsonValueKind.Object
                && outer.TryGetProperty(property2, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }
    }
}
