namespace Template.Toolkit.ConfigBridge
{
    /// <summary>配置表单个字段的结构声明：中文显示名、英文标识名、类型名与是否主键。</summary>
    public sealed class TableFieldSchema
    {
        /// <summary>Excel 表头里显示的中文列名。</summary>
        public string DisplayName { get; set; }

        /// <summary>镜像 JSON 里使用的英文键名，也是生成代码使用的字段名。</summary>
        public string IdentifierName { get; set; }

        /// <summary>字段类型名，取值 Int32 / Int64 / Single / Boolean / String。</summary>
        public string TypeName { get; set; }

        /// <summary>是否为主键字段。</summary>
        public bool IsPrimaryKey { get; set; }
    }
}
