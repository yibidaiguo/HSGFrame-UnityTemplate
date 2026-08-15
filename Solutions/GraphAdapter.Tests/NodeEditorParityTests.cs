using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameTemplateForAgent.GraphAdapter;
using Xunit;

namespace GameTemplateForAgent.GraphAdapter.Tests
{
    /// <summary>真执行器与自带最小执行器的对拍，以及翻译层与真执行器自身的边界测试。</summary>
    public class NodeEditorParityTests
    {
        // ---- 对拍：同一份图喂给两个执行器，比较结果 ----

        [Fact]
        public void SampleGraphCompletesOnBothExecutors()
        {
            var graph = LoadSampleGraph();

            Assert.True(GraphExecutor.Run(graph).IsComplete);
            Assert.True(NodeEditorGraphRunner.Run(graph).IsComplete);
        }

        [Fact]
        public void SampleGraphVisitedIdsMatchAsSets()
        {
            var graph = LoadSampleGraph();

            var minimal = GraphExecutor.Run(graph).VisitedInstanceIds.OrderBy(id => id);
            var real = NodeEditorGraphRunner.Run(graph).VisitedInstanceIds.OrderBy(id => id);

            Assert.Equal(minimal, real);
        }

        [Fact]
        public void SampleGraphVariablesMatch()
        {
            var graph = LoadSampleGraph();

            var minimal = GraphExecutor.Run(graph).Variables;
            var real = NodeEditorGraphRunner.Run(graph).Variables;

            Assert.Equal(minimal.Keys.OrderBy(key => key), real.Keys.OrderBy(key => key));
            foreach (var key in minimal.Keys)
            {
                Assert.Equal(minimal[key], real[key]);
            }
        }

        [Fact]
        public void FalseBranchCompletesOnBothExecutors()
        {
            var graph = LoadSampleGraph();
            graph.FindInstance("判断好感").Parameters["等于"] = "99";

            Assert.True(GraphExecutor.Run(graph).IsComplete);
            Assert.True(NodeEditorGraphRunner.Run(graph).IsComplete);
        }

        [Fact]
        public void FalseBranchVisitedIdsMatchAsSets()
        {
            var graph = LoadSampleGraph();
            graph.FindInstance("判断好感").Parameters["等于"] = "99";

            var minimal = GraphExecutor.Run(graph).VisitedInstanceIds;
            var real = NodeEditorGraphRunner.Run(graph).VisitedInstanceIds;

            Assert.Equal(minimal.OrderBy(id => id), real.OrderBy(id => id));
            Assert.Contains("冷淡结局", real);
            Assert.DoesNotContain("友好结局", real);
        }

        [Fact]
        public void FalseBranchVariablesCarryColdEnding()
        {
            var graph = LoadSampleGraph();
            graph.FindInstance("判断好感").Parameters["等于"] = "99";

            var minimal = GraphExecutor.Run(graph).Variables;
            var real = NodeEditorGraphRunner.Run(graph).Variables;

            Assert.Equal("冷淡", minimal["结局"]);
            Assert.Equal("冷淡", real["结局"]);
        }

        [Fact]
        public void MissingEntryFailsOnBothExecutors()
        {
            var graph = LoadSampleGraph();
            graph.EntryInstanceIds.Clear();

            var minimal = GraphExecutor.Run(graph);
            var real = NodeEditorGraphRunner.Run(graph);

            Assert.False(minimal.IsComplete);
            Assert.False(real.IsComplete);
            Assert.Contains("入口", minimal.Message);
            Assert.Contains("入口", real.Message);
        }

        // ---- 真执行器自身的边界与失败路径 ----

        [Fact]
        public void UnknownNodeTypeFailsWithFourPartMessage()
        {
            var graph = new GraphDocument { GraphId = "未知类型图" };
            graph.EntryInstanceIds.Add("起点");
            graph.Instances.Add(CreateNode("起点", "没这个类型", new Dictionary<string, string>()));

            var result = NodeEditorGraphRunner.Run(graph);

            Assert.False(result.IsComplete);
            Assert.Contains("没这个类型", result.Message);
            Assert.Contains("位置", result.Message);
            Assert.Contains("原因", result.Message);
            Assert.Contains("修复", result.Message);
            Assert.Contains("参考", result.Message);
        }

