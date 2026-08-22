using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次按所有权过滤的结果：留下的对象 + 被挡掉的字段名。</summary>
    public sealed class OwnershipFilterResult
    {
        /// <summary>
        /// 构造一次过滤结果。
        /// </summary>
        /// <param name="kept">过滤后留下的对象。</param>
        /// <param name="blockedFields">被挡掉的字段名，按字典序。</param>
        public OwnershipFilterResult(JsonObject kept, IReadOnlyList<string> blockedFields)
        {
            Kept = kept ?? new JsonObject();
            BlockedFields = blockedFields ?? Array.Empty<string>();
        }

        /// <summary>过滤后留下的对象。</summary>
        public JsonObject Kept { get; }

        /// <summary>被挡掉的字段名。**必须往上报**，静默丢弃就是决策 42 那类假象。</summary>
        public IReadOnlyList<string> BlockedFields { get; }
    }

    /// <summary>
    /// 字段所有权闸门（决策 33）：schema 每个字段都标了「所有权」，
    /// 谁拥有的字段只有谁能改。这道闸门把「一份外来的对象」按所有权切开，
    /// 留下允许的、把不允许的挡掉并**报出来**。
    ///
    /// 两个用处：
    /// - **入站**（下游拉回来的记录）：只许改下游拥有的那几个字段，
    ///   否则下游那边随便动一下就能覆盖掉工程侧的状态。
    /// - **助手草稿**：模型只该填策划端字段，id / 状态 / 锁定 这些工程字段由引擎补，
    ///   模型填了也不算数——它没有分配 id 的权力。
    /// </summary>
    public static class RequirementFieldOwnership
    {
        /// <summary>工程侧所有权的取值。</summary>
        public const string EngineOwner = "工程";

        /// <summary>策划端所有权的取值。</summary>
        public const string PlannerOwner = "策划端";

        /// <summary>
        /// 列出 schema 里归某个所有者的字段名。
        /// </summary>
        /// <param name="schema">需求 schema。</param>
        /// <param name="owner">所有者，如「工程」「策划端」。</param>
        public static IReadOnlyList<string> FieldsOwnedBy(PoolSchema schema, string owner)
        {
            if (schema == null)
            {
                return Array.Empty<string>();
            }

            return schema.Fields
                .Where(field => string.Equals(field.Ownership, owner, StringComparison.Ordinal))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// 这个字段名归不归工程侧。**schema 里没有这个字段时算「不归工程」**：
        /// 分类型必填与项目层自由加的业务字段都不在字段表里，它们是策划写的，
        /// 一律当工程字段挡掉的话，卡片上就一条人关心的内容都剩不下。
        /// </summary>
        /// <param name="schema">需求 schema。</param>
        /// <param name="fieldName">字段名。</param>
        public static bool IsEngineField(PoolSchema schema, string fieldName)
        {
            var field = schema?.FindField(fieldName);
            return field != null && string.Equals(field.Ownership, EngineOwner, StringComparison.Ordinal);
        }

        /// <summary>
        /// 只留下白名单里的字段，其余挡掉并报出来。
        /// 白名单是**字段名**，不是所有权——调用方可能要放行某个工程字段
        /// （比如入站允许下游写「来源」），所以闸门收的是最终名单。
        /// </summary>
        /// <param name="source">外来的对象。</param>
        /// <param name="allowedFields">允许的字段名。</param>
        public static OwnershipFilterResult KeepOnly(JsonObject source, IEnumerable<string> allowedFields)
        {
            var kept = new JsonObject();
            var blocked = new List<string>();
            if (source == null)
            {
                return new OwnershipFilterResult(kept, blocked);
            }

            var allowed = new HashSet<string>(allowedFields ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var pair in source)
            {
                if (allowed.Contains(pair.Key))
                {
                    kept[pair.Key] = pair.Value?.DeepClone();
                }
                else
                {
                    blocked.Add(pair.Key);
                }
            }

            blocked.Sort(StringComparer.Ordinal);
            return new OwnershipFilterResult(kept, blocked);
        }

        /// <summary>
        /// 需求入站闸门：从需求编辑端拉回来的一条记录，只许带回**策划端拥有的字段**
        /// 加上幂等键本身（不带 id 就不知道这条是谁）。
        /// 状态 / 锁定 / 同步 / 冲突 / 关联设计记录 这些归工程，下游那边动一下就能
        /// 覆盖掉引擎的状态机——那正是这道闸门要拦的。
        /// **分类型必填的那几个字段（现状 / 期望 / 目标 / 玩法 / 复现步骤 / 实际）
        /// 不在 schema 的字段表里**，但它们同样是策划写的，所以显式放行。
        /// </summary>
        /// <param name="record">下游记录。</param>
        /// <param name="schema">需求 schema。</param>
        /// <param name="identifierField">幂等键字段名，一律放行。</param>
        public static OwnershipFilterResult FilterInboundRequirement(JsonObject record, PoolSchema schema, string identifierField)
        {
            var allowed = new List<string>(FieldsOwnedBy(schema, PlannerOwner));
            if (schema != null)
            {
                foreach (var pair in schema.RequiredByType)
                {
                    allowed.AddRange(pair.Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(identifierField))
            {
                allowed.Add(identifierField);
            }

            return KeepOnly(record, allowed);
        }

        /// <summary>
        /// 专项入站闸门（决策 33）：**只许改「认领」与「来源」两个键**，其余一字不动。
        /// 这条比需求那条严得多，因为专项文件的其余内容全是工程侧算出来的。
        /// </summary>
        /// <param name="record">下游记录。</param>
        /// <param name="identifierField">幂等键字段名，一律放行。</param>
        public static OwnershipFilterResult FilterInboundEpicClaim(JsonObject record, string identifierField)
        {
            var allowed = new List<string> { "认领", "来源" };
            if (!string.IsNullOrWhiteSpace(identifierField))
            {
                allowed.Add(identifierField);
            }

            return KeepOnly(record, allowed);
        }
    }
}
