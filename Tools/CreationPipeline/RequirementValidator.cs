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
        private const string ReferencePath = "Pools/Schema/基线/需求.schema.json";

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
                    $"JSON 语法错误：{exception.Message}",
                    "修复 JSON 语法后重新校验",
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
                    "缺少字段「id」",
                    "补上 id 字段",
                    ReferencePath));
                return;
            }

            var id = idElement.GetString();

            if (schema.IdentifierPattern.Length > 0 && !Regex.IsMatch(id, schema.IdentifierPattern))
            {
                findings.Add(new PoolFinding(
                    filePath,
                    $"字段「id」的值「{id}」不匹配 id 模式「{schema.IdentifierPattern}」",
                    "把 id 改成匹配 id 模式的格式",
                    ReferencePath));
            }

            if (!string.Equals(id, fileName, StringComparison.Ordinal))
            {
                findings.Add(new PoolFinding(
                    filePath,
                    $"字段「id」的值「{id}」与文件名「{fileName}」不一致",
                    "让文件名的 id 与字段 id 保持一致",
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
                        $"必填字段「{field.Name}」缺失或为 null",
                        "补上该字段并给一个非 null 的值",
                        ReferencePath));
                    continue;
                }

                if (string.Equals(field.FieldType, "string", StringComparison.Ordinal)
                    && value.ValueKind == JsonValueKind.String
                    && string.IsNullOrWhiteSpace(value.GetString()))
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        $"必填字段「{field.Name}」是空字符串",
                        "填上实际内容",
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
                        $"字段「{field.Name}」的值不在枚举「{string.Join("、", field.EnumValues)}」里",
                        "改成合法的枚举值",
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
                        $"字段「{field.Name}」的值不是数组",
                        "把该字段的值改成 JSON 数组",
                        ReferencePath));
                    continue;
                }

                var count = value.GetArrayLength();
                if (count < field.MinimumCount)
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        $"字段「{field.Name}」的数组条数 {count} 少于最少条数 {field.MinimumCount}",
                        "补足数组条数",
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
                        $"字段「{field.Name}」的值不是对象",
                        "把该字段的值改成 JSON 对象",
                        ReferencePath));
                    continue;
                }

                if (string.Equals(field.FieldType, "bool", StringComparison.Ordinal)
                    && value.ValueKind != JsonValueKind.True
                    && value.ValueKind != JsonValueKind.False)
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        $"字段「{field.Name}」的值不是 true/false",
                        "把该字段的值改成 true 或 false",
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
                        $"类型「{typeValue}」的必填字段「{fieldName}」缺失或为空",
                        "补上该类型要求的字段",
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
                        $"字段「{property.Name}」未在合并 schema 中声明",
                        "删掉该字段，或在项目扩展 schema 里声明它",
                        ReferencePath));
                }
            }
        }
    }
}
