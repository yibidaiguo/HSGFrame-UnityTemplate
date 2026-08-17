using System;
using System.Collections.Generic;
using System.Globalization;
using Dialogue;
using NodeEditor;

namespace HSGFrame.GraphAdapter
{
    /// <summary>用 NodeGraph 运行时执行旧兼容投影，供迁移期对拍。</summary>
    public static class NodeEditorGraphRunner
    {
        /// <summary>执行一张图，返回与 GraphExecutor.Run 同形状的结果。</summary>
        public static GraphRunResult Run(GraphDocument document)
        {
            var result = new GraphRunResult();

            if (document.EntryInstanceIds.Count == 0)
            {
                result.Message = "图缺少入口节点";
                return result;
            }

            NodeEditorGraphBundle bundle;
            try
            {
                bundle = NodeEditorGraphTranslator.Translate(document);
            }
            catch (GraphTranslationException exception)
            {
                result.Message = exception.Message;
                return result;
            }

            // 上游在正常走到 End 节点时触发 OnEnd，但在端口断线、入口实例缺失等兜底收尾时也会触发一次，
            // 所以仅凭 OnEnd 无法区分「正常完成」与「兜底结束」，见下面用 reachedEnd 修正。
            var endRaised = false;
            var runner = new DialogueRunner(bundle.Schemas, bundle.Blackboard, null, string.Empty);
            runner.OnEnd += () => endRaised = true;
            runner.Run(bundle.Graph);

            // 上游只用一个集合记走过的节点，对外只有 StatusOf 这个快照式查询，取不到有序的执行轨迹，
            // 所以这里的访问序列只能是「按图里实例的声明顺序」过滤掉没走到的节点。
            var visited = new List<string>();
            var reachedEnd = false;
            foreach (var instance in document.Instances)
            {
                var status = runner.StatusOf(instance.InstanceId);
                if (status != Status.None)
                {
                    visited.Add(instance.InstanceId);
                }

                if (instance.NodeType == NodeEditorGraphTranslator.EndNodeType && status != Status.None)
                {
                    reachedEnd = true;
                }
            }

            result.IsComplete = endRaised && reachedEnd;
            result.VisitedInstanceIds = visited;

            var variables = new Dictionary<string, string>();
            foreach (var layer in bundle.Blackboard.Layers)
            {
                foreach (var variable in layer.Variables)
                {
                    variables[variable.key] = ConvertToString(runner.Blackboard.Get(variable.key));
                }
            }

            result.Variables = variables;
            return result;
        }

        // 把黑板里读回的 object 转成字符串：null 用空串（与 GraphBlackboard.Get 取不到时返回空串一致），
        // 数字与布尔走 InvariantCulture 以免小数点与本地化符号干扰。
        private static string ConvertToString(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }
    }
}
