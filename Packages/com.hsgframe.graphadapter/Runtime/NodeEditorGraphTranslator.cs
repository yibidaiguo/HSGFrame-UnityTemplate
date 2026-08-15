using System;
using System.Collections.Generic;
using NodeEditor;

namespace HSGFrame.GraphAdapter
{
    /// <summary>把模板的图镜像翻译成 NodeGraph 运行时要的图、节点定义表与黑板声明。</summary>
    public static class NodeEditorGraphTranslator
    {
        /// <summary>设置变量节点的类型名。</summary>
        public const string SetVariableNodeType = "设置变量";

        /// <summary>分支节点的类型名。</summary>
        public const string BranchNodeType = "分支";

        /// <summary>结束节点的类型名。</summary>
        public const string EndNodeType = "结束";

        private static readonly IReadOnlyDictionary<string, string> _nodeKindByNodeType = new Dictionary<string, string>
        {
            [SetVariableNodeType] = "Action",
            [BranchNodeType] = "Condition",
            [EndNodeType] = "End",
        };

        // 节点定义表由上面那张映射表推出来，而不是另写一份字面量：两份各写各的时，
        // 加一种节点类型只改了其中一份就会变成「翻译得过、执行器却认不出」的沉默错误。
        private static readonly SchemaSet _schemas = new SchemaSet(BuildSchemas());

        /// <summary>节点类型名到上游节点种类的映射：设置变量→Action、分支→Condition、结束→End。</summary>
        public static IReadOnlyDictionary<string, string> NodeKindByNodeType => _nodeKindByNodeType;

        /// <summary>把一份镜像文档翻译成上游的图、定义表与黑板声明，翻译不了的地方抛 GraphTranslationException。</summary>
        public static NodeEditorGraphBundle Translate(GraphDocument document)
        {
            var graph = new GraphData
            {
                graphId = document.GraphId,
                graphType = GraphType.ControlFlow,
            };
            graph.entryInstanceIds.AddRange(document.EntryInstanceIds);

            var blackboardKeys = new HashSet<string>();

            foreach (var source in document.Instances)
            {
                if (!NodeKindByNodeType.TryGetValue(source.NodeType, out _))
                {
                    throw new GraphTranslationException(
                        "位置：图「" + document.GraphId + "」的节点实例「" + source.InstanceId + "」；" +
                        "原因：节点类型「" + source.NodeType + "」不在支持的映射表中；" +
                        "修复：把节点类型改成 设置变量、分支 或 结束 之一；" +
                        "参考：NodeEditorGraphTranslator.NodeKindByNodeType。");
                }

                var instance = new NodeInstance
                {
                    instanceId = source.InstanceId,
                    definitionId = source.NodeType,
                };

                foreach (var port in source.Ports)
                {
                    instance.connections.Add(new Connection
                    {
                        fromPort = MapPortName(port.Key),
                        toInstanceId = port.Value,
                    });
                }

                AttachUnit(instance, source, blackboardKeys);

                graph.instances.Add(instance);
            }

            var declaration = new BlackboardDecl();
            foreach (var key in blackboardKeys)
            {
                declaration.Add(key, TypeRef.String, string.Empty);
            }

            var blackboard = new BlackboardSet(new IBlackboardDecl[] { declaration });

            return new NodeEditorGraphBundle(graph, _schemas, blackboard);
        }

        // 每种节点类型烘一条定义：id 取类型名（实例的 definitionId 就填类型名），
        // kind 决定执行器怎么解释这个节点，role 是分类元数据，判定类的是 Condition，其余是 Action。
        private static IEnumerable<NodeSchema> BuildSchemas()
        {
            foreach (var pair in _nodeKindByNodeType)
            {
                yield return new NodeSchema
                {
                    id = pair.Key,
                    kind = pair.Value,
                    role = pair.Value == "Condition" ? NodeRole.Condition : NodeRole.Action,
                };
            }
        }

        // 把模板一侧的中文端口名换成上游执行器认识的英文端口名；映射表之外的端口名照原文搬过去，
        // 上游按名字找边，找不到就当断线，与模板一侧的语义一致。
        private static string MapPortName(string portName)
        {
            switch (portName)
            {
                case "下一步": return "next";
                case "真": return "true";
                case "假": return "false";
                default: return portName;
            }
        }

        // 把动作与判定挂到实例的单元槽上，同时把用到的变量名收集进黑板声明键集合。
        // 参数不齐时不挂槽：上游对空槽的语义就是「什么也不做」，与模板一侧一致。
        private static void AttachUnit(NodeInstance instance, GraphNodeInstance source, HashSet<string> blackboardKeys)
        {
            if (source.NodeType == SetVariableNodeType)
            {
                var hasVariableName = source.Parameters.TryGetValue("变量名", out var variableName);
                var hasValue = source.Parameters.TryGetValue("值", out var value);
                if (hasVariableName && hasValue)
                {
                    instance.unitOverrides.Add(new UnitOverride
                    {
                        paramName = "actions",
                        value = new SetVariableLiteralAction { key = variableName, value = value },
                    });
                }

                if (hasVariableName)
                {
                    blackboardKeys.Add(variableName);
                }
            }
            else if (source.NodeType == BranchNodeType)
            {
                var hasVariableName = source.Parameters.TryGetValue("变量名", out var variableName);
                var hasExpected = source.Parameters.TryGetValue("等于", out var expected);
                if (hasVariableName && hasExpected)
                {
                    instance.unitOverrides.Add(new UnitOverride
                    {
                        paramName = "predicate",
                        value = new BlackboardCompareCondition { key = variableName, op = CompareOp.Eq, value = expected },
                    });
                }

                if (hasVariableName)
                {
                    blackboardKeys.Add(variableName);
                }
            }
            // 「结束」节点不挂槽，也不收集变量名。
        }
    }

    /// <summary>一次翻译的产物：上游要的图、节点定义表与黑板声明，三样一起交给执行器。</summary>
    public sealed class NodeEditorGraphBundle
    {
        /// <summary>翻译出的上游图。</summary>
        public GraphData Graph { get; }

        /// <summary>节点定义表，按节点类型名索引。</summary>
        public SchemaSet Schemas { get; }

        /// <summary>黑板声明，变量统一按字符串解析。</summary>
        public BlackboardSet Blackboard { get; }

        internal NodeEditorGraphBundle(GraphData graph, SchemaSet schemas, BlackboardSet blackboard)
        {
            Graph = graph;
            Schemas = schemas;
            Blackboard = blackboard;
        }
    }

    /// <summary>图翻译失败时抛出，消息按四要素书写。</summary>
    public sealed class GraphTranslationException : Exception
    {
        /// <summary>用一条四要素消息构造翻译异常。</summary>
        public GraphTranslationException(string message) : base(message)
        {
        }

        /// <summary>用一条四要素消息与内层异常构造翻译异常。</summary>
        public GraphTranslationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
