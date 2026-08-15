using System.Collections.Generic;

namespace Template.Hotfix
{
    /// <summary>出包验收里被序列化的样本类型，字段覆盖字符串、整数与集合三种形状。</summary>
    public sealed class HotfixProbeDocument
    {
        /// <summary>标题，用中文值顺带验 UTF-8 往返。</summary>
        public string Title { get; set; }

        /// <summary>计数，验数值往返。</summary>
        public int Count { get; set; }

        /// <summary>标签集合，验泛型集合在 IL2CPP 下的往返（泛型是 AOT 最容易缺实例的地方）。</summary>
        public List<string> Tags { get; set; }
    }
}
