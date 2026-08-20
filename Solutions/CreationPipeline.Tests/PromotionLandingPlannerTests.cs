using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 晋升落地规划器测试：只有已批准的提案能落地；检查器去向写草案 Markdown；
    /// 预审规则去向合并追加、不重复、不丢已有条目。
    /// </summary>
    public class PromotionLandingPlannerTests
    {
        /// <summary>造一条已批准的提案。</summary>
        private static PromotionRecord ApprovedRecord(string category, string targetChannel, string identifier = "PR-0001")
        {
            return new PromotionRecord(
                identifier,
                category,
                3,
                "可代码化",
                targetChannel,
                new[] { "签到" },
                new[] { "这里没判 null" },
                PromotionRecord.ApprovedState,
                "2026-08-20T10:00:00+09:00",
                "张三",
                "2026-08-20T11:00:00+09:00",
                "");
        }

        /// <summary>状态是 待批 的提案 → 失败，reason 提到只有已批准能落地。</summary>
        [Fact]
        public void LandRejectsNonApprovedRecord()
        {
            using var workspace = new PoolTestWorkspace();
            var record = new PromotionRecord(
                "PR-0001",
                "空引用未防",
                3,
                "可代码化",
                "检查器",
                new[] { "签到" },
                new[] { "这里没判 null" },
                PromotionRecord.PendingState,
                "2026-08-20T10:00:00+09:00",
                "",
                "",
                "");

            var result = PromotionLandingPlanner.Land(workspace.RepositoryRoot, record);

            Assert.False(result.Succeeded);
            Assert.Contains("已批准", result.Reason);
        }

        /// <summary>检查器去向 → 草案 Markdown 真存在，含五个标题，行数小于 200。</summary>
        [Fact]
        public void LandCheckerDraftWritesMarkdownUnderTwoHundredLines()
        {
            using var workspace = new PoolTestWorkspace();
            var record = ApprovedRecord("空引用未防", "检查器");

            var result = PromotionLandingPlanner.Land(workspace.RepositoryRoot, record);

            Assert.True(result.Succeeded);
            var expectedPath = Path.Combine(SpecificationPaths.CheckerDraftDirectory(workspace.RepositoryRoot), "空引用未防.md");
            Assert.Equal(expectedPath, result.ArtifactPath);
            Assert.True(File.Exists(expectedPath));

            var content = File.ReadAllText(expectedPath);
            Assert.Contains("# 检查器草案：空引用未防", content);
            Assert.Contains("## 来自哪条提案", content);
            Assert.Contains("## 要查什么", content);
            Assert.Contains("## 原文引用", content);
            Assert.Contains("## 建议接进哪道门禁", content);
            // 类别全中文字符 → ASCII 化后为空 → 用 promotion。
            Assert.Contains("gate.promotion", content);

            var lineCount = content.Split('\n').Length;
            Assert.True(lineCount < 200, $"草案 {lineCount} 行，必须短于 200 行");
        }

        /// <summary>预审规则去向 → 文件存在，规则数组里有一条，来源提案对得上。</summary>
        [Fact]
        public void LandPreReviewRuleAppendsOneRule()
        {
            using var workspace = new PoolTestWorkspace();
            var record = ApprovedRecord("空引用未防", "预审规则");

            var result = PromotionLandingPlanner.Land(workspace.RepositoryRoot, record);

            Assert.True(result.Succeeded);
            var filePath = SpecificationPaths.ProjectPreReviewRuleFile(workspace.RepositoryRoot);
            Assert.Equal(filePath, result.ArtifactPath);
            Assert.True(File.Exists(filePath));

            var rules = JsonNode.Parse(File.ReadAllText(filePath))["规则"].AsArray();
            var rule = Assert.Single(rules).AsObject();
            Assert.Equal("PRR-0001", rule["id"].GetValue<string>());
            Assert.Equal("空引用未防", rule["问题类别"].GetValue<string>());
            Assert.Equal("PR-0001", rule["来源提案"].GetValue<string>());
            Assert.Contains("检查是否存在", rule["提示词"].GetValue<string>());
        }

        /// <summary>文件里先放一条别的规则，落地之后那条还在（合并写，不是覆盖写）。</summary>
        [Fact]
        public void LandPreReviewRuleKeepsExistingRules()
        {
            using var workspace = new PoolTestWorkspace();
            var filePath = SpecificationPaths.ProjectPreReviewRuleFile(workspace.RepositoryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, """
                {
                  "规则": [
                    {
                      "id": "PRR-0001",
                      "问题类别": "命名歧义",
                      "提示词": "检查是否存在命名歧义。",
                      "来源提案": "PR-0099"
                    }
                  ]
                }
                """, new UTF8Encoding(false));

            var result = PromotionLandingPlanner.Land(workspace.RepositoryRoot, ApprovedRecord("空引用未防", "预审规则"));

            Assert.True(result.Succeeded);
            var rules = JsonNode.Parse(File.ReadAllText(filePath))["规则"].AsArray();
            Assert.Equal(2, rules.Count);
            var existing = rules[0].AsObject();
            Assert.Equal("命名歧义", existing["问题类别"].GetValue<string>());
            Assert.Equal("PR-0099", existing["来源提案"].GetValue<string>());
            var added = rules[1].AsObject();
            Assert.Equal("空引用未防", added["问题类别"].GetValue<string>());
            Assert.Equal("PRR-0002", added["id"].GetValue<string>());
        }

        /// <summary>同一条提案落地两次 → 第二次成功但 reason 说幂等跳过，规则数组仍然只有一条。</summary>
        [Fact]
        public void LandPreReviewRuleTwiceIsIdempotent()
        {
            using var workspace = new PoolTestWorkspace();
            var record = ApprovedRecord("空引用未防", "预审规则");

            var first = PromotionLandingPlanner.Land(workspace.RepositoryRoot, record);
            Assert.True(first.Succeeded);

            var second = PromotionLandingPlanner.Land(workspace.RepositoryRoot, record);

            Assert.True(second.Succeeded);
            Assert.Contains("幂等", second.Reason);
            Assert.Equal(first.ArtifactPath, second.ArtifactPath);

            var rules = JsonNode.Parse(File.ReadAllText(SpecificationPaths.ProjectPreReviewRuleFile(workspace.RepositoryRoot)))["规则"].AsArray();
            Assert.Single(rules);
        }
    }
}
