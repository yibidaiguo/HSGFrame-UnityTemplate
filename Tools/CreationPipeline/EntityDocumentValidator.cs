using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>吃任意 PoolSchema 的通用文档校验器：必填、枚举、数组、对象、布尔与未声明字段。</summary>
    public static class EntityDocumentValidator
    {
        /// <summary>
        /// 校验单个 JSON 文档是否符合传入的合并 schema，返回全部违规发现。
        /// 判定顺序：文件不存在 → JSON 语法 → 顶层对象 → 必填缺失/空串 → 枚举越界 →
        /// 非数组/数组过短 → 非对象 → 非布尔 → 未声明字段。id 模式与文件名一致性
        /// 不属于通用校验范围，由调用方按实体自己决定。
        /// </summary>
        /// <param name="filePath">文档 JSON 文件路径。</param>
        /// <param name="schema">合并后的实体 schema。</param>
        public static IReadOnlyList<PoolFinding> Validate(string filePath, PoolSchema schema)
        {
            var findings = new List<PoolFinding>();

            if (!File.Exists(filePath))
            {
                findings.Add(new PoolFinding(filePath, "文件不存在", "先产出这份文件再校验", ""));
                return findings;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (JsonException exception)
            {
                findings.Add(new PoolFinding(
                    filePath,
                    ValidationMessageCatalog.Format("需求.JSON语法", exception.Message),
                    ValidationMessageCatalog.FormatFix("需求.JSON语法"),
                    ""));
                return findings;
            }

            using (document)
            {
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    findings.Add(new PoolFinding(filePath, "顶层不是 JSON 对象", "把文件顶层改成 JSON 对象", ""));
                    return findings;
                }

                CheckRequiredFields(root, filePath, schema, findings);
                CheckEnumValues(root, filePath, schema, findings);
                CheckArrays(root, filePath, schema, findings);
                CheckObjectsAndBooleans(root, filePath, schema, findings);
                CheckUndeclaredFields(root, filePath, schema, findings);
            }

            return findings;
        }

        private static void CheckRequiredFields(JsonElement root, string filePath, PoolSchema schema, List<PoolFinding> findings)
        {
            foreach (var field in schema.Fields)
            {
                if (!field.IsRequired)
                {
                    continue;
                }

                var exists = root.TryGetProperty(field.Name, out var value);
                if (!exists || value.ValueKind == JsonValueKind.Null)
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.必填缺失", field.Name),
                        ValidationMessageCatalog.FormatFix("需求.必填缺失"),
                        ""));
                    continue;
                }

                if (string.Equals(field.FieldType, "string", StringComparison.Ordinal)
                    && value.ValueKind == JsonValueKind.String
                    && string.IsNullOrWhiteSpace(value.GetString()))
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.必填空串", field.Name),
                        ValidationMessageCatalog.FormatFix("需求.必填空串"),
                        ""));
                }
            }
        }

        private static void CheckEnumValues(JsonElement root, string filePath, PoolSchema schema, List<PoolFinding> findings)
        {
            foreach (var field in schema.Fields)
            {
                if (!string.Equals(field.FieldType, "enum", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!root.TryGetProperty(field.Name, out var value) || value.ValueKind == JsonValueKind.Null)
                {
                    // 字段缺失或为 null 时由必填检查覆盖，这里不重复报。
                    continue;
                }

                if (value.ValueKind != JsonValueKind.String || !field.EnumValues.Contains(value.GetString(), StringComparer.Ordinal))
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.枚举越界", field.Name, string.Join("、", field.EnumValues)),
                        ValidationMessageCatalog.FormatFix("需求.枚举越界"),
                        ""));
                }
            }
        }

        private static void CheckArrays(JsonElement root, string filePath, PoolSchema schema, List<PoolFinding> findings)
        {
            foreach (var field in schema.Fields)
            {
                if (!string.Equals(field.FieldType, "数组", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!root.TryGetProperty(field.Name, out var value) || value.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (value.ValueKind != JsonValueKind.Array)
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.非数组", field.Name),
                        ValidationMessageCatalog.FormatFix("需求.非数组"),
                        ""));
                    continue;
                }

                var count = value.GetArrayLength();
                if (count < field.MinimumCount)
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.数组过短", field.Name, count, field.MinimumCount),
                        ValidationMessageCatalog.FormatFix("需求.数组过短"),
                        ""));
                }
            }
        }

        private static void CheckObjectsAndBooleans(JsonElement root, string filePath, PoolSchema schema, List<PoolFinding> findings)
        {
            foreach (var field in schema.Fields)
            {
                if (!root.TryGetProperty(field.Name, out var value) || value.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (string.Equals(field.FieldType, "对象", StringComparison.Ordinal) && value.ValueKind != JsonValueKind.Object)
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.非对象", field.Name),
                        ValidationMessageCatalog.FormatFix("需求.非对象"),
                        ""));
                    continue;
                }

                if (string.Equals(field.FieldType, "bool", StringComparison.Ordinal)
                    && value.ValueKind != JsonValueKind.True
                    && value.ValueKind != JsonValueKind.False)
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.非布尔", field.Name),
                        ValidationMessageCatalog.FormatFix("需求.非布尔"),
                        ""));
                }
            }
        }

        private static void CheckUndeclaredFields(JsonElement root, string filePath, PoolSchema schema, List<PoolFinding> findings)
        {
            var allowedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in schema.Fields)
            {
                allowedNames.Add(field.Name);
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!allowedNames.Contains(property.Name))
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.未声明字段", property.Name),
                        ValidationMessageCatalog.FormatFix("需求.未声明字段"),
                        ""));
                }
            }
        }
    }
}