        [Fact]
        public void EmptyInstancesWithEntryDoesNotCompleteOrThrow()
        {
            var graph = new GraphDocument { GraphId = "空实例图" };
            graph.EntryInstanceIds.Add("起点");

            // 入口指向的实例不存在，上游兜底收尾，既不该谎报完成，也不该抛异常。
            var result = NodeEditorGraphRunner.Run(graph);

            Assert.False(result.IsComplete);
        }

        [Fact]
        public void DanglingPortDoesNotCompleteOrThrow()
        {
            var graph = LoadSampleGraph();
            graph.FindInstance("起点").Ports["下一步"] = "不存在";

            // 端口指向不存在的实例编号，上游兜底收尾，既不该谎报完成，也不该抛异常。
            var result = NodeEditorGraphRunner.Run(graph);

            Assert.False(result.IsComplete);
        }

        [Fact]
        public void VariableSetTwiceKeepsLastValue()
        {
            var graph = new GraphDocument { GraphId = "连设图" };
            graph.EntryInstanceIds.Add("甲");
            var first = CreateNode("甲", "设置变量", new Dictionary<string, string> { { "下一步", "乙" } });
            first.Parameters["变量名"] = "v";
            first.Parameters["值"] = "1";
            var second = CreateNode("乙", "设置变量", new Dictionary<string, string> { { "下一步", "结束" } });
            second.Parameters["变量名"] = "v";
            second.Parameters["值"] = "2";
            graph.Instances.Add(first);
            graph.Instances.Add(second);
            graph.Instances.Add(CreateNode("结束", "结束", new Dictionary<string, string>()));

            var result = NodeEditorGraphRunner.Run(graph);

            Assert.True(result.IsComplete);
            Assert.Equal("2", result.Variables["v"]);
        }

        [Fact]
        public void RunningSameDocumentTwiceYieldsEqualResults()
        {
            var graph = LoadSampleGraph();

            var first = NodeEditorGraphRunner.Run(graph);
            var second = NodeEditorGraphRunner.Run(graph);

            Assert.Equal(first.IsComplete, second.IsComplete);
            Assert.Equal(first.VisitedInstanceIds.OrderBy(id => id), second.VisitedInstanceIds.OrderBy(id => id));
            Assert.Equal(first.Variables.OrderBy(pair => pair.Key), second.Variables.OrderBy(pair => pair.Key));
        }

        // ---- 翻译层 ----

        [Fact]
        public void TranslateMapsChinesePortNameToEnglish()
        {
            var graph = LoadSampleGraph();

            var bundle = NodeEditorGraphTranslator.Translate(graph);

            var start = bundle.Graph.Find("起点");
            var connection = Assert.Single(start.connections);
            Assert.Equal("next", connection.fromPort);
        }

        [Fact]
        public void TranslateDeclaresBlackboardKeys()
        {
            var graph = LoadSampleGraph();

            var bundle = NodeEditorGraphTranslator.Translate(graph);

            var keys = bundle.Blackboard.Layers[0].Variables.Select(variable => variable.key).ToHashSet();
            Assert.Contains("好感度", keys);
            Assert.Contains("结局", keys);
        }

        private static GraphDocument LoadSampleGraph()
        {
            var filePath = Path.Combine(FindTemplateRoot(), "Graphs", "示例流程图.json");
            return GraphJsonCodec.LoadFromFile(filePath);
        }

        private static GraphNodeInstance CreateNode(
            string instanceId,
            string nodeType,
            IDictionary<string, string> ports)
        {
            var instance = new GraphNodeInstance { InstanceId = instanceId, NodeType = nodeType };
            foreach (var pair in ports)
            {
                instance.Ports[pair.Key] = pair.Value;
            }

            return instance;
        }

        private static string FindTemplateRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Tools", "Gates", "Config", "gate-config.json")))
            {
                directory = directory.Parent;
            }

            return directory == null
                ? throw new InvalidOperationException("找不到仓库根目录")
                : directory.FullName;
        }
    }
}
