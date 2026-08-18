using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>从池子 schema 目录读取基线与项目扩展，按合并语义合出一份 PoolSchema。</summary>
    public static class PoolSchemaLoader
    {
        /// <summary>
        /// 读取某实体的基线 schema。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="entityName">实体名，如「需求」。</param>
        /// <exception cref="FileNotFoundException">基线 schema 文件不存在时抛出，消息里带完整路径。</exception>
        public static PoolSchema LoadBaseline(string poolRoot, string entityName)
        {
            var path = PoolPaths.BaselineSchemaFile(poolRoot, entityName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"基线 schema 文件不存在：{path}", path);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            var schemaVersion = GetStringOrDefault(root, "schema版本");
            var entity = GetStringOrDefault(root, "实体");
            var identifierPattern = GetStringOrDefault(root, "id模式", "");

            var fields = new List<PoolSchemaField>();
            if (root.TryGetProperty("字段", out var fieldsElement) && fieldsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in fieldsElement.EnumerateArray())
                {
                    fields.Add(ParseField(item));
                }
            }

            var requiredByType = new Dictionary<string, IReadOnlyList<string>>();
            if (root.TryGetProperty("分类型必填", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in requiredElement.EnumerateObject())
                {
                    var fieldNames = new List<string>();
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var nameElement in property.Value.EnumerateArray())
                        {
                            fieldNames.Add(nameElement.GetString() ?? "");
                        }
                    }

                    requiredByType[property.Name] = fieldNames;
                }
            }

            PoolStateMachine stateMachine = null;
            if (root.TryGetProperty("状态机", out var machineElement) && machineElement.ValueKind == JsonValueKind.Object)
            {
                stateMachine = ParseStateMachine(machineElement);
            }

            return new PoolSchema(schemaVersion, entity, identifierPattern, fields, requiredByType, stateMachine);
        }

        /// <summary>
        /// 读取基线 schema，若存在项目扩展则按合并语义合出最终 schema。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="entityName">实体名，如「需求」。</param>
        /// <exception cref="FileNotFoundException">基线 schema 文件不存在时抛出。</exception>
        public static PoolSchema Load(string poolRoot, string entityName)
        {
            var baseline = LoadBaseline(poolRoot, entityName);
            if (!ProjectSchemaExists(poolRoot, entityName))
            {
                return baseline;
            }

            var projectPath = PoolPaths.ProjectSchemaFile(poolRoot, entityName);
            using var document = JsonDocument.Parse(File.ReadAllText(projectPath));
            var root = document.RootElement;

            var fields = new List<PoolSchemaField>(baseline.Fields);

            // 合并语义一：项目层「字段」逐项追加到基线字段列表末尾。
            if (root.TryGetProperty("字段", out var fieldsElement) && fieldsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in fieldsElement.EnumerateArray())
                {
                    fields.Add(ParseField(item));
                }
            }

            // 合并语义二：「枚举增补」给基线同名字段的枚举值追加去重，位置不变；指向的字段不存在时静默跳过。
            if (root.TryGetProperty("枚举增补", out var extensionElement) && extensionElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in extensionElement.EnumerateObject())
                {
                    var additionalValues = new List<string>();
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var valueElement in property.Value.EnumerateArray())
                        {
                            additionalValues.Add(valueElement.GetString() ?? "");
                        }
                    }

                    var index = FindBaselineFieldIndex(baseline, property.Name);
                    if (index < 0)
                    {
                        continue;
                    }

                    var existing = fields[index];
                    var mergedValues = new List<string>(existing.EnumValues);
                    foreach (var value in additionalValues)
                    {
                        if (!mergedValues.Contains(value, StringComparer.Ordinal))
                        {
                            mergedValues.Add(value);
                        }
                    }

                    fields[index] = new PoolSchemaField(
                        existing.Name,
                        existing.FieldType,
                        existing.IsRequired,
                        mergedValues,
                        existing.ElementType,
                        existing.MinimumCount,
                        existing.Ownership,
                        existing.IsNullable,
                        existing.IsEditableAfterLock);
                }
            }

            // 版本 / 实体 / id 模式 / 分类型必填 / 状态机一律取基线的值，项目层动不了。
            return new PoolSchema(
                baseline.SchemaVersion,
                baseline.EntityName,
                baseline.IdentifierPattern,
                fields,
                baseline.RequiredByType,
                baseline.StateMachine);
        }

        /// <summary>
        /// 项目扩展 schema 是否存在。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="entityName">实体名。</param>
        public static bool ProjectSchemaExists(string poolRoot, string entityName)
        {
            return File.Exists(PoolPaths.ProjectSchemaFile(poolRoot, entityName));
        }

        private static int FindBaselineFieldIndex(PoolSchema baseline, string fieldName)
        {
            for (var i = 0; i < baseline.Fields.Count; i++)
            {
                if (string.Equals(baseline.Fields[i].Name, fieldName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static PoolSchemaField ParseField(JsonElement element)
        {
            var enumValues = new List<string>();
            if (element.TryGetProperty("枚举", out var enumElement) && enumElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var valueElement in enumElement.EnumerateArray())
                {
                    enumValues.Add(valueElement.GetString() ?? "");
                }
            }

            return new PoolSchemaField(
                GetStringOrDefault(element, "名称"),
                GetStringOrDefault(element, "类型"),
                GetBooleanOrDefault(element, "必填", false),
                enumValues,
                GetStringOrDefault(element, "元素类型", ""),
                GetInt32OrDefault(element, "最少条数", 0),
                GetStringOrDefault(element, "所有权", ""),
                GetBooleanOrDefault(element, "可空", false),
                GetBooleanOrDefault(element, "锁定后可改", true));
        }

        private static PoolStateMachine ParseStateMachine(JsonElement element)
        {
            var initialState = GetStringOrDefault(element, "初始状态", "");
            var transitions = new List<PoolStateTransition>();
            if (element.TryGetProperty("转换", out var transitionsElement) && transitionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in transitionsElement.EnumerateArray())
                {
                    transitions.Add(new PoolStateTransition(
                        GetStringOrDefault(item, "从"),
                        GetStringOrDefault(item, "到"),
                        GetStringOrDefault(item, "谁")));
                }
            }

            return new PoolStateMachine(initialState, transitions);
        }

        private static string GetStringOrDefault(JsonElement element, string propertyName)
        {
            return GetStringOrDefault(element, propertyName, "");
        }

        private static string GetStringOrDefault(JsonElement element, string propertyName, string fallback)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? fallback;
            }

            return fallback;
        }

        private static bool GetBooleanOrDefault(JsonElement element, string propertyName, bool fallback)
        {
            if (element.TryGetProperty(propertyName, out var value))
            {
                if (value.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (value.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
            }

            return fallback;
        }

        private static int GetInt32OrDefault(JsonElement element, string propertyName, int fallback)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt32();
            }

            return fallback;
        }
    }
}
