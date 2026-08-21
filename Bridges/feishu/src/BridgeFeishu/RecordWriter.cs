using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// push 动作：把引擎侧的记录写进「需求」表。
    /// 铁律（任务书红线与决策同源）：
    /// - 表 id 不许写死，每次先 GET tables 按表名找（表名从建表描述的「表名」来）；
    ///   找不到 → 错误码「下游报错」，人话指路 bridge.apply。
    /// - 先查再写：按幂等键列现有记录比对，已存在 → 更新，不存在 → 新建；
    ///   同一个幂等键绝不许建出两条记录（决策 7 同源：协作层账本靠 id 唯一）。
    /// - 单选值必须在选项列表里，不在就报错，不许自动加选项（自动加让下游枚举悄悄漂移，
    ///   而字段所有权归工程，决策 33 同源）。
    /// - 复选框收 bool、文本收 string，类型对不上报错，不许硬转。
    /// - 干跑默认 true：真写别人的工作区，默认不写，要写得显式说。
    /// </summary>
    public static class RecordWriter
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>建表描述文件在工作目录（仓库根）下的相对路径。</summary>
        private const string TableDescriptionRelativePath = "_Generated/Bridges/feishu/table-description.json";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>列记录的单页大小。</summary>
        private const int PageSize = 100;

        /// <summary>幂等键字段的缺省名。</summary>
        private const string DefaultIdempotencyKeyField = "id";

        /// <summary>
        /// 执行 push 动作：干跑返回将写什么（不发任何写请求）；
        /// 真跑按幂等键先查后写，已存在更新、不存在新建。
        /// </summary>
        public static BridgeResponse RunPush(BridgeRequest request)
        {
            var appId = ReadConfigurationString(request, "应用标识", "");
            var appToken = ReadConfigurationString(request, "多维表格标识", "");
            var secretKey = ReadConfigurationString(request, "飞书应用密钥", "");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);
            var idempotencyKeyField = ReadPayloadString(request, "幂等键字段", DefaultIdempotencyKeyField);

            if (appToken.Length == 0)
            {
                return Failure("下游不可达", "多维表格标识未配置（配置键「多维表格标识」为空）", retryable: false);
            }

            if (appId.Length == 0)
            {
                return Failure("凭据无效", "应用标识未配置（配置键「应用标识」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return Failure("凭据无效", "飞书应用密钥未配置（配置键「飞书应用密钥」为空）", retryable: false);
            }

            if (idempotencyKeyField.Length == 0)
            {
                return Failure("请求不合协议", "载荷里的「幂等键字段」为空", retryable: false);
            }

            if (!TryLoadTableDescription(out var tableName, out var fields, out var loadReason))
            {
                return Failure("本机配置错误", loadReason, retryable: false);
            }

            if (!TryReadRecords(request, out var records, out var readReason))
            {
                return Failure("请求不合协议", readReason, retryable: false);
            }

            if (records.Count == 0)
            {
                return Failure("请求不合协议", "载荷「记录」是空数组，没有可写的记录", retryable: false);
            }

            // 先做离线校验：字段名在不在建表描述里、值类型对不对、单选值在不在选项里。
            // 全部校验通过才发第一个请求——先查再写，别写一半发现第三条记录类型错。
            var prepared = new List<PreparedRecord>(records.Count);
            foreach (var record in records)
            {
                if (!TryPrepare(record, idempotencyKeyField, fields, out var preparedRecord, out var prepareReason))
                {
                    return Failure("请求不合协议", prepareReason, retryable: false);
                }

                prepared.Add(preparedRecord);
            }

            // 干跑：查表 id 与现有记录（只读），给出每条会新建还是更新，不发任何写请求。
            var tableIdResult = FindTableId(appId, appToken, secretKey, timeoutSeconds, tableName);
            if (!tableIdResult.Succeeded)
            {
                return tableIdResult.Response;
            }

            var existingById = ListRecordsByKey(appId, appToken, secretKey, timeoutSeconds, tableIdResult.TableId, idempotencyKeyField);
            if (!existingById.Succeeded)
            {
                return existingById.Response;
            }

            if (isDryRun)
            {
                return Success(BuildDryRunPayload(prepared, existingById.ById, idempotencyKeyField));
            }

            return RunWrites(appId, appToken, secretKey, timeoutSeconds, tableIdResult.TableId, idempotencyKeyField, prepared, existingById.ById);
        }

        /// <summary>真跑：按幂等键分派，已存在 → 更新，不存在 → 新建；无字段可写 → 跳过。</summary>
        private static BridgeResponse RunWrites(
            string appId,
            string appToken,
            string secretKey,
            int timeoutSeconds,
            string tableId,
            string idempotencyKeyField,
            IReadOnlyList<PreparedRecord> prepared,
            IReadOnlyDictionary<string, string> existingById)
        {
            var created = new JsonArray();
            var updated = new JsonArray();
            var skipped = new JsonArray();

            foreach (var record in prepared)
            {
                var keyValue = record.KeyValue;
                if (!record.HasNonKeyFields)
                {
                    // 除幂等键外没有任何可写字段：写了也没有意义，跳过并写清原因。
                    skipped.Add(BuildSkipNode(record, "没有可写的字段（除幂等键「" + idempotencyKeyField + "」外字段为空）"));
                    continue;
                }

                var fieldsBody = BuildFieldsBody(record.WriteFields);
                if (existingById.TryGetValue(keyValue, out var existingRecordId))
                {
                    var call = FeishuClient.Send(
                        "PUT",
                        FeishuClient.BitableUrl(appToken, "tables/" + tableId + "/records/" + existingRecordId),
                        fieldsBody,
                        appId,
                        secretKey,
                        timeoutSeconds);
                    if (!call.Succeeded)
                    {
                        return call.Response;
                    }

                    var recordId = ReadNestedString(call.ResponseBody, "data", "record", "record_id");
                    if (recordId.Length == 0)
                    {
                        return Failure("下游报错", "飞书更新记录的响应里没有 record_id", retryable: false);
                    }

                    updated.Add(new JsonObject { ["id"] = keyValue, ["record_id"] = recordId });
                }
                else
                {
                    var call = FeishuClient.Send(
                        "POST",
                        FeishuClient.BitableUrl(appToken, "tables/" + tableId + "/records"),
                        fieldsBody,
                        appId,
                        secretKey,
                        timeoutSeconds);
                    if (!call.Succeeded)
                    {
                        return call.Response;
                    }

                    var recordId = ReadNestedString(call.ResponseBody, "data", "record", "record_id");
                    if (recordId.Length == 0)
                    {
                        return Failure("下游报错", "飞书新建记录的响应里没有 record_id", retryable: false);
                    }

                    created.Add(new JsonObject { ["id"] = keyValue, ["record_id"] = recordId });
                }
            }

            var payload = new JsonObject
            {
                ["新建"] = created,
                ["更新"] = updated,
                ["跳过"] = skipped
            };
            return Success(JsonSerializer.SerializeToElement(payload));
        }

        /// <summary>拼一次更新的请求体：{"fields":{…}}。</summary>
        private static string BuildFieldsBody(IReadOnlyList<KeyValuePair<string, JsonElement>> writeFields)
        {
            var fieldsObject = new JsonObject();
            foreach (var pair in writeFields)
            {
                fieldsObject[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
            }

            return new JsonObject { ["fields"] = fieldsObject }.ToJsonString();
        }

        /// <summary>干跑载荷：每条记录的动作（新建/更新）、幂等键值、要写的字段与值。</summary>
        private static JsonElement BuildDryRunPayload(
            IReadOnlyList<PreparedRecord> prepared,
            IReadOnlyDictionary<string, string> existingById,
            string idempotencyKeyField)
        {
            var plans = new JsonArray();
            foreach (var record in prepared)
            {
                var action = !record.HasNonKeyFields
                    ? "跳过"
                    : (existingById.ContainsKey(record.KeyValue) ? "更新" : "新建");

                var fieldsObject = new JsonObject();
                foreach (var pair in record.WriteFields)
                {
                    fieldsObject[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
                }

                plans.Add(new JsonObject
                {
                    ["幂等键字段"] = idempotencyKeyField,
                    ["id"] = record.KeyValue,
                    ["动作"] = action,
                    ["要写的字段"] = fieldsObject
                });
            }

            return JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = true,
                ["计划"] = plans
            });
        }

        /// <summary>跳过项节点：id + 原因。</summary>
        private static JsonObject BuildSkipNode(PreparedRecord record, string reason)
        {
            return new JsonObject { ["id"] = record.KeyValue, ["原因"] = reason };
        }

        /// <summary>一条已通过离线校验、待写的记录：幂等键值 + 要写的字段（按记录原始顺序）。</summary>
        private sealed class PreparedRecord
        {
            public string KeyValue;
            public List<KeyValuePair<string, JsonElement>> WriteFields;
            public bool HasNonKeyFields;

            public PreparedRecord(string keyValue, List<KeyValuePair<string, JsonElement>> writeFields, bool hasNonKeyFields)
            {
                KeyValue = keyValue;
                WriteFields = writeFields;
                HasNonKeyFields = hasNonKeyFields;
            }
        }

        /// <summary>
        /// 离线校验并准备一条记录：
        /// - 幂等键字段必须存在且为字符串；
        /// - 每个字段都必须在建表描述里（写错字段名不该静默）；
        /// - 值类型按建表描述的「下游类型」校验（文本收 string、复选框收 bool、单选收 string 且值必须在选项里）；
        /// 除幂等键外没有其他字段时不报错，留给跳过判定。
        /// </summary>
        private static bool TryPrepare(
            JsonElement record,
            string idempotencyKeyField,
            IReadOnlyList<FieldSchema> fields,
            out PreparedRecord prepared,
            out string reason)
        {
            prepared = null;
            reason = "";

            if (record.ValueKind != JsonValueKind.Object)
            {
                reason = "「记录」数组里有不是对象的条目";
                return false;
            }

            if (!record.TryGetProperty(idempotencyKeyField, out var keyElement) || keyElement.ValueKind != JsonValueKind.String)
            {
                reason = $"记录里没有字符串字段「{idempotencyKeyField}」（幂等键）";
                return false;
            }

            var keyValue = keyElement.GetString() ?? "";
            if (keyValue.Length == 0)
            {
                reason = $"记录里「{idempotencyKeyField}」的值是空串";
                return false;
            }

            var byName = new Dictionary<string, FieldSchema>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                byName[field.Name] = field;
            }

            var writeFields = new List<KeyValuePair<string, JsonElement>>();
            var hasNonKeyFields = false;
            foreach (var property in record.EnumerateObject())
            {
                if (!byName.TryGetValue(property.Name, out var schema))
                {
                    reason = $"字段「{property.Name}」不在建表描述里（建表描述只有这些字段：{string.Join("、", byName.Keys)}）";
                    return false;
                }

                if (!FeishuRecordFieldMap.TryMapWrite(schema, property.Value, out var feishuValue, out var mapReason))
                {
                    reason = $"字段「{property.Name}」：{mapReason}";
                    return false;
                }

                writeFields.Add(new KeyValuePair<string, JsonElement>(property.Name, feishuValue));
                if (!string.Equals(property.Name, idempotencyKeyField, StringComparison.Ordinal))
                {
                    hasNonKeyFields = true;
                }
            }

            // 幂等键字段本身也要写进表：它是表里的真实业务字段（id 列），
            // 读回来时要靠它做幂等比对——不写的话记录里没有它，重复 push 会查不到、建出第二条（曾实测踩过）。
            prepared = new PreparedRecord(keyValue, writeFields, hasNonKeyFields);
            return true;
        }

        /// <summary>查表 id 的结果：成功带 table_id，失败带协议响应。</summary>
        private sealed class TableIdResult
        {
            public bool Succeeded;
            public BridgeResponse Response;
            public string TableId;
        }

        /// <summary>先 GET tables 按表名找 table_id；找不到 → 下游报错 + 指路 bridge.apply。</summary>
        private static TableIdResult FindTableId(string appId, string appToken, string secretKey, int timeoutSeconds, string tableName)
        {
            var listCall = FeishuClient.Send("GET", FeishuClient.BitableUrl(appToken, "tables"), null, appId, secretKey, timeoutSeconds);
            if (!listCall.Succeeded)
            {
                return new TableIdResult { Succeeded = false, Response = listCall.Response };
            }

            if (TryReadTableIdByName(listCall.ResponseBody, tableName, out var tableId))
            {
                return new TableIdResult { Succeeded = true, TableId = tableId };
            }

            return new TableIdResult
            {
                Succeeded = false,
                Response = Failure("下游报错", $"表「{tableName}」不存在，先跑 bridge.apply 建表", retryable: false)
            };
        }

        /// <summary>列全部现有记录，按幂等键字段值建「键 → record_id」映射（分页处理）。</summary>
        private sealed class ExistingByIdResult
        {
            public bool Succeeded;
            public BridgeResponse Response;
            public Dictionary<string, string> ById;
        }

        private static ExistingByIdResult ListRecordsByKey(
            string appId,
            string appToken,
            string secretKey,
            int timeoutSeconds,
            string tableId,
            string idempotencyKeyField)
        {
            var byId = new Dictionary<string, string>(StringComparer.Ordinal);
            var pageToken = "";
            var firstPage = true;

            while (firstPage || pageToken.Length > 0)
            {
                firstPage = false;
                var url = FeishuClient.BitableUrl(appToken, "tables/" + tableId + "/records?page_size=" + PageSize + "&automatic_fields=true");
                if (pageToken.Length > 0)
                {
                    url += "&page_token=" + Uri.EscapeDataString(pageToken);
                }

                var call = FeishuClient.Send("GET", url, null, appId, secretKey, timeoutSeconds);
                if (!call.Succeeded)
                {
                    return new ExistingByIdResult { Succeeded = false, Response = call.Response };
                }

                foreach (var item in ReadRecordItems(call.ResponseBody))
                {
                    var recordId = ReadString(item, "record_id");
                    var keyValue = RecordReader.ReadFieldAsPlainString(item, idempotencyKeyField);
                    if (recordId.Length > 0 && keyValue.Length > 0 && !byId.ContainsKey(keyValue))
                    {
                        byId[keyValue] = recordId;
                    }
                }

                pageToken = ReadString(call.ResponseBody, "data", "page_token");
                var hasMore = ReadBool(call.ResponseBody, "data", "has_more");
                if (!hasMore)
                {
                    break;
                }
            }

            return new ExistingByIdResult { Succeeded = true, ById = byId };
        }

        /// <summary>读 records 响应里的记录条目数组；total=0 时飞书不返回 items，给空列表。</summary>
        internal static List<JsonElement> ReadRecordItems(JsonElement body)
        {
            var items = new List<JsonElement>();
            if (body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("items", out var array)
                && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    items.Add(item.Clone());
                }
            }

            return items;
        }

        /// <summary>从 GET tables 的响应体里按表名找 table_id；找不到返回 false。</summary>
        private static bool TryReadTableIdByName(JsonElement body, string tableName, out string tableId)
        {
            tableId = "";
            if (body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (string.Equals(ReadString(item, "name"), tableName, StringComparison.Ordinal))
                    {
                        tableId = ReadString(item, "table_id");
                        return tableId.Length > 0;
                    }
                }
            }

            return false;
        }

        /// <summary>读建表描述文件：表名 + 字段元数据（名称/下游类型/单选项）。失败给可读原因。</summary>
        internal static bool TryLoadTableDescription(out string tableName, out List<FieldSchema> fields, out string reason)
        {
            tableName = "";
            fields = new List<FieldSchema>();
            reason = "";

            if (!File.Exists(TableDescriptionRelativePath))
            {
                reason = $"建表描述文件不存在：{TableDescriptionRelativePath}（先跑供给）";
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(TableDescriptionRelativePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                reason = $"建表描述文件不是合法 JSON：{TableDescriptionRelativePath}：{exception.Message}";
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = $"建表描述文件顶层必须是对象：{TableDescriptionRelativePath}";
                    return false;
                }

                tableName = ReadString(root, "表名");
                if (tableName.Length == 0)
                {
                    reason = $"建表描述文件缺「表名」或它不是字符串：{TableDescriptionRelativePath}";
                    return false;
                }

                if (!root.TryGetProperty("字段", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Array)
                {
                    reason = $"建表描述文件缺「字段」或它不是数组：{TableDescriptionRelativePath}";
                    return false;
                }

                foreach (var fieldElement in fieldsElement.EnumerateArray())
                {
                    var name = ReadString(fieldElement, "名称");
                    var downstreamType = ReadString(fieldElement, "下游类型");
                    if (name.Length == 0 || downstreamType.Length == 0)
                    {
                        reason = $"建表描述里有字段缺「名称」或「下游类型」：{TableDescriptionRelativePath}";
                        return false;
                    }

                    fields.Add(new FieldSchema(
                        name,
                        downstreamType,
                        ReadStringArray(fieldElement, "单选项"),
                        ReadString(fieldElement, "逻辑类型")));
                }
            }

            return true;
        }

        /// <summary>从载荷读「记录」数组；缺键、类型不对给可读原因。</summary>
        private static bool TryReadRecords(BridgeRequest request, out List<JsonElement> records, out string reason)
        {
            records = new List<JsonElement>();
            reason = "";
            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty("记录", out var recordsElement)
                || recordsElement.ValueKind != JsonValueKind.Array)
            {
                reason = "载荷缺「记录」或它不是数组";
                return false;
            }

            foreach (var record in recordsElement.EnumerateArray())
            {
                records.Add(record.Clone());
            }

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

        /// <summary>读请求载荷里的字符串键；缺失给缺省值。</summary>
        private static string ReadPayloadString(BridgeRequest request, string key, string fallback)
        {
            if (request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? fallback;
            }

            return fallback;
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

        /// <summary>读 JSON 对象里的布尔键；缺失或类型不对给 false。</summary>
        private static bool ReadBool(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.ValueKind == JsonValueKind.True;
            }

            return false;
        }

        /// <summary>读嵌套路径上的布尔：data.has_more。</summary>
        private static bool ReadBool(JsonElement element, string property1, string property2)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(property1, out var outer)
                && outer.ValueKind == JsonValueKind.Object)
            {
                return ReadBool(outer, property2);
            }

            return false;
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

        /// <summary>读嵌套路径上的字符串：data.record.record_id。</summary>
        private static string ReadNestedString(JsonElement element, string property1, string property2, string property3)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(property1, out var outer1)
                && outer1.ValueKind == JsonValueKind.Object
                && outer1.TryGetProperty(property2, out var outer2)
                && outer2.ValueKind == JsonValueKind.Object
                && outer2.TryGetProperty(property3, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
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
    }

    /// <summary>建表描述里的一个字段：名称、下游类型名与单选项（单选/多选才有）。</summary>
    public sealed class FieldSchema
    {
        /// <summary>
        /// 构造一个字段元数据。
        /// </summary>
        /// <param name="name">字段名，如「标题」。</param>
        /// <param name="downstreamType">下游类型名，如 文本 / 多行文本 / 单选 / 复选框。</param>
        /// <param name="enumValues">单选/多选的选项列表；其余类型给空列表。</param>
        /// <param name="logicalType">这一列在 schema 里原本是什么逻辑类型（如 数组 / 对象）；空串表示与下游类型一致。</param>
        public FieldSchema(string name, string downstreamType, IReadOnlyList<string> enumValues, string logicalType = "")
        {
            Name = name;
            DownstreamType = downstreamType;
            EnumValues = enumValues ?? Array.Empty<string>();
            LogicalType = logicalType ?? "";
        }

        /// <summary>字段名。</summary>
        public string Name { get; }

        /// <summary>下游类型名。</summary>
        public string DownstreamType { get; }

        /// <summary>单选/多选的选项列表；其余类型为空。</summary>
        public IReadOnlyList<string> EnumValues { get; }

        /// <summary>
        /// schema 里的逻辑类型（string / 数组 / 对象…）。下游只有「文本」这一种容器，
        /// 逻辑类型是**唯一**能说清「这串文字原本是不是一个数组」的东西——
        /// 没有它，写进去的数组读回来就永远是一坨字符串，往返闭不上。
        /// </summary>
        public string LogicalType { get; }

        /// <summary>逻辑类型是不是数组。</summary>
        public bool IsLogicalArray()
        {
            return string.Equals(LogicalType, "数组", StringComparison.Ordinal);
        }

        /// <summary>逻辑类型是不是对象。</summary>
        public bool IsLogicalObject()
        {
            return string.Equals(LogicalType, "对象", StringComparison.Ordinal);
        }

        /// <summary>该字段是否是单选（单选值必须在校验过的选项列表里）。</summary>
        public bool IsSingleSelect()
        {
            return string.Equals(DownstreamType, "单选", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 字段值 ↔ 飞书记录字段值的双向映射（纯函数，脱离网络可测）。
    /// 写方向（逻辑值 → 飞书写入值）：文本收 string、复选框收 bool、单选收 string 且必须
    /// 在选项列表里（不在就报错，不许自动加选项）；类型对不上报错，不许硬转。
    /// 读方向（飞书读回值 → 逻辑值）：文本可能返回 string 或 [{"text":…}] 数组，归一化成
    /// string；单选返回选项名字符串；复选框返回 bool。归一化后 push 写进去的值与 pull 读回的
    /// 值逐字段一致，往返闭环靠的就是这层归一化。
    /// </summary>
    public static class FeishuRecordFieldMap
    {
        /// <summary>写方向：逻辑值 → 飞书写入值。</summary>
        public static bool TryMapWrite(FieldSchema schema, JsonElement logicalValue, out JsonElement feishuValue, out string reason)
        {
            feishuValue = default;
            reason = "";

            if (string.Equals(schema.DownstreamType, "复选框", StringComparison.Ordinal))
            {
                if (logicalValue.ValueKind != JsonValueKind.True && logicalValue.ValueKind != JsonValueKind.False)
                {
                    reason = $"复选框字段收布尔值，给的是 {DescribeKind(logicalValue)}，不许硬转";
                    return false;
                }

                feishuValue = logicalValue.Clone();
                return true;
            }

            if (string.Equals(schema.DownstreamType, "单选", StringComparison.Ordinal))
            {
                if (logicalValue.ValueKind != JsonValueKind.String)
                {
                    reason = $"单选字段收字符串，给的是 {DescribeKind(logicalValue)}，不许硬转";
                    return false;
                }

                var text = logicalValue.GetString() ?? "";
                if (text.Length == 0)
                {
                    reason = "单选字段的值是空串";
                    return false;
                }

                foreach (var option in schema.EnumValues)
                {
                    if (string.Equals(option, text, StringComparison.Ordinal))
                    {
                        feishuValue = logicalValue.Clone();
                        return true;
                    }
                }

                reason = $"单选值「{text}」不在选项列表里（选项：{string.Join("、", schema.EnumValues)}），不许自动加选项";
                return false;
            }

            // 文本 / 多行文本 等其余类型：收 string。
            // 例外是**声明过逻辑类型**的那些：schema 说这一列是数组或对象，
            // 而下游只有文本一种容器，所以按约定序列化——数组一行一条、对象一份 JSON。
            // 这不是「硬转」：硬转是拿一个没人定义过的规则去猜，这里规则写在建表描述里，
            // 读方向按同一条规则切回来，往返是闭的（TryMapRead 对称处理）。
            if (schema.IsLogicalArray() && logicalValue.ValueKind == JsonValueKind.Array)
            {
                if (!TryJoinArray(logicalValue, out var joined, out var arrayReason))
                {
                    reason = arrayReason;
                    return false;
                }

                feishuValue = JsonSerializer.SerializeToElement(joined);
                return true;
            }

            if (schema.IsLogicalObject() && logicalValue.ValueKind == JsonValueKind.Object)
            {
                feishuValue = JsonSerializer.SerializeToElement(logicalValue.GetRawText());
                return true;
            }

            if (logicalValue.ValueKind != JsonValueKind.String)
            {
                reason = $"文本字段收字符串，给的是 {DescribeKind(logicalValue)}，不许硬转";
                return false;
            }

            feishuValue = logicalValue.Clone();
            return true;
        }

        /// <summary>
        /// 数组 → 多行文本：元素必须全是字符串，一行一条。
        /// 元素里有非字符串就报错——那种东西拼出来的文本切不回原样，往返就闭不上了。
        /// 元素自己带换行也报错，同样的理由。
        /// </summary>
        private static bool TryJoinArray(JsonElement array, out string joined, out string reason)
        {
            joined = "";
            reason = "";
            var items = new List<string>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    reason = $"数组字段的元素必须都是字符串，遇到 {DescribeKind(item)}";
                    return false;
                }

                var text = item.GetString() ?? "";
                if (text.Contains('\n') || text.Contains('\r'))
                {
                    reason = "数组字段的元素里不许带换行——一行一条是切回数组的唯一依据";
                    return false;
                }

                items.Add(text);
            }

            joined = string.Join("\n", items);
            return true;
        }

        /// <summary>读方向：飞书读回值 → 逻辑值。</summary>
        public static bool TryMapRead(FieldSchema schema, JsonElement feishuValue, out JsonElement logicalValue, out string reason)
        {
            logicalValue = default;
            reason = "";

            if (string.Equals(schema.DownstreamType, "复选框", StringComparison.Ordinal))
            {
                if (feishuValue.ValueKind != JsonValueKind.True && feishuValue.ValueKind != JsonValueKind.False)
                {
                    reason = $"复选框字段读回的不是布尔值，是 {DescribeKind(feishuValue)}";
                    return false;
                }

                logicalValue = feishuValue.Clone();
                return true;
            }

            if (string.Equals(schema.DownstreamType, "单选", StringComparison.Ordinal))
            {
                if (feishuValue.ValueKind != JsonValueKind.String)
                {
                    reason = $"单选字段读回的不是字符串，是 {DescribeKind(feishuValue)}";
                    return false;
                }

                logicalValue = feishuValue.Clone();
                return true;
            }

            // 文本 / 多行文本：飞书可能返回字符串，也可能返回 [{"text":…}] 富文本数组，归一化成字符串。
            // 归一化之后，如果 schema 声明这一列逻辑上是数组或对象，再按写方向的同一条规则切回去。
            if (feishuValue.ValueKind == JsonValueKind.String)
            {
                return TryRestoreLogicalShape(schema, feishuValue.GetString() ?? "", out logicalValue, out reason);
            }

            if (feishuValue.ValueKind == JsonValueKind.Array)
            {
                var text = ExtractPlainTextFromArray(feishuValue);
                if (text != null)
                {
                    return TryRestoreLogicalShape(schema, text, out logicalValue, out reason);
                }

                reason = $"文本字段读回的是数组但提不出纯文本：{feishuValue.GetRawText()}";
                return false;
            }

            reason = $"文本字段读回的不是字符串也不是数组，是 {DescribeKind(feishuValue)}";
            return false;
        }

        /// <summary>
        /// 按逻辑类型把读回来的文本切回原来的形状：数组按行切、对象按 JSON 解析、其余原样。
        /// 对象那一支解析失败就报错，**不许把一段 JSON 文本当成普通字符串塞回去**——
        /// 那样池子里会多出一个类型不对的字段，而校验器要到很后面才炸。
        /// </summary>
        private static bool TryRestoreLogicalShape(FieldSchema schema, string text, out JsonElement logicalValue, out string reason)
        {
            reason = "";
            if (schema.IsLogicalArray())
            {
                var items = new List<string>();
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                {
                    if (line.Trim().Length > 0)
                    {
                        items.Add(line);
                    }
                }

                logicalValue = JsonSerializer.SerializeToElement(items);
                return true;
            }

            if (schema.IsLogicalObject())
            {
                if (text.Trim().Length == 0)
                {
                    logicalValue = JsonSerializer.SerializeToElement(new Dictionary<string, string>());
                    return true;
                }

                try
                {
                    using var document = JsonDocument.Parse(text);
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        logicalValue = default;
                        reason = $"字段「{schema.Name}」声明是对象，但读回来的文本解析出来不是对象";
                        return false;
                    }

                    logicalValue = document.RootElement.Clone();
                    return true;
                }
                catch (JsonException exception)
                {
                    logicalValue = default;
                    reason = $"字段「{schema.Name}」声明是对象，但读回来的文本不是合法 JSON：{exception.Message}";
                    return false;
                }
            }

            logicalValue = JsonSerializer.SerializeToElement(text);
            return true;
        }

        /// <summary>从 [{"text":…}] 形状的数组提取纯文本；提不出给 null。</summary>
        private static string ExtractPlainTextFromArray(JsonElement array)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("text", out var text)
                    || text.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                builder.Append(text.GetString());
            }

            return builder.ToString();
        }

        /// <summary>描述 JSON 值的类型，用于报错文案。</summary>
        private static string DescribeKind(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return "字符串";
                case JsonValueKind.Number:
                    return "数字";
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return "布尔值";
                case JsonValueKind.Array:
                    return "数组";
                case JsonValueKind.Object:
                    return "对象";
                case JsonValueKind.Null:
                    return "null";
                default:
                    return element.ValueKind.ToString();
            }
        }
    }
}
