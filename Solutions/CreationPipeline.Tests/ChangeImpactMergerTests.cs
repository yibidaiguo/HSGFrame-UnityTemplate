using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 影响评估合并写进 05-变更影响.md 的测试（子文档 03 §三）。
    /// 三条要点：只加一节不动别的节；重复跑覆盖不堆叠；没判成时**不许**写成「全净」。
    /// </summary>
    public class ChangeImpactMergerTests
    {
        /// <summary>合并只加自己那一节，重规划算出来的确定性小节一个字不动。</summary>
        [Fact]
        public void MergeAddsOwnSectionAndKeepsOthers()
        {
            var root = NewTemporaryDirectory();
            try
            {
                WriteBaseDocument(root, "REQ-0001");

                var result = ChangeImpactMerger.Merge(root, "REQ-0001", BuildReport());

                Assert.True(result.Merged, result.Reason);
                Assert.False(result.ReplacedExistingSection);

                var text = File.ReadAllText(PipelinePaths.ChangeImpactFile(root, "REQ-0001"));
                Assert.Contains("## 直接脏（字段 diff 直接命中）", text);
                Assert.Contains("## 净项（原样保留，一个字未改）", text);
                Assert.Contains(ChangeImpactMerger.SectionHeading, text);
                Assert.Contains("**WI-0002** → 净：这一项只读了标题", text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>重复合并覆盖上一次，不许两份互相矛盾的结论同时躺在文档里。</summary>
        [Fact]
        public void MergeTwiceReplacesInsteadOfStacking()
        {
            var root = NewTemporaryDirectory();
            try
            {
                WriteBaseDocument(root, "REQ-0001");
                ChangeImpactMerger.Merge(root, "REQ-0001", BuildReport());

                var second = ChangeImpactMerger.Merge(root, "REQ-0001", BuildReport());

                Assert.True(second.Merged);
                Assert.True(second.ReplacedExistingSection);

                var text = File.ReadAllText(PipelinePaths.ChangeImpactFile(root, "REQ-0001"));
                var first = text.IndexOf(ChangeImpactMerger.SectionHeading, StringComparison.Ordinal);
                Assert.True(first >= 0);
                Assert.Equal(-1, text.IndexOf(ChangeImpactMerger.SectionHeading, first + 1, StringComparison.Ordinal));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>没判成要如实写「没判成」，并写明不许当成全净——决策 42。</summary>
        [Fact]
        public void UnparsedReportSaysSoAndForbidsTreatingAsClean()
        {
            var section = ChangeImpactMerger.BuildSection(
                ImpactAssessReport.NotParsed("某模型", "v1", "key", "回答不是 JSON"));

            Assert.Contains("没判成", section);
            Assert.Contains("回答不是 JSON", section);
            Assert.Contains("不许当成「全净」", section);
        }

        /// <summary>模型漏答的工作项要单列一节并写明按脏处理。</summary>
        [Fact]
        public void MissingWorkItemsAreListedAndTreatedAsDirty()
        {
            var report = new ImpactAssessReport(
                parsed: true,
                model: "某模型",
                promptVersion: "v1",
                decisionKey: "key",
                verdicts: new[] { new ImpactAssessVerdict("WI-0001", "脏", "改到了") },
                missingWorkItems: new[] { "WI-0009" },
                dirtyCount: 1,
                cleanCount: 0,
                fromCache: false,
                parseReason: "",
                timestamp: "2026-08-21T10:00:00+09:00");

            var section = ChangeImpactMerger.BuildSection(report);

            Assert.Contains("模型漏答的工作项", section);
            Assert.Contains("WI-0009", section);
            Assert.Contains("不许默认成净", section);
        }

        /// <summary>模型名与提示词版本必须写进文档——不写的话没人说得清那条结论是怎么来的（决策 89）。</summary>
        [Fact]
        public void SectionCarriesModelAndPromptVersion()
        {
            var section = ChangeImpactMerger.BuildSection(BuildReport());

            Assert.Contains("模型：某模型", section);
            Assert.Contains("提示词版本：impact-v1", section);
            Assert.Contains("建议，不是判定", section);
        }

        /// <summary>文档不存在时不新建，只如实报原因——新建会让人以为重规划跑过了。</summary>
        [Fact]
        public void MergeDoesNotCreateDocumentWhenMissing()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var result = ChangeImpactMerger.Merge(root, "REQ-0404", BuildReport());

                Assert.False(result.Merged);
                Assert.Contains("task.replan", result.Reason);
                Assert.False(File.Exists(PipelinePaths.ChangeImpactFile(root, "REQ-0404")));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>造一份判成了的报告。</summary>
        private static ImpactAssessReport BuildReport()
        {
            return new ImpactAssessReport(
                parsed: true,
                model: "某模型",
                promptVersion: "impact-v1",
                decisionKey: "key-1",
                verdicts: new[]
                {
                    new ImpactAssessVerdict("WI-0001", "脏", "这一项引用了被改的字段"),
                    new ImpactAssessVerdict("WI-0002", "净", "这一项只读了标题")
                },
                missingWorkItems: Array.Empty<string>(),
                dirtyCount: 1,
                cleanCount: 1,
                fromCache: false,
                parseReason: "",
                timestamp: "2026-08-21T10:00:00+09:00");
        }

        /// <summary>写一份重规划落地时那种形状的变更影响文档。</summary>
        private static void WriteBaseDocument(string root, string requirementIdentifier)
        {
            var filePath = PipelinePaths.ChangeImpactFile(root, requirementIdentifier);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            var text = string.Join(Environment.NewLine, new[]
            {
                $"# 变更影响 · {requirementIdentifier} · 基准 v2",
                "",
                "## 直接脏（字段 diff 直接命中）",
                "- WI-0001 做排序",
                "",
                "## 净项（原样保留，一个字未改）",
                "- WI-0002 写文案",
                "",
                "## 过程发现",
                "- 无",
                ""
            });
            File.WriteAllText(filePath, text, new UTF8Encoding(false));
        }

        /// <summary>开一个临时目录。</summary>
        private static string NewTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "变更影响合并测试-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>删临时目录，删不掉不报错。</summary>
        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
            }
        }
    }
}
