using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>冲突探测器测试：三条判据、置信度分档、坏文件容错、确定性排序与不写盘。</summary>
    public class ConflictDetectorTests
    {
        /// <summary>拼一份需求 JSON；参数除 state 外全部显式给出，避免用例之间互相串判据。</summary>
        private static string Requirement(
            string id,
            string title,
            string modulesJson,
            string designRecordsJson,
            string specialProject,
            string acceptanceJson,
            string state = "已确认")
        {
            return $$"""
            {
              "id": "{{id}}",
              "标题": "{{title}}",
              "模块": {{modulesJson}},
              "关联设计记录": {{designRecordsJson}},
              "专项": "{{specialProject}}",
              "验收标准": {{acceptanceJson}},
              "状态": "{{state}}"
            }
            """;
        }

        /// <summary>需求目录不存在 → Scanned == false，且不是「零候选」（决策 42 的两个分支）。</summary>
        [Fact]
        public void MissingRequirementsDirectoryIsNotScanned()
        {
            using var workspace = new PoolTestWorkspace();
            Directory.Delete(PoolPaths.RequirementsDirectory(workspace.Root), true);

            var report = ConflictDetector.Detect(workspace.Root, "REQ-0001");

            Assert.False(report.Scanned);
            Assert.Empty(report.Candidates);
            Assert.NotEqual("", report.LoadFailureReason);
        }

        /// <summary>模块无交集的两条标题再像也不产候选。</summary>
        [Fact]
        public void NoCommonModulesNeverComparesTitles()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0001.json", Requirement("REQ-0001", "七日签到", "[\"签到\"]", "[]", "", "[]"));
            workspace.WriteRequirement("REQ-0002.json", Requirement("REQ-0002", "七日签到", "[\"背包\"]", "[]", "", "[]"));

            var report = ConflictDetector.Detect(workspace.Root, "REQ-0002");

            Assert.True(report.Scanned);
            Assert.Empty(report.Candidates);
        }

        /// <summary>模块有交集 + 标题几乎一样 → 标题相似候选，Confidence 高，ShouldRaiseCard true。</summary>
        [Fact]
        public void CommonModulesAndSimilarTitlesRaiseHighCard()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0001.json", Requirement("REQ-0001", "七日签到", "[\"签到\"]", "[]", "", "[]"));
            workspace.WriteRequirement("REQ-0002.json", Requirement("REQ-0002", "七日签到啦", "[\"签到\"]", "[]", "", "[]"));

            var report = ConflictDetector.Detect(workspace.Root, "REQ-0002");

            var candidate = Assert.Single(report.Candidates);
            Assert.Equal("标题相似", candidate.Reason);
            Assert.Equal("高", candidate.Confidence);
            Assert.True(candidate.ShouldRaiseCard);
            Assert.Equal("REQ-0001", candidate.OldIdentifier);
            Assert.Equal("REQ-0002", candidate.NewIdentifier);
        }

        /// <summary>模块有交集但标题差很远 → Score 落在低档，ShouldRaiseCard false。</summary>
        [Fact]
        public void DifferentTitlesScoreLowAndDoNotRaiseCard()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0001.json", Requirement("REQ-0001", "背包扩容", "[\"签到\"]", "[]", "", "[]"));
            workspace.WriteRequirement("REQ-0002.json", Requirement("REQ-0002", "七日签到", "[\"签到\"]", "[]", "", "[]"));

            var report = ConflictDetector.Detect(workspace.Root, "REQ-0002");

            var candidate = Assert.Single(report.Candidates);
            Assert.Equal("标题相似", candidate.Reason);
            Assert.Equal("低", candidate.Confidence);
            Assert.False(candidate.ShouldRaiseCard);
            Assert.True(candidate.Score < 0.5);
        }

        /// <summary>共用设计记录 → 候选，分数按交集条数走（1 条 = 0.6，中档）。</summary>
        [Fact]
        public void SharedDesignRecordsProduceCandidateWithScore()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0001.json", Requirement("REQ-0001", "七日签到", "[]", "[\"DR-0001\"]", "", "[]"));
            workspace.WriteRequirement("REQ-0002.json", Requirement("REQ-0002", "七日签到", "[]", "[\"DR-0001\", \"DR-0002\"]", "", "[]"));

            var report = ConflictDetector.Detect(workspace.Root, "REQ-0002");

            var candidate = Assert.Single(report.Candidates);
            Assert.Equal("共用设计记录", candidate.Reason);
            Assert.Equal(0.6, candidate.Score);
            Assert.Equal("中", candidate.Confidence);
            Assert.False(candidate.ShouldRaiseCard);
            Assert.Contains("DR-0001", candidate.Detail);
        }

        /// <summary>同专项 + 一条验收标准逐字相同 → 验收标准重合候选（1 条 = 0.7）。</summary>
        [Fact]
        public void SameSpecialProjectAndExactAcceptanceOverlap()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement(
                "REQ-0001.json",
                Requirement("REQ-0001", "七日签到", "[]", "[]", "EP-0001", "[\"登录弹出签到界面\", \"第7天发放大奖\"]"));
            workspace.WriteRequirement(
                "REQ-0002.json",
                Requirement("REQ-0002", "七日签到", "[]", "[]", "EP-0001", "[\"登录弹出签到界面\"]"));

            var report = ConflictDetector.Detect(workspace.Root, "REQ-0002");

            var candidate = Assert.Single(report.Candidates);
            Assert.Equal("验收标准重合", candidate.Reason);
            Assert.Equal(0.7, candidate.Score);
            Assert.Contains("登录弹出签到界面", candidate.Detail);
        }

        /// <summary>同一对需求同时命中两条判据 → 产两条候选，不合并（每条判据各产一条）。</summary>
        [Fact]
        public void SamePairHittingTwoCriteriaProducesTwoCandidates()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement(
                "REQ-0001.json",
                Requirement("REQ-0001", "背包扩容", "[]", "[\"DR-0001\"]", "EP-0001", "[\"登录弹出签到界面\"]"));
            workspace.WriteRequirement(
                "REQ-0002.json",
                Requirement("REQ-0002", "七日签到", "[]", "[\"DR-0001\"]", "EP-0001", "[\"登录弹出签到界面\"]"));

            var report = ConflictDetector.Detect(workspace.Root, "REQ-0002");

            Assert.Equal(2, report.Candidates.Count);
            // 排序：分数降序 → 0.7 的验收标准重合在前，0.6 的共用设计记录在后。
            Assert.Equal("验收标准重合", report.Candidates[0].Reason);
            Assert.Equal("共用设计记录", report.Candidates[1].Reason);
        }

        /// <summary>一份坏 JSON → 跳过它，Scanned 仍 true，原因非空，其余照常比对。</summary>
        [Fact]
        public void BrokenRequirementSkippedButScanStillTrue()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0001.json", Requirement("REQ-0001", "七日签到", "[]", "[\"DR-0001\"]", "", "[]"));
            // 坏 JSON 的内容刻意只用 ASCII：裸中文写在这里会被命名门禁当成「标识符含中文」判红。
            workspace.WriteRequirement("REQ-0003.json", "not-json");
            workspace.WriteRequirement("REQ-0002.json", Requirement("REQ-0002", "七日签到", "[]", "[\"DR-0001\"]", "", "[]"));

            var report = ConflictDetector.Detect(workspace.Root, "REQ-0002");

            Assert.True(report.Scanned);
            Assert.Single(report.Candidates);
            Assert.Contains("REQ-0003.json", report.LoadFailureReason);
        }

        /// <summary>确定性：同一份输入连跑两次，候选序列逐条相等（id、判据、分数全一样）。</summary>
        [Fact]
        public void DetectionIsDeterministic()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement(
                "REQ-0001.json",
                Requirement("REQ-0001", "七日签到", "[\"签到\"]", "[\"DR-0001\"]", "EP-0001", "[\"登录弹出签到界面\"]"));
            workspace.WriteRequirement(
                "REQ-0002.json",
                Requirement("REQ-0002", "七日签到啦", "[\"签到\"]", "[\"DR-0001\"]", "EP-0001", "[\"登录弹出签到界面\", \"第7天发放大奖\"]"));
            workspace.WriteRequirement(
                "REQ-0004.json",
                Requirement("REQ-0004", "背包扩容", "[\"签到\"]", "[]", "EP-0001", "[\"仓库格子上限提升\"]"));

            var first = ConflictDetector.Detect(workspace.Root, "REQ-0002");
            var second = ConflictDetector.Detect(workspace.Root, "REQ-0002");

            Assert.Equal(first.Candidates.Count, second.Candidates.Count);
            for (var i = 0; i < first.Candidates.Count; i++)
            {
                Assert.Equal(first.Candidates[i].OldIdentifier, second.Candidates[i].OldIdentifier);
                Assert.Equal(first.Candidates[i].Reason, second.Candidates[i].Reason);
                Assert.Equal(first.Candidates[i].Score, second.Candidates[i].Score);
            }
        }

        /// <summary>不写盘：跑完 Detect 之后，需求目录里的文件数与内容与跑之前完全一致。</summary>
        [Fact]
        public void DetectNeverWritesToPool()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteRequirement("REQ-0001.json", Requirement("REQ-0001", "七日签到", "[\"签到\"]", "[]", "", "[]"));
            workspace.WriteRequirement("REQ-0002.json", Requirement("REQ-0002", "七日签到啦", "[\"签到\"]", "[]", "", "[]"));
            var before = SnapshotRequirements(workspace.Root);

            ConflictDetector.Detect(workspace.Root, "REQ-0002");

            Assert.Equal(before, SnapshotRequirements(workspace.Root));
        }

        /// <summary>需求目录快照：文件名 + 内容拼成行，按序数序排序。</summary>
        private static IReadOnlyList<string> SnapshotRequirements(string poolRoot)
        {
            var directory = PoolPaths.RequirementsDirectory(poolRoot);
            var files = Directory.GetFiles(directory, "REQ-*.json").ToList();
            files.Sort(StringComparer.Ordinal);
            return files.Select(path => Path.GetFileName(path) + "|" + File.ReadAllText(path)).ToList();
        }
    }
}
