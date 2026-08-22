using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 往任务表加一行（task-row）：一条需求确认之后，在任务表里给它开一行，等 PM 派。
    ///
    /// **只建行，不派活**：执行人、进展、日期这几列一律留空，由 PM 在飞书里填。
    /// 引擎替人把「谁来做、什么时候做」定了，那是越权——那几列是人的决定，不是机器的推论。
    ///
    /// 任务与需求的连线是**一列超链接**，不是飞书的关联列：关联列只能关联同一个多维表格里的记录，
    /// 而需求是知识空间里的一份文档，关联不过去。链接由 doc.push 推完回写，这里原样带上。
    ///
    /// 幂等靠「需求id」这一列：同一条需求点两次按钮，第二次先按 need id 查一遍，
    /// 查到就不再加行。没有这一道，聊天记录里那张卡每点一次就多出一行任务。
    /// </summary>
    public static class TaskRowWriter
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>任务表里存需求 id 的列名，幂等查的就是它。</summary>
        private const string RequirementIdentifierColumn = "需求id";

        /// <summary>任务表里存需求文档地址的列名。</summary>
        private const string DocumentLinkColumn = "需求文档";

        /// <summary>任务表里存任务描述的列名。</summary>
        private const string DescriptionColumn = "任务描述";

        /// <summary>
        /// 执行 task-row 动作：查一遍有没有同一条需求的行，没有才加。
        /// </summary>
        /// <param name="request">请求信封：配置含 应用标识 / 飞书应用密钥 / 超时秒 / 多维表格标识 / 任务表标识；
        /// 载荷含 干跑（缺省 true）、需求id、任务描述，可选 需求文档链接。</param>
        public static BridgeResponse AddRow(BridgeRequest request)
        {
            var appId = ReadConfigurationString(request, "应用标识", "");
            var secretKey = ReadConfigurationString(request, "飞书应用密钥", "");
            var appToken = ReadConfigurationString(request, ObjectProvisioner.BitableKey, "");
            var tableId = ReadConfigurationString(request, ObjectProvisioner.TaskTableKey, "");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);

            var requirementIdentifier = ReadPayloadString(request, "需求id");
            var description = ReadPayloadString(request, "任务描述");
            var documentLink = ReadPayloadString(request, "需求文档链接");

            if (appId.Length == 0 || secretKey.Length == 0)
            {
                return Failure("凭据无效", "应用标识或飞书应用密钥未配置", retryable: false);
            }

            if (appToken.Length == 0 || tableId.Length == 0)
            {
                return Failure(
                    "下游不可达",
                    "还不知道任务表在哪（" + ObjectProvisioner.BitableKey + " 或 " + ObjectProvisioner.TaskTableKey
                        + " 为空）——先跑一次 bridge.ensure，它会把表建出来并回填台账",
                    retryable: false);
            }

            if (requirementIdentifier.Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「需求id」：任务行得知道它对应哪条需求", retryable: false);
            }

            var fields = new JsonObject
            {
                [RequirementIdentifierColumn] = requirementIdentifier,
                [DescriptionColumn] = description
            };

            if (documentLink.Length > 0)
            {
                // 超链接列收的是 {link, text} 这个形状，直接塞一个字符串会被判成类型不对。
                fields[DocumentLinkColumn] = new JsonObject
                {
                    ["link"] = documentLink,
                    ["text"] = requirementIdentifier + " 需求文档"
                };
            }

            if (isDryRun)
            {
                return Success(JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["干跑"] = true,
                    ["要加的行"] = fields.DeepClone()
                }));
            }

            // 幂等：先查同一条需求有没有行。查不动时**不许当成「没有」**就往下加——
            // 那会在人点第二次时静默多出一行。
            var filter = "CurrentValue.[" + RequirementIdentifierColumn + "] = \"" + requirementIdentifier + "\"";
            var searchUrl = FeishuClient.BitableUrl(
                appToken,
                "tables/" + Uri.EscapeDataString(tableId) + "/records?page_size=1&filter=" + Uri.EscapeDataString(filter));
            var searchCall = FeishuClient.Send("GET", searchUrl, null, appId, secretKey, timeoutSeconds);
            if (!searchCall.Succeeded)
            {
                return searchCall.Response;
            }

            var existing = ReadFirstRecordIdentifier(searchCall.ResponseBody);
            if (existing.Length > 0)
            {
                return Success(JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["干跑"] = false,
                    ["已存在"] = true,
                    ["记录标识"] = existing
                }));
            }

            var body = new JsonObject { ["fields"] = fields.DeepClone() }.ToJsonString();
            var createCall = FeishuClient.Send(
                "POST",
                FeishuClient.BitableUrl(appToken, "tables/" + Uri.EscapeDataString(tableId) + "/records"),
                body,
                appId,
                secretKey,
                timeoutSeconds);
            if (!createCall.Succeeded)
            {
                return createCall.Response;
            }

            var recordIdentifier = ReadString(createCall.ResponseBody, "data", "record", "record_id");
            if (recordIdentifier.Length == 0)
            {
                return Failure("下游报错", "加任务行的响应里没有 record_id，没法证明真加进去了", retryable: false);
            }

            return Success(JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = false,
                ["已存在"] = false,
                ["记录标识"] = recordIdentifier
            }));
        }

        /// <summary>从查询响应里取第一条记录的 record_id；没有记录给空串。</summary>
        private static string ReadFirstRecordIdentifier(JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            foreach (var item in items.EnumerateArray())
            {
                var identifier = ReadString(item, "record_id");
                if (identifier.Length > 0)
                {
                    return identifier;
                }
            }

            return "";
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

        /// <summary>从响应体里按一串键逐级读字符串；中途缺一级就给空串。</summary>
        private static string ReadString(JsonElement element, params string[] path)
        {
            var current = element;
            foreach (var key in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
                {
                    return "";
                }
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : "";
        }
    }
}
