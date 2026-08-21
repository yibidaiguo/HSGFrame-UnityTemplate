using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>检查需求 JSON 文件是否符合合并后的 schema：id、必填、枚举、数组、对象与未声明字段。</summary>
    public static class RequirementValidator
    {
        /// <summary>参考示例固定指向需求基线 schema。</summary>
        private const string ReferencePath = "Pools/Schema/Baseline/requirement.schema.json";

        /// <summary>
        /// 校验单个需求 JSON 文件，返回全部违规发现。
        /// </summary>
        /// <param name="filePath">需求 JSON 文件路径。</param>
        /// <param name="schema">合并后的需求 schema。</param>
        public static IReadOnlyList<PoolFinding> CheckFile(string filePath, PoolSchema schema)
        {
            var findings = new List<PoolFinding>();

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
                    ReferencePath));
                return findings;
            }

            using (document)
            {
                var root = document.RootElement;
                var fileName = Path.GetFileNameWithoutExtension(filePath);

                CheckIdentifier(root, filePath, fileName, schema, findings);
                CheckRequiredFields(root, filePath, schema, findings);
                CheckEnumValues(root, filePath, schema, findings);
                CheckArrays(root, filePath, schema, findings);
                CheckObjectsAndBooleans(root, filePath, schema, findings);
                CheckRequiredByType(root, filePath, schema, findings);
                CheckUndeclaredFields(root, filePath, schema, findings);
            }

            return findings;
        }

        /// <summary>
        /// 校验目录下全部需求 JSON 文件（不递归），汇总各文件的违规发现；目录不存在时返回空列表。
        /// </summary>
        /// <param name="requirementsDirectory">需求文件所在目录。</param>
        /// <param name="schema">合并后的需求 schema。</param>
        public static IReadOnlyList<PoolFinding> CheckDirectory(string requirementsDirectory, PoolSchema schema)
        {
            var findings = new List<PoolFinding>();
            if (!Directory.Exists(requirementsDirectory))
            {
                return findings;
            }

            foreach (var filePath in Directory.EnumerateFiles(requirementsDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                findings.AddRange(CheckFile(filePath, schema));
            }

            return findings;
        }

        private static void CheckIdentifier(JsonElement root, string filePath, string fileName, PoolSchema schema, List<PoolFinding> findings)
        {
            if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
            {
                findings.Add(new PoolFinding(
                    filePath,
                    ValidationMessageCatalog.Format("需求.id缺失"),
                    ValidationMessageCatalog.FormatFix("需求.id缺失"),
                    ReferencePath));
                return;
            }

            var id = idElement.GetString();

            if (schema.IdentifierPattern.Length > 0 && !Regex.IsMatch(id, schema.IdentifierPattern))
            {
                findings.Add(new PoolFinding(
                    filePath,
                    ValidationMessageCatalog.Format("需求.id模式", id, schema.IdentifierPattern),
                    ValidationMessageCatalog.FormatFix("需求.id模式"),
                    ReferencePath));
            }

            if (!string.Equals(id, fileName, StringComparison.Ordinal))
            {
                findings.Add(new PoolFinding(
                    filePath,
                    ValidationMessageCatalog.Format("需求.id与文件名", id, fileName),
                    ValidationMessageCatalog.FormatFix("需求.id与文件名"),
                    ReferencePath));
            }
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
                        ReferencePath));
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
                        ReferencePath));
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
                        ReferencePath));
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
                        ReferencePath));
                    continue;
                }

                var count = value.GetArrayLength();
                if (count < field.MinimumCount)
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.数组过短", field.Name, count, field.MinimumCount),
                        ValidationMessageCatalog.FormatFix("需求.数组过短"),
                        ReferencePath));
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
                        ReferencePath));
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
                        ReferencePath));
                }
            }
        }

        private static void CheckRequiredByType(JsonElement root, string filePath, PoolSchema schema, List<PoolFinding> findings)
        {
            var typeValue = root.TryGetProperty("类型", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;
            if (typeValue == null || !schema.RequiredByType.TryGetValue(typeValue, out var requiredFields))
            {
                return;
            }

            foreach (var fieldName in requiredFields)
            {
                var exists = root.TryGetProperty(fieldName, out var value);
                var isBlankString = value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString());
                if (!exists || value.ValueKind == JsonValueKind.Null || isBlankString)
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.分类型必填", typeValue, fieldName),
                        ValidationMessageCatalog.FormatFix("需求.分类型必填"),
                        ReferencePath));
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

            var typeValue = root.TryGetProperty("类型", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;
            if (typeValue != null && schema.RequiredByType.TryGetValue(typeValue, out var typeRequiredFields))
            {
                foreach (var fieldName in typeRequiredFields)
                {
                    allowedNames.Add(fieldName);
                }
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!allowedNames.Contains(property.Name))
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        ValidationMessageCatalog.Format("需求.未声明字段", property.Name),
                        ValidationMessageCatalog.FormatFix("需求.未声明字段"),
                        ReferencePath));
                }
            }
        }
    }
}
