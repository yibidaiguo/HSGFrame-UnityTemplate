using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一条未决冲突的债务视图：从冲突条目投影出审查/助手要展示的那几个字段，
    /// 并把「是否强制推送挂账」与「一句话人话摘要」算好，消费方不用再碰原始判据。
    /// </summary>
    public sealed class ConflictDebtItem
    {
        /// <summary>
        /// 构造一条未决冲突债务项。
        /// </summary>
        /// <param name="identifier">冲突 id，形如 CF-0009。</param>
        /// <param name="oldIdentifier">旧设计或旧需求 id。</param>
        /// <param name="newIdentifier">新需求 id。</param>
        /// <param name="discoveryStage">发现阶段：入库 / 影响评估。</param>
        /// <param name="isForcePushed">是否强制推送挂的账。</param>
        /// <param name="forcePusherName">强制推送的人；不是强推的是空串。</param>
        /// <param name="summary">一句人话摘要。</param>
        internal ConflictDebtItem(
            string identifier,
            string oldIdentifier,
            string newIdentifier,
            string discoveryStage,
            bool isForcePushed,
            string forcePusherName,
            string summary)
        {
            Identifier = identifier;
            OldIdentifier = oldIdentifier;
            NewIdentifier = newIdentifier;
            DiscoveryStage = discoveryStage;
            IsForcePushed = isForcePushed;
            ForcePusherName = forcePusherName;
            Summary = summary;
        }

        /// <summary>冲突 id，形如 CF-0009。</summary>
        public string Identifier { get; }

        /// <summary>旧设计或旧需求 id。</summary>
        public string OldIdentifier { get; }

        /// <summary>新需求 id。</summary>
        public string NewIdentifier { get; }

        /// <summary>发现阶段：入库 / 影响评估。</summary>
        public string DiscoveryStage { get; }

        /// <summary>是不是强制推送挂的账。</summary>
        public bool IsForcePushed { get; }

        /// <summary>强制推送的人；不是强推的是空串。</summary>
        public string ForcePusherName { get; }

        /// <summary>一句人话：冲突双方、发现阶段、谁强推的、还没销账。</summary>
        public string Summary { get; }
    }

    /// <summary>
    /// 某次冲突债务查询的结果：未决条目、池子未决总数与「到底查成了没有」。
    /// Scanned 与条目数必须分开看——「零未决」和「冲突列表读不动」是两个分支，
    /// 把读不动说成没有冲突是最典型的假绿（决策 42）。
    /// </summary>
    public sealed class ConflictDebtReport
    {
        /// <summary>
        /// 构造一次冲突债务查询结果。
        /// </summary>
        /// <param name="items">未决条目，按冲突 id 序数序。</param>
        /// <param name="totalPending">池子里的未决总数（不只本需求的）。</param>
        /// <param name="scanned">冲突列表读成了没有；读不动时 false。</param>
        /// <param name="loadFailureReason">加载失败原因；查成了是空串。</param>
        internal ConflictDebtReport(
            IReadOnlyList<ConflictDebtItem> items,
            int totalPending,
            bool scanned,
            string loadFailureReason)
        {
            Items = items ?? Array.Empty<ConflictDebtItem>();
            TotalPending = totalPending;
            Scanned = scanned;
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>未决条目，按冲突 id 序数序。</summary>
        public IReadOnlyList<ConflictDebtItem> Items { get; }

        /// <summary>池子里的未决总数（不只本需求的），不受需求过滤影响。</summary>
        public int TotalPending { get; }

        /// <summary>冲突列表读成了没有；读不动时 false。零未决与读不动是两回事。</summary>
        public bool Scanned { get; }

        /// <summary>加载失败原因；查成了是空串。</summary>
        public string LoadFailureReason { get; }
    }

    /// <summary>
    /// 冲突债务的只读视图：从冲突列表算出某需求相关的未决冲突与涉区提醒。
    /// 纯计算器——不写盘、不读盘（ConflictList 已经替它读过了），
    /// 未决判据与 ConflictList.PendingCount() 完全一致，不许另写一套。
    /// </summary>
    public static class ConflictDebtView
    {
        /// <summary>
        /// 查一个需求的未决冲突；需求 id 为空白时留全部未决条目（给「看全局」用）。
        /// </summary>
        /// <param name="list">已加载的冲突列表；null 视为没查成。</param>
        /// <param name="requirementIdentifier">需求或设计 id；空白 = 不过滤。</param>
        public static ConflictDebtReport ForRequirement(ConflictList list, string requirementIdentifier)
        {
            if (list == null)
            {
                return new ConflictDebtReport(Array.Empty<ConflictDebtItem>(), 0, false, "冲突列表没加载");
            }

            if (list.LoadFailureReason.Length > 0)
            {
                // 列表残缺（LoadFailureReason 非空但 Entries 有内容也算）就不能拿它下「无冲突」的结论。
                return new ConflictDebtReport(Array.Empty<ConflictDebtItem>(), 0, false, list.LoadFailureReason);
            }

            var pending = new List<ConflictDebtItem>();
            var totalPending = 0;
            foreach (var entry in list.Entries)
            {
                if (!IsPending(entry))
                {
                    continue;
                }

                totalPending++;
                if (MatchesRequirement(entry, requirementIdentifier))
                {
                    pending.Add(ToItem(entry));
                }
            }

            pending.Sort((left, right) => string.CompareOrdinal(left.Identifier, right.Identifier));
            return new ConflictDebtReport(pending, totalPending, true, "");
        }

        /// <summary>查整个池子的未决冲突，等价于传空白需求 id。</summary>
        /// <param name="list">已加载的冲突列表；null 视为没查成。</param>
        public static ConflictDebtReport All(ConflictList list)
        {
            return ForRequirement(list, "");
        }

        /// <summary>
        /// 未决冲突涉及的全部需求/设计 id，去重后按序数序排列——这就是「涉区」，
        /// 助手拿它提醒「你这条需求碰到的区域上还挂着账」。
        /// </summary>
        /// <param name="report">冲突债务查询结果。</param>
        public static IReadOnlyList<string> AffectedIdentifiers(ConflictDebtReport report)
        {
            if (report == null)
            {
                return Array.Empty<string>();
            }

            var identifiers = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var item in report.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.OldIdentifier))
                {
                    identifiers.Add(item.OldIdentifier);
                }

                if (!string.IsNullOrWhiteSpace(item.NewIdentifier))
                {
                    identifiers.Add(item.NewIdentifier);
                }
            }

            return new List<string>(identifiers);
        }

        /// <summary>未决判据与 ConflictList.PendingCount() 完全一致：状态不是已裁决，或选择是强制推送。</summary>
        private static bool IsPending(ConflictEntry entry)
        {
            return string.Equals(entry.State, ConflictEntry.PendingState, StringComparison.Ordinal)
                || string.Equals(entry.Choice, "强制推送", StringComparison.Ordinal);
        }

        /// <summary>需求 id 为空白时留全部；否则只看旧或新命中该 id 的条目。</summary>
        private static bool MatchesRequirement(ConflictEntry entry, string requirementIdentifier)
        {
            if (string.IsNullOrWhiteSpace(requirementIdentifier))
            {
                return true;
            }

            return string.Equals(entry.OldIdentifier, requirementIdentifier, StringComparison.Ordinal)
                || string.Equals(entry.NewIdentifier, requirementIdentifier, StringComparison.Ordinal);
        }

        /// <summary>投影出一条债务项：强推信息取裁决对象里的「人」，摘要按既定句式组。</summary>
        private static ConflictDebtItem ToItem(ConflictEntry entry)
        {
            var isForcePushed = string.Equals(entry.Choice, "强制推送", StringComparison.Ordinal);
            var forcePusherName = isForcePushed ? entry.ResolverName : "";
            var summary = $"{entry.Identifier}：{entry.OldIdentifier} 与 {entry.NewIdentifier} 冲突（{entry.DiscoveryStage}阶段发现）";
            if (isForcePushed)
            {
                summary += $"，{forcePusherName}强制推送挂账，尚未销账";
            }
            else
            {
                summary += "，尚未销账";
            }

            return new ConflictDebtItem(
                entry.Identifier,
                entry.OldIdentifier,
                entry.NewIdentifier,
                entry.DiscoveryStage,
                isForcePushed,
                forcePusherName,
                summary);
        }
    }
}
