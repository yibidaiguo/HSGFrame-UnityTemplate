using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>工作项依赖图的单个节点：id、依赖、引用需求字段与状态。</summary>
    public sealed class WorkItemNode
    {
        /// <summary>
        /// 构造一个工作项节点。
        /// </summary>
        /// <param name="identifier">工作项 id，形如 WI-0042-03。</param>
        /// <param name="dependencies">依赖的工作项 id 列表。</param>
        /// <param name="referencedRequirementFields">引用的需求字段名列表。</param>
        /// <param name="state">工作项状态。</param>
        internal WorkItemNode(
            string identifier,
            IReadOnlyList<string> dependencies,
            IReadOnlyList<string> referencedRequirementFields,
            string state)
        {
            Identifier = identifier;
            Dependencies = dependencies;
            ReferencedRequirementFields = referencedRequirementFields;
            State = state;
        }

        /// <summary>工作项 id，形如 WI-0042-03。</summary>
        public string Identifier { get; }

        /// <summary>依赖的工作项 id 列表。</summary>
        public IReadOnlyList<string> Dependencies { get; }

        /// <summary>引用的需求字段名列表，供重规划的影响映射用。</summary>
        public IReadOnlyList<string> ReferencedRequirementFields { get; }

        /// <summary>工作项状态。</summary>
        public string State { get; }
    }

    /// <summary>
    /// 工作项依赖图：从 _Tasks/&lt;需求id&gt;/20-工作项/ 逐文件加载，支持沿依赖边的脏传播与环检测。
    /// 目录不存在返回空图不抛；单个坏文件跳过并累加原因。
    /// </summary>
    public sealed class WorkItemGraph
    {
        private readonly IReadOnlyList<WorkItemNode> _nodes;

        /// <summary>
        /// 构造一个工作项依赖图。
        /// </summary>
        /// <param name="nodes">全部工作项节点，按 id 序数序。</param>
        /// <param name="loadFailureReason">加载失败原因，正常时为空串。</param>
        internal WorkItemGraph(IReadOnlyList<WorkItemNode> nodes, string loadFailureReason)
        {
            _nodes = nodes;
            LoadFailureReason = loadFailureReason;
        }

        /// <summary>全部工作项节点，按 id 序数序排列。</summary>
        public IReadOnlyList<WorkItemNode> Nodes
        {
            get { return _nodes; }
        }

        /// <summary>加载失败原因；正常（含空图）为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 加载某需求的全部工作项：目录为 _Tasks/&lt;需求id&gt;/20-工作项/，一项一文件。
        /// 读不到目录返回空图不抛；单个坏文件跳过并累加原因到 LoadFailureReason。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static WorkItemGraph Load(string repositoryRoot, string requirementIdentifier)
        {
            var directory = Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "20-工作项");
            if (!Directory.Exists(directory))
            {
                return new WorkItemGraph(Array.Empty<WorkItemNode>(), "");
            }

            var nodes = new List<WorkItemNode>();
            var failures = new List<string>();
            foreach (var filePath in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(filePath));
                    if (root is not JsonObject entryObject)
                    {
                        failures.Add($"{Path.GetFileName(filePath)}：顶层不是对象，已跳过");
                        continue;
                    }

                    if (!TryReadNode(entryObject, out var node, out var failureReason))
                    {
                        failures.Add($"{Path.GetFileName(filePath)}：{failureReason}，已跳过");
                        continue;
                    }

                    nodes.Add(node);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    failures.Add($"{Path.GetFileName(filePath)}：{exception.Message}，已跳过");
                }
            }

            nodes.Sort((left, right) => string.CompareOrdinal(left.Identifier, right.Identifier));
            var reason = failures.Count == 0 ? "" : string.Join("；", failures);
            return new WorkItemGraph(nodes, reason);
        }

        /// <summary>
        /// 沿依赖边向下游传播脏标记：传入的 id 直接脏，任何「依赖」含脏项的工作项也脏，递归到不动点。
        /// 用已访问集合防环，环上不会栈溢出；传入空列表返回空列表。返回全部脏项（含传入项），按序数序去重。
        /// </summary>
        /// <param name="directlyDirtyIdentifiers">直接脏的工作项 id 列表。</param>
        public IReadOnlyList<string> PropagateDirty(IReadOnlyList<string> directlyDirtyIdentifiers)
        {
            if (directlyDirtyIdentifiers == null || directlyDirtyIdentifiers.Count == 0)
            {
                return Array.Empty<string>();
            }

            var downstream = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var node in _nodes)
            {
                foreach (var dependency in node.Dependencies)
                {
                    if (!downstream.TryGetValue(dependency, out var children))
                    {
                        children = new List<string>();
                        downstream[dependency] = children;
                    }

                    children.Add(node.Identifier);
                }
            }

            var dirty = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var identifier in directlyDirtyIdentifiers)
            {
                if (dirty.Add(identifier))
                {
                    queue.Enqueue(identifier);
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!downstream.TryGetValue(current, out var children))
                {
                    continue;
                }

                foreach (var child in children)
                {
                    if (dirty.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            var result = new List<string>(dirty);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>依赖图上是否有环（A 依赖 B、B 依赖 A 之类）；无环返回 false。</summary>
        public bool HasCycle()
        {
            var byIdentifier = new Dictionary<string, WorkItemNode>(StringComparer.Ordinal);
            foreach (var node in _nodes)
            {
                byIdentifier[node.Identifier] = node;
            }

            // 0 = 未访问，1 = 访问中，2 = 完成；DFS 三色法，访问中再遇即成环。
            var states = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var identifier in byIdentifier.Keys)
            {
                states[identifier] = 0;
            }

            foreach (var identifier in byIdentifier.Keys)
            {
                if (HasCycleFrom(identifier, byIdentifier, states))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>从某节点出发沿依赖边做 DFS，返回是否遇到环。</summary>
        private static bool HasCycleFrom(
            string identifier,
            IReadOnlyDictionary<string, WorkItemNode> byIdentifier,
            IDictionary<string, int> states)
        {
            if (!states.TryGetValue(identifier, out var state))
            {
                // 依赖指向图外的 id：它没有出边，不算环。
                return false;
            }

            if (state == 1)
            {
                return true;
            }

            if (state == 2)
            {
                return false;
            }

            states[identifier] = 1;
            if (byIdentifier.TryGetValue(identifier, out var node))
            {
                foreach (var dependency in node.Dependencies)
                {
                    if (HasCycleFrom(dependency, byIdentifier, states))
                    {
                        return true;
                    }
                }
            }

            states[identifier] = 2;
            return false;
        }

        /// <summary>读一个工作项节点；id 缺失或类型不对、依赖/引用需求字段不是数组都算坏文件。</summary>
        private static bool TryReadNode(JsonObject obj, out WorkItemNode node, out string failureReason)
        {
            node = null;
            failureReason = "";

            if (!TryReadString(obj, "id", out var identifier) || identifier.Length == 0)
            {
                failureReason = "缺少 id";
                return false;
            }

            if (!TryReadStringArray(obj, "依赖", out var dependencies))
            {
                failureReason = "缺少 依赖 数组";
                return false;
            }

            if (!TryReadStringArray(obj, "引用需求字段", out var referencedRequirementFields))
            {
                failureReason = "缺少 引用需求字段 数组";
                return false;
            }

            TryReadString(obj, "状态", out var state);
            node = new WorkItemNode(identifier, dependencies, referencedRequirementFields, state);
            return true;
        }

        /// <summary>读必须为字符串数组的键；缺失、null 或类型不对返回 false。</summary>
        private static bool TryReadStringArray(JsonObject obj, string key, out IReadOnlyList<string> values)
        {
            values = Array.Empty<string>();
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
            {
                return false;
            }

            var result = new List<string>();
            foreach (var element in array)
            {
                if (element is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String)
                {
                    var value = jsonValue.GetValue<string>();
                    if (value != null)
                    {
                        result.Add(value);
                    }
                }
            }

            values = result;
            return true;
        }

        /// <summary>读必须为字符串的键；缺失、null 或类型不对返回 false。</summary>
        private static bool TryReadString(JsonObject obj, string key, out string value)
        {
            value = "";
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue)
            {
                return false;
            }

            if (jsonValue.GetValueKind() != JsonValueKind.String)
            {
                return false;
            }

            value = jsonValue.GetValue<string>() ?? "";
            return true;
        }
    }
}
