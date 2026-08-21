using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次合并写的结果：写没写成、文件路径与原因。</summary>
    public sealed class ChangeImpactMergeResult
    {
        /// <summary>
        /// 构造一次合并写结果。
        /// </summary>
        /// <param name="merged">写成了没有。</param>
        /// <param name="filePath">变更影响文档路径。</param>
        /// <param name="reason">没写成时的原因；写成了为空串。</param>
        /// <param name="replacedExistingSection">是不是覆盖了上一次的评估小节。</param>
        public ChangeImpactMergeResult(bool merged, string filePath, string reason, bool replacedExistingSection)
        {
            Merged = merged;
            FilePath = filePath ?? "";
            Reason = reason ?? "";
            ReplacedExistingSection = replacedExistingSection;
        }

        /// <summary>写成了没有。</summary>
        public bool Merged { get; }

        /// <summary>变更影响文档路径。</summary>
        public string FilePath { get; }

        /// <summary>没写成时的原因；写成了为空串。</summary>
        public string Reason { get; }

        /// <summary>是不是覆盖了上一次的评估小节。</summary>
        public bool ReplacedExistingSection { get; }
    }

    /// <summary>
    /// 把执行后端的影响评估结果合并写进 <c>_Tasks/&lt;需求id&gt;/05-变更影响.md</c>
    /// （子文档 03 §三：未命中项由执行后端评估一轮，**合并写** 05-变更影响.md）。
    ///
    /// 三条硬规矩：
    /// 1. **只加一节，不动别的节。** 那份文档的前几节是重规划算出来的确定性结论，
    ///    LLM 的东西不许混进去改它们（决策 89：LLM 产的只是建议）。
    /// 2. **小节标题里写死「建议」二字**，读的人一眼知道这一节的性质与前面几节不同。
    /// 3. **重复合并要覆盖，不要越堆越多**：同一份文档跑两次评估，留最后一次那份，
    ///    并说明覆盖过——否则文档里会同时躺着两份互相矛盾的结论。
    ///
    /// 文档不存在时**不新建**：那份文档是重规划落地的产物，没有它说明还没重规划过，
    /// 这时候写一份只有 LLM 建议的文档会让人以为重规划跑过了。
    /// </summary>
    public static class ChangeImpactMerger
    {
        /// <summary>合并进去的小节标题，认得出来才能覆盖。</summary>
        public const string SectionHeading = "## 执行后端评估（建议，不是判定）";

        /// <summary>
        /// 合并一份影响评估报告进变更影响文档。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        /// <param name="report">影响评估报告。</param>
        public static ChangeImpactMergeResult Merge(string repositoryRoot, string requirementIdentifier, ImpactAssessReport report)
        {
            var filePath = PipelinePaths.ChangeImpactFile(repositoryRoot, requirementIdentifier);
            if (!File.Exists(filePath))
            {
                return new ChangeImpactMergeResult(
                    false,
                    filePath,
                    "变更影响文档还不存在，先跑一次 task.replan 让重规划把它落出来；这里不新建（新建会让人以为重规划跑过了）",
                    false);
            }

            string existing;
            try
            {
                existing = File.ReadAllText(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new ChangeImpactMergeResult(false, filePath, "变更影响文档读不动：" + exception.Message, false);
            }

            var stripped = RemoveSection(existing, out var replaced);
            var merged = stripped.TrimEnd() + Environment.NewLine + Environment.NewLine + BuildSection(report);

            try
            {
                File.WriteAllText(filePath, merged, new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new ChangeImpactMergeResult(false, filePath, "变更影响文档写不动：" + exception.Message, replaced);
            }

            return new ChangeImpactMergeResult(true, filePath, "", replaced);
        }

        /// <summary>
        /// 组这一节的正文：报告没判成、判成了但零条、逐条结论，三种情况都要有话说。
        /// **模型名与提示词版本必须写进去**（决策 89）——不写的话，两个月后没人说得清
        /// 那条「净」是怎么来的。
        /// </summary>
        /// <param name="report">影响评估报告。</param>
        public static string BuildSection(ImpactAssessReport report)
        {
            var builder = new StringBuilder();
            builder.Append(SectionHeading).Append(Environment.NewLine);
            builder.Append(Environment.NewLine);

            if (report == null)
            {
                builder.Append("- 没有报告").Append(Environment.NewLine);
                return builder.ToString();
            }

            builder.Append($"> 模型：{Blank(report.Model)}　提示词版本：{Blank(report.PromptVersion)}　判定键：{Blank(report.DecisionKey)}　来自缓存：{(report.FromCache ? "是" : "否")}")
                .Append(Environment.NewLine);
            builder.Append("> **这一节是建议，不是判定**：要不要因此回去改，是人和引擎的决定，门禁不看它（决策 89）。")
                .Append(Environment.NewLine);
            builder.Append(Environment.NewLine);

            if (!report.Parsed)
            {
                builder.Append($"- 这一轮**没判成**：{Blank(report.ParseReason)}").Append(Environment.NewLine);
                builder.Append("- 没判成不等于没问题——**不许当成「全净」**（决策 42）。").Append(Environment.NewLine);
                return builder.ToString();
            }

            builder.Append($"- 判定：脏 {report.DirtyCount} 项、净 {report.CleanCount} 项").Append(Environment.NewLine);
            if (report.Verdicts.Count == 0)
            {
                builder.Append("- 一条结论都没有（要评估的工作项是空的）").Append(Environment.NewLine);
            }

            foreach (var verdict in report.Verdicts)
            {
                builder.Append($"- **{verdict.WorkItem}** → {verdict.Conclusion}：{verdict.Reason}").Append(Environment.NewLine);
            }

            if (report.MissingWorkItems.Count > 0)
            {
                builder.Append(Environment.NewLine);
                builder.Append("### 模型漏答的工作项").Append(Environment.NewLine);
                builder.Append(Environment.NewLine);
                foreach (var missing in report.MissingWorkItems)
                {
                    builder.Append($"- {missing}（**没有结论，按脏处理，不许默认成净**）").Append(Environment.NewLine);
                }
            }

            return builder.ToString();
        }

        /// <summary>把已有的那一节整段去掉：从小节标题起，到下一个同级标题（## 开头）或文末为止。</summary>
        private static string RemoveSection(string text, out bool replaced)
        {
            replaced = false;
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            var start = lines.FindIndex(line => line.TrimEnd() == SectionHeading);
            if (start < 0)
            {
                return text;
            }

            replaced = true;
            var end = lines.Count;
            for (var index = start + 1; index < lines.Count; index++)
            {
                var line = lines[index];
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    end = index;
                    break;
                }
            }

            var kept = new List<string>();
            kept.AddRange(lines.Take(start));
            kept.AddRange(lines.Skip(end));
            return string.Join(Environment.NewLine, kept);
        }

        /// <summary>空串换成「（没写）」，别在文档里留一个空洞。</summary>
        private static string Blank(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? "（没写）" : text;
        }
    }
}
