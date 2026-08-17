using System.Collections.Generic;

namespace HSGFrame.GraphAdapter
{
    /// <summary>旧兼容投影的最小执行器：用于回归测试，不是资产创作入口。</summary>
    public static class GraphExecutor
    {
        /// <summary>执行一张图，从入口节点开始逐节点推进直到结束或失败。</summary>
        public static GraphRunResult Run(GraphDocument graph, int maxSteps = 128)
        {
            var blackboard = new GraphBlackboard();
            var visited = new List<string>();
            var result = new GraphRunResult();

            if (graph.EntryInstanceIds.Count == 0)
            {
                result.Message = "图缺少入口节点";
                return result;
            }

            var currentInstanceId = graph.EntryInstanceIds[0];

            for (var step = 0; step < maxSteps; step++)
            {
                var instance = graph.FindInstance(currentInstanceId);
                if (instance == null)
                {
                    return Fail(result, visited, blackboard, "端口指向的节点实例「" + currentInstanceId + "」不存在");
                }

                visited.Add(currentInstanceId);

                if (instance.NodeType == "设置变量")
                {
                    if (instance.Parameters.TryGetValue("变量名", out var variableName)
                        && instance.Parameters.TryGetValue("值", out var value))
                    {
                        blackboard.Set(variableName, value);
                    }

                    currentInstanceId = NextInstanceId(instance, "下一步");
                }
                else if (instance.NodeType == "分支")
                {
                    var variableName = instance.Parameters.TryGetValue("变量名", out var name) ? name : string.Empty;
                    var expected = instance.Parameters.TryGetValue("等于", out var expectedValue) ? expectedValue : string.Empty;
                    var branchPort = blackboard.Get(variableName) == expected ? "真" : "假";
                    currentInstanceId = NextInstanceId(instance, branchPort);
                }
                else if (instance.NodeType == "结束")
                {
                    result.IsComplete = true;
                    result.VisitedInstanceIds = visited;
                    result.Variables = blackboard.Snapshot();
                    return result;
                }
                else
                {
                    return Fail(result, visited, blackboard,
                        "未知节点类型「" + instance.NodeType + "」，实例编号「" + instance.InstanceId + "」");
                }

                if (currentInstanceId == null)
                {
                    return Fail(result, visited, blackboard, "节点「" + instance.InstanceId + "」缺少下一节点端口");
                }
            }

            // 成环保护是必须的：图数据由 AI 生成，死循环会把无人值守的流水线挂死。
            return Fail(result, visited, blackboard, "疑似成环，已在 " + maxSteps + " 步后中止");
        }

        private static GraphRunResult Fail(
            GraphRunResult result,
            IReadOnlyList<string> visited,
            GraphBlackboard blackboard,
            string message)
        {
            result.Message = message;
            result.VisitedInstanceIds = visited;
            result.Variables = blackboard.Snapshot();
            return result;
        }

        private static string NextInstanceId(GraphNodeInstance instance, string portName)
            => instance.Ports.TryGetValue(portName, out var next) ? next : null;
    }
}
