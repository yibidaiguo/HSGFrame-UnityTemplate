using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 审查包组装所需的全部输入：需求标识、变更清单、方案对照、预审报告、验收报告与提交清单。
    /// 本类只带数据，不跑 git、不起子进程——变更清单由调用方传进来。
    /// </summary>
    public sealed class ReviewPackageInput
    {
        /// <summary>
        /// 构造审查包输入。
        /// </summary>
        /// <param name="requirementIdentifier">需求 id。</param>
        /// <param name="changedPaths">本次改动的仓库相对路径列表。</param>
        /// <param name="changedLineCount">改动行数。</param>
        /// <param name="planDeviationText">方案对照文本：实现 vs 方案的偏差说明。</param>
        /// <param name="preReviewText">预审报告文本。</param>
        /// <param name="acceptanceText">验收报告文本。</param>
        /// <param name="commitSubjects">提交清单，按工作项分 commit 的主题行。</param>
        public ReviewPackageInput(
            string requirementIdentifier,
            IReadOnlyList<string> changedPaths,
            int changedLineCount,
            string planDeviationText,
            string preReviewText,
            string acceptanceText,
            IReadOnlyList<string> commitSubjects)
        {
            RequirementIdentifier = requirementIdentifier ?? "";
            ChangedPaths = changedPaths ?? Array.Empty<string>();
            ChangedLineCount = changedLineCount;
            PlanDeviationText = planDeviationText ?? "";
            PreReviewText = preReviewText ?? "";
            AcceptanceText = acceptanceText ?? "";
            CommitSubjects = commitSubjects ?? Array.Empty<string>();
        }

        /// <summary>需求 id。</summary>
        public string RequirementIdentifier { get; }

        /// <summary>本次改动的仓库相对路径列表。</summary>
        public IReadOnlyList<string> ChangedPaths { get; }

        /// <summary>改动行数。</summary>
        public int ChangedLineCount { get; }

        /// <summary>方案对照文本：实现 vs 方案的偏差说明。</summary>
        public string PlanDeviationText { get; }

        /// <summary>预审报告文本。</summary>
        public string PreReviewText { get; }

        /// <summary>验收报告文本。</summary>
        public string AcceptanceText { get; }

        /// <summary>提交清单，按工作项分 commit 的主题行。</summary>
        public IReadOnlyList<string> CommitSubjects { get; }
    }

    /// <summary>
    /// 审查包五件套的组装器：变更地图、方案对照、预审报告、验收报告、提交清单。
    /// 本类只组装文本，不跑 git、不起子进程。
    /// </summary>
    public static class ReviewPackageBuilder
    {
        /// <summary>高危范围静态清单，与 RiskGrader 的缺省值一致；变更地图里这些范围的组标题加「（高危）」。</summary>
        private static readonly string[] HighRiskScopes = { "框架", "引擎", "检查器", "构建", "规范" };

        /// <summary>
        /// 组装一份审查包 Markdown。
        /// </summary>
        /// <param name="input">审查包输入。</param>
        /// <param name="risk">风险分级结果。</param>
        /// <param name="decision">放行判定结果。</param>
        public static string Build(ReviewPackageInput input, RiskGradeResult risk, ReleaseDecision decision)
        {
            var scopes = risk?.Scopes ?? Array.Empty<string>();
            var builder = new StringBuilder();

            builder.AppendLine($"# 审查包：{input?.RequirementIdentifier ?? ""}");
            builder.AppendLine();
            builder.AppendLine($"风险级：{risk?.Grade ?? ""}");
            builder.AppendLine($"范围：{(scopes.Count == 0 ? "无" : string.Join("、", scopes))}");

            var isAutomatic = decision?.IsAutomatic ?? false;
            var conclusion = isAutomatic ? "自动放行" : "人审";
            builder.AppendLine($"放行结论：{conclusion}");
            if (decision != null && decision.Reasons.Count > 0)
            {
                foreach (var reason in decision.Reasons)
                {
                    builder.AppendLine($"- {reason}");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## 一、变更地图");
            AppendChangeMap(builder, input?.ChangedPaths ?? Array.Empty<string>(), input?.ChangedLineCount ?? 0, scopes);

            builder.AppendLine();
            builder.AppendLine("## 二、方案对照");
            AppendOptionalText(builder, input?.PlanDeviationText);

            builder.AppendLine();
            builder.AppendLine("## 三、预审报告");
            AppendOptionalText(builder, input?.PreReviewText);

            builder.AppendLine();
            builder.AppendLine("## 四、验收报告");
            AppendOptionalText(builder, input?.AcceptanceText);

            builder.AppendLine();
            builder.AppendLine("## 五、提交清单");
            var subjects = input?.CommitSubjects ?? Array.Empty<string>();
            if (subjects.Count == 0)
            {
                builder.AppendLine("（未提供）");
            }
            else
            {
                foreach (var subject in subjects)
                {
                    builder.AppendLine($"- {subject}");
                }
            }

            return builder.ToString();
        }

        /// <summary>按范围分组列路径：高危范围的组标题加「（高危）」，路径为空写「（未提供）」。</summary>
        private static void AppendChangeMap(StringBuilder builder, IReadOnlyList<string> changedPaths, int changedLineCount, IReadOnlyList<string> riskScopes)
        {
            builder.AppendLine($"改动行数：{changedLineCount}");

            if (changedPaths.Count == 0)
            {
                builder.AppendLine("（未提供）");
                return;
            }

            var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var path in changedPaths)
            {
                var scope = ChangeScopeClassifier.Classify(path);
                if (!grouped.TryGetValue(scope, out var paths))
                {
                    paths = new List<string>();
                    grouped[scope] = paths;
                }

                paths.Add(path);
            }

            // 组顺序：先风险级里出现过的范围（已序数序），再其余范围按序数序。
            var orderedScopes = new List<string>();
            foreach (var scope in riskScopes)
            {
                if (grouped.ContainsKey(scope))
                {
                    orderedScopes.Add(scope);
                }
            }

            foreach (var scope in grouped.Keys.OrderBy(s => s, StringComparer.Ordinal))
            {
                if (!orderedScopes.Contains(scope))
                {
                    orderedScopes.Add(scope);
                }
            }

            foreach (var scope in orderedScopes)
            {
                var highRiskMark = HighRiskScopes.Contains(scope, StringComparer.Ordinal) ? "（高危）" : "";
                builder.AppendLine($"{scope}{highRiskMark}：");
                foreach (var path in grouped[scope])
                {
                    builder.AppendLine($"- {path}");
                }
            }
        }

        /// <summary>四段文本之一为空时写「（未提供）」，不留空段。</summary>
        private static void AppendOptionalText(StringBuilder builder, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                builder.AppendLine("（未提供）");
            }
            else
            {
                builder.AppendLine(text);
            }
        }
    }
}
