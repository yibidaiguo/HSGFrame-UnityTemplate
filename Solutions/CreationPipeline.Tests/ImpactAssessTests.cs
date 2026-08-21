using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>影响评估提示词与报告测试：确定性组装、坏回复绝不给零结论、漏判绝不默认成净。</summary>
    public class ImpactAssessTests
    {
        /// <summary>同一份输入两次组装 → 提示词逐字符相同（决策 58：不许随机，否则两次跑出不同提示词）。</summary>
        [Fact]
        public void SameInputTwiceProducesIdenticalPrompt()
        {
            using var workspace = new PoolTestWorkspace();
            var workItems = new[] { "WI-0001", "WI-0002", "WI-0003" };

            var first = ImpactAssessPrompt.Build(workspace.Root, "diff 内容", workItems, ImpactAssessPrompt.PromptVersion);
            var second = ImpactAssessPrompt.Build(workspace.Root, "diff 内容", workItems, ImpactAssessPrompt.PromptVersion);

            Assert.Equal(first.PromptText, second.PromptText);
            Assert.Equal(first.PromptVersion, second.PromptVersion);
            Assert.False(string.IsNullOrWhiteSpace(first.PromptText));
        }

        /// <summary>提示词版本不同 → 判定键（缓存键）不同：同一份 diff 换版提示词就是另一次判定，缓存天然不串（决策 90）。</summary>
        [Fact]
        public void DifferentPromptVersionProducesDifferentDecisionKey()
        {
            using var workspace = new PoolTestWorkspace();
            var workItems = new[] { "WI-0001" };

            var v1 = ImpactAssessPrompt.Build(workspace.Root, "diff 内容", workItems, "impact-assess-v1");
            var v2 = ImpactAssessPrompt.Build(workspace.Root, "diff 内容", workItems, "impact-assess-v2");

            var key1 = PreReviewCache.ComputeKey(v1.PromptText, "模型甲", v1.PromptVersion);
            var key2 = PreReviewCache.ComputeKey(v2.PromptText, "模型甲", v2.PromptVersion);
            Assert.NotEqual(key1, key2);
        }

        /// <summary>坏回复（不是 JSON）→ 判成了=false、零结论、原因写清——绝不许当成「没问题」。</summary>
        [Fact]
        public void GarbageReplyIsNotParsedAndHasNoVerdicts()
        {
            var modelText = "我看了一下，这个变更影响不大，应该都不用重跑。";

            Assert.False(ImpactAssessReport.TryParse(modelText, new[] { "WI-0001" }, out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Verdicts);
            Assert.Equal(0, report.DirtyCount);
            Assert.Equal(0, report.CleanCount);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        /// <summary>模型漏答某个工作项 → 进「漏判的工作项」列表，不默认成净（决策 42 最贵的一种长相）。</summary>
        [Fact]
        public void MissingWorkItemGoesToMissingListAndIsNotTreatedAsClean()
        {
            var modelText = "{\"评估\":[{\"工作项\":\"WI-0001\",\"结论\":\"净\",\"理由\":\"不受影响\"}]}";
            var requested = new[] { "WI-0001", "WI-0002" };

            Assert.True(ImpactAssessReport.TryParse(modelText, requested, out var report, out var reason));
            Assert.Equal("", reason);
            Assert.True(report.Parsed);

            // WI-0002 没被判，进漏判列表；绝不默认成「净」。
            var missing = Assert.Single(report.MissingWorkItems);
            Assert.Equal("WI-0002", missing);
            // CleanCount 只含模型明确判成「净」的 WI-0001；WI-0002 不在任何结论里。
            Assert.Equal(1, report.CleanCount);
            Assert.Equal(1, report.DirtyCount + report.CleanCount);
            Assert.DoesNotContain(report.Verdicts, verdict => verdict.WorkItem == "WI-0002");
        }

        /// <summary>全部工作项都答了 → 漏判列表为空。</summary>
        [Fact]
        public void MissingListIsEmptyWhenAllWorkItemsAnswered()
        {
            var modelText = "{\"评估\":[" +
                "{\"工作项\":\"WI-0001\",\"结论\":\"脏\",\"理由\":\"命中 diff\"}," +
                "{\"工作项\":\"WI-0002\",\"结论\":\"净\",\"理由\":\"不受影响\"}]}";

            Assert.True(ImpactAssessReport.TryParse(modelText, new[] { "WI-0001", "WI-0002" }, out var report, out var reason));
            Assert.True(report.Parsed);
            Assert.Empty(report.MissingWorkItems);
            Assert.Equal(1, report.DirtyCount);
            Assert.Equal(1, report.CleanCount);
        }

        /// <summary>结论值非法 → 整单解析失败，不把非法结论当判定。</summary>
        [Fact]
        public void IllegalConclusionValueIsNotParsed()
        {
            var modelText = "{\"评估\":[{\"工作项\":\"WI-0001\",\"结论\":\"未知\",\"理由\":\"说不清\"}]}";

            Assert.False(ImpactAssessReport.TryParse(modelText, new[] { "WI-0001" }, out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Verdicts);
            Assert.Contains("结论", reason);
        }

        /// <summary>JSON 合法但缺「评估」数组 → 判成了=false，不给零结论。</summary>
        [Fact]
        public void JsonWithoutAssessmentArrayIsNotParsed()
        {
            var modelText = "{\"结论\":\"没问题\"}";

            Assert.False(ImpactAssessReport.TryParse(modelText, new[] { "WI-0001" }, out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Verdicts);
            Assert.Contains("评估", reason);
        }

        /// <summary>模型回复裹在 ```json 代码块里 → 照常解析出判定。</summary>
        [Fact]
        public void FencedJsonCodeBlockParses()
        {
            var modelText = "```json\n" +
                "{\"评估\":[{\"工作项\":\"WI-0001\",\"结论\":\"脏\",\"理由\":\"命中验收标准\"}]}\n" +
                "```";

            Assert.True(ImpactAssessReport.TryParse(modelText, new[] { "WI-0001" }, out var report, out var reason));
            Assert.Equal("", reason);
            Assert.True(report.Parsed);
            var verdict = Assert.Single(report.Verdicts);
            Assert.Equal("WI-0001", verdict.WorkItem);
            Assert.Equal("脏", verdict.Conclusion);
            Assert.Equal("命中验收标准", verdict.Reason);
            Assert.Empty(report.MissingWorkItems);
        }

        /// <summary>空回复 → 判成了=false 且零结论。</summary>
        [Fact]
        public void EmptyReplyIsNotParsed()
        {
            Assert.False(ImpactAssessReport.TryParse("", new[] { "WI-0001" }, out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Verdicts);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        /// <summary>报告落盘 _Tasks/&lt;需求id&gt;/影响评估.json，且在临时目录内（Path.GetTempPath）。</summary>
        [Fact]
        public void WriteReportLandsInTaskDirectory()
        {
            using var workspace = new PoolTestWorkspace();
            var report = new ImpactAssessReport(
                parsed: true,
                model: "模型甲",
                promptVersion: ImpactAssessPrompt.PromptVersion,
                decisionKey: "键",
                verdicts: new[] { new ImpactAssessVerdict("WI-0001", "脏", "命中") },
                missingWorkItems: new[] { "WI-0002" },
                dirtyCount: 1,
                cleanCount: 0,
                fromCache: false,
                parseReason: "",
                timestamp: "2026-08-20T10:00:00+09:00");

            var filePath = report.WriteReport(workspace.RepositoryRoot, "REQ-0042");

            Assert.True(File.Exists(filePath));
            Assert.StartsWith(Path.GetTempPath(), Path.GetFullPath(filePath));
            Assert.EndsWith("REQ-0042" + Path.DirectorySeparatorChar + "影响评估.json", filePath);

            // 写盘后能读回来，字段不丢。
            var loaded = ImpactAssessReport.TryFromJson(File.ReadAllText(filePath));
            Assert.NotNull(loaded);
            Assert.True(loaded.Parsed);
            var verdict = Assert.Single(loaded.Verdicts);
            Assert.Equal("WI-0001", verdict.WorkItem);
            Assert.Equal("脏", verdict.Conclusion);
            Assert.Equal("WI-0002", Assert.Single(loaded.MissingWorkItems));
        }
    }
}
