using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一格的同步去向。</summary>
    public enum ProgressSyncDirection
    {
        /// <summary>两侧一样，什么都不用做。</summary>
        None,

        /// <summary>工程侧的值要盖到下游去。</summary>
        Outbound,

        /// <summary>下游的值要收回仓库。</summary>
        Inbound,

        /// <summary>两侧相对上次同步都动过，谁也不许盖谁——落冲突。</summary>
        Conflict
    }

    /// <summary>一格的裁定：哪条需求的哪个字段、三个值各是什么、去哪、为什么。</summary>
    public sealed class ProgressSyncDecision
    {
        /// <summary>
        /// 构造一格裁定。
        /// </summary>
        /// <param name="identifier">需求 id。</param>
        /// <param name="fieldName">字段名。</param>
        /// <param name="direction">去向。</param>
        /// <param name="engineValue">工程侧当前值。</param>
        /// <param name="downstreamValue">下游当前值。</param>
        /// <param name="baselineValue">上次同步的商定值。</param>
        /// <param name="reason">为什么是这个去向，一句人话。</param>
        public ProgressSyncDecision(
            string identifier,
            string fieldName,
            ProgressSyncDirection direction,
            string engineValue,
            string downstreamValue,
            string baselineValue,
            string reason)
        {
            Identifier = identifier ?? "";
            FieldName = fieldName ?? "";
            Direction = direction;
            EngineValue = engineValue ?? "";
            DownstreamValue = downstreamValue ?? "";
            BaselineValue = baselineValue ?? "";
            Reason = reason ?? "";
        }

        /// <summary>需求 id。</summary>
        public string Identifier { get; }

        /// <summary>字段名。</summary>
        public string FieldName { get; }

        /// <summary>去向。</summary>
        public ProgressSyncDirection Direction { get; }

        /// <summary>工程侧当前值。</summary>
        public string EngineValue { get; }

        /// <summary>下游当前值。</summary>
        public string DownstreamValue { get; }

        /// <summary>上次同步的商定值。</summary>
        public string BaselineValue { get; }

        /// <summary>为什么是这个去向。</summary>
        public string Reason { get; }

        /// <summary>同步之后这一格该是什么值：出站取工程侧、入站取下游、其余保持工程侧。</summary>
        public string SettledValue
        {
            get
            {
                return Direction == ProgressSyncDirection.Inbound ? DownstreamValue : EngineValue;
            }
        }
    }

    /// <summary>一轮同步的计划：逐格裁定 + 下游还没有行的需求。</summary>
    public sealed class ProgressSyncPlan
    {
        /// <summary>
        /// 构造一份计划。
        /// </summary>
        /// <param name="decisions">逐格裁定。</param>
        /// <param name="rowsToCreate">池子里有、下游任务表里还没有行的需求 id。</param>
        /// <param name="firstRun">这一轮是不是「没有基线」的第一次。</param>
        public ProgressSyncPlan(IReadOnlyList<ProgressSyncDecision> decisions, IReadOnlyList<string> rowsToCreate, bool firstRun)
        {
            Decisions = decisions ?? Array.Empty<ProgressSyncDecision>();
            RowsToCreate = rowsToCreate ?? Array.Empty<string>();
            FirstRun = firstRun;
        }

        /// <summary>逐格裁定。</summary>
        public IReadOnlyList<ProgressSyncDecision> Decisions { get; }

        /// <summary>池子里有、下游还没有行的需求 id。</summary>
        public IReadOnlyList<string> RowsToCreate { get; }

        /// <summary>这一轮有没有基线可比。</summary>
        public bool FirstRun { get; }

        /// <summary>要出站的那些格。</summary>
        public IReadOnlyList<ProgressSyncDecision> Outbound()
        {
            return Decisions.Where(decision => decision.Direction == ProgressSyncDirection.Outbound).ToList();
        }

        /// <summary>要入站的那些格。</summary>
        public IReadOnlyList<ProgressSyncDecision> Inbound()
        {
            return Decisions.Where(decision => decision.Direction == ProgressSyncDirection.Inbound).ToList();
        }

        /// <summary>判成冲突的那些格。</summary>
        public IReadOnlyList<ProgressSyncDecision> Conflicts()
        {
            return Decisions.Where(decision => decision.Direction == ProgressSyncDirection.Conflict).ToList();
        }

        /// <summary>
        /// 按裁定折出「这一轮结束时两侧应该长什么样」的快照，用来更新基线。
        /// **冲突那几格取工程侧当前值**，不是取下游、也不是取基线：
        /// 冲突尚未裁决，这一轮谁也没盖谁，工程侧的值就是仓库此刻的事实。
        /// 基线记事实，不记愿望。
        /// </summary>
        /// <param name="engineSnapshot">工程侧快照，全局那一块原样带过去。</param>
        public ProgressSnapshot SettledSnapshot(ProgressSnapshot engineSnapshot)
        {
            var byIdentifier = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var decision in Decisions)
            {
                if (!byIdentifier.TryGetValue(decision.Identifier, out var fields))
                {
                    fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    byIdentifier[decision.Identifier] = fields;
                }

                fields[decision.FieldName] = decision.SettledValue;
            }

            var entries = byIdentifier
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ProgressEntry(pair.Key, pair.Value))
                .ToList();

            return new ProgressSnapshot(entries, engineSnapshot?.Global);
        }
    }

    /// <summary>
    /// 三值比对：工程侧、下游、上次同步的基线，逐格裁出去向。
    ///
    /// 规矩（子文档 02 §一「同步永远单向复制」的进度版）：
    /// 1. 两侧一样 → 什么都不做。
    /// 2. 没有基线（第一次同步）→ 一律按权威侧复制。这一步**不判冲突**：
    ///    第一次比对时「不一样」只说明两侧还没对过账，不说明有人改过什么。
    /// 3. 只有一侧相对基线动过 → 按权威侧复制。注意是「按权威侧」不是「按动的那一侧」：
    ///    非权威侧擅自改了权威侧的字段，正是要被盖回去的那种改动。
    /// 4. 两侧相对基线都动过 → <see cref="ProgressSyncDirection.Conflict"/>，谁也不盖谁。
    ///    这一条是任务书里那句「同时改了不许静默挑一边」的落点。
    /// </summary>
    public static class ProgressSyncPlanner
    {
        /// <summary>
        /// 裁一轮计划。
        /// </summary>
        /// <param name="engineSnapshot">工程侧快照。</param>
        /// <param name="downstreamSnapshot">下游快照。</param>
        /// <param name="baseline">上次同步的基线。</param>
        /// <param name="hasBaseline">基线在不在（不在 = 第一次同步）。</param>
        /// <param name="schema">权威侧表。</param>
        public static ProgressSyncPlan Plan(
            ProgressSnapshot engineSnapshot,
            ProgressSnapshot downstreamSnapshot,
            ProgressSnapshot baseline,
            bool hasBaseline,
            ProgressSyncSchema schema)
        {
            var decisions = new List<ProgressSyncDecision>();
            var rowsToCreate = new List<string>();
            var engine = engineSnapshot ?? new ProgressSnapshot(null, null);
            var downstream = downstreamSnapshot ?? new ProgressSnapshot(null, null);
            var baselineSnapshot = baseline ?? new ProgressSnapshot(null, null);
            var fields = schema?.Fields ?? Array.Empty<ProgressSyncField>();

            foreach (var engineEntry in engine.Entries)
            {
                var downstreamEntry = downstream.Find(engineEntry.Identifier);
                if (downstreamEntry == null)
                {
                    // 下游还没有这一行。**不当成「下游把值清空了」**——那会让每条新需求
                    // 一进池子就先判出一串「下游改成了空」的假动静。要做的是去建行。
                    rowsToCreate.Add(engineEntry.Identifier);
                    continue;
                }

                var baselineEntry = baselineSnapshot.Find(engineEntry.Identifier);
                foreach (var field in fields)
                {
                    decisions.Add(Decide(engineEntry, downstreamEntry, baselineEntry, hasBaseline, field));
                }
            }

            return new ProgressSyncPlan(decisions, rowsToCreate, !hasBaseline);
        }

        /// <summary>裁一格。</summary>
        private static ProgressSyncDecision Decide(
            ProgressEntry engineEntry,
            ProgressEntry downstreamEntry,
            ProgressEntry baselineEntry,
            bool hasBaseline,
            ProgressSyncField field)
        {
            var engineValue = engineEntry.Value(field.Name);
            var downstreamValue = downstreamEntry.Value(field.Name);
            var baselineValue = baselineEntry == null ? "" : baselineEntry.Value(field.Name);
            var authoritySide = field.IsEngineOwned ? ProgressSyncDirection.Outbound : ProgressSyncDirection.Inbound;

            if (string.Equals(engineValue, downstreamValue, StringComparison.Ordinal))
            {
                return new ProgressSyncDecision(
                    engineEntry.Identifier, field.Name, ProgressSyncDirection.None,
                    engineValue, downstreamValue, baselineValue, "两侧一样");
            }

            var hasRow = hasBaseline && baselineEntry != null;
            if (!hasRow)
            {
                return new ProgressSyncDecision(
                    engineEntry.Identifier, field.Name, authoritySide,
                    engineValue, downstreamValue, baselineValue,
                    $"没有可比的基线，按权威侧「{field.Authority}」复制");
            }

            var engineMoved = !string.Equals(engineValue, baselineValue, StringComparison.Ordinal);
            var downstreamMoved = !string.Equals(downstreamValue, baselineValue, StringComparison.Ordinal);

            if (engineMoved && downstreamMoved)
            {
                return new ProgressSyncDecision(
                    engineEntry.Identifier, field.Name, ProgressSyncDirection.Conflict,
                    engineValue, downstreamValue, baselineValue,
                    "两侧相对上次同步都改过，谁也不许盖谁");
            }

            var mover = engineMoved ? "工程侧" : "下游";
            return new ProgressSyncDecision(
                engineEntry.Identifier, field.Name, authoritySide,
                engineValue, downstreamValue, baselineValue,
                $"只有{mover}改过，按权威侧「{field.Authority}」复制");
        }
    }
}
