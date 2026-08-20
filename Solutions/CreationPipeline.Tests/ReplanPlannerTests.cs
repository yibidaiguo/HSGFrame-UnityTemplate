using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>打断重规划计划的测试：直接脏、传播脏、净项、后端评估名单与问人判定。</summary>
    public class ReplanPlannerTests
    {
        /// <summary>diff 命中某工作项的引用需求字段 → 它进 DirectlyDirty。</summary>
        [Fact]
        public void DiffHitMakesDirectlyDirty()
        {
            using var workspace = new PoolTestWorkspace();
            var graph = BuildChainGraph(workspace);

            var result = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);

            Assert.Contains("WI-0001-01", result.DirectlyDirty);
            Assert.Contains("WI-0001-02", result.DirectlyDirty);
        }

        /// <summary>它的下游进 PropagatedDirty 但不在 DirectlyDirty。</summary>
        [Fact]
        public void DownstreamIsPropagatedButNotDirectlyDirty()
        {
            using var workspace = new PoolTestWorkspace();
            var graph = BuildChainGraph(workspace);

            var result = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);

            Assert.Contains("WI-0001-03", result.PropagatedDirty);
            Assert.DoesNotContain("WI-0001-03", result.DirectlyDirty);
        }

        /// <summary>未命中且不在脏集的进 NeedsBackendEvaluation。</summary>
        [Fact]
        public void UnhitOutsideDirtyGoesToBackendEvaluation()
        {
            using var workspace = new PoolTestWorkspace();
            var graph = BuildChainGraph(workspace);

            var result = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);

            Assert.Contains("WI-0001-04", result.NeedsBackendEvaluation);
            Assert.DoesNotContain("WI-0001-04", result.PropagatedDirty);
        }

        /// <summary>脏项 + 净项 = 全部工作项，且两者无交集。</summary>
        [Fact]
        public void DirtyPlusCleanCoversAllWithoutOverlap()
        {
            using var workspace = new PoolTestWorkspace();
            var graph = BuildChainGraph(workspace);

            var result = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);

            Assert.Equal(4, result.PropagatedDirty.Count + result.Clean.Count);
            Assert.Empty(result.PropagatedDirty.Intersect(result.Clean));
            foreach (var node in graph.Nodes)
            {
                Assert.Contains(node.Identifier, result.PropagatedDirty.Concat(result.Clean));
            }
        }

        /// <summary>authoritativeFilesByWorkItem 里有个 key 落在脏集 → MustAskHuman 为 true。</summary>
        [Fact]
        public void AuthoritativeFileInDirtySetAsksHuman()
        {
            using var workspace = new PoolTestWorkspace();
            var graph = BuildChainGraph(workspace);
            var files = new Dictionary<string, IReadOnlyList<string>>
            {
                ["WI-0001-03"] = new[] { "30-产物/金币袋.png" }
            };

            var result = ReplanPlanner.Plan(graph, new[] { "玩法" }, files);

            Assert.True(result.MustAskHuman);
            Assert.Contains("30-产物/金币袋.png", result.AuthoritativeFilesInDirtySet);
        }

        /// <summary>changedRequirementFields 为空 → 全部为空 + 一条 finding。</summary>
        [Fact]
        public void EmptyDiffYieldsEmptyPlanWithFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var graph = BuildChainGraph(workspace);

            var result = ReplanPlanner.Plan(graph, Array.Empty<string>(), null);

            Assert.Empty(result.DirectlyDirty);
            Assert.Empty(result.PropagatedDirty);
            Assert.Empty(result.Clean);
            Assert.False(result.MustAskHuman);
            var finding = Assert.Single(result.Findings);
            Assert.Contains("零字段变更", finding);
        }

        /// <summary>有环的图 → 一条 finding 但照常算完。</summary>
        [Fact]
        public void CyclicGraphStillPlansWithFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = Path.Combine(workspace.Root, "_Tasks", "REQ-0002", "20-工作项");
            Directory.CreateDirectory(directory);
            WriteWorkItem(directory, "WI-0002-01", new[] { "玩法" }, new[] { "WI-0002-02" });
            WriteWorkItem(directory, "WI-0002-02", new[] { "目标" }, new[] { "WI-0002-01" });
            var graph = WorkItemGraph.Load(workspace.Root, "REQ-0002");

            var result = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);

            Assert.Contains("WI-0002-01", result.DirectlyDirty);
            Assert.Contains("WI-0002-02", result.PropagatedDirty);
            var finding = Assert.Single(result.Findings);
            Assert.Contains("环", finding);
        }

        /// <summary>建 A ← B ← C 的链加独立 D 的图。</summary>
        private static WorkItemGraph BuildChainGraph(PoolTestWorkspace workspace)
        {
            var directory = Path.Combine(workspace.Root, "_Tasks", "REQ-0001", "20-工作项");
            Directory.CreateDirectory(directory);
            WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>());
            WriteWorkItem(directory, "WI-0001-02", new[] { "玩法" }, new[] { "WI-0001-01" });
            WriteWorkItem(directory, "WI-0001-03", new[] { "目标" }, new[] { "WI-0001-02" });
            WriteWorkItem(directory, "WI-0001-04", new[] { "目标" }, Array.Empty<string>());
            return WorkItemGraph.Load(workspace.Root, "REQ-0001");
        }

        private static void WriteWorkItem(
            string directory,
            string identifier,
            IReadOnlyList<string> referencedRequirementFields,
            IReadOnlyList<string> dependencies)
        {
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
