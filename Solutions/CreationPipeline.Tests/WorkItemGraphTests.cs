using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>工作项依赖图加载、脏传播与环检测的测试。</summary>
    public class WorkItemGraphTests
    {
        /// <summary>工作项目录不存在时返回空图、不抛异常。</summary>
        [Fact]
        public void MissingDirectoryLoadsEmptyGraph()
        {
            using var workspace = new PoolTestWorkspace();
            var graph = WorkItemGraph.Load(workspace.Root, "REQ-9999");

            Assert.Empty(graph.Nodes);
            Assert.Equal("", graph.LoadFailureReason);
        }

        /// <summary>A ← B ← C 的链：A 脏 → 传播出 A、B、C 三个。</summary>
        [Fact]
        public void DirtyPropagatesDownstreamChain()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.Root, "REQ-0001");
            WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>());
            WriteWorkItem(directory, "WI-0001-02", new[] { "玩法" }, new[] { "WI-0001-01" });
            WriteWorkItem(directory, "WI-0001-03", new[] { "目标" }, new[] { "WI-0001-02" });

            var graph = WorkItemGraph.Load(workspace.Root, "REQ-0001");
            var dirty = graph.PropagateDirty(new[] { "WI-0001-01" });

            Assert.Equal(3, dirty.Count);
            Assert.Contains("WI-0001-01", dirty);
            Assert.Contains("WI-0001-02", dirty);
            Assert.Contains("WI-0001-03", dirty);
        }

        /// <summary>无关的 D 不进脏集。</summary>
        [Fact]
        public void UnrelatedNodeStaysClean()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.Root, "REQ-0001");
            WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>());
            WriteWorkItem(directory, "WI-0001-02", new[] { "玩法" }, new[] { "WI-0001-01" });
            WriteWorkItem(directory, "WI-0001-04", new[] { "目标" }, Array.Empty<string>());

            var graph = WorkItemGraph.Load(workspace.Root, "REQ-0001");
            var dirty = graph.PropagateDirty(new[] { "WI-0001-01" });

            Assert.DoesNotContain("WI-0001-04", dirty);
        }

        /// <summary>A 依赖 B、B 依赖 A 的环：HasCycle 为 true 且 PropagateDirty 正常返回不栈溢出。</summary>
        [Fact]
        public void CycleIsDetectedAndPropagationTerminates()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.Root, "REQ-0001");
            WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, new[] { "WI-0001-02" });
            WriteWorkItem(directory, "WI-0001-02", new[] { "目标" }, new[] { "WI-0001-01" });

            var graph = WorkItemGraph.Load(workspace.Root, "REQ-0001");
            Assert.True(graph.HasCycle());

            var dirty = graph.PropagateDirty(new[] { "WI-0001-01" });
            Assert.Equal(2, dirty.Count);
            Assert.Contains("WI-0001-01", dirty);
            Assert.Contains("WI-0001-02", dirty);
        }

        /// <summary>传入空列表 → 返回空列表。</summary>
        [Fact]
        public void EmptyInputReturnsEmpty()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.Root, "REQ-0001");
            WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>());

            var graph = WorkItemGraph.Load(workspace.Root, "REQ-0001");
            var dirty = graph.PropagateDirty(Array.Empty<string>());

            Assert.Empty(dirty);
        }

        private static string WorkItemDirectory(string repositoryRoot, string requirementIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "20-工作项");
        }

        private static void WriteWorkItem(
            string directory,
            string identifier,
            IReadOnlyList<string> referencedRequirementFields,
            IReadOnlyList<string> dependencies)
        {
            Directory.CreateDirectory(directory);
            var entryObject = new JsonObject
            {
                ["id"] = identifier,
                ["需求id"] = "REQ-0001",
                ["域"] = "文档",
                ["标题"] = "t",
                ["状态"] = "待执行",
                ["依赖"] = ToJsonArray(dependencies),
                ["验收点"] = "v",
                ["引用需求字段"] = ToJsonArray(referencedRequirementFields),
                ["产物"] = new JsonArray()
            };
            File.WriteAllText(Path.Combine(directory, identifier + ".json"), entryObject.ToJsonString(), new UTF8Encoding(false));
        }

        private static JsonArray ToJsonArray(IReadOnlyList<string> values)
        {
            var array = new JsonArray();
            foreach (var value in values)
            {
                array.Add(value);
            }

            return array;
        }
    }
}
