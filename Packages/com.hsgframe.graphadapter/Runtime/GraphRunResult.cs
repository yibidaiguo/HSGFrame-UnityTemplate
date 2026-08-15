using System.Collections.Generic;

namespace HSGFrame.GraphAdapter
{
    /// <summary>一次图执行的结果：是否完成、消息、访问轨迹与最终变量表。</summary>
    public sealed class GraphRunResult
    {
        /// <summary>是否正常走到结束节点。</summary>
        public bool IsComplete { get; set; }

        /// <summary>结果消息：失败时说明原因，成功时为空字符串。</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>访问过的实例编号，按执行顺序排列。</summary>
        public IReadOnlyList<string> VisitedInstanceIds { get; set; } = new List<string>();

        /// <summary>执行结束时的变量快照。</summary>
        public IReadOnlyDictionary<string, string> Variables { get; set; } = new Dictionary<string, string>();
    }
}
