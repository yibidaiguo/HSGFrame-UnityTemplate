using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>配置表里的一个字段。</summary>
    /// <param name="DisplayName">参数名（表头那个中文名）。</param>
    /// <param name="IdentifierName">标识名（生成的代码里那个名字）。</param>
    /// <param name="TypeName">参数类型。</param>
    /// <param name="IsPrimaryKey">是不是主键。</param>
    public sealed record ConfigTableField(
        string DisplayName, string IdentifierName, string TypeName, bool IsPrimaryKey);

    /// <summary>一张配置表的结构。</summary>
    /// <param name="TableName">表名（中文）。</param>
    /// <param name="IdentifierName">表的标识名，也是 schema 文件名。</param>
    /// <param name="SheetName">页签名。</param>
    /// <param name="Fields">字段，按 schema 里的顺序。</param>
    public sealed record ConfigTableStructure(
        string TableName, string IdentifierName, string SheetName, IReadOnlyList<ConfigTableField> Fields);

    /// <summary>
    /// 读配置表结构：`Config/Schema/&lt;表&gt;.schema.json`。
    ///
    /// **策划案里那张参数表是从这儿渲的，不许人手抄。** 手抄的那份一定会与表漂——
    /// 加一列、改个类型没人会想起同步文档，而漂了的参数表比没有更坏：
    /// 程序照着它写代码，跑起来才发现字段名对不上。
    ///
    /// 哪几张表归哪个模块由人在策划案的 frontmatter 里声明，机器不猜：
    /// `Bag` 归 `Inventory` 没有任何字面依据可推（名字都对不上），
    /// 靠猜迟早把 Monster 表算进背包。
    /// </summary>
    public static class ConfigTableSchemaReader
    {
        /// <summary>配置表 schema 目录：Config/Schema。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string SchemaDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Config", "Schema");
        }

        /// <summary>某张表的 schema 文件路径。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="tableIdentifier">表的标识名，如 Bag。</param>
        public static string SchemaFile(string repositoryRoot, string tableIdentifier)
        {
            return Path.Combine(SchemaDirectory(repositoryRoot), (tableIdentifier ?? "") + ".schema.json");
        }

        /// <summary>
        /// 读一张表的结构。
        ///
        /// 读不动**不抛异常**，回 null 加一句原因：一张表的 schema 缺了或坏了，
        /// 不该让整份策划案渲不出来——别的几节还是好的，这一节如实说这张表读不动就行
        /// （决策 42：读不动与「没有这张表」是两支）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="tableIdentifier">表的标识名。</param>
        /// <param name="reason">读不动的原因；成功时为空串。</param>
        public static ConfigTableStructure Read(string repositoryRoot, string tableIdentifier, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(tableIdentifier))
            {
                reason = "表名是空的";
                return null;
            }

            var path = SchemaFile(repositoryRoot, tableIdentifier);
            if (!File.Exists(path))
            {
                reason = $"找不到 {tableIdentifier} 的 schema：{path}";
                return null;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                reason = $"{tableIdentifier} 的 schema 读不动：{exception.Message}";
                return null;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text);
            }
            catch (JsonException exception)
            {
                reason = $"{tableIdentifier} 的 schema 不是合法 JSON：{exception.Message}";
                return null;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = $"{tableIdentifier} 的 schema 顶层不是对象";
                    return null;
                }

                var fields = new List<ConfigTableField>();
                if (root.TryGetProperty("fields", out var fieldArray) && fieldArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fieldArray.EnumerateArray())
                    {
                        if (field.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        fields.Add(new ConfigTableField(
                            ReadString(field, "displayName"),
                            ReadString(field, "identifierName"),
                            ReadString(field, "typeName"),
                            field.TryGetProperty("isPrimaryKey", out var primary)
                                && primary.ValueKind == JsonValueKind.True));
                    }
                }

                if (fields.Count == 0)
                {
                    reason = $"{tableIdentifier} 的 schema 里一个字段都没有";
                    return null;
                }

                return new ConfigTableStructure(
                    ReadString(root, "tableName"),
                    ReadString(root, "tableIdentifierName"),
                    ReadString(root, "sheetName"),
                    fields);
            }
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }
    }
}
