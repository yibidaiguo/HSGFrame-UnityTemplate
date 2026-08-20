using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.Dashboard
{
    /// <summary>总览页聚合数据：进行中任务、停在关卡、待确认需求、队列长度、门禁状态与下游供给数。</summary>
    public sealed class PanelOverview
    {
        /// <summary>
        /// 构造一份总览数据。
        /// </summary>
        /// <param name="runningTaskCount">进行中任务数。</param>
        /// <param name="waitingGateCount">停在关卡数。</param>
        /// <param name="draftRequirementCount">待确认需求数。</param>
        /// <param name="queueLength">队列长度。</param>
        /// <param name="gateStatus">门禁状态：绿 / 红 / 未跑。</param>
        /// <param name="driverCount">下游数。</param>
        /// <param name="provisionedDriverCount">已供给数。</param>
        public PanelOverview(
            int runningTaskCount,
            int waitingGateCount,
            int draftRequirementCount,
            int queueLength,
            string gateStatus,
            int driverCount,
            int provisionedDriverCount)
        {
            RunningTaskCount = runningTaskCount;
            WaitingGateCount = waitingGateCount;
            DraftRequirementCount = draftRequirementCount;
            QueueLength = queueLength;
            GateStatus = gateStatus ?? "";
            DriverCount = driverCount;
            ProvisionedDriverCount = provisionedDriverCount;
        }

        /// <summary>进行中任务数。</summary>
        [JsonPropertyName("进行中任务")]
        public int RunningTaskCount { get; }

        /// <summary>停在关卡数。</summary>
        [JsonPropertyName("停在关卡")]
        public int WaitingGateCount { get; }

        /// <summary>待确认需求数。</summary>
        [JsonPropertyName("待确认需求")]
        public int DraftRequirementCount { get; }

        /// <summary>队列长度。</summary>
        [JsonPropertyName("队列长度")]
        public int QueueLength { get; }

        /// <summary>门禁状态：绿 / 红 / 未跑。</summary>
        [JsonPropertyName("门禁")]
        public string GateStatus { get; }

        /// <summary>下游数。</summary>
        [JsonPropertyName("下游数")]
        public int DriverCount { get; }

        /// <summary>已供给数。</summary>
        [JsonPropertyName("已供给")]
        public int ProvisionedDriverCount { get; }
    }

    /// <summary>任务列表页的一行：需求 id、标题、阶段、子状态、关卡待审与当前工作项。</summary>
    public sealed class PanelTaskRow
    {
        /// <summary>
        /// 构造一行任务。
        /// </summary>
        /// <param name="requirementIdentifier">需求 id。</param>
        /// <param name="title">标题。</param>
        /// <param name="stage">阶段。</param>
        /// <param name="subState">子状态。</param>
        /// <param name="pendingGate">停在关卡，可空。</param>
        /// <param name="currentWorkItem">当前工作项，可空。</param>
        public PanelTaskRow(
            string requirementIdentifier,
            string title,
            string stage,
            string subState,
            string pendingGate,
            string currentWorkItem)
        {
            RequirementIdentifier = requirementIdentifier ?? "";
            Title = title ?? "";
            Stage = stage ?? "";
            SubState = subState ?? "";
            PendingGate = pendingGate ?? "";
            CurrentWorkItem = currentWorkItem ?? "";
        }

        /// <summary>需求 id。</summary>
        [JsonPropertyName("需求id")]
        public string RequirementIdentifier { get; }

        /// <summary>标题。</summary>
        [JsonPropertyName("标题")]
        public string Title { get; }

        /// <summary>阶段。</summary>
        [JsonPropertyName("阶段")]
        public string Stage { get; }

        /// <summary>子状态。</summary>
        [JsonPropertyName("子状态")]
        public string SubState { get; }

        /// <summary>停在关卡，可空。</summary>
        [JsonPropertyName("停在关卡")]
        public string PendingGate { get; }

        /// <summary>当前工作项，可空。</summary>
        [JsonPropertyName("当前工作项")]
        public string CurrentWorkItem { get; }
    }

    /// <summary>需求池页的一行：id、标题、类型、状态、专项与锁定标记。</summary>
    public sealed class PanelRequirementRow
    {
        /// <summary>
        /// 构造一行需求。
        /// </summary>
        /// <param name="identifier">需求 id。</param>
        /// <param name="title">标题。</param>
        /// <param name="requirementType">类型。</param>
        /// <param name="state">状态。</param>
        /// <param name="epic">专项。</param>
        /// <param name="isLocked">是否锁定。</param>
        public PanelRequirementRow(
            string identifier,
            string title,
            string requirementType,
            string state,
            string epic,
            bool isLocked)
        {
            Identifier = identifier ?? "";
            Title = title ?? "";
            RequirementType = requirementType ?? "";
            State = state ?? "";
            Epic = epic ?? "";
            IsLocked = isLocked;
        }

        /// <summary>需求 id。</summary>
        [JsonPropertyName("id")]
        public string Identifier { get; }

        /// <summary>标题。</summary>
        [JsonPropertyName("标题")]
        public string Title { get; }

        /// <summary>类型。</summary>
        [JsonPropertyName("类型")]
        public string RequirementType { get; }

        /// <summary>状态。</summary>
        [JsonPropertyName("状态")]
        public string State { get; }

        /// <summary>专项。</summary>
        [JsonPropertyName("专项")]
        public string Epic { get; }

        /// <summary>是否锁定。</summary>
        [JsonPropertyName("锁定")]
        public bool IsLocked { get; }
    }

    /// <summary>门禁报告里的一个条目：名称、结果与问题数。</summary>
    public sealed class PanelGateEntry
    {
        /// <summary>
        /// 构造一个门禁条目。
        /// </summary>
        /// <param name="name">门禁名称。</param>
        /// <param name="result">门禁结果。</param>
        /// <param name="findingCount">问题数。</param>
        public PanelGateEntry(string name, string result, int findingCount)
        {
            Name = name ?? "";
            Result = result ?? "";
            FindingCount = findingCount;
        }

        /// <summary>门禁名称。</summary>
        [JsonPropertyName("名称")]
        public string Name { get; }

        /// <summary>门禁结果。</summary>
        [JsonPropertyName("结果")]
        public string Result { get; }

        /// <summary>问题数。</summary>
        [JsonPropertyName("问题数")]
        public int FindingCount { get; }
    }

    /// <summary>门禁报告：状态（绿 / 红 / 未跑）、报告路径与条目列表。</summary>
    public sealed class PanelGateReport
    {
        /// <summary>
        /// 构造一份门禁报告。
        /// </summary>
        /// <param name="status">状态：绿 / 红 / 未跑。</param>
        /// <param name="reportPath">报告路径。</param>
        /// <param name="entries">条目列表。</param>
        public PanelGateReport(string status, string reportPath, IReadOnlyList<PanelGateEntry> entries)
        {
            Status = status ?? "";
            ReportPath = reportPath ?? "";
            Entries = entries ?? Array.Empty<PanelGateEntry>();
        }

        /// <summary>状态：绿 / 红 / 未跑。</summary>
        [JsonPropertyName("状态")]
        public string Status { get; }

        /// <summary>报告路径。</summary>
        [JsonPropertyName("报告路径")]
        public string ReportPath { get; }

        /// <summary>条目列表。</summary>
        [JsonPropertyName("条目")]
        public IReadOnlyList<PanelGateEntry> Entries { get; }
    }

    /// <summary>引擎快照里队列的一行：需求 id、入队时间与理由。</summary>
    public sealed class PanelQueueRow
    {
        /// <summary>
        /// 构造一行队列条目。
        /// </summary>
        /// <param name="requirementIdentifier">需求 id。</param>
        /// <param name="enqueueTime">入队时间。</param>
        /// <param name="reason">理由。</param>
        public PanelQueueRow(string requirementIdentifier, string enqueueTime, string reason)
        {
            RequirementIdentifier = requirementIdentifier ?? "";
            EnqueueTime = enqueueTime ?? "";
            Reason = reason ?? "";
        }

        /// <summary>需求 id。</summary>
        [JsonPropertyName("需求id")]
        public string RequirementIdentifier { get; }

        /// <summary>入队时间。</summary>
        [JsonPropertyName("入队时间")]
        public string EnqueueTime { get; }

        /// <summary>理由。</summary>
        [JsonPropertyName("理由")]
        public string Reason { get; }
    }

    /// <summary>引擎页快照：模式、确认人、执行队列与卡片路由。</summary>
    public sealed class PanelEngineSnapshot
    {
        /// <summary>
        /// 构造一份引擎快照。
        /// </summary>
        /// <param name="mode">引擎模式中文名。</param>
        /// <param name="confirmers">确认人姓名列表。</param>
        /// <param name="queueEntries">执行队列条目。</param>
        /// <param name="cardRoutes">卡片路由表。</param>
        public PanelEngineSnapshot(
            string mode,
            IReadOnlyList<string> confirmers,
            IReadOnlyList<PanelQueueRow> queueEntries,
            IReadOnlyDictionary<string, string> cardRoutes)
        {
            Mode = mode ?? "";
            Confirmers = confirmers ?? Array.Empty<string>();
            QueueEntries = queueEntries ?? Array.Empty<PanelQueueRow>();
            CardRoutes = cardRoutes ?? new Dictionary<string, string>();
        }

        /// <summary>引擎模式中文名。</summary>
        [JsonPropertyName("模式")]
        public string Mode { get; }

        /// <summary>确认人姓名列表。</summary>
        [JsonPropertyName("确认人")]
        public IReadOnlyList<string> Confirmers { get; }

        /// <summary>执行队列条目。</summary>
        [JsonPropertyName("队列")]
        public IReadOnlyList<PanelQueueRow> QueueEntries { get; }

        /// <summary>卡片路由表。</summary>
        [JsonPropertyName("卡片路由")]
        public IReadOnlyDictionary<string, string> CardRoutes { get; }
    }

    /// <summary>资产页的一行：资产 id、所属需求、类型、落点、规格摘要与变体/弃置/预览计数。</summary>
    public sealed class PanelAssetRow
    {
        /// <summary>
        /// 构造一行资产。
        /// </summary>
        /// <param name="assetIdentifier">资产 id。</param>
        /// <param name="requirementIdentifier">所属需求 id。</param>
        /// <param name="assetType">资产类型。</param>
        /// <param name="destination">落点目录。</param>
        /// <param name="specSummary">规格摘要，形如「宽=256 高=256 格式=PNG」。</param>
        /// <param name="requestedVariantCount">请求里写的变体数。</param>
        /// <param name="qualifiedVariantCount">实际带溯源边车的合格变体数。</param>
        /// <param name="rejectedVariantCount">弃置目录里的文件数。</param>
        /// <param name="hasPreview">是否有实机预览截图。</param>
        public PanelAssetRow(
            string assetIdentifier,
            string requirementIdentifier,
            string assetType,
            string destination,
            string specSummary,
            int requestedVariantCount,
            int qualifiedVariantCount,
            int rejectedVariantCount,
            bool hasPreview)
        {
            AssetIdentifier = assetIdentifier ?? "";
            RequirementIdentifier = requirementIdentifier ?? "";
            AssetType = assetType ?? "";
            Destination = destination ?? "";
            SpecSummary = specSummary ?? "";
            RequestedVariantCount = requestedVariantCount;
            QualifiedVariantCount = qualifiedVariantCount;
            RejectedVariantCount = rejectedVariantCount;
            HasPreview = hasPreview;
        }

        /// <summary>资产 id。</summary>
        [JsonPropertyName("资产id")]
        public string AssetIdentifier { get; }

        /// <summary>所属需求 id。</summary>
        [JsonPropertyName("需求")]
        public string RequirementIdentifier { get; }

        /// <summary>资产类型。</summary>
        [JsonPropertyName("类型")]
        public string AssetType { get; }

        /// <summary>落点目录。</summary>
        [JsonPropertyName("落点")]
        public string Destination { get; }

        /// <summary>规格摘要，形如「宽=256 高=256 格式=PNG」。</summary>
        [JsonPropertyName("规格")]
        public string SpecSummary { get; }

        /// <summary>请求里写的变体数。</summary>
        [JsonPropertyName("请求变体")]
        public int RequestedVariantCount { get; }

        /// <summary>实际带溯源边车的合格变体数。</summary>
        [JsonPropertyName("合格变体")]
        public int QualifiedVariantCount { get; }

        /// <summary>弃置目录里的文件数。</summary>
        [JsonPropertyName("弃置")]
        public int RejectedVariantCount { get; }

        /// <summary>是否有实机预览截图。</summary>
        [JsonPropertyName("预览")]
        public bool HasPreview { get; }
    }

    /// <summary>设计池页的一行：分类、文件名、标题、版本、时间与可读性。</summary>
    public sealed class PanelDesignRow
    {
        /// <summary>
        /// 构造一行设计文档。
        /// </summary>
        /// <param name="category">分类：定稿 / 汇总 / 记录。</param>
        /// <param name="name">文件名去掉扩展名。</param>
        /// <param name="title">文件里的名称或标题字段，没有时为空串。</param>
        /// <param name="version">文件里的版本字段，没有时为空串。</param>
        /// <param name="moment">文件里的时间或创建时间字段，没有时为空串。</param>
        /// <param name="isReadable">文件能否解析成 JSON 对象。</param>
        public PanelDesignRow(string category, string name, string title, string version, string moment, bool isReadable)
        {
            Category = category ?? "";
            Name = name ?? "";
            Title = title ?? "";
            Version = version ?? "";
            Moment = moment ?? "";
            IsReadable = isReadable;
        }

        /// <summary>分类：定稿 / 汇总 / 记录。</summary>
        [JsonPropertyName("分类")]
        public string Category { get; }

        /// <summary>文件名去掉扩展名。</summary>
        [JsonPropertyName("名称")]
        public string Name { get; }

        /// <summary>文件里的名称或标题字段，没有时为空串。</summary>
        [JsonPropertyName("标题")]
        public string Title { get; }

        /// <summary>文件里的版本字段，没有时为空串。</summary>
        [JsonPropertyName("版本")]
        public string Version { get; }

        /// <summary>文件里的时间或创建时间字段，没有时为空串。</summary>
        [JsonPropertyName("时间")]
        public string Moment { get; }

        /// <summary>文件能否解析成 JSON 对象。</summary>
        [JsonPropertyName("可读")]
        public bool IsReadable { get; }
    }

    /// <summary>供给对账页的一行：driver 名、形态、端口、供给状态、对账状态、依赖清单与配方/问题计数。</summary>
    public sealed class PanelProvisionRow
    {
        /// <summary>
        /// 构造一行供给对账。
        /// </summary>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="form">形态：线上或本地；自述损坏时为空串。</param>
        /// <param name="ports">对外提供的端口列表。</param>
        /// <param name="provisionState">供给状态：已供给 / 未供给 / 自述损坏。</param>
        /// <param name="reconcileState">对账状态：一致 / 失配 / 未跑。</param>
        /// <param name="hasDependencyManifest">是否有依赖清单。</param>
        /// <param name="recipeCount">配方数。</param>
        /// <param name="findingCount">本 driver 摊到的对账发现数。</param>
        public PanelProvisionRow(
            string driverName,
            string form,
            IReadOnlyList<string> ports,
            string provisionState,
            string reconcileState,
            bool hasDependencyManifest,
            int recipeCount,
            int findingCount)
        {
            DriverName = driverName ?? "";
            Form = form ?? "";
            Ports = ports ?? Array.Empty<string>();
            ProvisionState = provisionState ?? "";
            ReconcileState = reconcileState ?? "";
            HasDependencyManifest = hasDependencyManifest;
            RecipeCount = recipeCount;
            FindingCount = findingCount;
        }

        /// <summary>driver 名称。</summary>
        [JsonPropertyName("driver")]
        public string DriverName { get; }

        /// <summary>形态：线上或本地；自述损坏时为空串。</summary>
        [JsonPropertyName("形态")]
        public string Form { get; }

        /// <summary>对外提供的端口列表。</summary>
        [JsonPropertyName("端口")]
        public IReadOnlyList<string> Ports { get; }

        /// <summary>供给状态：已供给 / 未供给 / 自述损坏。</summary>
        [JsonPropertyName("供给")]
        public string ProvisionState { get; }

        /// <summary>对账状态：一致 / 失配 / 未跑。</summary>
        [JsonPropertyName("对账")]
        public string ReconcileState { get; }

        /// <summary>是否有依赖清单。</summary>
        [JsonPropertyName("依赖清单")]
        public bool HasDependencyManifest { get; }

        /// <summary>配方数。</summary>
        [JsonPropertyName("配方数")]
        public int RecipeCount { get; }

        /// <summary>本 driver 摊到的对账发现数。</summary>
        [JsonPropertyName("问题数")]
        public int FindingCount { get; }
    }

    /// <summary>任务依赖图的一行：工作项 id、标题、状态、依赖与拓扑深度。</summary>
    public sealed class PanelDagNode
    {
        /// <summary>
        /// 构造一行任务依赖图节点。
        /// </summary>
        /// <param name="identifier">工作项 id。</param>
        /// <param name="title">工作项标题（当前数据源不提供，恒为空串）。</param>
        /// <param name="state">工作项状态。</param>
        /// <param name="dependencies">依赖的工作项 id 列表。</param>
        /// <param name="depth">拓扑深度；环上节点为 -1。</param>
        public PanelDagNode(
            string identifier,
            string title,
            string state,
            IReadOnlyList<string> dependencies,
            int depth)
        {
            Identifier = identifier ?? "";
            Title = title ?? "";
            State = state ?? "";
            Dependencies = dependencies ?? Array.Empty<string>();
            Depth = depth;
        }

        /// <summary>工作项 id。</summary>
        [JsonPropertyName("id")]
        public string Identifier { get; }

        /// <summary>工作项标题（当前数据源不提供，恒为空串）。</summary>
        [JsonPropertyName("标题")]
        public string Title { get; }

        /// <summary>工作项状态。</summary>
        [JsonPropertyName("状态")]
        public string State { get; }

        /// <summary>依赖的工作项 id 列表。</summary>
        [JsonPropertyName("依赖")]
        public IReadOnlyList<string> Dependencies { get; }

        /// <summary>拓扑深度；环上节点为 -1。</summary>
        [JsonPropertyName("深度")]
        public int Depth { get; }
    }

    /// <summary>冲突页的一行：新旧配对、发现阶段、状态、裁决与未销账标记。</summary>
    public sealed class PanelConflictRow
    {
        /// <summary>
        /// 构造一行冲突。
        /// </summary>
        /// <param name="identifier">冲突 id。</param>
        /// <param name="oldIdentifier">旧设计或旧需求 id。</param>
        /// <param name="newIdentifier">新需求 id。</param>
        /// <param name="discoveryStage">发现阶段。</param>
        /// <param name="state">状态：未决 / 已裁决。</param>
        /// <param name="choice">裁决选择，未决时为空串。</param>
        /// <param name="resolverName">裁决人，未决时为空串。</param>
        /// <param name="resolvedMoment">裁决时间，未决时为空串。</param>
        /// <param name="isPending">是否未销账（状态未决或选择强制推送）。</param>
        public PanelConflictRow(
            string identifier,
            string oldIdentifier,
            string newIdentifier,
            string discoveryStage,
            string state,
            string choice,
            string resolverName,
            string resolvedMoment,
            bool isPending)
        {
            Identifier = identifier ?? "";
            OldIdentifier = oldIdentifier ?? "";
            NewIdentifier = newIdentifier ?? "";
            DiscoveryStage = discoveryStage ?? "";
            State = state ?? "";
            Choice = choice ?? "";
            ResolverName = resolverName ?? "";
            ResolvedMoment = resolvedMoment ?? "";
            IsPending = isPending;
        }

        /// <summary>冲突 id。</summary>
        [JsonPropertyName("id")]
        public string Identifier { get; }

        /// <summary>旧设计或旧需求 id。</summary>
        [JsonPropertyName("旧")]
        public string OldIdentifier { get; }

        /// <summary>新需求 id。</summary>
        [JsonPropertyName("新")]
        public string NewIdentifier { get; }

        /// <summary>发现阶段。</summary>
        [JsonPropertyName("发现阶段")]
        public string DiscoveryStage { get; }

        /// <summary>状态：未决 / 已裁决。</summary>
        [JsonPropertyName("状态")]
        public string State { get; }

        /// <summary>裁决选择，未决时为空串。</summary>
        [JsonPropertyName("选择")]
        public string Choice { get; }

        /// <summary>裁决人，未决时为空串。</summary>
        [JsonPropertyName("裁决人")]
        public string ResolverName { get; }

        /// <summary>裁决时间，未决时为空串。</summary>
        [JsonPropertyName("时间")]
        public string ResolvedMoment { get; }

        /// <summary>是否未销账（状态未决或选择强制推送，与 ConflictList.PendingCount 同一口径）。</summary>
        [JsonPropertyName("未决")]
        public bool IsPending { get; }
    }

    /// <summary>晋升页的一行：问题类别、条数、可规则化性、去向、模块与原文举例。</summary>
    public sealed class PanelPromotionRow
    {
        /// <summary>
        /// 构造一行晋升提案。
        /// </summary>
        /// <param name="category">问题类别。</param>
        /// <param name="count">同类条数。</param>
        /// <param name="rulability">该类里出现最多的可规则化性。</param>
        /// <param name="targetChannel">晋升去向：检查器 / 预审规则 / 无。</param>
        /// <param name="moduleNames">涉及模块，序数序。</param>
        /// <param name="quotations">原文引用，最多三条。</param>
        public PanelPromotionRow(
            string category,
            int count,
            string rulability,
            string targetChannel,
            IReadOnlyList<string> moduleNames,
            IReadOnlyList<string> quotations)
        {
            Category = category ?? "";
            Count = count;
            Rulability = rulability ?? "";
            TargetChannel = targetChannel ?? "";
            ModuleNames = moduleNames ?? Array.Empty<string>();
            Quotations = quotations ?? Array.Empty<string>();
        }

        /// <summary>问题类别。</summary>
        [JsonPropertyName("问题类别")]
        public string Category { get; }

        /// <summary>同类条数。</summary>
        [JsonPropertyName("条数")]
        public int Count { get; }

        /// <summary>该类里出现最多的可规则化性。</summary>
        [JsonPropertyName("可规则化性")]
        public string Rulability { get; }

        /// <summary>晋升去向：检查器 / 预审规则 / 无。</summary>
        [JsonPropertyName("晋升去向")]
        public string TargetChannel { get; }

        /// <summary>涉及模块，序数序。</summary>
        [JsonPropertyName("模块")]
        public IReadOnlyList<string> ModuleNames { get; }

        /// <summary>原文引用，最多三条。</summary>
        [JsonPropertyName("原文举例")]
        public IReadOnlyList<string> Quotations { get; }
    }

    /// <summary>终审队列的一行：需求、标题、关卡待审、阶段与等待时长，附状态文件坏掉的原因。</summary>
    public sealed class PanelReviewQueueRow
    {
        /// <summary>
        /// 构造一行终审队列。
        /// </summary>
        /// <param name="requirementIdentifier">需求 id。</param>
        /// <param name="title">需求标题。</param>
        /// <param name="pendingGate">关卡待审的值。</param>
        /// <param name="stage">阶段。</param>
        /// <param name="subState">子状态。</param>
        /// <param name="grade">风险级，取自放行流水里该需求最近一条。</param>
        /// <param name="lastTouchedMoment">状态文件最后写入时间（ISO 8601，UTC）。</param>
        /// <param name="waitingLabel">等待时长的人话。</param>
        /// <param name="hasStateFailure">状态文件是否读不动或坏掉。</param>
        /// <param name="stateFailureReason">状态文件坏掉的原因，正常时为空串。</param>
        public PanelReviewQueueRow(
            string requirementIdentifier,
            string title,
            string pendingGate,
            string stage,
            string subState,
            string grade,
            string lastTouchedMoment,
            string waitingLabel,
            bool hasStateFailure,
            string stateFailureReason)
        {
            RequirementIdentifier = requirementIdentifier ?? "";
            Title = title ?? "";
            PendingGate = pendingGate ?? "";
            Stage = stage ?? "";
            SubState = subState ?? "";
            Grade = grade ?? "";
            LastTouchedMoment = lastTouchedMoment ?? "";
            WaitingLabel = waitingLabel ?? "";
            HasStateFailure = hasStateFailure;
            StateFailureReason = stateFailureReason ?? "";
        }

        /// <summary>需求 id。</summary>
        [JsonPropertyName("需求id")]
        public string RequirementIdentifier { get; }

        /// <summary>需求标题，从需求文件取；取不到给空串。</summary>
        [JsonPropertyName("标题")]
        public string Title { get; }

        /// <summary>关卡待审的值。</summary>
        [JsonPropertyName("关卡待审")]
        public string PendingGate { get; }

        /// <summary>阶段。</summary>
        [JsonPropertyName("阶段")]
        public string Stage { get; }

        /// <summary>子状态。</summary>
        [JsonPropertyName("子状态")]
        public string SubState { get; }

        /// <summary>风险级，从放行流水里该需求最近一条的 Grade 搬来；找不到给空串。面板不自己算风险级。</summary>
        [JsonPropertyName("风险级")]
        public string Grade { get; }

        /// <summary>状态文件最后写入时间（ISO 8601，UTC）。</summary>
        [JsonPropertyName("最后修改时间")]
        public string LastTouchedMoment { get; }

        /// <summary>
        /// 等待时长的人话，例「等了 3 天 4 小时」。
        /// 注意：按状态文件最后修改时间算的，不是「进关卡的时间」——状态文件里没有进关卡的时间戳。
        /// </summary>
        [JsonPropertyName("等待")]
        public string WaitingLabel { get; }

        /// <summary>状态文件是否读不动或坏掉；坏掉时这一行仍然产出，让人看见。</summary>
        [JsonPropertyName("状态失败")]
        public bool HasStateFailure { get; }

        /// <summary>状态文件坏掉的原因，正常时为空串。</summary>
        [JsonPropertyName("状态失败原因")]
        public string StateFailureReason { get; }
    }

    /// <summary>放行流水的一行：流水 id、需求、风险级、范围、放行时间与抽查状态。</summary>
    public sealed class PanelReleaseRow
    {
        /// <summary>
        /// 构造一行放行流水。
        /// </summary>
        /// <param name="identifier">流水 id，形如 RL-0001。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        /// <param name="grade">风险级。</param>
        /// <param name="scopeText">范围，用「、」连起来的文本。</param>
        /// <param name="releasedMoment">放行时间，ISO 8601 字符串。</param>
        /// <param name="mergeCommit">合并提交哈希。</param>
        /// <param name="spotCheckState">抽查状态：未抽查 / 合格 / 发现问题。</param>
        /// <param name="spotCheckConclusion">抽查结论正文。</param>
        /// <param name="revertCommit">回滚提交哈希。</param>
        /// <param name="isSpotChecked">是否抽查过（抽查状态不是「未抽查」）。</param>
        /// <param name="hasProblem">抽查状态是否是「发现问题」。</param>
        public PanelReleaseRow(
            string identifier,
            string requirementIdentifier,
            string grade,
            string scopeText,
            string releasedMoment,
            string mergeCommit,
            string spotCheckState,
            string spotCheckConclusion,
            string revertCommit,
            bool isSpotChecked,
            bool hasProblem)
        {
            Identifier = identifier ?? "";
            RequirementIdentifier = requirementIdentifier ?? "";
            Grade = grade ?? "";
            ScopeText = scopeText ?? "";
            ReleasedMoment = releasedMoment ?? "";
            MergeCommit = mergeCommit ?? "";
            SpotCheckState = spotCheckState ?? "";
            SpotCheckConclusion = spotCheckConclusion ?? "";
            RevertCommit = revertCommit ?? "";
            IsSpotChecked = isSpotChecked;
            HasProblem = hasProblem;
        }

        /// <summary>流水 id，形如 RL-0001。</summary>
        [JsonPropertyName("id")]
        public string Identifier { get; }

        /// <summary>需求 id。</summary>
        [JsonPropertyName("需求id")]
        public string RequirementIdentifier { get; }

        /// <summary>风险级。</summary>
        [JsonPropertyName("风险级")]
        public string Grade { get; }

        /// <summary>范围，用「、」连起来的文本。</summary>
        [JsonPropertyName("范围")]
        public string ScopeText { get; }

        /// <summary>放行时间，ISO 8601 字符串。</summary>
        [JsonPropertyName("放行时间")]
        public string ReleasedMoment { get; }

        /// <summary>合并提交哈希。</summary>
        [JsonPropertyName("合并提交")]
        public string MergeCommit { get; }

        /// <summary>抽查状态：未抽查 / 合格 / 发现问题。</summary>
        [JsonPropertyName("抽查状态")]
        public string SpotCheckState { get; }

        /// <summary>抽查结论正文。</summary>
        [JsonPropertyName("抽查结论")]
        public string SpotCheckConclusion { get; }

        /// <summary>回滚提交哈希。</summary>
        [JsonPropertyName("回滚提交")]
        public string RevertCommit { get; }

        /// <summary>是否抽查过（抽查状态不是「未抽查」）。</summary>
        [JsonPropertyName("已抽查")]
        public bool IsSpotChecked { get; }

        /// <summary>抽查状态是否是「发现问题」。</summary>
        [JsonPropertyName("发现问题")]
        public bool HasProblem { get; }
    }

    /// <summary>放行流水页的汇总：行列表、三个计数与「读成没有」。</summary>
    public sealed class PanelReleaseSummary
    {
        /// <summary>
        /// 构造一份放行流水汇总。
        /// </summary>
        /// <param name="rows">流水行，按流水 id 序数序。</param>
        /// <param name="totalCount">总条数。</param>
        /// <param name="uncheckedCount">未抽查条数。</param>
        /// <param name="problemCount">发现问题条数。</param>
        /// <param name="loaded">流水是否读成。</param>
        /// <param name="loadFailureReason">读不成的原因，正常时为空串。</param>
        public PanelReleaseSummary(
            IReadOnlyList<PanelReleaseRow> rows,
            int totalCount,
            int uncheckedCount,
            int problemCount,
            bool loaded,
            string loadFailureReason)
        {
            Rows = rows ?? Array.Empty<PanelReleaseRow>();
            TotalCount = totalCount;
            UncheckedCount = uncheckedCount;
            ProblemCount = problemCount;
            Loaded = loaded;
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>流水行，按流水 id 序数序。</summary>
        [JsonPropertyName("行")]
        public IReadOnlyList<PanelReleaseRow> Rows { get; }

        /// <summary>总条数。</summary>
        [JsonPropertyName("总数")]
        public int TotalCount { get; }

        /// <summary>未抽查条数。</summary>
        [JsonPropertyName("未抽查数")]
        public int UncheckedCount { get; }

        /// <summary>发现问题条数。</summary>
        [JsonPropertyName("问题数")]
        public int ProblemCount { get; }

        /// <summary>流水是否读成；LoadFailureReason 非空时为 false。</summary>
        [JsonPropertyName("读成")]
        public bool Loaded { get; }

        /// <summary>读不成的原因，正常时为空串。残缺的流水不能拿来下「零问题」的结论。</summary>
        [JsonPropertyName("失败原因")]
        public string LoadFailureReason { get; }
    }

    /// <summary>规范浏览的一行：层、模块、文件名、字节数、规则条数与可读性。</summary>
    public sealed class PanelSpecificationRow
    {
        /// <summary>
        /// 构造一行规范文件。
        /// </summary>
        /// <param name="layer">层：基线 / 项目 / 业务。</param>
        /// <param name="moduleName">业务层的模块名，其余层为空串。</param>
        /// <param name="fileName">文件名（含后缀）。</param>
        /// <param name="relativePath">相对仓库根的路径，分隔符统一成 /。</param>
        /// <param name="byteCount">文件字节数。</param>
        /// <param name="ruleCount">规则条数；算不出来给 -1。</param>
        /// <param name="isReadable">文件是否读得动。</param>
        /// <param name="failureReason">读不动或 JSON 坏的原因，正常时为空串。</param>
        public PanelSpecificationRow(
            string layer,
            string moduleName,
            string fileName,
            string relativePath,
            long byteCount,
            int ruleCount,
            bool isReadable,
            string failureReason)
        {
            Layer = layer ?? "";
            ModuleName = moduleName ?? "";
            FileName = fileName ?? "";
            RelativePath = relativePath ?? "";
            ByteCount = byteCount;
            RuleCount = ruleCount;
            IsReadable = isReadable;
            FailureReason = failureReason ?? "";
        }

        /// <summary>层：基线 / 项目 / 业务。</summary>
        [JsonPropertyName("层")]
        public string Layer { get; }

        /// <summary>业务层的模块名，其余层为空串。</summary>
        [JsonPropertyName("模块")]
        public string ModuleName { get; }

        /// <summary>文件名（含后缀）。</summary>
        [JsonPropertyName("文件名")]
        public string FileName { get; }

        /// <summary>相对仓库根的路径，分隔符统一成 /。</summary>
        [JsonPropertyName("相对路径")]
        public string RelativePath { get; }

        /// <summary>文件字节数。</summary>
        [JsonPropertyName("字节数")]
        public long ByteCount { get; }

        /// <summary>规则条数；只对 .json 且顶层是数组、或顶层对象里有「规则」数组时给条数，算不出来给 -1。</summary>
        [JsonPropertyName("规则数")]
        public int RuleCount { get; }

        /// <summary>文件是否读得动。</summary>
        [JsonPropertyName("可读")]
        public bool IsReadable { get; }

        /// <summary>读不动或 JSON 坏的原因，正常时为空串；这一行仍然产出，让人看见。</summary>
        [JsonPropertyName("失败原因")]
        public string FailureReason { get; }
    }

    /// <summary>晋升提案待批的一行：提案 id、类别、状态、裁决信息与原文引用。</summary>
    public sealed class PanelPromotionProposalRow
    {
        /// <summary>
        /// 构造一行晋升提案。
        /// </summary>
        /// <param name="identifier">提案 id，形如 PR-0001。</param>
        /// <param name="category">问题类别。</param>
        /// <param name="count">同类条数。</param>
        /// <param name="rulability">可规则化性。</param>
        /// <param name="targetChannel">晋升去向：检查器 / 预审规则 / 无。</param>
        /// <param name="moduleText">涉及模块，用「、」连起来的文本。</param>
        /// <param name="state">状态：待批 / 已批准 / 已拒绝 / 已落地。</param>
        /// <param name="proposedMoment">提出时间，ISO 8601 字符串。</param>
        /// <param name="deciderName">裁决人，未裁决时为空串。</param>
        /// <param name="decidedMoment">裁决时间，未裁决时为空串。</param>
        /// <param name="landingArtifact">落地产物路径，未落地时为空串。</param>
        /// <param name="isOpen">是否未关闭（状态是 待批 或 已批准）。</param>
        /// <param name="isPending">状态是否是 待批。</param>
        /// <param name="quotations">原文引用，最多前三条。</param>
        public PanelPromotionProposalRow(
            string identifier,
            string category,
            int count,
            string rulability,
            string targetChannel,
            string moduleText,
            string state,
            string proposedMoment,
            string deciderName,
            string decidedMoment,
            string landingArtifact,
            bool isOpen,
            bool isPending,
            IReadOnlyList<string> quotations)
        {
            Identifier = identifier ?? "";
            Category = category ?? "";
            Count = count;
            Rulability = rulability ?? "";
            TargetChannel = targetChannel ?? "";
            ModuleText = moduleText ?? "";
            State = state ?? "";
            ProposedMoment = proposedMoment ?? "";
            DeciderName = deciderName ?? "";
            DecidedMoment = decidedMoment ?? "";
            LandingArtifact = landingArtifact ?? "";
            IsOpen = isOpen;
            IsPending = isPending;
            Quotations = quotations ?? Array.Empty<string>();
        }

        /// <summary>提案 id，形如 PR-0001。</summary>
        [JsonPropertyName("id")]
        public string Identifier { get; }

        /// <summary>问题类别。</summary>
        [JsonPropertyName("问题类别")]
        public string Category { get; }

        /// <summary>同类条数。</summary>
        [JsonPropertyName("同类条数")]
        public int Count { get; }

        /// <summary>可规则化性。</summary>
        [JsonPropertyName("可规则化性")]
        public string Rulability { get; }

        /// <summary>晋升去向：检查器 / 预审规则 / 无。</summary>
        [JsonPropertyName("晋升去向")]
        public string TargetChannel { get; }

        /// <summary>涉及模块，用「、」连起来的文本。</summary>
        [JsonPropertyName("模块")]
        public string ModuleText { get; }

        /// <summary>状态：待批 / 已批准 / 已拒绝 / 已落地。</summary>
        [JsonPropertyName("状态")]
        public string State { get; }

        /// <summary>提出时间，ISO 8601 字符串。</summary>
        [JsonPropertyName("提出时间")]
        public string ProposedMoment { get; }

        /// <summary>裁决人，未裁决时为空串。</summary>
        [JsonPropertyName("裁决人")]
        public string DeciderName { get; }

        /// <summary>裁决时间，未裁决时为空串。</summary>
        [JsonPropertyName("裁决时间")]
        public string DecidedMoment { get; }

        /// <summary>落地产物路径，未落地时为空串。</summary>
        [JsonPropertyName("落地产物")]
        public string LandingArtifact { get; }

        /// <summary>是否未关闭（状态是 待批 或 已批准，与决策 62 同一套判据）。</summary>
        [JsonPropertyName("未关闭")]
        public bool IsOpen { get; }

        /// <summary>状态是否是 待批。</summary>
        [JsonPropertyName("待批")]
        public bool IsPending { get; }

        /// <summary>原文引用，最多前三条。</summary>
        [JsonPropertyName("原文引用")]
        public IReadOnlyList<string> Quotations { get; }
    }

    /// <summary>晋升提案待批页的汇总：行列表、三个计数与「读成没有」。</summary>
    public sealed class PanelPromotionProposalSummary
    {
        /// <summary>
        /// 构造一份晋升提案汇总。
        /// </summary>
        /// <param name="rows">提案行，按提案 id 序数序。</param>
        /// <param name="totalCount">总条数。</param>
        /// <param name="pendingCount">待批条数。</param>
        /// <param name="openCount">未关闭条数。</param>
        /// <param name="loaded">账本是否读成。</param>
        /// <param name="loadFailureReason">读不成的原因，正常时为空串。</param>
        public PanelPromotionProposalSummary(
            IReadOnlyList<PanelPromotionProposalRow> rows,
            int totalCount,
            int pendingCount,
            int openCount,
            bool loaded,
            string loadFailureReason)
        {
            Rows = rows ?? Array.Empty<PanelPromotionProposalRow>();
            TotalCount = totalCount;
            PendingCount = pendingCount;
            OpenCount = openCount;
            Loaded = loaded;
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>提案行，按提案 id 序数序。</summary>
        [JsonPropertyName("行")]
        public IReadOnlyList<PanelPromotionProposalRow> Rows { get; }

        /// <summary>总条数。</summary>
        [JsonPropertyName("总数")]
        public int TotalCount { get; }

        /// <summary>待批条数。</summary>
        [JsonPropertyName("待批数")]
        public int PendingCount { get; }

        /// <summary>未关闭条数（状态是 待批 或 已批准）。</summary>
        [JsonPropertyName("未关闭数")]
        public int OpenCount { get; }

        /// <summary>账本是否读成；LoadFailureReason 非空时为 false。</summary>
        [JsonPropertyName("读成")]
        public bool Loaded { get; }

        /// <summary>读不成的原因，正常时为空串。</summary>
        [JsonPropertyName("失败原因")]
        public string LoadFailureReason { get; }
    }

    /// <summary>
    /// 面板八页的数据读取器：每页只读磁盘文件，返回可直接序列化成 JSON 的对象。
    /// 全部方法零私有状态、零缓存，每次调用现读文件；任何单点失败都降级，不往上抛。
    /// </summary>
    public static class CreationPanelReader
    {
        /// <summary>
        /// 读需求池：列 Pools/Requirements 下的 *.json（不递归），按文件名序数序。
        /// 每份读顶层 id / 标题 / 类型 / 状态 / 专项 / 锁定；缺的字段填空串（锁定缺为 false）。
        /// 单份文件解析失败只跳过该份，不影响其余；目录不存在返回空列表。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static IReadOnlyList<PanelRequirementRow> ReadRequirements(string repositoryRoot, string poolRoot)
        {
            var requirementsDirectory = PoolPaths.RequirementsDirectory(poolRoot);
            if (!Directory.Exists(requirementsDirectory))
            {
                return Array.Empty<PanelRequirementRow>();
            }

            var files = Directory.GetFiles(requirementsDirectory, "*.json").ToList();
            files.Sort(StringComparer.Ordinal);

            var rows = new List<PanelRequirementRow>();
            foreach (var filePath in files)
            {
                var row = TryReadRequirement(filePath);
                if (row != null)
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        /// <summary>
        /// 读任务列表：列仓库根 _Tasks/ 下的一级子目录（目录名即需求 id，按序数序）。
        /// 逐个调 TaskState.TryLoad，失败的跳过；标题去 Pools/Requirements/&lt;id&gt;.json 取，取不到留空串。
        /// _Tasks/ 不存在返回空列表。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static IReadOnlyList<PanelTaskRow> ReadTasks(string repositoryRoot, string poolRoot)
        {
            var taskDirectory = Path.Combine(repositoryRoot, "_Tasks");
            if (!Directory.Exists(taskDirectory))
            {
                return Array.Empty<PanelTaskRow>();
            }

            var identifiers = Directory.GetDirectories(taskDirectory)
                .Select(directory => Path.GetFileName(directory))
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            var rows = new List<PanelTaskRow>();
            foreach (var identifier in identifiers)
            {
                if (!TaskState.TryLoad(repositoryRoot, identifier, out var state, out _))
                {
                    continue;
                }

                rows.Add(new PanelTaskRow(
                    identifier,
                    ReadRequirementTitle(poolRoot, identifier),
                    state.Stage,
                    state.SubState,
                    state.PendingGate ?? "",
                    state.CurrentWorkItem ?? ""));
            }

            return rows;
        }

        /// <summary>
        /// 读门禁报告：读仓库根 _Generated/门禁报告.json。
        /// 文件不存在时返回 Status = 未跑、空条目（门禁报告是后续期才产的东西，这里如实说没有）；
        /// 存在时按「条目」数组读，任一条目结果不是「成功」即整份为红，否则绿；解析失败退回未跑。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static PanelGateReport ReadGateReport(string repositoryRoot)
        {
            var reportPath = Path.Combine(repositoryRoot, "_Generated", "门禁报告.json");
            if (!File.Exists(reportPath))
            {
                // 路径照样报出去：面板要能告诉人「该有的报告长在哪」，空字符串等于什么都没说。
                return new PanelGateReport("未跑", reportPath, Array.Empty<PanelGateEntry>());
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(reportPath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 路径照样报出去：面板要能告诉人「该有的报告长在哪」，空字符串等于什么都没说。
                return new PanelGateReport("未跑", reportPath, Array.Empty<PanelGateEntry>());
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("条目", out var entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
                {
                    // 路径照样报出去：面板要能告诉人「该有的报告长在哪」，空字符串等于什么都没说。
                return new PanelGateReport("未跑", reportPath, Array.Empty<PanelGateEntry>());
                }

                var entries = new List<PanelGateEntry>();
                var allSucceeded = true;
                foreach (var element in entriesElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var result = ReadStringOrEmpty(element, "结果");
                    if (!string.Equals(result, "成功", StringComparison.Ordinal))
                    {
                        allSucceeded = false;
                    }

                    entries.Add(new PanelGateEntry(
                        ReadStringOrEmpty(element, "名称"),
                        result,
                        ReadInt(element, "问题数", 0)));
                }

                var reportRelativePath = Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(reportPath)).Replace('\\', '/');
                return new PanelGateReport(allSucceeded ? "绿" : "红", reportRelativePath, entries);
            }
        }

        /// <summary>
        /// 读引擎快照：模式取引擎配置的中文名，确认人取成员目录里具备确认权者的姓名（序数序），
        /// 队列条目按队列文件原序，卡片路由按卡片路由表的类型到职责映射。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static PanelEngineSnapshot ReadEngine(string repositoryRoot, string poolRoot)
        {
            var mode = EngineSettings.ToChineseName(EngineSettings.Load(repositoryRoot).Mode);

            var confirmers = MemberDirectory.Load(poolRoot).Members
                .Where(member => member.IsConfirmer)
                .Select(member => member.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            var queueRows = new List<PanelQueueRow>();
            foreach (var entry in ExecutionQueue.Load(poolRoot).Entries)
            {
                queueRows.Add(new PanelQueueRow(entry.RequirementIdentifier, entry.EnqueueTime, entry.Reason));
            }

            var routeTable = CardRouteTable.Load(poolRoot);
            var cardRoutes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var cardType in routeTable.CardTypes)
            {
                cardRoutes[cardType] = routeTable.DutyOf(cardType);
            }

            return new PanelEngineSnapshot(mode, confirmers, queueRows, cardRoutes);
        }

        /// <summary>
        /// 读总览聚合：从任务、需求、队列、门禁与供给对账聚合出总览页的数字。
        /// 供给对账这一步抛任何异常都吞掉并把下游数与已供给填 0——总览页不能因为某个
        /// driver 自述写坏了就整页打不开。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static PanelOverview ReadOverview(string repositoryRoot, string poolRoot)
        {
            var tasks = ReadTasks(repositoryRoot, poolRoot);
            var runningTaskCount = tasks.Count(task => !string.IsNullOrEmpty(task.Stage));
            var waitingGateCount = tasks.Count(task => !string.IsNullOrEmpty(task.PendingGate));

            var requirements = ReadRequirements(repositoryRoot, poolRoot);
            var draftRequirementCount = requirements.Count(requirement => string.Equals(requirement.State, "草稿", StringComparison.Ordinal));

            var queueLength = ExecutionQueue.Load(poolRoot).Entries.Count;
            var gateStatus = ReadGateReport(repositoryRoot).Status;

            var driverCount = 0;
            var provisionedDriverCount = 0;
            try
            {
                var report = ProvisionReconciler.Reconcile(repositoryRoot, poolRoot);
                driverCount = report.DriverNames.Count;
                provisionedDriverCount = report.ProvisionedCount;
            }
            catch (Exception)
            {
                // 供给对账读坏了某个 driver 自述时，总览页两个数字按 0 出，页面照常打开。
            }

            return new PanelOverview(
                runningTaskCount,
                waitingGateCount,
                draftRequirementCount,
                queueLength,
                gateStatus,
                driverCount,
                provisionedDriverCount);
        }

        /// <summary>
        /// 读单个任务的状态文本树：直接返回 TaskStatusReport.RenderOne 的渲染结果。
        /// 抛异常就把异常消息当返回值，别让路由 500。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static string ReadTaskDetail(string repositoryRoot, string poolRoot, string requirementIdentifier)
        {
            try
            {
                return TaskStatusReport.RenderOne(repositoryRoot, poolRoot, requirementIdentifier);
            }
            catch (Exception exception)
            {
                return $"渲染任务详情失败：{exception.Message}";
            }
        }

        /// <summary>
        /// 读资产页：扫 _Tasks/&lt;需求id&gt;/资产请求/ 下的 *.json（各一层，不递归）。
        /// 每份用 AssetRequest.Read 读，读不动的跳过不产行；资产类型与请求里的规格摘要直接取自请求。
        /// 变体合格判定与选片一致：顶层图片文件且有同名「.溯源.json」边车才算合格，弃置数与预览存在性用 AssetPaths 数。
        /// 结果按资产 id 序数序。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static IReadOnlyList<PanelAssetRow> ReadAssets(string repositoryRoot, string poolRoot)
        {
            var taskDirectory = Path.Combine(repositoryRoot, "_Tasks");
            if (!Directory.Exists(taskDirectory))
            {
                return Array.Empty<PanelAssetRow>();
            }

            // 落点缺省时从合并后的资产规格目录补：资产规格三层合并是管线的事，面板只做只读消费。
            var catalog = AssetSpecCatalog.Load(repositoryRoot, "");
            var rows = new List<PanelAssetRow>();
            foreach (var requirementDirectory in Directory.GetDirectories(taskDirectory))
            {
                var requirementIdentifier = Path.GetFileName(requirementDirectory);
                var requestDirectory = AssetPaths.AssetRequestDirectory(repositoryRoot, requirementIdentifier);
                if (!Directory.Exists(requestDirectory))
                {
                    continue;
                }

                foreach (var filePath in Directory.GetFiles(requestDirectory, "*.json"))
                {
                    var request = AssetRequest.Read(filePath);
                    if (string.IsNullOrEmpty(request.Identifier))
                    {
                        // 读不动的请求跳过：不产一行假数据，坏文件不该让整页 500。
                        continue;
                    }

                    var destination = request.Destination;
                    if (string.IsNullOrEmpty(destination))
                    {
                        var spec = catalog.Find(request.AssetType);
                        destination = spec != null ? spec.Destination : "";
                    }

                    rows.Add(new PanelAssetRow(
                        request.Identifier,
                        request.RequirementIdentifier,
                        request.AssetType,
                        destination,
                        BuildSpecSummary(request.Specification),
                        request.VariantCount,
                        CountQualifiedVariants(repositoryRoot, requirementIdentifier, request.Identifier),
                        CountRejectedVariants(repositoryRoot, requirementIdentifier, request.Identifier),
                        File.Exists(AssetPaths.PreviewFile(repositoryRoot, requirementIdentifier, request.Identifier))));
                }
            }

            rows.Sort((left, right) => StringComparer.Ordinal.Compare(left.AssetIdentifier, right.AssetIdentifier));
            return rows;
        }

        /// <summary>
        /// 读设计池页：扫 &lt;池根&gt;/Designs/定稿、汇总、记录 三个目录（各自顶层，不递归）。
        /// 目录不存在跳过那一类；解析不了的文件照样产一行、IsReadable 为 false——设计池页要让人
        /// 看见「这里有个坏文件」，静默吞掉才是骗人。结果先按分类再按名称序数序。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static IReadOnlyList<PanelDesignRow> ReadDesigns(string poolRoot)
        {
            var rows = new List<PanelDesignRow>();
            foreach (var category in DesignCategories)
            {
                var directory = Path.Combine(poolRoot, "Designs", category);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                var files = Directory.GetFiles(directory, "*.json").ToList();
                files.Sort(StringComparer.Ordinal);
                foreach (var filePath in files)
                {
                    rows.Add(ReadDesignRow(category, filePath));
                }
            }

            rows.Sort((left, right) =>
            {
                var byCategory = StringComparer.Ordinal.Compare(left.Category, right.Category);
                return byCategory != 0 ? byCategory : StringComparer.Ordinal.Compare(left.Name, right.Name);
            });
            return rows;
        }

        /// <summary>
        /// 读供给对账页：driver 名从 Bridges/&lt;名&gt;/driver.json 扫出（目录名即 driver 名）。
        /// 自述用 BridgeDriverDescriptor.Load，抛异常则该行供给状态记「自述损坏」并继续下一个；
        /// 对账整体调一次 ProvisionReconciler.Reconcile，Findings 按文本含 driver 名分摊到行。
        /// 未供给一律「未跑」；对账整体没跑成也一律「未跑」——没有的东西不说成一致。
        /// 结果按 driver 名序数序。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static IReadOnlyList<PanelProvisionRow> ReadProvision(string repositoryRoot, string poolRoot)
        {
            var driverNames = DiscoverDriverNames(repositoryRoot);
            if (driverNames.Count == 0)
            {
                return Array.Empty<PanelProvisionRow>();
            }

            IReadOnlyList<PoolFinding> findings = Array.Empty<PoolFinding>();
            var reconcileRan = true;
            try
            {
                findings = ProvisionReconciler.Reconcile(repositoryRoot, poolRoot).Findings;
            }
            catch (Exception)
            {
                // 对账整体没跑成（如基线 schema 缺失）：全部行一律「未跑」。
                // 这里必须单立一个标志——只把 findings 置空是不够的：有指纹的 driver
                // 会因为「零 finding」被判成「一致」，把崩掉的对账说成对上了，
                // 正是门禁报告那条「不存在就报未跑，不报绿」要防的假绿。
                reconcileRan = false;
            }

            var rows = new List<PanelProvisionRow>();
            foreach (var driverName in driverNames)
            {
                var provisionState = "未供给";
                var form = "";
                IReadOnlyList<string> ports = Array.Empty<string>();
                try
                {
                    var descriptor = BridgeDriverDescriptor.Load(repositoryRoot, driverName);
                    form = descriptor.Form;
                    ports = descriptor.Ports;
                }
                catch (Exception)
                {
                    provisionState = "自述损坏";
                }

                // 已供给的判定口径与 ProvisionReconciler 一致：指纹文件在即计入「已供给」计数。
                var hasFingerprint = File.Exists(ProvisionPaths.FingerprintFile(repositoryRoot, driverName));
                if (provisionState != "自述损坏")
                {
                    provisionState = hasFingerprint ? "已供给" : "未供给";
                }

                var findingCount = 0;
                foreach (var finding in findings)
                {
                    if (finding.ToDisplayText().Contains(driverName, StringComparison.Ordinal))
                    {
                        findingCount++;
                    }
                }

                string reconcileState;
                if (!reconcileRan || !hasFingerprint)
                {
                    reconcileState = "未跑";
                }
                else if (findingCount == 0)
                {
                    reconcileState = "一致";
                }
                else
                {
                    reconcileState = "失配";
                }

                rows.Add(new PanelProvisionRow(
                    driverName,
                    form,
                    ports,
                    provisionState,
                    reconcileState,
                    DependencyManifest.Exists(repositoryRoot, driverName),
                    RecipeDefinition.DiscoverNames(repositoryRoot, driverName).Count,
                    findingCount));
            }

            rows.Sort((left, right) => StringComparer.Ordinal.Compare(left.DriverName, right.DriverName));
            return rows;
        }

        /// <summary>
        /// 读某需求的工作项依赖图：直接用 WorkItemGraph.Load（面板与 CLI 同源，不另写一份读取）。
        /// 深度 = 从无依赖节点起算的最长路径长度；图有环时环上节点深度记 -1，不因环死循环。
        /// 结果先按深度升序、再按 id 序数序；需求不存在或无工作项返回空列表，不抛。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        public static IReadOnlyList<PanelDagNode> ReadTaskDag(string repositoryRoot, string requirementIdentifier)
        {
            var graph = WorkItemGraph.Load(repositoryRoot, requirementIdentifier ?? "");
            if (graph.Nodes.Count == 0)
            {
                return Array.Empty<PanelDagNode>();
            }

            var byIdentifier = new Dictionary<string, WorkItemNode>(StringComparer.Ordinal);
            foreach (var node in graph.Nodes)
            {
                byIdentifier[node.Identifier] = node;
            }

            // Kahn 拓扑排序求深度：入度为零的节点深度 0，每推进一层深度取 max(父深度 + 1)。
            // 依赖指向图外的 id 不贡献入度（那个节点没有出边），免得把正常节点误标成环。
            var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
            var downstream = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var node in graph.Nodes)
            {
                var dependencyCount = 0;
                foreach (var dependency in node.Dependencies)
                {
                    if (!byIdentifier.ContainsKey(dependency))
                    {
                        continue;
                    }

                    dependencyCount++;
                    if (!downstream.TryGetValue(dependency, out var children))
                    {
                        children = new List<string>();
                        downstream[dependency] = children;
                    }

                    children.Add(node.Identifier);
                }

                indegree[node.Identifier] = dependencyCount;
            }

            var depth = new Dictionary<string, int>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var node in graph.Nodes)
            {
                if (indegree[node.Identifier] == 0)
                {
                    depth[node.Identifier] = 0;
                    queue.Enqueue(node.Identifier);
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!downstream.TryGetValue(current, out var children))
                {
                    continue;
                }

                var parentDepth = depth[current];
                foreach (var child in children)
                {
                    var candidate = parentDepth + 1;
                    if (!depth.TryGetValue(child, out var existing) || candidate > existing)
                    {
                        depth[child] = candidate;
                    }

                    indegree[child]--;
                    if (indegree[child] == 0)
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            // 拓扑排不出去的（入度消不掉）就是环上节点，深度记 -1。
            var rows = new List<PanelDagNode>();
            foreach (var node in graph.Nodes)
            {
                var nodeDepth = depth.TryGetValue(node.Identifier, out var computedDepth) ? computedDepth : -1;
                rows.Add(new PanelDagNode(node.Identifier, node.Title, node.State, node.Dependencies, nodeDepth));
            }

            rows.Sort((left, right) =>
            {
                var byDepth = left.Depth.CompareTo(right.Depth);
                return byDepth != 0 ? byDepth : string.CompareOrdinal(left.Identifier, right.Identifier);
            });
            return rows;
        }

        /// <summary>
        /// 读冲突列表：直接用 ConflictList.Load（面板与 CLI 同源，不另写一份读取）。
        /// 空列表是正常状态；未销账口径与 ConflictList.PendingCount 一致（状态未决或选择强制推送）。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static IReadOnlyList<PanelConflictRow> ReadConflicts(string poolRoot)
        {
            var list = ConflictList.Load(poolRoot);
            var rows = new List<PanelConflictRow>();
            foreach (var entry in list.Entries)
            {
                rows.Add(new PanelConflictRow(
                    entry.Identifier,
                    entry.OldIdentifier,
                    entry.NewIdentifier,
                    entry.DiscoveryStage,
                    entry.State,
                    entry.Choice,
                    entry.ResolverName,
                    entry.ResolvedMoment,
                    string.Equals(entry.State, ConflictEntry.PendingState, StringComparison.Ordinal)
                        || string.Equals(entry.Choice, "强制推送", StringComparison.Ordinal)));
            }

            return rows;
        }

        /// <summary>
        /// 读晋升提案：直接用 ReviewOpinionBook.Load + PromotionProposalBuilder.Build
        /// （面板与 CLI 同源，不另写一份读取）。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="threshold">同类条数阈值。</param>
        public static IReadOnlyList<PanelPromotionRow> ReadPromotions(string poolRoot, int threshold)
        {
            var book = ReviewOpinionBook.Load(poolRoot);
            var rows = new List<PanelPromotionRow>();
            foreach (var proposal in PromotionProposalBuilder.Build(book, threshold))
            {
                rows.Add(new PanelPromotionRow(
                    proposal.Category,
                    proposal.Count,
                    proposal.Rulability,
                    proposal.TargetChannel,
                    proposal.ModuleNames,
                    proposal.Quotations));
            }

            return rows;
        }

        /// <summary>
        /// 读终审队列：列 _Tasks/ 下一级目录（目录名即需求 id），只留「关卡待审」非空的；
        /// 状态文件读不动或坏掉的也产出一行并带原因（有东西烂在库里必须让人看见）。
        /// 风险级从放行流水里找该需求最近一条的 Grade 搬来，面板不自己算（决策 21）；
        /// 等待时长按状态文件最后修改时间算——状态文件里没有进关卡的时间戳。
        /// _Tasks/ 不存在返回空列表，这是正常的，不是错误。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static IReadOnlyList<PanelReviewQueueRow> ReadReviewQueue(string repositoryRoot, string poolRoot)
        {
            var taskDirectory = Path.Combine(repositoryRoot, "_Tasks");
            if (!Directory.Exists(taskDirectory))
            {
                return Array.Empty<PanelReviewQueueRow>();
            }

            var identifiers = Directory.GetDirectories(taskDirectory)
                .Select(directory => Path.GetFileName(directory))
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            var gradeByRequirement = ReadLatestGrades(poolRoot);

            var candidates = new List<ReviewQueueCandidate>();
            foreach (var identifier in identifiers)
            {
                var stateFilePath = PipelinePaths.TaskStateFile(repositoryRoot, identifier);
                if (!File.Exists(stateFilePath))
                {
                    continue;
                }

                var pendingGate = "";
                var stage = "";
                var subState = "";
                var stateFailureReason = "";
                try
                {
                    using (var document = JsonDocument.Parse(File.ReadAllText(stateFilePath)))
                    {
                        var root = document.RootElement;
                        if (root.ValueKind != JsonValueKind.Object)
                        {
                            stateFailureReason = "状态文件根不是对象";
                        }
                        else
                        {
                            pendingGate = ReadStringOrEmpty(root, "关卡待审");
                            stage = ReadStringOrEmpty(root, "阶段");
                            subState = ReadStringOrEmpty(root, "子状态");
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    stateFailureReason = $"状态文件解析失败：{exception.Message}";
                }

                // 状态文件坏掉的照常产出一行让人看见；正常的只留「关卡待审」非空的。
                if (stateFailureReason.Length == 0 && string.IsNullOrEmpty(pendingGate))
                {
                    continue;
                }

                var touchedUtc = File.GetLastWriteTimeUtc(stateFilePath);
                // 状态文件必然存在，mtime 早于 Unix 纪元（1970）视为异常/缺失：文件系统不支持那么早的
                // 时间时会返回默认值（1601-01-01），把它当「没有时间」处理，排最后。
                var hasTouchedTime = touchedUtc >= new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var grade = gradeByRequirement.TryGetValue(identifier, out var existingGrade) ? existingGrade : "";
                candidates.Add(new ReviewQueueCandidate(
                    identifier,
                    ReadRequirementTitle(poolRoot, identifier),
                    pendingGate,
                    stage,
                    subState,
                    grade,
                    touchedUtc,
                    hasTouchedTime,
                    stateFailureReason));
            }

            // 等待时长按状态文件最后修改时间升序（等得最久的排最前），时间相同或缺失的按需求 id 序数序；缺失时间的排最后。
            candidates.Sort((left, right) =>
            {
                if (!left.HasTouchedTime && !right.HasTouchedTime)
                {
                    return string.CompareOrdinal(left.RequirementIdentifier, right.RequirementIdentifier);
                }

                if (!left.HasTouchedTime)
                {
                    return 1;
                }

                if (!right.HasTouchedTime)
                {
                    return -1;
                }

                var byTime = left.TouchedUtc.CompareTo(right.TouchedUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(left.RequirementIdentifier, right.RequirementIdentifier);
            });

            var nowUtc = DateTime.UtcNow;
            var rows = new List<PanelReviewQueueRow>();
            foreach (var candidate in candidates)
            {
                rows.Add(new PanelReviewQueueRow(
                    candidate.RequirementIdentifier,
                    candidate.Title,
                    candidate.PendingGate,
                    candidate.Stage,
                    candidate.SubState,
                    candidate.Grade,
                    candidate.HasTouchedTime ? candidate.TouchedUtc.ToString("o") : "",
                    BuildWaitingLabel(candidate.HasTouchedTime ? nowUtc - candidate.TouchedUtc : (TimeSpan?)null),
                    candidate.StateFailureReason.Length > 0,
                    candidate.StateFailureReason));
            }

            return rows;
        }

        /// <summary>
        /// 读放行流水：直接用 ReleaseLedger.Load（面板与 CLI 同源，不另写一份读取）。
        /// 空流水是正常状态；LoadFailureReason 非空时 Loaded 为 false——残缺的流水
        /// 不能拿来下「零问题」的结论（决策 42）。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static PanelReleaseSummary ReadReleases(string poolRoot)
        {
            var ledger = ReleaseLedger.Load(poolRoot);
            var rows = new List<PanelReleaseRow>();
            foreach (var entry in ledger.Entries)
            {
                rows.Add(new PanelReleaseRow(
                    entry.Identifier,
                    entry.RequirementIdentifier,
                    entry.Grade,
                    string.Join("、", entry.Scopes),
                    entry.ReleasedMoment,
                    entry.MergeCommit,
                    entry.SpotCheckState,
                    entry.SpotCheckConclusion,
                    entry.RevertCommit,
                    entry.IsSpotChecked,
                    string.Equals(entry.SpotCheckState, "发现问题", StringComparison.Ordinal)));
            }

            return new PanelReleaseSummary(
                rows,
                rows.Count,
                ledger.UncheckedCount(),
                ledger.ProblemCount(),
                ledger.LoadFailureReason.Length == 0,
                ledger.LoadFailureReason);
        }

        /// <summary>
        /// 读规范浏览：扫三层（基线 / 项目 / 业务）各自目录下（不递归）的 .json 与 .md，
        /// 业务层按一级子目录分模块。任一层不存在该层零行，不是错误。
        /// 排序：层固定顺序（基线 → 项目 → 业务）→ 模块名序数序 → 文件名序数序，不依赖文件系统枚举顺序。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static IReadOnlyList<PanelSpecificationRow> ReadSpecifications(string repositoryRoot)
        {
            var rows = new List<PanelSpecificationRow>();
            CollectSpecificationFiles(
                repositoryRoot,
                SpecificationPaths.BaselineDirectory(repositoryRoot),
                "基线",
                "",
                rows);
            CollectSpecificationFiles(
                repositoryRoot,
                SpecificationPaths.ProjectDirectory(repositoryRoot),
                "项目",
                "",
                rows);

            var businessRoot = Path.Combine(repositoryRoot, "规范", "业务");
            if (Directory.Exists(businessRoot))
            {
                var moduleNames = Directory.GetDirectories(businessRoot)
                    .Select(directory => Path.GetFileName(directory))
                    .Where(name => !string.IsNullOrEmpty(name))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
                foreach (var moduleName in moduleNames)
                {
                    CollectSpecificationFiles(
                        repositoryRoot,
                        SpecificationPaths.BusinessDirectory(repositoryRoot, moduleName),
                        "业务",
                        moduleName,
                        rows);
                }
            }

            return rows;
        }

        /// <summary>
        /// 读晋升提案待批：直接用 PromotionLedger.Load（不是 PromotionProposalBuilder——
        /// 那是现有「晋升」页读的候选，两回事）。
        /// 空账本是正常状态；LoadFailureReason 非空时 Loaded 为 false。
        /// 未关闭不判红也不告警——待批提案是待办不是违规（决策 51 同理）。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static PanelPromotionProposalSummary ReadPromotionProposals(string poolRoot)
        {
            var ledger = PromotionLedger.Load(poolRoot);
            var rows = new List<PanelPromotionProposalRow>();
            var pendingCount = 0;
            foreach (var record in ledger.Records)
            {
                var quotations = record.Quotations;
                if (quotations.Count > 3)
                {
                    quotations = quotations.Take(3).ToList();
                }

                var isPending = string.Equals(record.State, PromotionRecord.PendingState, StringComparison.Ordinal);
                if (isPending)
                {
                    pendingCount++;
                }

                rows.Add(new PanelPromotionProposalRow(
                    record.Identifier,
                    record.Category,
                    record.Count,
                    record.Rulability,
                    record.TargetChannel,
                    string.Join("、", record.ModuleNames),
                    record.State,
                    record.ProposedMoment,
                    record.DeciderName,
                    record.DecidedMoment,
                    record.LandingArtifact,
                    record.IsOpen,
                    isPending,
                    quotations));
            }

            return new PanelPromotionProposalSummary(
                rows,
                rows.Count,
                pendingCount,
                ledger.OpenCount(),
                ledger.LoadFailureReason.Length == 0,
                ledger.LoadFailureReason);
        }

        /// <summary>把放行流水里每个需求最近一条（流水 id 最大）的 Grade 收进字典；流水读不动时给空字典。</summary>
        private static Dictionary<string, string> ReadLatestGrades(string poolRoot)
        {
            var grades = new Dictionary<string, string>(StringComparer.Ordinal);
            var ledger = ReleaseLedger.Load(poolRoot);
            foreach (var entry in ledger.Entries)
            {
                // 流水已按 id 序数序，后匹配到的就是该需求最近一条。
                grades[entry.RequirementIdentifier] = entry.Grade;
            }

            return grades;
        }

        /// <summary>把等待时长拼成人话：「等了 X 天 Y 小时」；不足一小时「等了不到 1 小时」；时间缺失给空串。</summary>
        private static string BuildWaitingLabel(TimeSpan? elapsed)
        {
            if (elapsed == null || elapsed.Value < TimeSpan.Zero)
            {
                return "";
            }

            if (elapsed.Value.TotalMinutes < 1)
            {
                return "等了不到 1 小时";
            }

            var totalHours = (int)elapsed.Value.TotalHours;
            var days = totalHours / 24;
            var hours = totalHours % 24;
            return days > 0 ? $"等了 {days} 天 {hours} 小时" : $"等了 {hours} 小时";
        }

        /// <summary>收某一层目录下（不递归）的 .json 与 .md 文件，各产一行；目录不存在返回。</summary>
        private static void CollectSpecificationFiles(
            string repositoryRoot,
            string layerDirectory,
            string layer,
            string moduleName,
            List<PanelSpecificationRow> rows)
        {
            if (!Directory.Exists(layerDirectory))
            {
                return;
            }

            var filePaths = Directory.EnumerateFiles(layerDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => IsSpecificationFile(path))
                .ToList();
            filePaths.Sort(StringComparer.Ordinal);

            foreach (var filePath in filePaths)
            {
                var fileName = Path.GetFileName(filePath);
                var relativePath = Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(filePath))
                    .Replace('\\', '/');
                var byteCount = 0L;
                var ruleCount = -1;
                var isReadable = true;
                var failureReason = "";
                try
                {
                    byteCount = new FileInfo(filePath).Length;
                    if (string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
                    {
                        ruleCount = CountSpecificationRules(filePath, out isReadable, out failureReason);
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    isReadable = false;
                    failureReason = $"读不了：{exception.Message}";
                }

                rows.Add(new PanelSpecificationRow(
                    layer,
                    moduleName,
                    fileName,
                    relativePath,
                    byteCount,
                    ruleCount,
                    isReadable,
                    failureReason));
            }
        }

        /// <summary>文件名后缀是否 .json 或 .md，大小写不敏感。</summary>
        private static bool IsSpecificationFile(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>数一份规范 JSON 的规则条数：顶层是数组给数组长度；顶层对象带「规则」数组给那个数组长度；其余给 -1。</summary>
        private static int CountSpecificationRules(string filePath, out bool isReadable, out string failureReason)
        {
            isReadable = true;
            failureReason = "";
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(filePath)))
                {
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        return root.GetArrayLength();
                    }

                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        return root.TryGetProperty("规则", out var rules) && rules.ValueKind == JsonValueKind.Array
                            ? rules.GetArrayLength()
                            : -1;
                    }

                    return -1;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                isReadable = false;
                failureReason = $"JSON 解析失败：{exception.Message}";
                return -1;
            }
        }

        /// <summary>终审队列排序用的中间候选：行字段 + 文件最后修改时间这一排序键。</summary>
        private sealed class ReviewQueueCandidate
        {
            internal ReviewQueueCandidate(
                string requirementIdentifier,
                string title,
                string pendingGate,
                string stage,
                string subState,
                string grade,
                DateTime touchedUtc,
                bool hasTouchedTime,
                string stateFailureReason)
            {
                RequirementIdentifier = requirementIdentifier;
                Title = title;
                PendingGate = pendingGate;
                Stage = stage;
                SubState = subState;
                Grade = grade;
                TouchedUtc = touchedUtc;
                HasTouchedTime = hasTouchedTime;
                StateFailureReason = stateFailureReason;
            }

            internal string RequirementIdentifier { get; }

            internal string Title { get; }

            internal string PendingGate { get; }

            internal string Stage { get; }

            internal string SubState { get; }

            internal string Grade { get; }

            internal DateTime TouchedUtc { get; }

            internal bool HasTouchedTime { get; }

            internal string StateFailureReason { get; }
        }

        /// <summary>设计池的分类目录名。</summary>
        private static readonly string[] DesignCategories = { "定稿", "汇总", "记录" };

        /// <summary>允许的图片后缀，比较时大小写不敏感（与选片一致）。</summary>
        private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

        /// <summary>扫 Bridges/ 下一级含 driver.json 的目录名（目录名即 driver 名），序数序；Bridges 不存在返回空列表。</summary>
        private static List<string> DiscoverDriverNames(string repositoryRoot)
        {
            var bridgesDirectory = Path.Combine(repositoryRoot, "Bridges");
            var driverNames = new List<string>();
            if (!Directory.Exists(bridgesDirectory))
            {
                return driverNames;
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(bridgesDirectory))
            {
                if (File.Exists(Path.Combine(directoryPath, "driver.json")))
                {
                    driverNames.Add(Path.GetFileName(directoryPath));
                }
            }

            driverNames.Sort(StringComparer.Ordinal);
            return driverNames;
        }

        /// <summary>读一份设计文档：解析成 JSON 对象成功则取名称/标题、版本、时间/创建时间，失败则产一行不可读。</summary>
        private static PanelDesignRow ReadDesignRow(string category, string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            var title = "";
            var version = "";
            var moment = "";
            var isReadable = false;
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(filePath)))
                {
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        isReadable = true;
                        title = ReadStringOrEmpty(root, "名称");
                        if (string.IsNullOrEmpty(title))
                        {
                            title = ReadStringOrEmpty(root, "标题");
                        }

                        version = ReadStringOrEmpty(root, "版本");
                        moment = ReadStringOrEmpty(root, "时间");
                        if (string.IsNullOrEmpty(moment))
                        {
                            moment = ReadStringOrEmpty(root, "创建时间");
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 解析不了照样产一行：IsReadable 为 false、其余字段空串，让页面看见这个坏文件。
            }

            return new PanelDesignRow(category, name, title, version, moment, isReadable);
        }

        /// <summary>数某资产的合格变体：顶层图片文件且同名边车存在，判定口径与选片一致。</summary>
        private static int CountQualifiedVariants(string repositoryRoot, string requirementIdentifier, string assetIdentifier)
        {
            var variantDirectory = AssetPaths.VariantDirectory(repositoryRoot, requirementIdentifier, assetIdentifier);
            if (!Directory.Exists(variantDirectory))
            {
                return 0;
            }

            var qualifiedCount = 0;
            foreach (var filePath in Directory.EnumerateFiles(variantDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                var variantName = Path.GetFileName(filePath);
                if (!IsImageFile(variantName))
                {
                    continue;
                }

                if (File.Exists(AssetPaths.SidecarFile(repositoryRoot, requirementIdentifier, assetIdentifier, variantName)))
                {
                    qualifiedCount++;
                }
            }

            return qualifiedCount;
        }

        /// <summary>数某资产弃置目录里的文件数；目录不存在按 0。</summary>
        private static int CountRejectedVariants(string repositoryRoot, string requirementIdentifier, string assetIdentifier)
        {
            var rejectedDirectory = AssetPaths.RejectedDirectory(repositoryRoot, requirementIdentifier, assetIdentifier);
            if (!Directory.Exists(rejectedDirectory))
            {
                return 0;
            }

            return Directory.EnumerateFiles(rejectedDirectory, "*", SearchOption.TopDirectoryOnly).Count();
        }

        /// <summary>文件名后缀是否属于允许的图片格式，大小写不敏感（与选片一致）。</summary>
        private static bool IsImageFile(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            foreach (var allowed in AllowedImageExtensions)
            {
                if (string.Equals(extension, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>把规格字典拼成「键=值」空格连接的一行，键按序数序；字符串值去掉 JSON 引号。</summary>
        private static string BuildSpecSummary(IReadOnlyDictionary<string, string> specification)
        {
            var keys = new List<string>(specification.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (var key in keys)
            {
                parts.Add($"{key}={StripJsonStringQuotes(specification[key])}");
            }

            return string.Join(" ", parts);
        }

        /// <summary>去掉 JSON 字符串值的首尾引号；非引号包裹的原样返回。</summary>
        private static string StripJsonStringQuotes(string rawText)
        {
            if (rawText.Length >= 2 && rawText[0] == '"' && rawText[rawText.Length - 1] == '"')
            {
                return rawText.Substring(1, rawText.Length - 2);
            }

            return rawText;
        }

        /// <summary>读一份需求 JSON；解析失败或根不是对象返回 null，让调用方跳过该份。</summary>
        private static PanelRequirementRow TryReadRequirement(string filePath)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return null;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                return new PanelRequirementRow(
                    ReadStringOrEmpty(root, "id"),
                    ReadStringOrEmpty(root, "标题"),
                    ReadStringOrEmpty(root, "类型"),
                    ReadStringOrEmpty(root, "状态"),
                    ReadStringOrEmpty(root, "专项"),
                    root.TryGetProperty("锁定", out var lockedElement) && lockedElement.ValueKind == JsonValueKind.True);
            }
        }

        /// <summary>从 Pools/Requirements/&lt;id&gt;.json 读「标题」，取不到留空串。</summary>
        private static string ReadRequirementTitle(string poolRoot, string requirementIdentifier)
        {
            var filePath = Path.Combine(PoolPaths.RequirementsDirectory(poolRoot), $"{requirementIdentifier}.json");
            if (!File.Exists(filePath))
            {
                return "";
            }

            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(filePath)))
                {
                    var root = document.RootElement;
                    return root.ValueKind == JsonValueKind.Object ? ReadStringOrEmpty(root, "标题") : "";
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return "";
            }
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读整数属性；缺失或类型不对给默认值。</summary>
        private static int ReadInt(JsonElement element, string propertyName, int fallback)
        {
            if (element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number))
            {
                return number;
            }

            return fallback;
        }
    }
}
