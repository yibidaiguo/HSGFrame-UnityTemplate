namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一条冲突：新旧配对、发现阶段、状态与裁决结果，形状见子文档 01 §六。</summary>
    public sealed class ConflictEntry
    {
        /// <summary>合法的三选一裁决值。</summary>
        public static readonly string[] AllowedChoices = { "改新的", "改旧的", "强制推送" };

        /// <summary>合法的发现阶段。</summary>
        public static readonly string[] AllowedStages = { "入库", "影响评估" };

        /// <summary>id 模式：CF- 加四位数字。</summary>
        public const string IdentifierPatternText = "^CF-\\d{4}$";

        /// <summary>未决状态值。</summary>
        public const string PendingState = "未决";

        /// <summary>已裁决状态值。</summary>
        public const string ResolvedState = "已裁决";

        /// <summary>
        /// 构造一条冲突条目。
        /// </summary>
        /// <param name="identifier">冲突 id，形如 CF-0009。</param>
        /// <param name="oldIdentifier">旧设计或旧需求 id。</param>
        /// <param name="newIdentifier">新需求 id。</param>
        /// <param name="discoveryStage">发现阶段：入库 / 影响评估。</param>
        /// <param name="state">状态：未决 / 已裁决。</param>
        /// <param name="resolverName">裁决人，未决时为空串。</param>
        /// <param name="choice">裁决选择，未决时为空串。</param>
        /// <param name="resolvedMoment">裁决时间，未决时为空串。</param>
        /// <param name="hasResolutionPayload">裁决对象是否非 null。</param>
        internal ConflictEntry(
            string identifier,
            string oldIdentifier,
            string newIdentifier,
            string discoveryStage,
            string state,
            string resolverName,
            string choice,
            string resolvedMoment,
            bool hasResolutionPayload)
        {
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

        /// <summary>发现阶段：入库 / 影响评估。</summary>
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
    }
}
