using System;

namespace Template.Toolkit.CommandFramework
{
    /// <summary>
    /// 给命令方法或参数属性挂一段中文说明，供 schema 推导与 describe 输出使用。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class SummaryAttribute : Attribute
    {
        /// <summary>
        /// 用一段说明文本标注命令或参数。
        /// </summary>
        /// <param name="description">说明文本。</param>
        public SummaryAttribute(string description)
        {
            Description = description;
        }

        /// <summary>说明文本。</summary>
        public string Description { get; }
    }
}
