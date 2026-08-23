using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 任务表的进度同步两个动作：
    /// - <c>task-rows</c>：把任务表整表读回来，一行一条，值一律归一成字符串；
    /// - <c>task-row-set</c>：按需求 id 找到行，改**指定的那几列**。
    ///
    /// 与 <see cref="TaskRowWriter"/> 分开写而不是加参数：那一个只做「建行」，
    /// 而建行是**一次性**的（幂等靠查重），改行是**每轮都做**的。
    /// 合在一处的话，「这次是要建还是要改」会变成一个隐藏在载荷里的开关，
    /// 而按错开关的后果是每轮同步都往任务表里多加一行。
    ///
    /// **只改文本列**。归工程侧的那几格（引擎阶段/引擎门禁/引擎产出/任务描述）全是文本；
    /// 人员、日期、单选这些列归策划端，引擎一格都不该写。碰上非文本列时逐条报出来，
    /// 不猜、不转换——猜错一次就把 PM 填的东西改掉了，而那种改动没有撤回。
    /// </summary>
    public static class TaskRowSyncer
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>列记录的单页大小。</summary>
        private const int PageSize = 100;

        /// <summary>任务表里存需求 id 的列名。</summary>
        private const string RequirementIdentifierColumn = "需求id";

        /// <summary>
        /// 执行 task-rows 动作：整表读回来。任务表的量级是「一条需求一行」，
        /// 分页拉全量即可——这里**不带水位**：进度同步要的是「下游此刻长什么样」，
        /// 按水位只拉增量的话，没被改过的那些行读不回来，比对时会全部变成「下游是空的」。
        /// </summary>
        /// <param name="request">请求信封：配置含 应用标识 / 飞书应用密钥 / 超时秒 / 多维表格标识 / 任务表标识。</param>
        public static BridgeResponse ReadRows(BridgeRequest request)
        {
            if (!TryReadCredentials(request, out var context, out var failure))
            {
                return failure;
            }

            var rows = new JsonArray();
            var pageToken = "";
            var firstPage = true;
            while (firstPage || pageToken.Length > 0)
            {
                firstPage = false;
                var url = FeishuClient.BitableUrl(
                    context.AppToken,
                    "tables/" + Uri.EscapeDataString(context.TableId) + "/records?page_size=" + PageSize);
                if (pageToken.Length > 0)
                {
                    url += "&page_token=" + Uri.EscapeDataString(pageToken);
                }

                var call = FeishuClient.Send("GET", url, null, context.AppId, context.SecretKey, context.TimeoutSeconds);
                if (!call.Succeeded)
                {
                    return call.Response;
                }

                foreach (var item in RecordWriter.ReadRecordItems(call.ResponseBody))
                {
                    var fields = new JsonObject();
                    if (item.TryGetProperty("fields", out var fieldElement) && fieldElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in fieldElement.EnumerateObject())
                        {
                            fields[property.Name] = Flatten(property.Value);
                        }
                    }

                    rows.Add(new JsonObject
                    {
                        ["记录标识"] = ReadString(item, "record_id"),
                        ["字段"] = fields
                    });
                }

                pageToken = ReadString(call.ResponseBody, "data", "page_token");
                if (!ReadBool(call.ResponseBody, "data", "has_more"))
                {
                    break;
                }
            }

            return Success(JsonSerializer.SerializeToElement(new JsonObject { ["行"] = rows }));
        }

        /// <summary>
        /// 执行 task-row-set 动作：按需求 id 找行、改指定列。
        /// 找不到行的**不建**——建行是 task-row 的事，这里静默补建会让两条链都以为自己在管建行。
        /// </summary>
        /// <param name="request">请求信封：载荷含 干跑（缺省 true）、更新（数组：需求id + 字段）。</param>
        public static BridgeResponse SetRows(BridgeRequest request)
        {
            if (!TryReadCredentials(request, out var context, out var failure))
            {
                return failure;
            }

            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);
            if (!TryReadUpdates(request, out var updates, out var updateFailure))
            {
                return updateFailure;
            }

            if (isDryRun)
            {
                var planned = new JsonArray();
                foreach (var update in updates)
                {
                    planned.Add(new JsonObject
                    {
                        ["需求id"] = update.Identifier,
                        ["字段"] = update.Fields.DeepClone()
                    });
                }

                return Success(JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["干跑"] = true,
                    ["要改的行"] = planned
                }));
            }

            // 列类型先查一遍：不是文本列就不动它。查不动时**整次失败**而不是跳过检查——
            // 跳过检查等于「不知道这一列是什么就往里写」，那正是会把人填的东西改掉的那条路。
            if (!TryReadTextColumns(context, out var textColumns, out var columnFailure))
            {
                return columnFailure;
            }

            var updated = new JsonArray();
            var skipped = new JsonArray();
            foreach (var update in updates)
            {
                var recordIdentifier = update.RecordIdentifier;
                if (recordIdentifier.Length == 0)
                {
                    recordIdentifier = FindRecordIdentifier(context, update.Identifier, out var lookupFailure);
                    if (lookupFailure != null)
                    {
                        return lookupFailure;
                    }
                }

                if (recordIdentifier.Length == 0)
                {
                    skipped.Add(update.Identifier + "：任务表里没有这一行（建行走 task-row）");
                    continue;
                }

                var writable = new JsonObject();
                foreach (var pair in update.Fields)
                {
                    if (!textColumns.Contains(pair.Key))
                    {
                        skipped.Add(update.Identifier + " 的「" + pair.Key + "」：不是文本列，引擎不写它");
                        continue;
                    }

                    writable[pair.Key] = pair.Value?.DeepClone();
                }

                if (writable.Count == 0)
                {
                    continue;
                }

                var body = new JsonObject { ["fields"] = writable }.ToJsonString();
                var call = FeishuClient.Send(
                    "PUT",
                    FeishuClient.BitableUrl(
                        context.AppToken,
                        "tables/" + Uri.EscapeDataString(context.TableId) + "/records/" + Uri.EscapeDataString(recordIdentifier)),
                    body,
                    context.AppId,
                    context.SecretKey,
                    context.TimeoutSeconds);
                if (!call.Succeeded)
                {
                    return call.Response;
                }

                updated.Add(update.Identifier);
            }

            return Success(JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = false,
                ["改了的行"] = updated,
                ["没改的"] = skipped
            }));
        }

        /// <summary>一条更新：改哪条需求的哪几列。</summary>
        private sealed class RowUpdate
        {
            /// <summary>需求 id。</summary>
            public string Identifier { get; set; } = "";

            /// <summary>记录标识；调用方给了就省一次查询。</summary>
            public string RecordIdentifier { get; set; } = "";

            /// <summary>要改的列。</summary>
            public JsonObject Fields { get; set; } = new JsonObject();
        }

        /// <summary>凭据与目标表。</summary>
        private sealed class SyncContext
        {
            /// <summary>应用标识。</summary>
            public string AppId { get; set; } = "";

            /// <summary>应用密钥。</summary>
            public string SecretKey { get; set; } = "";

            /// <summary>多维表格标识。</summary>
            public string AppToken { get; set; } = "";

            /// <summary>任务表标识。</summary>
            public string TableId { get; set; } = "";

            /// <summary>超时秒数。</summary>
            public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
        }

        /// <summary>读凭据与目标表；缺一样就带人话失败。</summary>
        private static bool TryReadCredentials(BridgeRequest request, out SyncContext context, out BridgeResponse failure)
        {
            context = new SyncContext
            {
                AppId = ReadConfigurationString(request, "应用标识", ""),
                SecretKey = ReadConfigurationString(request, "飞书应用密钥", ""),
                AppToken = ReadConfigurationString(request, ObjectProvisioner.BitableKey, ""),
                TableId = ReadConfigurationString(request, ObjectProvisioner.TaskTableKey, ""),
                TimeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds)
            };

            if (context.AppId.Length == 0 || context.SecretKey.Length == 0)
            {
                failure = Failure("凭据无效", "应用标识或飞书应用密钥未配置", retryable: false);
                return false;
            }

            if (context.AppToken.Length == 0 || context.TableId.Length == 0)
            {
                failure = Failure(
                    "下游不可达",
                    "还不知道任务表在哪（" + ObjectProvisioner.BitableKey + " 或 " + ObjectProvisioner.TaskTableKey
                        + " 为空）——先跑一次 bridge.ensure",
                    retryable: false);
                return false;
            }

            failure = null;
            return true;
        }

        /// <summary>解析载荷里的「更新」数组。</summary>
        private static bool TryReadUpdates(BridgeRequest request, out List<RowUpdate> updates, out BridgeResponse failure)
        {
            updates = new List<RowUpdate>();
            failure = null;

            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty("更新", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                failure = Failure("请求不合协议", "载荷缺「更新」数组", retryable: false);
                return false;
            }

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var identifier = ReadString(item, "需求id");
                if (identifier.Length == 0)
                {
                    failure = Failure("请求不合协议", "「更新」里有一项缺「需求id」", retryable: false);
                    return false;
                }

                var fields = new JsonObject();
                if (item.TryGetProperty("字段", out var fieldElement) && fieldElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in fieldElement.EnumerateObject())
                    {
                        fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() ?? ""
                            : property.Value.ToString();
                    }
                }

                updates.Add(new RowUpdate
                {
                    Identifier = identifier,
                    RecordIdentifier = ReadString(item, "记录标识"),
                    Fields = fields
                });
            }

            return true;
        }

        /// <summary>读任务表里哪几列是文本列。</summary>
        private static bool TryReadTextColumns(SyncContext context, out HashSet<string> textColumns, out BridgeResponse failure)
        {
            textColumns = new HashSet<string>(StringComparer.Ordinal);
            var call = FeishuClient.Send(
                "GET",
                FeishuClient.BitableUrl(context.AppToken, "tables/" + Uri.EscapeDataString(context.TableId) + "/fields?page_size=" + PageSize),
                null,
                context.AppId,
                context.SecretKey,
                context.TimeoutSeconds);
            if (!call.Succeeded)
            {
                failure = call.Response;
                return false;
            }

            if (call.ResponseBody.ValueKind == JsonValueKind.Object
                && call.ResponseBody.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var type)
                        && type.ValueKind == JsonValueKind.Number
                        && type.GetInt32() == FeishuFieldTypeCodec.TextCode)
                    {
                        textColumns.Add(ReadString(item, "field_name"));
                    }
                }
            }

            failure = null;
            return true;
        }

        /// <summary>按需求 id 找记录标识；没有给空串。查询本身失败时把失败响应交出去。</summary>
        private static string FindRecordIdentifier(SyncContext context, string identifier, out BridgeResponse failure)
        {
            failure = null;
            var filter = "CurrentValue.[" + RequirementIdentifierColumn + "] = \"" + identifier + "\"";
            var call = FeishuClient.Send(
                "GET",
                FeishuClient.BitableUrl(
                    context.AppToken,
                    "tables/" + Uri.EscapeDataString(context.TableId) + "/records?page_size=1&filter=" + Uri.EscapeDataString(filter)),
                null,
                context.AppId,
                context.SecretKey,
                context.TimeoutSeconds);
            if (!call.Succeeded)
            {
                failure = call.Response;
                return "";
            }

            foreach (var item in RecordWriter.ReadRecordItems(call.ResponseBody))
            {
                var recordIdentifier = ReadString(item, "record_id");
                if (recordIdentifier.Length > 0)
                {
                    return recordIdentifier;
                }
            }

            return "";
        }

        /// <summary>
        /// 把一格飞书返回值压成字符串。
        /// 归一到字符串是有意的：进度比对只问「这一格变没变」，
        /// 而两侧的原生类型永远对不齐（工程侧一律是字符串），
        /// 不压平的话每一轮都会把「人员对象 vs 人名字符串」判成变化。
        /// </summary>
        private static JsonNode Flatten(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString() ?? "";
                case JsonValueKind.Number:
                    return FlattenNumber(element);
                case JsonValueKind.True:
                    return "是";
                case JsonValueKind.False:
                    return "否";
                case JsonValueKind.Array:
                    var parts = new List<string>();
                    foreach (var item in element.EnumerateArray())
                    {
                        var flattened = Flatten(item)?.ToString() ?? "";
                        if (flattened.Length > 0)
                        {
                            parts.Add(flattened);
                        }
                    }

                    return string.Join("、", parts);
                case JsonValueKind.Object:
                    foreach (var key in new[] { "text", "name", "link" })
                    {
                        if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                        {
                            return value.GetString() ?? "";
                        }
                    }

                    return element.ToString();
                default:
                    return "";
            }
        }

        /// <summary>
        /// 数字那一格：日期列返回的是毫秒时间戳，压成 yyyy-MM-dd。
        /// 判据是「大得不像别的东西」——大于 10^11（约 1973 年）才当时间戳。
        /// 这条判据不完美，但比按列名猜稳：列名是人取的，会改。
        /// </summary>
        private static JsonNode FlattenNumber(JsonElement element)
        {
            if (element.TryGetInt64(out var number))
            {
                if (number > 100000000000L)
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(number).ToLocalTime()
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }

                return number.ToString(CultureInfo.InvariantCulture);
            }

            return element.GetDouble().ToString(CultureInfo.InvariantCulture);
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
            return request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        /// <summary>读请求配置里的整数键；缺失给缺省值。</summary>
        private static int ReadConfigurationInt(BridgeRequest request, string key, int fallback)
        {
            return request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number)
                ? number
                : fallback;
        }

        /// <summary>读载荷里的布尔键；缺失给缺省值。</summary>
        private static bool ReadPayloadBool(BridgeRequest request, string key, bool defaultValue)
        {
            if (request.Payload.ValueKind != JsonValueKind.Object || !request.Payload.TryGetProperty(key, out var value))
            {
                return defaultValue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => defaultValue
            };
        }

        /// <summary>读一层字符串属性；缺失给空串。</summary>
        private static string ReadString(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        /// <summary>读两层字符串属性；缺失给空串。</summary>
        private static string ReadString(JsonElement element, string first, string second)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(first, out var nested)
                ? ReadString(nested, second)
                : "";
        }

        /// <summary>读两层布尔属性；缺失给 false。</summary>
        private static bool ReadBool(JsonElement element, string first, string second)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(first, out var nested)
                && nested.ValueKind == JsonValueKind.Object
                && nested.TryGetProperty(second, out var value)
                && value.ValueKind == JsonValueKind.True;
        }
    }
}
