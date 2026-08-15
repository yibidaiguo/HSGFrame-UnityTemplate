using System.Collections.Generic;

namespace HSGFrame.GraphAdapter
{
    /// <summary>运行期变量表，执行过程中节点读写变量的唯一去处。</summary>
    public sealed class GraphBlackboard
    {
        private readonly Dictionary<string, string> _variables = new Dictionary<string, string>();

        /// <summary>设置一个变量。</summary>
        public void Set(string variableName, string value) => _variables[variableName] = value;

        /// <summary>读取一个变量，缺省返回空字符串。</summary>
        public string Get(string variableName) => _variables.TryGetValue(variableName, out var value) ? value : string.Empty;

        /// <summary>判断变量是否存在。</summary>
        public bool Contains(string variableName) => _variables.ContainsKey(variableName);

        /// <summary>取全部变量的只读快照。</summary>
        public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(_variables);
    }
}
