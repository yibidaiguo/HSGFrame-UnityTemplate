namespace Template.Toolkit.CommandFramework
{
    /// <summary>
    /// 单个命令参数的 schema：参数名、类型名、是否必填以及说明文本。
    /// </summary>
    public sealed class CommandParameterSchema
    {
        /// <summary>
        /// 构造一条参数 schema。
        /// </summary>
        /// <param name="parameterName">参数属性名，例如 SolutionPath。</param>
        /// <param name="parameterTypeName">参数类型名，例如 String / Int32 / Boolean。</param>
        /// <param name="isRequired">是否为必填参数。</param>
        /// <param name="description">来自 <see cref="SummaryAttribute"/> 的说明，没标注时为空字符串。</param>
        public CommandParameterSchema(string parameterName, string parameterTypeName, bool isRequired, string description)
        {
            ParameterName = parameterName;
            ParameterTypeName = parameterTypeName;
            IsRequired = isRequired;
            Description = description;
        }

        /// <summary>参数属性名，例如 SolutionPath。</summary>
        public string ParameterName { get; }

        /// <summary>参数类型名，例如 String / Int32 / Boolean。</summary>
        public string ParameterTypeName { get; }

        /// <summary>是否为必填参数（没有 <c>DefaultValue</c> 标注即必填）。</summary>
        public bool IsRequired { get; }

        /// <summary>来自 <see cref="SummaryAttribute"/> 的说明，没标注时为空字符串。</summary>
        public string Description { get; }
    }
}
