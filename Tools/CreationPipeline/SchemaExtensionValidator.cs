using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>检查项目层扩展 schema 是不是基线 schema 的合法扩展：顶层键、实体名、重名与枚举增补。</summary>
    public static class SchemaExtensionValidator
    {
        /// <summary>参考示例固定指向需求基线 schema。</summary>
        private const string ReferencePath = "Pools/Schema/基线/需求.schema.json";

        /// <summary>
        /// 逐条检查项目扩展 schema，返回全部违规发现；扩展文件不存在时视为合法，返回空列表。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="entityName">实体名，如「需求」。</param>
        public static IReadOnlyList<PoolFinding> Check(string poolRoot, string entityName)
        {
            var findings = new List<PoolFinding>();
            var projectPath = PoolPaths.ProjectSchemaFile(poolRoot, entityName);
            if (!File.Exists(projectPath))
            {
                return findings;
            }

            var baseline = PoolSchemaLoader.LoadBaseline(poolRoot, entityName);

            using var document = JsonDocument.Parse(File.ReadAllText(projectPath));
            var root = document.RootElement;

            CheckTopLevelKeys(root, projectPath, findings);
            CheckEntityName(root, projectPath, entityName, findings);
            CheckDuplicateFieldNames(root, projectPath, baseline, findings);
            CheckEnumExtensions(root, projectPath, baseline, findings);

            return findings;
        }

        private static void CheckTopLevelKeys(JsonElement root, string projectPath, List<PoolFinding> findings)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == "字段" || property.Name == "枚举增补" || property.Name == "实体")
                {
                    continue;
                }

                if (property.Name.StartsWith("_", StringComparison.Ordinal))
                {
                    continue;
                }

                findings.Add(new PoolFinding(
                    projectPath,
                    $"项目层只许追加字段与枚举值，不认识的顶层键：{property.Name}",
                    "删掉这个键；要加业务字段就写进「字段」，要扩枚举值就写进「枚举增补」",
                    ReferencePath));
            }
        }

        private static void CheckEntityName(JsonElement root, string projectPath, string entityName, List<PoolFinding> findings)
        {
            var declared = root.TryGetProperty("实体", out var entityElement) && entityElement.ValueKind == JsonValueKind.String
                ? entityElement.GetString()
                : "";
            if (!string.Equals(declared, entityName, StringComparison.Ordinal))
            {
                findings.Add(new PoolFinding(
                    projectPath,
                    $"扩展文件声明的实体「{declared}」与传入实体名「{entityName}」不一致",
                    "把扩展文件的「实体」改成与基线一致",
                    ReferencePath));
            }
        }

        private static void CheckDuplicateFieldNames(JsonElement root, string projectPath, PoolSchema baseline, List<PoolFinding> findings)
        {
            var skeletonNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in baseline.Fields)
            {
                skeletonNames.Add(field.Name);
            }

            foreach (var fieldNames in baseline.RequiredByType.Values)
            {
                foreach (var fieldName in fieldNames)
                {
                    skeletonNames.Add(fieldName);
                }
            }

            if (!root.TryGetProperty("字段", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in fieldsElement.EnumerateArray())
            {
                var name = item.TryGetProperty("名称", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : "";
                if (name.Length > 0 && skeletonNames.Contains(name))
                {
                    findings.Add(new PoolFinding(
                        projectPath,
                        $"扩展字段「{name}」与骨架字段重名",
                        "给扩展字段换一个骨架里没有的名字",
                        ReferencePath));
                }
            }
        }

        private static void CheckEnumExtensions(JsonElement root, string projectPath, PoolSchema baseline, List<PoolFinding> findings)
        {
            if (!root.TryGetProperty("枚举增补", out var extensionElement) || extensionElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in extensionElement.EnumerateObject())
            {
                var field = baseline.FindField(property.Name);
                if (field == null)
                {
                    findings.Add(new PoolFinding(
                        projectPath,
                        $"枚举增补指向的字段「{property.Name}」在基线 schema 中不存在",
                        "核对字段名拼写，只能增补基线已有的字段",
                        ReferencePath));
                    continue;
                }

                if (!string.Equals(field.FieldType, "enum", StringComparison.Ordinal))
                {
                    findings.Add(new PoolFinding(
                        projectPath,
                        $"枚举增补指向的字段「{property.Name}」不是枚举类型",
                        "只能给枚举类型的字段追加枚举值",
                        ReferencePath));
                }
            }
        }
    }
}
