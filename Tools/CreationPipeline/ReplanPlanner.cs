using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一次打断重规划算出的计划：直接脏、传播后全部脏、净项、要后端评估的、
    /// 落在脏集内的「人改权威」文件、要不要问人，以及过程中的发现。
    /// </summary>
    public sealed class ReplanResult
    {
        /// <summary>
        /// 构造一次重规划计划。
        /// </summary>
        /// <param name="directlyDirty">被字段 diff 直接命中的工作项 id，序数序。</param>
        /// <param name="propagatedDirty">传播后的全部脏项，序数序。</param>
        /// <param name="clean">净项（全部工作项减去脏集），序数序。</param>
        /// <param name="needsBackendEvaluation">未被 diff 命中、需要执行后端评估一轮的工作项 id，序数序。</param>
        /// <param name="authoritativeFilesInDirtySet">落在脏集内的「人改权威」文件。</param>
        /// <param name="mustAskHuman">脏集内是否有人改权威文件，需要停下问人。</param>
        /// <param name="findings">过程中的发现，如零字段变更、依赖图有环。</param>
        internal ReplanResult(
            IReadOnlyList<string> directlyDirty,
            IReadOnlyList<string> propagatedDirty,
            IReadOnlyList<string> clean,
            IReadOnlyList<string> needsBackendEvaluation,
            IReadOnlyList<string> authoritativeFilesInDirtySet,
            bool mustAskHuman,
            IReadOnlyList<string> findings)
        {
            DirectlyDirty = directlyDirty;
            PropagatedDirty = propagatedDirty;
            Clean = clean;
            NeedsBackendEvaluation = needsBackendEvaluation;
            AuthoritativeFilesInDirtySet = authoritativeFilesInDirtySet;
            MustAskHuman = mustAskHuman;
            Findings = findings;
        }

        /// <summary>被字段 diff 直接命中的工作项 id，序数序。</summary>
        public IReadOnlyList<string> DirectlyDirty { get; }

        /// <summary>传播后的全部脏项（含直接脏），序数序。</summary>
        public IReadOnlyList<string> PropagatedDirty { get; }

        /// <summary>净项：全部工作项减去传播后的脏集，序数序。</summary>
        public IReadOnlyList<string> Clean { get; }

        /// <summary>未被 diff 命中、需要执行后端评估一轮的工作项 id，序数序；本类只列名单不调后端。</summary>
        public IReadOnlyList<string> NeedsBackendEvaluation { get; }

        /// <summary>落在脏集内的「人改权威」文件。</summary>
        public IReadOnlyList<string> AuthoritativeFilesInDirtySet { get; }

        /// <summary>脏集内是否有人改权威文件，需要停下问人。</summary>
        public bool MustAskHuman { get; }

        /// <summary>过程中的发现，中文文案。</summary>
        public IReadOnlyList<string> Findings { get; }
    }

    /// <summary>
    /// 打断重规划：按字段 diff 把工作项分成脏集与净集，并列出要后端评估、要问人的地方。
    /// 本类只算计划，不改任何工作项状态、不写盘、不调执行后端。
    /// </summary>
    public static class ReplanPlanner
    {
        /// <summary>
        /// 算一次重规划计划：命中引用需求字段的进直接脏，沿依赖图向下游传播，
        /// 未命中且不在脏集的进后端评估名单，人改权威文件落在脏集内则要问人。
        /// 字段 diff 为空时全部为空并出一条「零字段变更」finding；依赖图有环时出一条 finding 但照常算完。
        /// </summary>
        /// <param name="graph">工作项依赖图。</param>
        /// <param name="changedRequirementFields">累积字段 diff 命中的需求字段名。</param>
        /// <param name="authoritativeFilesByWorkItem">工作项 id 到「人改权威」文件的映射，传 null 视为空字典。</param>
        public static ReplanResult Plan(
            WorkItemGraph graph,
            IReadOnlyList<string> changedRequirementFields,
            IReadOnlyDictionary<string, IReadOnlyList<string>> authoritativeFilesByWorkItem)
        {
            var findings = new List<string>();
            var changedFields = changedRequirementFields ?? Array.Empty<string>();
            var authoritativeFiles = authoritativeFilesByWorkItem
                ?? (IReadOnlyDictionary<string, IReadOnlyList<string>>)new Dictionary<string, IReadOnlyList<string>>();

            if (changedFields.Count == 0)
            {
                findings.Add("零字段变更，无需重规划");
                return new ReplanResult(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    false,
                    findings);
            }

            var changedSet = new HashSet<string>(changedFields, StringComparer.Ordinal);
            var directlyDirty = new List<string>();
            foreach (var node in graph.Nodes)
            {
                foreach (var field in node.ReferencedRequirementFields)
                {
                    if (changedSet.Contains(field))
                    {
                        directlyDirty.Add(node.Identifier);
                        break;
                    }
                }
            }

            directlyDirty.Sort(StringComparer.Ordinal);

            var propagatedDirty = graph.PropagateDirty(directlyDirty);
            var dirtySet = new HashSet<string>(propagatedDirty, StringComparer.Ordinal);

            var clean = new List<string>();
            var needsBackendEvaluation = new List<string>();
            foreach (var node in graph.Nodes)
            {
                if (dirtySet.Contains(node.Identifier))
                {
                    continue;
                }

                clean.Add(node.Identifier);
                needsBackendEvaluation.Add(node.Identifier);
            }

            var authoritativeFilesInDirtySet = new List<string>();
            foreach (var pair in authoritativeFiles)
            {
                if (!dirtySet.Contains(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                foreach (var filePath in pair.Value)
                {
                    authoritativeFilesInDirtySet.Add(filePath);
                }
            }

            authoritativeFilesInDirtySet.Sort(StringComparer.Ordinal);
            var mustAskHuman = authoritativeFilesInDirtySet.Count > 0;

            if (graph.HasCycle())
            {
                findings.Add("依赖图存在环：脏传播已按去重处理不栈溢出，请检查工作项依赖");
            }

            return new ReplanResult(
                directlyDirty,
                propagatedDirty,
                clean,
                needsBackendEvaluation,
                authoritativeFilesInDirtySet,
                mustAskHuman,
                findings);
        }
    }
}
