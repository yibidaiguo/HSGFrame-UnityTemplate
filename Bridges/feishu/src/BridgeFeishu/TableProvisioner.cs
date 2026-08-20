using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 建表动作（apply）：读建表描述，在飞书多维表格里幂等建表。
    /// 幂等铁律（决策 92 同源：写别人工作区的动作只做一次）：先 GET 列一遍现有表，
    /// 同名表已存在就跳过、绝不重建——建重了没有自动撤的路，只能人去手删。
    /// 字段类型码（下游类型名 → 飞书数字码）建表前先用一张「-探测」后缀的临时表验证一遍：
    /// 建错类型的字段同样没法自动撤，所以先建探测表、读回字段的类型码比对、确认后再删掉它、
    /// 最后才建正式表。探测表只删自己刚建的那张，别的表一律不碰。
    /// 表单那三项（建表描述里的「表单」）本批不做。
    /// </summary>
    public static class TableProvisioner
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>建表描述文件在工作目录（仓库根）下的相对路径。</summary>
        private const string TableDescriptionRelativePath = "_Generated/Bridges/feishu/建表描述.json";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>缺省默认视图名，建表时写进 table.default_view_name。</summary>
        private const string DefaultViewName = "表格";

        /// <summary>
        /// 执行 apply 动作：干跑返回建表计划；真跑幂等建表（含探测验证）。
        /// </summary>
        /// <param name="request">请求信封：配置含 应用标识/多维表格标识/超时秒/飞书应用密钥，
        /// 载荷含 干跑（缺省 true，缺省即不写）。</param>
        public static BridgeResponse RunApply(BridgeRequest request)
        {
            var appId = ReadConfigurationString(request, "应用标识", "");
            var appToken = ReadConfigurationString(request, "多维表格标识", "");
            var secretKey = ReadConfigurationString(request, "飞书应用密钥", "");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);

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

            if (!TryLoadProvisionDescription(out var description, out var loadReason))
            {
                return Failure("本机配置错误", loadReason, retryable: false);
            }

            var plannedFields = new List<PlannedField>();
            foreach (var field in description.Fields)
            {
                if (!FeishuFieldTypeCodec.TryMap(field.DownstreamType, out var typeCode, out var mapReason))
                {
                    return Failure("本机配置错误", $"建表描述里的字段「{field.Name}」：{mapReason}", retryable: false);
                }

                plannedFields.Add(new PlannedField(field.Name, field.DownstreamType, typeCode, field.EnumValues));
            }

            if (isDryRun)
            {
                return Success(BuildDryRunPayload(description.TableName, plannedFields));
            }

            return RunProvision(appId, appToken, secretKey, timeoutSeconds, description.TableName, plannedFields);
        }

        /// <summary>真跑建表：查同名 → 探测验证 → 删探测表 → 建正式表。</summary>
        private static BridgeResponse RunProvision(
            string appId,
            string appToken,
            string secretKey,
            int timeoutSeconds,
            string tableName,
            IReadOnlyList<PlannedField> plannedFields)
        {
            var listCall = FeishuClient.Send("GET", FeishuClient.BitableUrl(appToken, "tables"), null, appId, secretKey, timeoutSeconds);
            if (!listCall.Succeeded)
            {
                return listCall.Response;
            }

            if (TryReadTableIdByName(listCall.ResponseBody, tableName, out _))
            {
                // 同名表已存在：跳过，绝不重建。绝不许建出两张同名表。
                var skipped = new JsonObject { ["表名"] = tableName, ["原因"] = "已存在，跳过" };
                var skipPayload = new JsonObject { ["建了"] = new JsonArray(), ["跳过的"] = new JsonArray(skipped) };
                return Success(JsonSerializer.SerializeToElement(skipPayload));
            }

            var probeResult = ProbeAndVerify(appId, appToken, secretKey, timeoutSeconds, tableName, plannedFields, listCall.ResponseBody);
            if (!probeResult.Succeeded)
            {
                return probeResult.Response;
            }

            var createCall = FeishuClient.Send(
                "POST",
                FeishuClient.BitableUrl(appToken, "tables"),
                BuildCreateTableBody(tableName, plannedFields),
                appId,
                secretKey,
                timeoutSeconds);
            if (!createCall.Succeeded)
            {
                return createCall.Response;
            }

            var tableId = ReadString(createCall.ResponseBody, "data", "table_id");
            if (tableId.Length == 0)
            {
                return Failure("下游报错", "飞书建表响应里没有 table_id", retryable: false);
            }

            var created = new JsonObject { ["表名"] = tableName, ["table_id"] = tableId };
            var payload = new JsonObject
            {
                ["建了"] = new JsonArray(created),
                ["跳过的"] = new JsonArray()
            };

            var verification = probeResult.Verification;
            if (verification != null && verification.Count > 0)
            {
                var verificationArray = new JsonArray();
                foreach (var item in verification)
                {
                    verificationArray.Add(new JsonObject
                    {
                        ["字段"] = item.FieldName,
                        ["下游类型"] = item.DownstreamType,
                        ["预期码"] = item.ExpectedCode,
                        ["实际码"] = item.ActualCode,
                        ["一致"] = item.Match
                    });
                }

                payload["探测验证"] = verificationArray;
            }

            return Success(JsonSerializer.SerializeToElement(payload));
        }

        /// <summary>一次探测验证的结果：成功标志、失败时的协议响应、逐字段的验证明细。</summary>
        private sealed class ProbeOutcome
        {
            public bool Succeeded;
            public BridgeResponse Response;
            public List<FieldVerification> Verification;
        }

        /// <summary>一个字段的类型码验证明细。</summary>
        private sealed class FieldVerification
        {
            public string FieldName;
            public string DownstreamType;
            public int ExpectedCode;
            public int ActualCode;
            public bool Match;
        }

        /// <summary>
        /// 探测验证：建一张「&lt;表名&gt;-探测」的临时表，读回字段类型码逐项比对；
        /// 无论验证成败都删掉探测表。验证通过才继续建正式表。
        /// </summary>
        private static ProbeOutcome ProbeAndVerify(
            string appId,
            string appToken,
            string secretKey,
            int timeoutSeconds,
            string tableName,
            IReadOnlyList<PlannedField> plannedFields,
            JsonElement listTablesBody)
        {
            var probeTableName = tableName + "-探测";

            // 上次跑残留的探测表（只删精确匹配「<表名>-探测」的那张，别的表一律不碰）。
            if (TryReadTableIdByName(listTablesBody, probeTableName, out var existingProbeId))
            {
                var deleteResidue = FeishuClient.Send("DELETE", FeishuClient.BitableUrl(appToken, "tables/" + existingProbeId), null, appId, secretKey, timeoutSeconds);
                if (!deleteResidue.Succeeded)
                {
                    return new ProbeOutcome { Succeeded = false, Response = deleteResidue.Response };
                }
            }

            var probeCall = FeishuClient.Send(
                "POST",
                FeishuClient.BitableUrl(appToken, "tables"),
                BuildCreateTableBody(probeTableName, plannedFields),
                appId,
                secretKey,
                timeoutSeconds);
            if (!probeCall.Succeeded)
            {
                return new ProbeOutcome { Succeeded = false, Response = probeCall.Response };
            }

            var probeTableId = ReadString(probeCall.ResponseBody, "data", "table_id");
            if (probeTableId.Length == 0)
            {
                return new ProbeOutcome
                {
                    Succeeded = false,
                    Response = Failure("下游报错", "飞书建探测表的响应里没有 table_id", retryable: false)
                };
            }

            var fieldsCall = FeishuClient.Send(
                "GET",
                FeishuClient.BitableUrl(appToken, "tables/" + probeTableId + "/fields"),
                null,
                appId,
                secretKey,
                timeoutSeconds);
            if (!fieldsCall.Succeeded)
            {
                DeleteProbeQuietly(appId, appToken, secretKey, timeoutSeconds, probeTableId);
                return new ProbeOutcome { Succeeded = false, Response = fieldsCall.Response };
            }

            var verification = VerifyFieldTypeCodes(fieldsCall.ResponseBody, plannedFields);
            var allMatch = true;
            foreach (var item in verification)
            {
                if (!item.Match)
                {
                    allMatch = false;
                }
            }

            var deleteCall = FeishuClient.Send("DELETE", FeishuClient.BitableUrl(appToken, "tables/" + probeTableId), null, appId, secretKey, timeoutSeconds);
            if (!deleteCall.Succeeded)
            {
                return new ProbeOutcome { Succeeded = false, Response = deleteCall.Response };
            }

            if (!allMatch)
            {
                var details = new List<string>();
                foreach (var item in verification)
                {
                    var mark = item.Match ? "一致" : "不一致";
                    details.Add($"{item.FieldName}（{item.DownstreamType}）：预期 {item.ExpectedCode}，实际 {item.ActualCode}，{mark}");
                }

                return new ProbeOutcome
                {
                    Succeeded = false,
                    Response = Failure(
                        "下游报错",
                        "探测表字段类型码与预期不符，已删掉探测表、未建正式表：" + string.Join("；", details),
                        retryable: false)
                };
            }

            return new ProbeOutcome { Succeeded = true, Verification = verification };
        }

        /// <summary>读探测表失败后的兜底删除：删不掉也只记 stderr，原错误优先返回。</summary>
        private static void DeleteProbeQuietly(string appId, string appToken, string secretKey, int timeoutSeconds, string probeTableId)
        {
            var deleteCall = FeishuClient.Send("DELETE", FeishuClient.BitableUrl(appToken, "tables/" + probeTableId), null, appId, secretKey, timeoutSeconds);
            if (!deleteCall.Succeeded)
            {
                Console.Error.WriteLine("BridgeFeishu：探测表删除失败（table_id=" + probeTableId + "），请人工检查");
            }
        }

        /// <summary>把探测表返回的字段清单与计划字段逐项比对类型码；响应里找不到的字段按不一致报。</summary>
        private static List<FieldVerification> VerifyFieldTypeCodes(JsonElement fieldsBody, IReadOnlyList<PlannedField> plannedFields)
        {
            var actualByFieldName = new Dictionary<string, int>(StringComparer.Ordinal);
            if (fieldsBody.ValueKind == JsonValueKind.Object
                && fieldsBody.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var name = ReadString(item, "field_name");
                    var typeCode = ReadInt(item, "type");
                    if (name.Length > 0)
                    {
                        actualByFieldName[name] = typeCode;
                    }
                }
            }

            var result = new List<FieldVerification>();
            foreach (var field in plannedFields)
            {
                var actual = actualByFieldName.TryGetValue(field.Name, out var code) ? code : -1;
                result.Add(new FieldVerification
                {
                    FieldName = field.Name,
                    DownstreamType = field.DownstreamType,
                    ExpectedCode = field.TypeCode,
                    ActualCode = actual,
                    Match = actual == field.TypeCode
                });
            }

            return result;
        }

        /// <summary>拼建表请求体：{"table":{"name":…,"default_view_name":"表格","fields":[…]}}。</summary>
        private static string BuildCreateTableBody(string tableName, IReadOnlyList<PlannedField> fields)
        {
            var fieldsArray = new JsonArray();
            foreach (var field in fields)
            {
                var fieldNode = new JsonObject
                {
                    ["field_name"] = field.Name,
                    ["type"] = field.TypeCode
                };

                if (FeishuFieldTypeCodec.RequiresOptions(field.DownstreamType))
                {
                    var options = new JsonArray();
                    foreach (var option in field.EnumValues)
                    {
                        options.Add(new JsonObject { ["name"] = option });
                    }

                    fieldNode["property"] = new JsonObject { ["options"] = options };
                }

                fieldsArray.Add(fieldNode);
            }

            var body = new JsonObject
            {
                ["table"] = new JsonObject
                {
                    ["name"] = tableName,
                    ["default_view_name"] = DefaultViewName,
                    ["fields"] = fieldsArray
                }
            };

            return body.ToJsonString();
        }

        /// <summary>干跑载荷：{"干跑":true,"计划":[{"表名","字段数","字段":[{"名称","下游类型","类型码"}]}]}。</summary>
        private static JsonElement BuildDryRunPayload(string tableName, IReadOnlyList<PlannedField> fields)
        {
            var fieldNodes = new JsonArray();
            foreach (var field in fields)
            {
                fieldNodes.Add(new JsonObject
                {
                    ["名称"] = field.Name,
                    ["下游类型"] = field.DownstreamType,
                    ["类型码"] = field.TypeCode
                });
            }

            var plan = new JsonObject
            {
                ["表名"] = tableName,
                ["字段数"] = fields.Count,
                ["字段"] = fieldNodes
            };

            return JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = true,
                ["计划"] = new JsonArray(plan)
            });
        }

        /// <summary>从 GET tables 的响应体里按表名找 table_id；找不到返回 false。</summary>
        private static bool TryReadTableIdByName(JsonElement body, string tableName, out string tableId)
        {
            tableId = "";
            if (body.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (string.Equals(ReadString(item, "name"), tableName, StringComparison.Ordinal))
                {
                    tableId = ReadString(item, "table_id");
                    return tableId.Length > 0;
                }
            }

            return false;
        }

        /// <summary>读建表描述文件并解析成 ProvisionDescription；失败给可读原因。</summary>
        private static bool TryLoadProvisionDescription(out ProvisionDescription description, out string reason)
        {
            description = null;
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

                var tableName = ReadString(root, "表名");
                if (tableName.Length == 0)
                {
                    reason = $"建表描述文件缺「表名」或它不是字符串：{TableDescriptionRelativePath}";
                    return false;
                }

                var fields = new List<TableFieldDescription>();
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

                    var enumValues = ReadStringArray(fieldElement, "单选项");
                    fields.Add(new TableFieldDescription(name, downstreamType, false, enumValues, "", false));
                }

                description = new ProvisionDescription(tableName, fields);
                return true;
            }
        }

        /// <summary>建表计划里的一列字段：名称、下游类型名、映射后的类型码与单选项。</summary>
        private sealed class PlannedField
        {
            public string Name;
            public string DownstreamType;
            public int TypeCode;
            public IReadOnlyList<string> EnumValues;

            public PlannedField(string name, string downstreamType, int typeCode, IReadOnlyList<string> enumValues)
            {
                Name = name;
                DownstreamType = downstreamType;
                TypeCode = typeCode;
                EnumValues = enumValues ?? Array.Empty<string>();
            }
        }

        /// <summary>解析后的建表描述：表名 + 字段清单（表单本批不用，不解析）。</summary>
        private sealed class ProvisionDescription
        {
            public string TableName;
            public IReadOnlyList<TableFieldDescription> Fields;

            public ProvisionDescription(string tableName, IReadOnlyList<TableFieldDescription> fields)
            {
                TableName = tableName;
                Fields = fields;
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
