using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 重规划落地器测试：三道拒绝闸磁盘零变化、先证据后状态、脏项标脏净项保留、回方案关不清账。
    /// </summary>
    public class ReplanLandingTests
    {
        /// <summary>零脏项 → Applied 为 false，原因含「零脏项」，磁盘上一个文件都没多、没变。</summary>
        [Fact]
        public void ZeroDirtyItemsRefusesAndWritesNothing()
        {
            using var workspace = new PoolTestWorkspace();
            var graph = WorkItemGraph.Load(workspace.RepositoryRoot, "REQ-0001");
            var plan = ReplanPlanner.Plan(graph, Array.Empty<string>(), null);

            var result = ReplanLanding.Apply(workspace.RepositoryRoot, "REQ-0001", plan, graph, "{}", false);

            Assert.False(result.Applied);
            Assert.Contains("零脏项", result.RefusalReason);
            // 任务目录根本没建出来：一个字都没写。
            Assert.False(Directory.Exists(PipelinePaths.TaskDirectory(workspace.RepositoryRoot, "REQ-0001")));
        }

        /// <summary>有「执行中」工作项 → 拒绝，原因点名那个 id，磁盘零变化。</summary>
        [Fact]
        public void RunningWorkItemRefusesAndWritesNothing()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.RepositoryRoot, "REQ-0001");
            Directory.CreateDirectory(directory);
            var before = WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>(), "执行中", "画金币袋");
            var graph = WorkItemGraph.Load(workspace.RepositoryRoot, "REQ-0001");
            var plan = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);

            var result = ReplanLanding.Apply(workspace.RepositoryRoot, "REQ-0001", plan, graph, "{}", false);

            Assert.False(result.Applied);
            Assert.Contains("WI-0001-01", result.RefusalReason);
            Assert.Contains("执行中", result.RefusalReason);
            AssertNoLandingFiles(workspace);
            Assert.Equal(before, File.ReadAllText(Path.Combine(directory, "WI-0001-01.json")));
        }

        /// <summary>MustAskHuman 且未确认 → 拒绝，原因列出人改权威文件路径，磁盘零变化。</summary>
        [Fact]
        public void MustAskHumanWithoutConfirmationRefusesAndWritesNothing()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.RepositoryRoot, "REQ-0001");
            Directory.CreateDirectory(directory);
            var before = WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>(), "待执行", "画金币袋");
            var graph = WorkItemGraph.Load(workspace.RepositoryRoot, "REQ-0001");
            var files = new Dictionary<string, IReadOnlyList<string>>
            {
                ["WI-0001-01"] = new[] { "30-outputs/金币袋.png" }
            };
            var plan = ReplanPlanner.Plan(graph, new[] { "玩法" }, files);
            Assert.True(plan.MustAskHuman);

            var result = ReplanLanding.Apply(workspace.RepositoryRoot, "REQ-0001", plan, graph, "{}", false);

            Assert.False(result.Applied);
            Assert.Contains("30-outputs/金币袋.png", result.RefusalReason);
            AssertNoLandingFiles(workspace);
            Assert.Equal(before, File.ReadAllText(Path.Combine(directory, "WI-0001-01.json")));
        }

        /// <summary>MustAskHuman 且已确认 → 落地，且 Findings 里有「人已确认」那条留痕。</summary>
        [Fact]
        public void MustAskHumanWithConfirmationLandsAndRecordsFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.RepositoryRoot, "REQ-0001");
            Directory.CreateDirectory(directory);
            WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>(), "待执行", "画金币袋");
            WriteDefaultTaskState(workspace, "REQ-0001");
            var graph = WorkItemGraph.Load(workspace.RepositoryRoot, "REQ-0001");
            var files = new Dictionary<string, IReadOnlyList<string>>
            {
                ["WI-0001-01"] = new[] { "30-outputs/金币袋.png" }
            };
            var plan = ReplanPlanner.Plan(graph, new[] { "玩法" }, files);
            Assert.True(plan.MustAskHuman);

            var result = ReplanLanding.Apply(
                workspace.RepositoryRoot,
                "REQ-0001",
                plan,
                graph,
                "{\"id\":\"REQ-0001\",\"标题\":\"金币袋\"}",
                true);

            Assert.True(result.Applied);
            Assert.Contains(result.Findings, finding => finding.Contains("人已确认"));
        }

        /// <summary>
        /// 正常落地：快照 v1 写出来、05-change-impact.md 写出来且七个小节标题都在、
        /// 脏项的 状态 变成 标脏、净项文件内容逐字节未变、
        /// 状态.json 阶段是 方案、子状态是 停在关卡、关卡待审是 方案，预算一字未变（不清账）。
        /// </summary>
        [Fact]
        public void NormalLandingWritesEvidenceThenChangesStateAndKeepsBudget()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.RepositoryRoot, "REQ-0001");
            Directory.CreateDirectory(directory);
            WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>(), "待执行", "画金币袋");
            WriteWorkItem(directory, "WI-0001-02", new[] { "目标" }, new[] { "WI-0001-01" }, "待执行", "摆关卡");
            var cleanBefore = WriteWorkItem(directory, "WI-0001-03", new[] { "美术风格" }, Array.Empty<string>(), "待执行", "定配色");
            WriteDefaultTaskState(workspace, "REQ-0001");
            var graph = WorkItemGraph.Load(workspace.RepositoryRoot, "REQ-0001");
            var plan = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);
            Assert.Equal(2, plan.PropagatedDirty.Count);
            Assert.Contains("WI-0001-03", plan.Clean);

            var requirementJsonText = "{\"id\":\"REQ-0001\",\"标题\":\"金币袋\",\"玩法\":\"翻滚\"}";
            var result = ReplanLanding.Apply(
                workspace.RepositoryRoot,
                "REQ-0001",
                plan,
                graph,
                requirementJsonText,
                false);

            Assert.True(result.Applied);
            Assert.Equal(1, result.SnapshotVersion);
            Assert.Equal(new[] { "WI-0001-01", "WI-0001-02" }, result.MarkedDirty);
            Assert.Equal(new[] { "WI-0001-03" }, result.KeptClean);

            // 快照 v1：逐字节是传入的需求原文。
            var snapshotPath = PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 1);
            Assert.True(File.Exists(snapshotPath));
            Assert.Equal(requirementJsonText, File.ReadAllText(snapshotPath));

            // 05-change-impact.md：七个小节标题都在。
            var impactPath = PipelinePaths.ChangeImpactFile(workspace.RepositoryRoot, "REQ-0001");
            Assert.True(File.Exists(impactPath));
            var impactText = File.ReadAllText(impactPath);
            Assert.Contains("# 变更影响 · REQ-0001 · 基准 v1", impactText);
            Assert.Contains("## 直接脏（字段 diff 直接命中）", impactText);
            Assert.Contains("## 传播脏（依赖上游脏项）", impactText);
            Assert.Contains("## 净项（原样保留，一个字未改）", impactText);
            Assert.Contains("## 要执行后端评估一轮的", impactText);
            Assert.Contains("## 人改权威文件", impactText);
            Assert.Contains("## 过程发现", impactText);
            // 直接脏节点 WI-01，传播脏节点 WI-02，净项节点 WI-03。
            Assert.Contains("WI-0001-01 画金币袋", impactText);
            Assert.Contains("WI-0001-02 摆关卡", impactText);
            Assert.Contains("WI-0001-03 定配色", impactText);

            // 脏项标脏：只改状态键。
            Assert.Equal("标脏", ReadWorkItemState(directory, "WI-0001-01"));
            Assert.Equal("标脏", ReadWorkItemState(directory, "WI-0001-02"));

            // 净项逐字节未变。
            Assert.Equal(cleanBefore, File.ReadAllText(Path.Combine(directory, "WI-0001-03.json")));

            // 回方案关，预算不清账。
            Assert.True(TaskState.TryLoad(workspace.RepositoryRoot, "REQ-0001", out var state, out var failureReason), failureReason);
            Assert.Equal("方案", state.Stage);
            Assert.Equal("停在关卡", state.SubState);
            Assert.Equal("方案", state.PendingGate);
            Assert.Equal("", state.CurrentWorkItem);
            Assert.Equal(500000, state.Budget.LanguageModelLimit);
            Assert.Equal(132000, state.Budget.LanguageModelUsed);
            Assert.Equal(60, state.Budget.ImageLimit);
            Assert.Equal(18, state.Budget.ImageUsed);
            Assert.Equal("abc123", state.ArtifactHashes["10-方案.md"]);
        }

        /// <summary>脏项标脏之后，那份工作项 JSON 里除「状态」外的每个键的值与键序都与原来相等（逐键比对）。</summary>
        [Fact]
        public void DirtyWorkItemKeepsEveryOtherKeyValueAndOrder()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.RepositoryRoot, "REQ-0001");
            Directory.CreateDirectory(directory);
            var before = WriteFullWorkItem(directory, "WI-0001-01");
            WriteDefaultTaskState(workspace, "REQ-0001");
            var graph = WorkItemGraph.Load(workspace.RepositoryRoot, "REQ-0001");
            var plan = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);

            var result = ReplanLanding.Apply(workspace.RepositoryRoot, "REQ-0001", plan, graph, "{}", false);

            Assert.True(result.Applied);
            var after = File.ReadAllText(Path.Combine(directory, "WI-0001-01.json"));
            var beforeRoot = JsonNode.Parse(before).AsObject();
            var afterRoot = JsonNode.Parse(after).AsObject();

            // 键序一字不动。
            Assert.Equal(beforeRoot.Select(pair => pair.Key), afterRoot.Select(pair => pair.Key));
            // 除 状态 外的每个键的值与原来相等（逐键比对，不是比整串）。
            foreach (var pair in beforeRoot)
            {
                if (pair.Key == "状态")
                {
                    continue;
                }

                Assert.True(afterRoot.ContainsKey(pair.Key), $"标脏后丢了键 {pair.Key}");
                Assert.Equal(pair.Value.ToJsonString(), afterRoot[pair.Key].ToJsonString());
            }

            Assert.Equal("标脏", afterRoot["状态"].GetValue<string>());
        }

        /// <summary>已经是标脏的脏项：跳过、文件内容逐字节相等，且 Findings 里有「已经是标脏，跳过」。</summary>
        [Fact]
        public void AlreadyDirtyWorkItemIsSkippedUntouchedWithFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.RepositoryRoot, "REQ-0001");
            Directory.CreateDirectory(directory);
            var before = WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>(), "标脏", "画金币袋");
            WriteDefaultTaskState(workspace, "REQ-0001");
            var graph = WorkItemGraph.Load(workspace.RepositoryRoot, "REQ-0001");
            var plan = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);

            var result = ReplanLanding.Apply(workspace.RepositoryRoot, "REQ-0001", plan, graph, "{}", false);

            Assert.True(result.Applied);
            // 仍然计入标脏项。
            Assert.Equal(new[] { "WI-0001-01" }, result.MarkedDirty);
            // 不重写文件：内容逐字节相等。
            Assert.Equal(before, File.ReadAllText(Path.Combine(directory, "WI-0001-01.json")));
            Assert.Contains(result.Findings, finding => finding.Contains("已经是标脏，跳过"));
        }

        /// <summary>状态文件缺失：快照与标脏照样成，Applied 为 true，Findings 里有「状态文件读不出来」。</summary>
        [Fact]
        public void MissingStateFileStillLandsWithFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.RepositoryRoot, "REQ-0001");
            Directory.CreateDirectory(directory);
            WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>(), "待执行", "画金币袋");
            var graph = WorkItemGraph.Load(workspace.RepositoryRoot, "REQ-0001");
            var plan = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);
            Assert.False(File.Exists(PipelinePaths.TaskStateFile(workspace.RepositoryRoot, "REQ-0001")));

            var result = ReplanLanding.Apply(workspace.RepositoryRoot, "REQ-0001", plan, graph, "{}", false);

            Assert.True(result.Applied);
            Assert.Equal(1, result.SnapshotVersion);
            Assert.True(File.Exists(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 1)));
            Assert.Equal("标脏", ReadWorkItemState(directory, "WI-0001-01"));
            Assert.Contains(result.Findings, finding => finding.Contains("状态文件读不出来"));
        }

        /// <summary>原本挂起的工作项：照常标脏，并出一条「原本挂起」的 finding。</summary>
        [Fact]
        public void SuspendedWorkItemIsMarkedDirtyWithFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = WorkItemDirectory(workspace.RepositoryRoot, "REQ-0001");
            Directory.CreateDirectory(directory);
            WriteWorkItem(directory, "WI-0001-01", new[] { "玩法" }, Array.Empty<string>(), "挂起", "画金币袋");
            WriteDefaultTaskState(workspace, "REQ-0001");
            var graph = WorkItemGraph.Load(workspace.RepositoryRoot, "REQ-0001");
            var plan = ReplanPlanner.Plan(graph, new[] { "玩法" }, null);

            var result = ReplanLanding.Apply(workspace.RepositoryRoot, "REQ-0001", plan, graph, "{}", false);

            Assert.True(result.Applied);
            Assert.Equal("标脏", ReadWorkItemState(directory, "WI-0001-01"));
            Assert.Contains(result.Findings, finding => finding.Contains("原本挂起"));
        }

        private static string WorkItemDirectory(string repositoryRoot, string requirementIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "20-work-items");
        }

        /// <summary>写一个工作项 JSON（ASCII 化的键值集合），返回写盘前的原文。</summary>
        private static string WriteWorkItem(
            string directory,
            string identifier,
            IReadOnlyList<string> referencedRequirementFields,
            IReadOnlyList<string> dependencies,
            string state,
            string title)
        {
            var entryObject = new JsonObject
            {
                ["id"] = identifier,
                ["需求id"] = "REQ-0001",
                ["域"] = "文档",
                ["标题"] = title,
                ["状态"] = state,
                ["依赖"] = ToJsonArray(dependencies),
                ["验收点"] = "v",
                ["引用需求字段"] = ToJsonArray(referencedRequirementFields),
                ["产物"] = new JsonArray()
            };
            var text = entryObject.ToJsonString();
            File.WriteAllText(Path.Combine(directory, identifier + ".json"), text, new UTF8Encoding(false));
            return text;
        }

        /// <summary>写一个带全套键（含对象与数组值）的工作项 JSON，返回原文。</summary>
        private static string WriteFullWorkItem(string directory, string identifier)
        {
            var entryObject = new JsonObject
            {
                ["id"] = identifier,
                ["需求id"] = "REQ-0001",
                ["域"] = "文档",
                ["标题"] = "画金币袋",
                ["状态"] = "待执行",
                ["依赖"] = new JsonArray(),
                ["验收点"] = "三连能翻",
                ["引用需求字段"] = ToJsonArray(new[] { "玩法", "目标" }),
                ["产物"] = ToJsonArray(new[] { "30-outputs/金币袋.png" }),
                ["消耗"] = new JsonObject { ["llm"] = 100 },
                ["人工产出"] = new JsonObject { ["说明"] = "手绘" }
            };
            var text = entryObject.ToJsonString();
            File.WriteAllText(Path.Combine(directory, identifier + ".json"), text, new UTF8Encoding(false));
            return text;
        }

        /// <summary>写一份默认的任务状态：执行中、带着预算与产物哈希，预算数字固定便于断言不清账。</summary>
        private static void WriteDefaultTaskState(PoolTestWorkspace workspace, string requirementIdentifier)
        {
            workspace.WriteTaskState(requirementIdentifier, """
                {
                  "阶段": "执行",
                  "子状态": "运行中",
                  "当前工作项": "WI-0001-01",
                  "关卡待审": null,
                  "预算": {"llm上限": 500000, "llm已用": 132000, "生图上限": 60, "生图已用": 18},
                  "产物哈希": {"10-方案.md": "abc123"}
                }
                """);
        }

        /// <summary>读工作项 JSON 的「状态」键。</summary>
        private static string ReadWorkItemState(string directory, string identifier)
        {
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, identifier + ".json"))).AsObject();
            return root["状态"].GetValue<string>();
        }

        /// <summary>拒绝场景必须磁盘零变化：没有快照、没有变更影响文档。</summary>
        private static void AssertNoLandingFiles(PoolTestWorkspace workspace)
        {
            Assert.False(File.Exists(PipelinePaths.RequirementSnapshotFile(workspace.RepositoryRoot, "REQ-0001", 1)));
            Assert.False(File.Exists(PipelinePaths.ChangeImpactFile(workspace.RepositoryRoot, "REQ-0001")));
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
