using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// pull 动作：把「需求」表里的记录读成入站信封（InboxEnvelope 格式）落盘。
    /// 铁律：
    /// - 表 id 不许写死，每次先 GET tables 按表名找（表名从建表描述的「表名」来）；
    ///   找不到 → 错误码「下游报错」，人话指路 bridge.apply。
    /// - 只读：一个写请求都不许发，全程只有 GET。
    /// - 水位（决策 65）：水位为空 = 全量拉；非空只拉最后修改时间 &gt; 水位的记录。
    /// - 文件名幂等：文件名含记录 id 与修订号（修订取自最后修改时间），
    ///   同一条记录未修改时重复拉产出同名文件，不重复落盘。
    /// - 字段值归一化：文本/多行文本可能读回 [{"text":…}] 数组，归一化成字符串；
    ///   单选读回选项名字符串；复选框读回布尔。归一化后与 push 写进去的值逐字段一致。
    /// </summary>
    public static class RecordReader
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>列记录的单页大小。</summary>
        private const int PageSize = 100;

        /// <summary>入站信封的渠道名：取自下游 driver 的名称（driver.json 的「名称」= feishu）。</summary>
        private const string ChannelName = "feishu";

        /// <summary>水位过滤的缺省起点：空串 = 全量拉（决策 65）。</summary>
        private const string EmptyWatermark = "";

        /// <summary>
        /// 执行 pull 动作：干跑返回将拉取的范围（不发任何请求之外的读操作）；
        /// 真跑列记录（分页）、按水位过滤、转信封落盘。
        /// </summary>
        public static BridgeResponse RunPull(BridgeRequest request)
        {
            var appId = ReadConfigurationString(request, "应用标识", "");
            var appToken = ReadConfigurationString(request, "多维表格标识", "");
            var secretKey = ReadConfigurationString(request, "飞书应用密钥", "");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);
            var watermark = ReadPayloadString(request, "水位", EmptyWatermark);
            var outputDirectory = ReadPayloadString(request, "输出目录", "");

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

            if (outputDirectory.Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「输出目录」或它是空串", retryable: false);
            }

            if (!RecordWriter.TryLoadTableDescription(out var tableName, out var fields, out var loadReason))
            {
                return Failure("本机配置错误", loadReason, retryable: false);
            }

            if (!TryParseWatermark(watermark, out var watermarkMoment, out var watermarkReason))
            {
                return Failure("请求不合协议", watermarkReason, retryable: false);
            }

            // 先 GET tables 按表名找 id（表 id 每次查，不许写死）。
            var listCall = FeishuClient.Send("GET", FeishuClient.BitableUrl(appToken, "tables"), null, appId, secretKey, timeoutSeconds);
            if (!listCall.Succeeded)
            {
                return listCall.Response;
            }

            if (!TryReadTableIdByName(listCall.ResponseBody, tableName, out var tableId))
            {
                return Failure("下游报错", $"表「{tableName}」不存在，先跑 bridge.apply 建表", retryable: false);
            }

            // 列全部记录（分页），按水位过滤。
            var recordsResult = ListRecords(appId, appToken, secretKey, timeoutSeconds, tableId, watermarkMoment);
            if (!recordsResult.Succeeded)
            {
                return recordsResult.Response;
            }

            var schemaByName = new Dictionary<string, FieldSchema>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                schemaByName[field.Name] = field;
            }

            if (isDryRun)
            {
                return Success(BuildDryRunPayload(recordsResult.Records.Count, watermark, tableName, schemaByName));
            }

            return WriteEnvelopes(recordsResult.Records, schemaByName, outputDirectory, watermark);
        }

        /// <summary>真跑：每条记录转信封落盘，返回拉到数、落盘路径与新水位。</summary>
        private static BridgeResponse WriteEnvelopes(
            IReadOnlyList<JsonElement> records,
            IReadOnlyDictionary<string, FieldSchema> schemaByName,
            string outputDirectory,
            string inputWatermark)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return Failure("本机配置错误", $"输出目录创建不了：{outputDirectory}：{exception.Message}", retryable: false);
            }

            var landedPaths = new List<string>();
            var maxMoment = inputWatermark.Length > 0
                ? DateTimeOffset.Parse(inputWatermark, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : DateTimeOffset.MinValue;
            var skippedMismatch = new List<string>();

            foreach (var record in records)
            {
                if (!TryBuildEnvelope(record, schemaByName, out var envelope, out var reason))
                {
                    // 单条记录转不了信封（缺 id 字段、字段值读不成）：跳过并记下来，
                    // 不整体失败——飞书表里可能有非本管线写的、不满足信封要求的记录。
                    skippedMismatch.Add($"record_id={ReadString(record, "record_id")}：{reason}");
                    continue;
                }

                var fileName = envelope.FileName;
                var filePath = Path.Combine(outputDirectory, fileName);
                try
                {
                    File.WriteAllText(filePath, envelope.ToJson(), new UTF8Encoding(false));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
                {
                    return Failure("本机配置错误", $"信封写不了：{filePath}：{exception.Message}", retryable: false);
                }

                landedPaths.Add(filePath);
                if (envelope.LastModifiedMoment > maxMoment)
                {
                    maxMoment = envelope.LastModifiedMoment;
                }
            }

            var payload = new JsonObject
            {
                ["拉到"] = landedPaths.Count,
                ["落盘"] = ToJsonArray(landedPaths),
                ["新水位"] = maxMoment == DateTimeOffset.MinValue ? "" : maxMoment.ToString("o", CultureInfo.InvariantCulture)
            };
            if (skippedMismatch.Count > 0)
            {
                payload["跳过的"] = ToJsonArray(skippedMismatch);
            }

            return Success(JsonSerializer.SerializeToElement(payload));
        }

        /// <summary>干跑载荷：将拉取的范围（表名、水位、命中条数），不发任何写请求。</summary>
        private static JsonElement BuildDryRunPayload(int hitCount, string watermark, string tableName, IReadOnlyDictionary<string, FieldSchema> schemaByName)
        {
            var fieldNames = new List<string>(schemaByName.Keys);
            fieldNames.Sort(StringComparer.Ordinal);
            return JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = true,
                ["表名"] = tableName,
                ["水位"] = watermark.Length == 0 ? "（空 = 全量拉）" : watermark,
                ["将拉到"] = hitCount,
                ["表字段"] = ToJsonArray(fieldNames)
            });
        }

        /// <summary>一条记录的入站信封：JSON 文本 + 落盘文件名 + 最后修改时刻（算新水位用）。</summary>
        public sealed class EnvelopeFile
        {
            public string FileName;
            public string Json;
            public DateTimeOffset LastModifiedMoment;
            public string RecordIdentifier;

            public string ToJson()
            {
                return Json;
            }
        }

        /// <summary>
        /// 把一条飞书记录转成 InboxEnvelope 格式的 JSON（纯函数，脱离网络可测）。
        /// 映射（与 InboxEnvelope.TryRead 的键一一对应）：
        /// 渠道 = feishu；记录id = 「id」字段值；修订 = 最后修改时间（秒级，非负整数）；
        /// 提交人 = 空串（飞书没有提交人字段）；提交时间 = 最后修改时间 ISO 串；
        /// 关联需求 = null；字段 = 全部业务字段归一化后的值。
        /// 文件名 = {记录id}.r{修订}.json——同一条记录未修改时重复拉文件名相同（幂等）。
        /// </summary>
        public static bool TryBuildEnvelope(
            JsonElement record,
            IReadOnlyDictionary<string, FieldSchema> schemaByName,
            out EnvelopeFile envelope,
            out string reason)
        {
            envelope = null;
            reason = "";

            var recordIdentifier = ReadFieldAsPlainString(record, "id");
            if (recordIdentifier.Length == 0)
            {
                reason = "记录没有字符串字段「id」（幂等键）";
                return false;
            }

            if (!TryReadLastModifiedMoment(record, out var lastModifiedMoment, out var momentReason))
            {
                reason = momentReason;
                return false;
            }

            var revision = ToRevisionInt(lastModifiedMoment);

            var fieldsObject = new JsonObject();
            foreach (var property in EnumerateFields(record))
            {
                if (!schemaByName.TryGetValue(property.Name, out var schema))
                {
                    // 表里存在但建表描述里没有的字段：跳过，不写进信封（非本管线的扩展列）。
                    continue;
                }

                if (!FeishuRecordFieldMap.TryMapRead(schema, property.Value, out var logicalValue, out var mapReason))
                {
                    reason = $"字段「{property.Name}」读不成：{mapReason}";
                    return false;
                }

                fieldsObject[property.Name] = JsonNode.Parse(logicalValue.GetRawText());
            }

            var envelopeObject = new JsonObject
            {
                ["渠道"] = ChannelName,
                ["记录id"] = recordIdentifier,
                ["修订"] = revision,
                ["提交人"] = ReadSubmitterName(record),
                ["提交时间"] = lastModifiedMoment.ToString("o", CultureInfo.InvariantCulture),
                ["关联需求"] = null,
                ["字段"] = fieldsObject
            };

            var fileName = SanitizeFileName(recordIdentifier) + ".r" + revision + ".json";
            envelope = new EnvelopeFile
            {
                FileName = fileName,
                Json = envelopeObject.ToJsonString(WriteOptions),
                LastModifiedMoment = lastModifiedMoment,
                RecordIdentifier = recordIdentifier
            };
            return true;
        }

        /// <summary>把 ISO 时刻推导成修订号：秒级时间戳（2026 年约 17 亿，int 内到 2038 年）。</summary>
        public static int ToRevisionInt(DateTimeOffset moment)
        {
            var seconds = moment.ToUnixTimeSeconds();
            if (seconds > int.MaxValue)
            {
                // 2038 年以后才可能撞到；真撞到时取低 31 位仍确定性、非负。
                return (int)(seconds & 0x7FFFFFFF);
            }

            return (int)seconds;
        }

        /// <summary>把可能含路径分隔符的记录 id 清成合法文件名（只替换非法字符，保持确定性）。</summary>
        public static string SanitizeFileName(string recordIdentifier)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(recordIdentifier.Length);
            foreach (var ch in recordIdentifier)
            {
                builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }

            var sanitized = builder.ToString();
            return sanitized.Length == 0 ? "record" : sanitized;
        }

        /// <summary>从记录的 last_modified_by.name 读提交人；读不到给空串。</summary>
        private static string ReadSubmitterName(JsonElement record)
        {
            if (record.ValueKind == JsonValueKind.Object
                && record.TryGetProperty("last_modified_by", out var modifier)
                && modifier.ValueKind == JsonValueKind.Object)
            {
                return ReadString(modifier, "name");
            }

            return "";
        }

        /// <summary>从记录的 fields 里读某字段的纯文本（文本字段可能读回数组，归一化后比较）。</summary>
        public static string ReadFieldAsPlainString(JsonElement record, string fieldName)
        {
            foreach (var property in EnumerateFields(record))
            {
                if (string.Equals(property.Name, fieldName, StringComparison.Ordinal))
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString() ?? "";
                    }

                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        var builder = new StringBuilder();
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.Object
                                && item.TryGetProperty("text", out var text)
                                && text.ValueKind == JsonValueKind.String)
                            {
                                builder.Append(text.GetString());
                            }
                        }

                        return builder.ToString();
                    }
                }
            }

            return "";
        }

        /// <summary>读记录里字段集合的键值对；飞书把业务字段放在 record.fields 对象里。</summary>
        public static IEnumerable<JsonProperty> EnumerateFields(JsonElement record)
        {
            if (record.ValueKind == JsonValueKind.Object
                && record.TryGetProperty("fields", out var fields)
                && fields.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in fields.EnumerateObject())
                {
                    yield return property;
                }
            }
        }

        /// <summary>读记录的最后修改时间：兼容毫秒时间戳（number）与 ISO 字符串两种飞书可能给的形状。</summary>
        private static bool TryReadLastModifiedMoment(JsonElement record, out DateTimeOffset moment, out string reason)
        {
            moment = default;
            reason = "";
            if (record.ValueKind != JsonValueKind.Object
                || !record.TryGetProperty("last_modified_time", out var value))
            {
                reason = "记录里没有 last_modified_time";
                return false;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    var milliseconds = value.GetInt64();
                    moment = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
                    return true;
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                    reason = $"last_modified_time 不是合法的毫秒时间戳：{value.GetRawText()}";
                    return false;
                }
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString() ?? "";
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out moment))
                {
                    return true;
                }

                reason = $"last_modified_time 不是合法的 ISO 时间：{text}";
                return false;
            }

            reason = $"last_modified_time 不是数字也不是字符串，是 {value.ValueKind}";
            return false;
        }

        /// <summary>解析水位串：空串 = 全量拉（合法）；非空必须是能解析的 ISO 时刻。</summary>
        private static bool TryParseWatermark(string watermark, out DateTimeOffset moment, out string reason)
        {
            moment = DateTimeOffset.MinValue;
            reason = "";
            if (watermark.Length == 0)
            {
                return true;
            }

            if (DateTimeOffset.TryParse(watermark, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out moment))
            {
                return true;
            }

            reason = $"载荷「水位」解析不了：{watermark}（要 ISO 8601 时间串，或空串 = 全量拉）";
            return false;
        }

        /// <summary>列记录（分页）；水位非空时只保留最后修改时间严格大于水位的记录。</summary>
        private sealed class RecordsResult
        {
            public bool Succeeded;
            public BridgeResponse Response;
            public List<JsonElement> Records;
        }

        private static RecordsResult ListRecords(
            string appId,
            string appToken,
            string secretKey,
            int timeoutSeconds,
            string tableId,
            DateTimeOffset watermarkMoment)
        {
            var records = new List<JsonElement>();
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
                    return new RecordsResult { Succeeded = false, Response = call.Response };
                }

                foreach (var item in RecordWriter.ReadRecordItems(call.ResponseBody))
                {
                    if (watermarkMoment != DateTimeOffset.MinValue
                        && TryReadLastModifiedMoment(item, out var itemMoment, out _)
                        && itemMoment <= watermarkMoment)
                    {
                        continue;
                    }

                    records.Add(item.Clone());
                }

                pageToken = ReadString(call.ResponseBody, "data", "page_token");
                var hasMore = ReadBool(call.ResponseBody, "data", "has_more");
                if (!hasMore)
                {
                    break;
                }
            }

            return new RecordsResult { Succeeded = true, Records = records };
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

        /// <summary>字符串列表转 JSON 数组。</summary>
        private static JsonArray ToJsonArray(IReadOnlyList<string> values)
        {
            var array = new JsonArray();
            foreach (var value in values)
            {
                array.Add(value);
            }

            return array;
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

        /// <summary>信封写盘选项：缩进 + 不转义中文（与 InboxEnvelope 的读取兼容，中文原样落盘）。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        private static JsonSerializerOptions CreateWriteOptions()
        {
            return new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }
    }
}
