using System;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.ConfigBridge
{
    /// <summary>从 *.schema.json 文件加载表结构声明。</summary>
    public static class SchemaLoader
    {
        // 白名单之外的字段类型没有任何代码路径能读写，尽早失败而不是等到运行时才发现。
        private static readonly string[] SupportedTypeNames =
        {
            "Int32", "Int64", "Single", "Boolean", "String"
        };

        /// <summary>读取并反序列化 schema 文件，校验字段类型名合法后返回。</summary>
        public static TableSchema LoadFromFile(string schemaPath)
        {
            var json = File.ReadAllText(schemaPath);
            var schema = JsonSerializer.Deserialize<TableSchema>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            foreach (var field in schema.Fields)
            {
                if (Array.IndexOf(SupportedTypeNames, field.TypeName) < 0)
                {
                    throw new NotSupportedException(
                        $"不支持的类型名：{field.TypeName}（字段 {field.IdentifierName}）");
                }
            }

            return schema;
        }
    }
}
