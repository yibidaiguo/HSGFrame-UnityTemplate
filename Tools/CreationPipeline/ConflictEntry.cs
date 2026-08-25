using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一条冲突：新旧配对、发现阶段、状态与裁决结果，形状见子文档 01 §六。</summary>
    public sealed class ConflictEntry
    {
        /// <summary>合法的三选一裁决值。</summary>
        public static readonly string[] AllowedChoices = { "改新的", "改旧的", "强制推送" };

        /// <summary>
        /// 合法的发现阶段。「进度同步」这一档是后加的：进度在仓库与下游之间双向同步时，
        /// 同一格两侧相对上次同步都改过 → 不许静默挑一边，落成冲突走同一条裁决通道
        /// （<see cref="ProgressSyncPlanner"/>）。加一档而不是复用「入库」，
        /// 是因为人在冲突页上第一眼要看出的就是「这条是从哪儿冒出来的」。
        /// </summary>
        public static readonly string[] AllowedStages = { "入库", "影响评估", "进度同步" };

        /// <summary>id 模式：CF- 加四位数字。</summary>
        public const string IdentifierPatternText = "^CF-\\d{4}$";

        /// <summary>未决状态值。</summary>
        public const string PendingState = "未决";

        /// <summary>已裁决状态值。</summary>
        public const string ResolvedState = "已裁决";

        /// <summary>
        /// 「强制推送」这个选择：挂账不销账。
        /// 这里也留一份是因为对齐待办要按它分档——强制推送不产生对齐义务（两侧本来就没动）。
        /// </summary>
        public const string ForcePushChoice = "强制推送";

        /// <summary>
        /// 构造一条冲突条目。
        /// </summary>
        /// <param name="identifier">冲突 id，形如 CF-0009。</param>
        /// <param name="oldIdentifier">旧设计或旧需求 id。</param>
        /// <param name="newIdentifier">新需求 id。</param>
        /// <param name="discoveryStage">发现阶段：入库 / 影响评估 / 进度同步。</param>
        /// <param name="state">状态：未决 / 已裁决。</param>
        /// <param name="resolverName">裁决人，未决时为空串。</param>
        /// <param name="choice">裁决选择，未决时为空串。</param>
        /// <param name="resolvedMoment">裁决时间，未决时为空串。</param>
        /// <param name="hasResolutionPayload">裁决对象是否非 null。</param>
        /// <param name="alignmentTodo">对齐待办：裁决落定后还要人动手改的那一侧，逐条。</param>
        /// <param name="isAligned">对齐待办是否已经做完。</param>
        /// <param name="alignerName">对齐人，没对齐时为空串。</param>
        /// <param name="alignedMoment">对齐时间，没对齐时为空串。</param>
        internal ConflictEntry(
            string identifier,
            string oldIdentifier,
            string newIdentifier,
            string discoveryStage,
            string state,
            string resolverName,
            string choice,
            string resolvedMoment,
            bool hasResolutionPayload,
            IReadOnlyList<string> alignmentTodo = null,
            bool isAligned = false,
            string alignerName = "",
            string alignedMoment = "")
        {
            AlignmentTodo = alignmentTodo ?? Array.Empty<string>();
            IsAligned = isAligned;
            AlignerName = alignerName ?? "";
            AlignedMoment = alignedMoment ?? "";
            Identifier = identifier;
            OldIdentifier = oldIdentifier;
            NewIdentifier = newIdentifier;
            DiscoveryStage = discoveryStage;
            State = state;
            ResolverName = resolverName;
            Choice = choice;
            ResolvedMoment = resolvedMoment;
            HasResolutionPayload = hasResolutionPayload;
        }

        /// <summary>冲突 id，形如 CF-0009。</summary>
        public string Identifier { get; }

        /// <summary>旧设计或旧需求 id。</summary>
        public string OldIdentifier { get; }

        /// <summary>新需求 id。</summary>
        public string NewIdentifier { get; }

        /// <summary>发现阶段：入库 / 影响评估 / 进度同步。</summary>
        public string DiscoveryStage { get; }

        /// <summary>状态：未决 / 已裁决。</summary>
        public string State { get; }

        /// <summary>裁决人，未决时为空串。</summary>
        public string ResolverName { get; }

        /// <summary>裁决选择，未决时为空串。</summary>
        public string Choice { get; }

        /// <summary>裁决时间，未决时为空串。</summary>
        public string ResolvedMoment { get; }

        /// <summary>裁决对象是否非 null；未决条目的裁决必须为 null，门禁用它查状态与裁决对不上。</summary>
        public bool HasResolutionPayload { get; }

        /// <summary>
        /// 对齐待办：裁决落定之后**还要人动手改的那一侧**，逐条。
        ///
        /// 裁决本身不改任何一侧的内容——那是有意的：冲突这时候确实还在，
        /// 一个命令自动去改需求或设计，改错了没人看得见。
        /// 但「不自动改」不等于「不用改」，以前这几句话只在裁决那一刻打印一次就没了，
        /// 于是下一轮探测照旧判出同一个冲突，而没人说得清上次到底做没做。
        /// 所以它落盘：待办是账的一部分，不是一次输出。
        /// </summary>
        public IReadOnlyList<string> AlignmentTodo { get; }

        /// <summary>对齐待办是否已经做完（走 conflict.align 销）。没有待办的条目恒为 true。</summary>
        public bool IsAligned { get; }

        /// <summary>对齐人，没对齐时为空串。</summary>
        public string AlignerName { get; }

        /// <summary>对齐时间，没对齐时为空串。</summary>
        public string AlignedMoment { get; }
    }
}
