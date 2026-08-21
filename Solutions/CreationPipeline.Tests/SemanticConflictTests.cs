using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>语义冲突比对提示词与报告测试：确定性组装、坏回复绝不给零候选、多条判据不合并、置信度分级。</summary>
    public class SemanticConflictTests
    {
        /// <summary>同一份输入两次组装 → 提示词逐字符相同（决策 58：不许随机，否则两次跑出不同提示词）。</summary>
        [Fact]
        public void SameInputTwiceProducesIdenticalPrompt()
        {
            using var workspace = new PoolTestWorkspace();
            var requirements = new[] { "### 需求：REQ-0001.json\n{...}", "### 需求：REQ-0002.json\n{...}" };

            var first = SemanticConflictPrompt.Build(workspace.Root, "设计池汇总文本", requirements, SemanticConflictPrompt.PromptVersion);
            var second = SemanticConflictPrompt.Build(workspace.Root, "设计池汇总文本", requirements, SemanticConflictPrompt.PromptVersion);

            Assert.Equal(first.PromptText, second.PromptText);
            Assert.Equal(first.PromptVersion, second.PromptVersion);
            Assert.False(string.IsNullOrWhiteSpace(first.PromptText));
            Assert.Contains("设计池汇总文本", first.PromptText);
        }

        /// <summary>提示词版本不同 → 判定键（缓存键）不同：同一份输入换版提示词就是另一次判定，缓存天然不串（决策 90）。</summary>
        [Fact]
        public void DifferentPromptVersionProducesDifferentDecisionKey()
        {
            using var workspace = new PoolTestWorkspace();
            var requirements = new[] { "### 需求：REQ-0001.json\n{...}" };

            var v1 = SemanticConflictPrompt.Build(workspace.Root, "设计池汇总", requirements, "semantic-conflict-v1");
            var v2 = SemanticConflictPrompt.Build(workspace.Root, "设计池汇总", requirements, "semantic-conflict-v2");

            var key1 = PreReviewCache.ComputeKey(v1.PromptText, "模型甲", v1.PromptVersion);
            var key2 = PreReviewCache.ComputeKey(v2.PromptText, "模型甲", v2.PromptVersion);
            Assert.NotEqual(key1, key2);
        }

        /// <summary>坏回复（不是 JSON）→ 判成了=false、零候选、原因写清——绝不许当成「没问题」。</summary>
        [Fact]
        public void GarbageReplyIsNotParsedAndHasNoCandidates()
        {
            var modelText = "我比了一圈，感觉都挺独立的，没有冲突。";

            Assert.False(SemanticConflictReport.TryParse(modelText, out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Candidates);
            Assert.Equal(0, report.HighCount);
            Assert.Equal(0, report.MediumCount);
            Assert.Equal(0, report.LowCount);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        /// <summary>同一对需求命中多条判据 → 每条判据各产一条候选，不合并、不取最大（决策 67）。</summary>
        [Fact]
        public void MultipleBasesProduceMultipleCandidates()
        {
            var modelText = "{\"冲突候选\":[" +
                "{\"需求A\":\"REQ-0001\",\"需求B\":\"REQ-0002\",\"置信度\":\"高\",\"判据\":\"验收标准重合\",\"说明\":\"两条验收标准逐字相同\"}," +
                "{\"需求A\":\"REQ-0001\",\"需求B\":\"REQ-0002\",\"置信度\":\"中\",\"判据\":\"设计记录共用\",\"说明\":\"共用同一份设计记录\"}]}";

            Assert.True(SemanticConflictReport.TryParse(modelText, out var report, out var reason));
            Assert.Equal("", reason);
            Assert.True(report.Parsed);

            // 两条判据 → 两条候选，不合并成一条。
            Assert.Equal(2, report.Candidates.Count);
            Assert.All(report.Candidates, candidate =>
            {
                Assert.Equal("REQ-0001", candidate.RequirementA);
                Assert.Equal("REQ-0002", candidate.RequirementB);
            });
            Assert.Contains(report.Candidates, candidate => candidate.Basis == "验收标准重合");
            Assert.Contains(report.Candidates, candidate => candidate.Basis == "设计记录共用");
            Assert.Equal(1, report.HighCount);
            Assert.Equal(1, report.MediumCount);
        }

        /// <summary>置信度分级：只有「高」SuggestRaiseCard=true，中/低只在需求上标注不发卡（决策 66）。</summary>
        [Fact]
        public void ConfidenceGradingMarksOnlyHighAsRaiseCard()
        {
            var modelText = "{\"冲突候选\":[" +
                "{\"需求A\":\"REQ-0001\",\"需求B\":\"REQ-0002\",\"置信度\":\"高\",\"判据\":\"标题语义相近\",\"说明\":\"同一件事\"}," +
                "{\"需求A\":\"REQ-0001\",\"需求B\":\"REQ-0003\",\"置信度\":\"中\",\"判据\":\"专项内目标重叠\",\"说明\":\"沾边\"}," +
                "{\"需求A\":\"REQ-0002\",\"需求B\":\"REQ-0003\",\"置信度\":\"低\",\"判据\":\"共用模块\",\"说明\":\"只是同模块\"}]}";

            Assert.True(SemanticConflictReport.TryParse(modelText, out var report, out var reason));
            Assert.Equal(3, report.Candidates.Count);

            Assert.True(report.Candidates[0].SuggestRaiseCard);
            Assert.False(report.Candidates[1].SuggestRaiseCard);
            Assert.False(report.Candidates[2].SuggestRaiseCard);
            Assert.Equal(1, report.HighCount);
            Assert.Equal(1, report.MediumCount);
            Assert.Equal(1, report.LowCount);
        }

        /// <summary>置信度值非法 → 整单解析失败，不把非法置信度当候选。</summary>
        [Fact]
        public void IllegalConfidenceValueIsNotParsed()
        {
            var modelText = "{\"冲突候选\":[{\"需求A\":\"REQ-0001\",\"需求B\":\"REQ-0002\",\"置信度\":\"极高\",\"判据\":\"标题\",\"说明\":\"说不清\"}]}";

            Assert.False(SemanticConflictReport.TryParse(modelText, out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Candidates);
            Assert.Contains("置信度", reason);
        }

        /// <summary>JSON 合法但缺「冲突候选」数组 → 判成了=false，不给零候选。</summary>
        [Fact]
        public void JsonWithoutCandidatesArrayIsNotParsed()
        {
            var modelText = "{\"结论\":\"没有冲突\"}";

            Assert.False(SemanticConflictReport.TryParse(modelText, out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Candidates);
            Assert.Contains("冲突候选", reason);
        }

        /// <summary>模型回复裹在 ```json 代码块里 → 照常解析出候选。</summary>
        [Fact]
        public void FencedJsonCodeBlockParses()
        {
            var modelText = "```json\n" +
                "{\"冲突候选\":[{\"需求A\":\"REQ-0001\",\"需求B\":\"REQ-0002\",\"置信度\":\"高\",\"判据\":\"验收标准重合\",\"说明\":\"逐字相同\"}]}\n" +
                "```";

            Assert.True(SemanticConflictReport.TryParse(modelText, out var report, out var reason));
            Assert.Equal("", reason);
            Assert.True(report.Parsed);
            var candidate = Assert.Single(report.Candidates);
            Assert.Equal("REQ-0001", candidate.RequirementA);
            Assert.Equal("REQ-0002", candidate.RequirementB);
            Assert.Equal("高", candidate.Confidence);
            Assert.Equal("验收标准重合", candidate.Basis);
            Assert.Equal("逐字相同", candidate.Description);
        }

        /// <summary>空回复 → 判成了=false 且零候选。</summary>
        [Fact]
        public void EmptyReplyIsNotParsed()
        {
            Assert.False(SemanticConflictReport.TryParse("", out var report, out var reason));
            Assert.False(report.Parsed);
            Assert.Empty(report.Candidates);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        /// <summary>报告落盘 _Tasks/语义冲突报告.json（不挂到某个需求名下），且在临时目录内（Path.GetTempPath）。</summary>
        [Fact]
        public void WriteReportLandsInTasksRoot()
        {
            using var workspace = new PoolTestWorkspace();
            var report = new SemanticConflictReport(
                parsed: true,
                model: "模型甲",
                promptVersion: SemanticConflictPrompt.PromptVersion,
                decisionKey: "键",
                candidates: new[] { new SemanticConflictCandidate("REQ-0001", "REQ-0002", "高", "验收标准重合", "逐字相同") },
                highCount: 1,
                mediumCount: 0,
                lowCount: 0,
                fromCache: false,
                parseReason: "",
                timestamp: "2026-08-20T10:00:00+09:00");

            var filePath = report.WriteReport(workspace.RepositoryRoot);

            Assert.True(File.Exists(filePath));
            Assert.StartsWith(Path.GetTempPath(), Path.GetFullPath(filePath));
            Assert.EndsWith("_Tasks" + Path.DirectorySeparatorChar + "语义冲突报告.json", filePath);

            // 写盘后能读回来，字段不丢；「建议发卡」只在高置信度上为 true。
            var loaded = SemanticConflictReport.TryFromJson(File.ReadAllText(filePath));
            Assert.NotNull(loaded);
            Assert.True(loaded.Parsed);
            var candidate = Assert.Single(loaded.Candidates);
            Assert.Equal("高", candidate.Confidence);
            Assert.True(candidate.SuggestRaiseCard);
        }
    }
}
