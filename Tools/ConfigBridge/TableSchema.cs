using System.Collections.Generic;
using System.Linq;

namespace Template.Toolkit.ConfigBridge
{
    /// <summary>一张配置表的结构声明：表名、Sheet 名与字段列表。</summary>
    public sealed class TableSchema
    {
        /// <summary>表名，例如「背包」，同时是 schema 与 xlsx 文件名的主干。</summary>
        public string TableName { get; set; }

        /// <summary>表的英文标识名，用作生成代码的类名，例如 Bag。</summary>
        public string TableIdentifierName { get; set; }

        /// <summary>xlsx 里承载这张表的 Sheet 名。</summary>
        public string SheetName { get; set; }

        /// <summary>字段列表，按 Excel 列顺序排列。</summary>
        public IReadOnlyList<TableFieldSchema> Fields { get; set; }

        /// <summary>按中文显示名查找字段，找不到返回 null。</summary>
        public TableFieldSchema FindByDisplayName(string displayName)
        {
            return Fields.FirstOrDefault(field => field.DisplayName == displayName);
        }

        /// <summary>按英文标识名查找字段，找不到返回 null。</summary>
        public TableFieldSchema FindByIdentifierName(string identifierName)
        {
            return Fields.FirstOrDefault(field => field.IdentifierName == identifierName);
        }
    }
}
