using System;
using System.Collections.Generic;
using System.IO;
using GameTemplateForAgent.GraphAdapter;
using Xunit;

namespace GameTemplateForAgent.GraphAdapter.Tests
{
    /// <summary>图 JSON 往返与最小执行器的测试。</summary>
    public class GraphExecutorTests
    {
        [Fact]
        public void SampleGraphRoundTripsThroughJson()
        {
            var filePath = Path.Combine(FindTemplateRoot(), "Graphs", "示例流程图.json");
            var original = GraphJsonCodec.LoadFromFile(filePath);

            var json = GraphJsonCodec.ToJson(original);
            var roundTripped = GraphJsonCodec.FromJson(json);

            Assert.Equal("示例流程图", roundTripped.GraphId);
            Assert.Equal(5, roundTripped.Instances.Count);

            var branch = roundTripped.FindInstance("判断好感");
            Assert.Equal("分支", branch.NodeType);
            Assert.Equal("10", branch.Parameters["等于"]);
            Assert.Equal("友好结局", branch.Ports["真"]);
        }

        [Fact]
        public void SampleGraphRunsToFriendlyEnding()
        {
            var graph = LoadSampleGraph();

            var result = GraphExecutor.Run(graph);

            Assert.True(result.IsComplete);
            Assert.Equal(new[] { "起点", "判断好感", "友好结局", "结束" }, result.VisitedInstanceIds);
            Assert.Equal("10", result.Variables["好感度"]);
            Assert.Equal("友好", result.Variables["结局"]);
        }

        [Fact]
        public void BranchWalksFalseSideWhenConditionMismatches()
        {
            var graph = LoadSampleGraph();
            graph.FindInstance("判断好感").Parameters["等于"] = "99";

            var result = GraphExecutor.Run(graph);

            Assert.True(result.IsComplete);
            Assert.Equal(new[] { "起点", "判断好感", "冷淡结局", "结束" }, result.VisitedInstanceIds);
            Assert.Equal("冷淡", result.Variables["结局"]);
        }

        [Fact]
        public void MissingEntryReportsEntryInMessage()
        {
            var graph = LoadSampleGraph();
            graph.EntryInstanceIds.Clear();

            var result = GraphExecutor.Run(graph);

            Assert.False(result.IsComplete);
            Assert.Contains("入口", result.Message);
        }

        [Fact]
        public void MutualLoopReportsCycleInMessage()
        {
            var graph = new GraphDocument { GraphId = "成环图" };
            graph.EntryInstanceIds.Add("甲");
            graph.Instances.Add(CreateNode("甲", "设置变量", new Dictionary<string, string> { { "下一步", "乙" } }));
            graph.Instances.Add(CreateNode("乙", "设置变量", new Dictionary<string, string> { { "下一步", "甲" } }));

            var result = GraphExecutor.Run(graph);

            Assert.False(result.IsComplete);
            Assert.Contains("成环", result.Message);
        }

        [Fact]
        public void UnknownNodeTypeReportsTypeName()
        {
            var graph = new GraphDocument { GraphId = "未知类型图" };
            graph.EntryInstanceIds.Add("起点");
            graph.Instances.Add(CreateNode("起点", "没这个类型", new Dictionary<string, string>()));

            var result = GraphExecutor.Run(graph);

            Assert.False(result.IsComplete);
            Assert.Contains("没这个类型", result.Message);
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
