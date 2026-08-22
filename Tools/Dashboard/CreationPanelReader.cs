using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>详情页的一个工作项：名称与状态。</summary>
    public sealed class PanelWorkItemRow
    {
        /// <summary>
        /// 构造一个工作项行。
        /// </summary>
        /// <param name="name">工作项名称（文件里的 id）。</param>
        /// <param name="state">工作项状态。</param>
        public PanelWorkItemRow(string name, string state)
        {
            Name = name ?? "";
            State = state ?? "";
        }

        /// <summary>工作项名称。</summary>
        [JsonPropertyName("名称")]
        public string Name { get; }

        /// <summary>工作项状态。</summary>
        [JsonPropertyName("状态")]
        public string State { get; }
    }

    /// <summary>详情页的完整数据：需求字段 + 验收标准 + 任务状态 + 工作项清单。</summary>
    public sealed class PanelTaskDetail
    {
        /// <summary>
        /// 构造一份详情。
        /// </summary>
        /// <param name="identifier">需求 id。</param>
        /// <param name="title">标题。</param>
        /// <param name="requirementType">类型。</param>
        /// <param name="state">状态。</param>
        /// <param name="epic">专项。</param>
        /// <param name="description">描述。</param>
        /// <param name="isLocked">是否锁定。</param>
        /// <param name="acceptanceCriteria">验收标准。</param>
        /// <param name="hasTask">引擎有没有接走它（_Tasks 下有目录）。</param>
        /// <param name="stage">任务阶段；没任务时空串。</param>
        /// <param name="subState">任务子状态；没任务时空串。</param>
        /// <param name="pendingGate">停在的关卡；没有时空串。</param>
        /// <param name="currentWorkItem">当前工作项；没有时空串。</param>
        /// <param name="workItems">工作项清单。</param>
        public PanelTaskDetail(
            string identifier,
            string title,
            string requirementType,
            string state,
            string epic,
            string description,
            bool isLocked,
            IReadOnlyList<string> acceptanceCriteria,
            bool hasTask,
            string stage,
            string subState,
            string pendingGate,
            string currentWorkItem,
            IReadOnlyList<PanelWorkItemRow> workItems)
        {
            Identifier = identifier ?? "";
            Title = title ?? "";
            RequirementType = requirementType ?? "";
            State = state ?? "";
            Epic = epic ?? "";
            Description = description ?? "";
            IsLocked = isLocked;
            AcceptanceCriteria = acceptanceCriteria ?? Array.Empty<string>();
            HasTask = hasTask;
            Stage = stage ?? "";
            SubState = subState ?? "";
            PendingGate = pendingGate ?? "";
            CurrentWorkItem = currentWorkItem ?? "";
            WorkItems = workItems ?? Array.Empty<PanelWorkItemRow>();
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

        /// <summary>描述。</summary>
        [JsonPropertyName("描述")]
        public string Description { get; }

        /// <summary>是否锁定。</summary>
        [JsonPropertyName("锁定")]
        public bool IsLocked { get; }

        /// <summary>验收标准。</summary>
        [JsonPropertyName("验收标准")]
        public IReadOnlyList<string> AcceptanceCriteria { get; }

        /// <summary>引擎有没有接走它。</summary>
        [JsonPropertyName("有任务")]
        public bool HasTask { get; }

        /// <summary>任务阶段。</summary>
        [JsonPropertyName("阶段")]
        public string Stage { get; }

        /// <summary>任务子状态。</summary>
        [JsonPropertyName("子状态")]
        public string SubState { get; }

        /// <summary>停在的关卡。</summary>
        [JsonPropertyName("停在关卡")]
        public string PendingGate { get; }

        /// <summary>当前工作项。</summary>
        [JsonPropertyName("当前工作项")]
        public string CurrentWorkItem { get; }

        /// <summary>工作项清单。</summary>
        [JsonPropertyName("工作项")]
        public IReadOnlyList<PanelWorkItemRow> WorkItems { get; }
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

    /// <summary>资产页的一行：资产 id、所属需求、类型、落点、规格摘要、变体/弃置/预览计数，以及离风格列的预览路径与风格锚点定稿名。</summary>
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
        /// <param name="previewPath">预览截图的仓库相对路径；文件不存在给空串。</param>
        /// <param name="anchorFinalName">资产请求「风格锚点.定稿」的值；没有给空串。</param>
        public PanelAssetRow(
            string assetIdentifier,
            string requirementIdentifier,
            string assetType,
            string destination,
            string specSummary,
            int requestedVariantCount,
            int qualifiedVariantCount,
            int rejectedVariantCount,
            bool hasPreview,
            string previewPath,
            string anchorFinalName)
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
            PreviewPath = previewPath ?? "";
            AnchorFinalName = anchorFinalName ?? "";
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

        /// <summary>预览截图的仓库相对路径（正斜杠）；文件不存在给空串。</summary>
        [JsonPropertyName("预览路径")]
        public string PreviewPath { get; }

        /// <summary>资产请求「风格锚点.定稿」的值（去掉 JSON 引号）；没有给空串。</summary>
        [JsonPropertyName("风格锚点定稿")]
        public string AnchorFinalName { get; }
    }

    /// <summary>设计池页的一行：分类、文件名、标题、版本、时间与可读性，以及定稿专属的色板与参考图。</summary>
    public sealed class PanelDesignRow
    {
        /// <summary>
        /// 构造一行设计文档。
        /// </summary>
        /// <param name="category">分类：定稿 / 汇总 / 记录。</param>
        /// <param name="name">文件名去掉扩展名。</param>
        /// <param name="title">文件里的名称或标题字段，没有时为空串。</param>
        /// <param name="version">文件里的版本字段，没有时为空串。</param>
        /// <param name="moment">记录的时间：优先取文件 JSON 里的「时间」字段，取不到时退化成文件最后写入时间（ISO 8601，UTC）——两个来源语义不同，标记位在 MomentFromFileTime。</param>
        /// <param name="isReadable">文件能否解析成 JSON 对象。</param>
        /// <param name="momentFromFileTime">Moment 是否退化成了文件最后写入时间（JSON 里没有「时间」字段时）。</param>
        /// <param name="paletteColors">定稿行的色板十六进制串（非法项已跳过）；其余行是空列表。</param>
        /// <param name="finalVersion">定稿行取文件里的数字版本，取不到给 0；其余行恒 0。</param>
        /// <param name="referenceImages">定稿行的参考图相对路径列表；其余行是空列表。</param>
        public PanelDesignRow(
            string category,
            string name,
            string title,
            string version,
            string moment,
            bool isReadable,
            bool momentFromFileTime,
            IReadOnlyList<string> paletteColors,
            int finalVersion,
            IReadOnlyList<string> referenceImages)
        {
            Category = category ?? "";
            Name = name ?? "";
            Title = title ?? "";
            Version = version ?? "";
            Moment = moment ?? "";
            IsReadable = isReadable;
            MomentFromFileTime = momentFromFileTime;
            PaletteColors = paletteColors ?? Array.Empty<string>();
            FinalVersion = finalVersion;
            ReferenceImages = referenceImages ?? Array.Empty<string>();
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

        /// <summary>记录的时间：优先取文件 JSON 里的「时间」字段，取不到时退化成文件最后写入时间（ISO 8601，UTC）——两个来源语义不同，是否退化了看 MomentFromFileTime。</summary>
        [JsonPropertyName("时间")]
        public string Moment { get; }

        /// <summary>文件能否解析成 JSON 对象。</summary>
        [JsonPropertyName("可读")]
        public bool IsReadable { get; }

        /// <summary>Moment 是否退化成了文件最后写入时间（JSON 里没有「时间」字段时）。</summary>
        [JsonPropertyName("时间取自文件")]
        public bool MomentFromFileTime { get; }

        /// <summary>定稿行的色板十六进制串（非法项已跳过）；其余行是空列表。</summary>
        [JsonPropertyName("色板")]
        public IReadOnlyList<string> PaletteColors { get; }

        /// <summary>定稿行取文件里的数字版本，取不到给 0；其余行恒 0。</summary>
        [JsonPropertyName("定稿版本")]
        public int FinalVersion { get; }

        /// <summary>定稿行的参考图相对路径列表；其余行是空列表。</summary>
        [JsonPropertyName("参考图")]
        public IReadOnlyList<string> ReferenceImages { get; }
    }

    /// <summary>下游页里一个配置字段：名称、类型、必填与配没配状态（密钥与非密钥字段都报「已配 / 未配」，值一律不显示）。</summary>
    public sealed class PanelBridgeFieldRow
    {
        /// <summary>
        /// 构造下游页里的一个配置字段行。
        /// </summary>
        /// <param name="name">字段名。</param>
        /// <param name="fieldType">控件类型：string / number / bool / enum / secret。</param>
        /// <param name="isRequired">是否必填。</param>
        /// <param name="options">enum 的可选值；其他类型是空列表。</param>
        /// <param name="isSecret">是不是密钥字段。密钥的值永不读取、永不输出（决策 5）。</param>
        /// <param name="secretState">该字段配没配：已配 / 未配。密钥字段只判本机配置里键在不在；
        /// 非密钥字段判「下游配置.&lt;driver&gt;.&lt;字段名&gt;」键在不在且非空串。值一律不显示。</param>
        public PanelBridgeFieldRow(
            string name,
            string fieldType,
            bool isRequired,
            IReadOnlyList<string> options,
            bool isSecret,
            string secretState)
        {
            Name = name ?? "";
            FieldType = fieldType ?? "";
            IsRequired = isRequired;
            Options = options ?? Array.Empty<string>();
            IsSecret = isSecret;
            SecretState = secretState ?? "";
            State = secretState ?? "";
        }

        /// <summary>字段名。</summary>
        [JsonPropertyName("名")]
        public string Name { get; }

        /// <summary>控件类型：string / number / bool / enum / secret。</summary>
        [JsonPropertyName("类型")]
        public string FieldType { get; }

        /// <summary>是否必填。</summary>
        [JsonPropertyName("必填")]
        public bool IsRequired { get; }

        /// <summary>enum 的可选值；其他类型是空列表。</summary>
        [JsonPropertyName("选项")]
        public IReadOnlyList<string> Options { get; }

        /// <summary>是不是密钥字段。密钥的值永不读取、永不输出（决策 5）。</summary>
        [JsonPropertyName("密钥")]
        public bool IsSecret { get; }

        /// <summary>该字段配没配：已配 / 未配。密钥与非密钥字段都填；值一律不显示。</summary>
        [JsonPropertyName("密钥状态")]
        public string SecretState { get; }

        /// <summary>该字段配没配：已配 / 未配。密钥与非密钥字段都填；值一律不显示（与「密钥状态」同值，页面统一读这个）。</summary>
        [JsonPropertyName("状态")]
        public string State { get; }
    }

    /// <summary>下游页里的一个 driver。</summary>
    public sealed class PanelBridgeRow
    {
        /// <summary>
        /// 构造下游页里的一行 driver。
        /// </summary>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="shape">形态：线上 / 本地；自述损坏时为空串。</param>
        /// <param name="contractVersion">契约版本。</param>
        /// <param name="implementation">实现名；实现还不存在时这里仍然有值（决策 23：不写桩，但自述照写）。</param>
        /// <param name="trialCommand">试跑命令；为空串表示还没有可跑的试跑。</param>
        /// <param name="probeCommand">能力探测命令；为空串表示没有探测器。</param>
        /// <param name="fields">配置字段行列表。</param>
        /// <param name="isProvisioned">供给过没有：_Generated/Bridges/&lt;名&gt;/ 下有指纹文件即为 true。</param>
        /// <param name="dependencyCount">能力对账的依赖总数；对账没跑成时是 -1。</param>
        /// <param name="satisfiedCount">能力对账的满足数；对账没跑成时是 -1。</param>
        /// <param name="capabilityMeasured">能力对账跑成了没有。</param>
        /// <param name="capabilityNotes">能力对账没跑成的原因，或逐条发现的文案。</param>
        /// <param name="loadFailureReason">driver.json 读不动时的原因；正常为空串。</param>
        /// <param name="localConfigNote">本机配置文件缺失时的说明；正常为空串。文件不存在与没填是两支，必须分开说（决策 42、77）。</param>
        public PanelBridgeRow(
            string driverName,
            string shape,
            string contractVersion,
            string implementation,
            string trialCommand,
            string probeCommand,
            IReadOnlyList<PanelBridgeFieldRow> fields,
            bool isProvisioned,
            int dependencyCount,
            int satisfiedCount,
            bool capabilityMeasured,
            IReadOnlyList<string> capabilityNotes,
            string loadFailureReason,
            string localConfigNote)
        {
            DriverName = driverName ?? "";
            Shape = shape ?? "";
            ContractVersion = contractVersion ?? "";
            Implementation = implementation ?? "";
            TrialCommand = trialCommand ?? "";
            ProbeCommand = probeCommand ?? "";
            Fields = fields ?? Array.Empty<PanelBridgeFieldRow>();
            IsProvisioned = isProvisioned;
            DependencyCount = dependencyCount;
            SatisfiedCount = satisfiedCount;
            CapabilityMeasured = capabilityMeasured;
            CapabilityNotes = capabilityNotes ?? Array.Empty<string>();
            LoadFailureReason = loadFailureReason ?? "";
            LocalConfigNote = localConfigNote ?? "";
        }

        /// <summary>driver 名称。</summary>
        [JsonPropertyName("driver")]
        public string DriverName { get; }

        /// <summary>形态：线上 / 本地；自述损坏时为空串。</summary>
        [JsonPropertyName("形态")]
        public string Shape { get; }

        /// <summary>契约版本。</summary>
        [JsonPropertyName("契约")]
        public string ContractVersion { get; }

        /// <summary>实现名；实现还不存在时这里仍然有值（决策 23：不写桩，但自述照写）。</summary>
        [JsonPropertyName("实现")]
        public string Implementation { get; }

        /// <summary>试跑命令；为空串表示还没有可跑的试跑。</summary>
        [JsonPropertyName("试跑")]
        public string TrialCommand { get; }

        /// <summary>能力探测命令；为空串表示没有探测器。</summary>
        [JsonPropertyName("探测")]
        public string ProbeCommand { get; }

        /// <summary>配置字段行列表。</summary>
        [JsonPropertyName("字段")]
        public IReadOnlyList<PanelBridgeFieldRow> Fields { get; }

        /// <summary>供给过没有：_Generated/Bridges/&lt;名&gt;/ 下有指纹文件即为 true。</summary>
        [JsonPropertyName("供给")]
        public bool IsProvisioned { get; }

        /// <summary>能力对账的依赖总数；对账没跑成时是 -1。</summary>
        [JsonPropertyName("依赖数")]
        public int DependencyCount { get; }

        /// <summary>能力对账的满足数；对账没跑成时是 -1。</summary>
        [JsonPropertyName("满足数")]
        public int SatisfiedCount { get; }

        /// <summary>能力对账跑成了没有。</summary>
        [JsonPropertyName("对账成")]
        public bool CapabilityMeasured { get; }

        /// <summary>能力对账没跑成的原因，或逐条发现的文案。</summary>
        [JsonPropertyName("对账说明")]
        public IReadOnlyList<string> CapabilityNotes { get; }

        /// <summary>driver.json 读不动时的原因；正常为空串。该行仍然产出。</summary>
        [JsonPropertyName("读失败")]
        public string LoadFailureReason { get; }

        /// <summary>本机配置文件缺失时的说明；正常为空串。「文件不存在」与「文件有但这项没填」是两支（决策 42、77）。</summary>
        [JsonPropertyName("本机配置说明")]
        public string LocalConfigNote { get; }
    }

    /// <summary>一条资产的离风格结果，供面板按需拉取。</summary>
    public sealed class PanelDeviationResult
    {
        /// <summary>
        /// 构造一条离风格结果。
        /// </summary>
        /// <param name="assetIdentifier">资产 id。</param>
        /// <param name="deviation">加权最小色板距离；没算成时是 -1。</param>
        /// <param name="swatches">主色十六进制串，最多前五色。</param>
        /// <param name="measured">算成了没有。</param>
        /// <param name="failureReason">没算成的原因；算成了是空串。</param>
        public PanelDeviationResult(
            string assetIdentifier,
            double deviation,
            IReadOnlyList<string> swatches,
            bool measured,
            string failureReason)
        {
            AssetIdentifier = assetIdentifier ?? "";
            Deviation = deviation;
            Swatches = swatches ?? Array.Empty<string>();
            Measured = measured;
            FailureReason = failureReason ?? "";
        }

        /// <summary>资产 id。</summary>
        [JsonPropertyName("资产id")]
        public string AssetIdentifier { get; }

        /// <summary>加权最小色板距离；没算成时是 -1。</summary>
        [JsonPropertyName("距离")]
        public double Deviation { get; }

        /// <summary>主色十六进制串，最多前五色。</summary>
        [JsonPropertyName("主色")]
        public IReadOnlyList<string> Swatches { get; }

        /// <summary>算成了没有。</summary>
        [JsonPropertyName("测成")]
        public bool Measured { get; }

        /// <summary>没算成的原因；算成了是空串。</summary>
        [JsonPropertyName("原因")]
        public string FailureReason { get; }
    }

    /// <summary>
    /// 桥接包页里的一件东西：编辑器包、下游节点/模型、随仓库走的驱动脚本。
    /// 「状态」四选一：已装 / 缺 / 未验 / 无需安装——「未验」是本机还没查过，不是没有，
    /// 页面上它既不染绿也不染红（决策 42 的又一处长相）。
    /// </summary>
    public sealed class PanelHostPackageRow
    {
        /// <summary>
        /// 构造一行桥接包。
        /// </summary>
        /// <param name="name">包名 / 依赖名 / 脚本名。</param>
        /// <param name="category">类别：编辑器包 / 节点 / 模型 / lora / 驱动脚本。</param>
        /// <param name="versionRequirement">要的版本或 git 记号；没写是空串。</param>
        /// <param name="state">状态：已装 / 缺 / 未验 / 无需安装。</param>
        /// <param name="evidence">判成这个状态的依据。</param>
        /// <param name="source">来源：git 地址、下载页；没有是空串。</param>
        /// <param name="installCommand">安装命令；空串表示清单没给。</param>
        /// <param name="nextStep">下一步动作；已装时是空串。</param>
        public PanelHostPackageRow(
            string name,
            string category,
            string versionRequirement,
            string state,
            string evidence,
            string source,
            string installCommand,
            string nextStep)
        {
            Name = name ?? "";
            Category = category ?? "";
            VersionRequirement = versionRequirement ?? "";
            State = state ?? "";
            Evidence = evidence ?? "";
            Source = source ?? "";
            InstallCommand = installCommand ?? "";
            NextStep = nextStep ?? "";
        }

        /// <summary>包名 / 依赖名 / 脚本名。</summary>
        [JsonPropertyName("名")]
        public string Name { get; }

        /// <summary>类别：编辑器包 / 节点 / 模型 / lora / 驱动脚本。</summary>
        [JsonPropertyName("类别")]
        public string Category { get; }

        /// <summary>要的版本或 git 记号；没写是空串。</summary>
        [JsonPropertyName("版本")]
        public string VersionRequirement { get; }

        /// <summary>状态：已装 / 缺 / 未验 / 无需安装。</summary>
        [JsonPropertyName("状态")]
        public string State { get; }

        /// <summary>判成这个状态的依据。</summary>
        [JsonPropertyName("依据")]
        public string Evidence { get; }

        /// <summary>来源：git 地址、下载页；没有是空串。</summary>
        [JsonPropertyName("来源")]
        public string Source { get; }

        /// <summary>安装命令；空串表示清单没给。</summary>
        [JsonPropertyName("安装命令")]
        public string InstallCommand { get; }

        /// <summary>下一步动作；已装时是空串。</summary>
        [JsonPropertyName("下一步")]
        public string NextStep { get; }
    }

    /// <summary>
    /// 面板的身份：这一份面板是**给哪个仓库**跑的。
    ///
    /// 存在的理由只有一个：端口应答不等于「这是我的面板」。一台机器上并行开几个项目时，
    /// 8766 上很可能跑着另一个仓库的面板——只探端口的脚本会说「已经在跑」，
    /// 然后把人送进别的项目的面板里，看着一切正常，看的却是别人的数据（真踩过）。
    /// 有了这个接口，探活就能问一句「你是谁的」，对不上就说清楚而不是默认是自己的。
    /// </summary>
    public sealed class PanelIdentityRow
    {
        /// <summary>
        /// 构造一份面板身份。
        /// </summary>
        /// <param name="repositoryRoot">这份面板挂着的仓库根；没配置时是空串。</param>
        /// <param name="repositoryName">仓库根的目录名，给人看的短名；没配置时是空串。</param>
        /// <param name="port">监听端口。</param>
        public PanelIdentityRow(string repositoryRoot, string repositoryName, int port)
        {
            RepositoryRoot = repositoryRoot ?? "";
            RepositoryName = repositoryName ?? "";
            Port = port;
        }

        /// <summary>这份面板挂着的仓库根；没配置时是空串。</summary>
        [JsonPropertyName("仓库根")]
        public string RepositoryRoot { get; }

        /// <summary>仓库根的目录名，给人看的短名。</summary>
        [JsonPropertyName("仓库名")]
        public string RepositoryName { get; }

        /// <summary>监听端口。</summary>
        [JsonPropertyName("端口")]
        public int Port { get; }
    }

    /// <summary>
    /// 桥接包页里一个能就地改的配置字段。密钥与非密钥的差别只在「值」这一栏：
    /// 非密钥带当前值（页面预填进输入框），密钥的「值」**恒为空串**——写放开了，读没放开，
    /// 页面上密钥永远只有「已配 / 未配」和一个空输入框。
    /// </summary>
    public sealed class PanelHostFieldRow
    {
        /// <summary>
        /// 构造一个可改字段。
        /// </summary>
        /// <param name="name">字段名。</param>
        /// <param name="fieldType">类型：string / number / boolean / secret。</param>
        /// <param name="isSecret">是不是密钥字段。</param>
        /// <param name="value">当前值；密钥恒为空串。</param>
        /// <param name="isConfigured">配没配。</param>
        /// <param name="hint">一句提示：这个字段该填什么。</param>
        public PanelHostFieldRow(string name, string fieldType, bool isSecret, string value, bool isConfigured, string hint)
        {
            Name = name ?? "";
            FieldType = fieldType ?? "";
            IsSecret = isSecret;
            Value = isSecret ? "" : (value ?? "");
            IsConfigured = isConfigured;
            Hint = hint ?? "";
        }

        /// <summary>字段名。</summary>
        [JsonPropertyName("名")]
        public string Name { get; }

        /// <summary>类型：string / number / boolean / secret。</summary>
        [JsonPropertyName("类型")]
        public string FieldType { get; }

        /// <summary>是不是密钥字段。</summary>
        [JsonPropertyName("密钥")]
        public bool IsSecret { get; }

        /// <summary>当前值；密钥恒为空串。</summary>
        [JsonPropertyName("值")]
        public string Value { get; }

        /// <summary>配没配。</summary>
        [JsonPropertyName("已配")]
        public bool IsConfigured { get; }

        /// <summary>一句提示：这个字段该填什么。</summary>
        [JsonPropertyName("提示")]
        public string Hint { get; }
    }

    /// <summary>
    /// 桥接包页里的一条插件声明原文。页面拿它预填「改这一条」的表单——
    /// 状态行（包）说的是「装没装」，这一条说的是「我们声明了什么」，两件事分开带。
    /// </summary>
    public sealed class PanelHostDeclarationRow
    {
        /// <summary>
        /// 构造一条声明原文。
        /// </summary>
        /// <param name="name">插件名。</param>
        /// <param name="hostName">宿主名。</param>
        /// <param name="markerPath">标志路径；空串表示还没填。</param>
        /// <param name="version">版本。</param>
        /// <param name="source">来源。</param>
        /// <param name="installSteps">安装步骤。</param>
        /// <param name="description">说明。</param>
        public PanelHostDeclarationRow(
            string name,
            string hostName,
            string markerPath,
            string version,
            string source,
            string installSteps,
            string description)
        {
            Name = name ?? "";
            HostName = hostName ?? "";
            MarkerPath = markerPath ?? "";
            Version = version ?? "";
            Source = source ?? "";
            InstallSteps = installSteps ?? "";
            Description = description ?? "";
        }

        /// <summary>插件名。</summary>
        [JsonPropertyName("名")]
        public string Name { get; }

        /// <summary>宿主名。</summary>
        [JsonPropertyName("宿主")]
        public string HostName { get; }

        /// <summary>标志路径；空串表示还没填。</summary>
        [JsonPropertyName("标志路径")]
        public string MarkerPath { get; }

        /// <summary>版本。</summary>
        [JsonPropertyName("版本")]
        public string Version { get; }

        /// <summary>来源。</summary>
        [JsonPropertyName("来源")]
        public string Source { get; }

        /// <summary>安装步骤。</summary>
        [JsonPropertyName("安装步骤")]
        public string InstallSteps { get; }

        /// <summary>说明。</summary>
        [JsonPropertyName("说明")]
        public string Description { get; }
    }

    /// <summary>
    /// 桥接包页里的一个宿主：一个编辑器，或一个下游服务。
    /// 「本体」与「桥接包」分两栏报——软件装了但包没解析、包在仓库里但软件没装，是两种不同的卡壳。
    /// </summary>
    public sealed class PanelHostRow
    {
        /// <summary>
        /// 构造一行宿主。
        /// </summary>
        /// <param name="name">宿主名。</param>
        /// <param name="kind">种类：编辑器 / 本机服务 / 线上服务。</param>
        /// <param name="hostState">本体状态：已装 / 缺 / 未验 / 无需安装。</param>
        /// <param name="hostDetail">本体状态的依据。</param>
        /// <param name="hostVersion">本体版本；判不出来是空串。</param>
        /// <param name="hostNextStep">本体的下一步动作；已装时是空串。</param>
        /// <param name="packages">这个宿主的桥接包 / 插件 / 脚本。</param>
        /// <param name="fields">能就地改的配置字段；没有就是空表。</param>
        /// <param name="declarations">这个宿主名下的插件声明原文；没有就是空表。</param>
        /// <param name="notes">补充说明。</param>
        /// <param name="trialCommand">能在面板上跑一次的命令；没有是空串。</param>
        /// <param name="loadFailureReason">这一行读不出来时的原因；正常是空串。</param>
        public PanelHostRow(
            string name,
            string kind,
            string hostState,
            string hostDetail,
            string hostVersion,
            string hostNextStep,
            IReadOnlyList<PanelHostPackageRow> packages,
            IReadOnlyList<PanelHostFieldRow> fields,
            IReadOnlyList<PanelHostDeclarationRow> declarations,
            IReadOnlyList<string> notes,
            string trialCommand,
            string loadFailureReason)
        {
            Name = name ?? "";
            Kind = kind ?? "";
            HostState = hostState ?? "";
            HostDetail = hostDetail ?? "";
            HostVersion = hostVersion ?? "";
            HostNextStep = hostNextStep ?? "";
            Packages = packages ?? Array.Empty<PanelHostPackageRow>();
            Fields = fields ?? Array.Empty<PanelHostFieldRow>();
            Declarations = declarations ?? Array.Empty<PanelHostDeclarationRow>();
            Notes = notes ?? Array.Empty<string>();
            TrialCommand = trialCommand ?? "";
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>宿主名。</summary>
        [JsonPropertyName("宿主")]
        public string Name { get; }

        /// <summary>种类：编辑器 / 本机服务 / 线上服务。</summary>
        [JsonPropertyName("种类")]
        public string Kind { get; }

        /// <summary>本体状态：已装 / 缺 / 未验 / 无需安装。</summary>
        [JsonPropertyName("本体")]
        public string HostState { get; }

        /// <summary>本体状态的依据。</summary>
        [JsonPropertyName("本体依据")]
        public string HostDetail { get; }

        /// <summary>本体版本；判不出来是空串。</summary>
        [JsonPropertyName("版本")]
        public string HostVersion { get; }

        /// <summary>本体的下一步动作；已装时是空串。</summary>
        [JsonPropertyName("本体下一步")]
        public string HostNextStep { get; }

        /// <summary>这个宿主的桥接包 / 插件 / 脚本。</summary>
        [JsonPropertyName("包")]
        public IReadOnlyList<PanelHostPackageRow> Packages { get; }

        /// <summary>能就地改的配置字段；没有就是空表。</summary>
        [JsonPropertyName("字段")]
        public IReadOnlyList<PanelHostFieldRow> Fields { get; }

        /// <summary>这个宿主名下的插件声明原文；没有就是空表。</summary>
        [JsonPropertyName("声明")]
        public IReadOnlyList<PanelHostDeclarationRow> Declarations { get; }

        /// <summary>补充说明。</summary>
        [JsonPropertyName("知会")]
        public IReadOnlyList<string> Notes { get; }

        /// <summary>能在面板上跑一次的命令；没有是空串。</summary>
        [JsonPropertyName("试跑")]
        public string TrialCommand { get; }

        /// <summary>这一行读不出来时的原因；正常是空串。</summary>
        [JsonPropertyName("读失败")]
        public string LoadFailureReason { get; }
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

            var rows = new List<PanelRequirementRow>();
            foreach (var identifier in PoolPaths.EnumerateRequirementIdentifiers(poolRoot))
            {
                var filePath = PoolPaths.RequirementFile(poolRoot, identifier);
                if (!File.Exists(filePath))
                {
                    // 目录在而骨架缺：这是 pool.validate 要报的违规，面板只管别把它渲染成一行空白。
                    continue;
                }

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
        /// 读门禁报告：读仓库根 _Generated/gate-report.json。
        /// 文件不存在时返回 Status = 未跑、空条目（门禁报告是后续期才产的东西，这里如实说没有）；
        /// 存在时按「条目」数组读，任一条目结果不是「成功」即整份为红，否则绿；解析失败退回未跑。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static PanelGateReport ReadGateReport(string repositoryRoot)
        {
            var reportPath = Path.Combine(repositoryRoot, "_Generated", "gate-report.json");
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
        /// 变体合格判定与选片一致：顶层图片文件且有同名「.provenance.json」边车才算合格，弃置数与预览存在性用 AssetPaths 数。
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

                    var previewFullPath = AssetPaths.PreviewFile(repositoryRoot, requirementIdentifier, request.Identifier);
                    rows.Add(new PanelAssetRow(
                        request.Identifier,
                        request.RequirementIdentifier,
                        request.AssetType,
                        destination,
                        BuildSpecSummary(request.Specification),
                        request.VariantCount,
                        CountQualifiedVariants(repositoryRoot, requirementIdentifier, request.Identifier),
                        CountRejectedVariants(repositoryRoot, requirementIdentifier, request.Identifier),
                        File.Exists(previewFullPath),
                        File.Exists(previewFullPath) ? RepositoryRelative(repositoryRoot, previewFullPath) : "",
                        ReadStyleAnchorFinalName(request)));
                }
            }

            rows.Sort((left, right) => StringComparer.Ordinal.Compare(left.AssetIdentifier, right.AssetIdentifier));
            return rows;
        }

        /// <summary>
        /// 读设计池页：扫 &lt;池根&gt;/Designs/Final、汇总、记录 三个目录（各自顶层，不递归）。
        /// 目录不存在跳过那一类；解析不了的文件照样产一行、IsReadable 为 false——设计池页要让人
        /// 看见「这里有个坏文件」，静默吞掉才是骗人。排序：分类固定 定稿 → 汇总 → 记录，
        /// 同类内按 Moment 降序（新的在前），时间相同按文件名序数序。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static IReadOnlyList<PanelDesignRow> ReadDesigns(string poolRoot)
        {
            var rows = new List<PanelDesignRow>();
            foreach (var (categoryDirectory, category) in DesignCategories)
            {
                var directory = Path.Combine(poolRoot, "Designs", categoryDirectory);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                var files = Directory.GetFiles(directory, "*.json").ToList();

                // 定稿是一稿一目录：Designs/Final/<名>/final.json（子文档 06 §五，
                // FinalPalette.Load 也是照这个找的）。只扫平铺的 *.json 就永远扫不到任何一份真定稿，
                // 定稿预览那一块会恒显示「还没有定稿」——P7 批次三验收时真踩到过。
                if (string.Equals(category, "定稿", StringComparison.Ordinal))
                {
                    foreach (var finalDirectory in Directory.EnumerateDirectories(directory))
                    {
                        var finalFile = Path.Combine(finalDirectory, "final.json");
                        if (File.Exists(finalFile))
                        {
                            files.Add(finalFile);
                        }
                    }
                }

                files.Sort(StringComparer.Ordinal);
                foreach (var filePath in files)
                {
                    rows.Add(ReadDesignRow(category, filePath));
                }
            }

            rows.Sort((left, right) =>
            {
                var byCategory = CategoryIndex(left.Category).CompareTo(CategoryIndex(right.Category));
                if (byCategory != 0)
                {
                    return byCategory;
                }

                var byMoment = string.CompareOrdinal(right.Moment, left.Moment);
                if (byMoment != 0)
                {
                    return byMoment;
                }

                return StringComparer.Ordinal.Compare(left.Name, right.Name);
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

            // 走 SpecificationPaths 而不是自己再拼一遍：这里原先是第二个来源，
            // 规范目录改名时它得靠人记得同步改——记不住就是规范页整层悄悄掉成 0 份，
            // 而且不会有任何一个测试红（夹具在临时目录里自己造目录，造的是什么就读得到什么）。
            var businessRoot = SpecificationPaths.BusinessRoot(repositoryRoot);
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

        /// <summary>
        /// 设计池的三个分类：**目录名（ASCII）与展示标签（中文）是两件事**。
        /// 决策 1 改写后路径一律 ASCII，而页面上给人看的仍是「定稿 / 汇总 / 记录」——
        /// 原来这两样共用一个字符串，改目录名的那一刻页面文案会跟着变成英文，
        /// 或者（更常见）忘了改其中一处，那一整类就恒显示为空。
        /// </summary>
        private static readonly (string Directory, string Label)[] DesignCategories =
        {
            ("Final", "定稿"),
            ("Digest", "汇总"),
            ("Records", "记录")
        };

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

        /// <summary>
        /// 读下游页：扫 Bridges/ 下每个带 driver.json 的目录（目录名即 driver 名，按序数序）。
        /// driver.json 读不动或坏时该行仍然产出，只填名称与 LoadFailureReason（决策 43）；
        /// 密钥（决策 5）只判 Tools/CreationPipeline/Config/local.json 里键在不在，值永不读取、永不输出；
        /// 能力对账只对本地形态 driver 跑，没跑成时不渲染成「全部满足」（决策 42）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static IReadOnlyList<PanelBridgeRow> ReadBridges(string repositoryRoot, string poolRoot)
        {
            var driverNames = DiscoverDriverNames(repositoryRoot);
            var rows = new List<PanelBridgeRow>();
            foreach (var driverName in driverNames)
            {
                rows.Add(ReadBridgeRow(repositoryRoot, driverName));
            }

            return rows;
        }

        /// <summary>
        /// 读桥接包页：每个编辑器 / 每个下游的本体装没装、要往里塞的包装没装、还差什么。
        /// 判定全在 <see cref="HostPackageInventory"/> 那一份，这里只做形状转换——
        /// 面板与 bridge.inventory 命令看到的必须是同一份判定，不许各算各的。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static IReadOnlyList<PanelHostRow> ReadHostPackages(string repositoryRoot)
        {
            var rows = new List<PanelHostRow>();
            foreach (var host in HostPackageInventory.Build(repositoryRoot))
            {
                var packages = host.Packages
                    .Select(package => new PanelHostPackageRow(
                        package.Name,
                        package.Category,
                        package.VersionRequirement,
                        package.State,
                        package.Evidence,
                        package.Source,
                        package.InstallCommand,
                        package.NextStep))
                    .ToList();
                var fields = host.EditableFields
                    .Select(field => new PanelHostFieldRow(
                        field.Name,
                        field.FieldType,
                        field.IsSecret,
                        field.Value,
                        field.IsConfigured,
                        field.Hint))
                    .ToList();
                rows.Add(new PanelHostRow(
                    host.Name,
                    host.Kind,
                    host.HostState,
                    host.HostDetail,
                    host.HostVersion,
                    host.HostNextStep,
                    packages,
                    fields,
                    host.Declarations
                        .Select(declaration => new PanelHostDeclarationRow(
                            declaration.Name,
                            declaration.HostName,
                            declaration.MarkerPath,
                            declaration.Version,
                            declaration.Source,
                            declaration.InstallSteps,
                            declaration.Description))
                        .ToList(),
                    host.Notes,
                    host.TrialCommand,
                    host.LoadFailureReason));
            }

            return rows;
        }

        /// <summary>
        /// 按需算一条资产的离风格：解码预览图、聚类主色、与定稿色板比距离（一律走 StyleDeviationAnalyzer，
        /// 面板不另写一份距离算法——决策 21）。只报告不行动：本方法不写盘、不移动任何资产文件。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        public static PanelDeviationResult ReadDeviation(
            string repositoryRoot,
            string poolRoot,
            string requirementIdentifier,
            string assetIdentifier)
        {
            if (string.IsNullOrWhiteSpace(requirementIdentifier))
            {
                return new PanelDeviationResult("", -1, Array.Empty<string>(), false, "缺需求 id");
            }

            if (string.IsNullOrWhiteSpace(assetIdentifier))
            {
                return new PanelDeviationResult("", -1, Array.Empty<string>(), false, "缺资产 id");
            }

            var request = AssetRequest.Read(AssetPaths.AssetRequestFile(repositoryRoot, requirementIdentifier, assetIdentifier));
            if (string.IsNullOrEmpty(request.Identifier))
            {
                return new PanelDeviationResult(assetIdentifier, -1, Array.Empty<string>(), false, "资产请求读不成或不存在");
            }

            var previewPath = AssetPaths.PreviewFile(repositoryRoot, requirementIdentifier, assetIdentifier);
            if (!File.Exists(previewPath))
            {
                return new PanelDeviationResult(assetIdentifier, -1, Array.Empty<string>(), false, "还没有预览图，无从比较");
            }

            if (!request.StyleAnchors.TryGetValue("定稿", out var anchorRaw) || string.IsNullOrWhiteSpace(anchorRaw))
            {
                return new PanelDeviationResult(assetIdentifier, -1, Array.Empty<string>(), false, "这条资产没有风格锚点");
            }

            var anchorFinalName = StripJsonStringQuotes(anchorRaw);
            if (string.IsNullOrWhiteSpace(anchorFinalName))
            {
                return new PanelDeviationResult(assetIdentifier, -1, Array.Empty<string>(), false, "这条资产没有风格锚点");
            }

            var palette = FinalPalette.Load(poolRoot, anchorFinalName);
            if (!palette.Loaded)
            {
                return new PanelDeviationResult(assetIdentifier, -1, Array.Empty<string>(), false, palette.LoadFailureReason);
            }

            var result = StyleDeviationAnalyzer.Measure(
                new[] { previewPath },
                palette,
                ColorPalette.DefaultClusterCount,
                1);
            if (result.Ranked.Count > 0)
            {
                var entry = result.Ranked[0];
                var swatches = entry.Swatches.Take(5).Select(swatch => swatch.Color.ToHex()).ToList();
                return new PanelDeviationResult(assetIdentifier, entry.Deviation, swatches, true, "");
            }

            if (result.Skipped.Count > 0)
            {
                return new PanelDeviationResult(assetIdentifier, -1, Array.Empty<string>(), false, result.Skipped[0].SkipReason);
            }

            return new PanelDeviationResult(assetIdentifier, -1, Array.Empty<string>(), false, "离风格没算成");
        }

        /// <summary>读下游页里的一行 driver；driver.json 读不动时只填名称与原因，该行照常产出。</summary>
        private static PanelBridgeRow ReadBridgeRow(string repositoryRoot, string driverName)
        {
            BridgeDriverDescriptor descriptor;
            try
            {
                descriptor = BridgeDriverDescriptor.Load(repositoryRoot, driverName);
            }
            catch (InvalidOperationException exception)
            {
                // 决策 43：烂在库里的必须让人看见——这一行仍然产出，只给名称与原因。
                return new PanelBridgeRow(
                    driverName, "", "", "", "", "",
                    Array.Empty<PanelBridgeFieldRow>(), false, -1, -1, false, Array.Empty<string>(), exception.Message,
                    LocalConfigNoteOf(repositoryRoot));
            }

            var (fields, probeCommand) = ReadBridgeFields(repositoryRoot, driverName, descriptor);
            var isProvisioned = File.Exists(ProvisionPaths.FingerprintFile(repositoryRoot, driverName));
            if (descriptor.Form != "本地")
            {
                var notes = new List<string>();
                if (string.Equals(descriptor.Form, "线上", StringComparison.Ordinal))
                {
                    notes.Add("线上 driver 不做本地能力对账");
                }

                return new PanelBridgeRow(
                    driverName, descriptor.Form, descriptor.ContractRange, descriptor.ImplementationName,
                    descriptor.TrialCommand, probeCommand, fields, isProvisioned, -1, -1, false, notes, "",
                    LocalConfigNoteOf(repositoryRoot));
            }

            try
            {
                var manifest = DependencyManifest.Load(repositoryRoot, driverName);
                var probePath = ProvisionPaths.ProbeResultFile(repositoryRoot, driverName);
                var probeResult = CapabilityProbeResult.LoadFromFile(probePath);
                var report = CapabilityReconciler.Reconcile(driverName, manifest, probeResult);
                var notes = report.Findings.Select(finding => finding.ToDisplayText()).ToList();
                return new PanelBridgeRow(
                    driverName, descriptor.Form, descriptor.ContractRange, descriptor.ImplementationName,
                    descriptor.TrialCommand, probeCommand, fields, isProvisioned,
                    report.DependencyCount, report.SatisfiedCount, true, notes, "",
                    LocalConfigNoteOf(repositoryRoot));
            }
            catch (InvalidOperationException exception)
            {
                // 决策 42：对账没跑成（依赖清单缺失、探测输出缺失、JSON 坏）一律计数 -1、原因写明，
                // 绝不渲染成「零个依赖全满足」。
                return new PanelBridgeRow(
                    driverName, descriptor.Form, descriptor.ContractRange, descriptor.ImplementationName,
                    descriptor.TrialCommand, probeCommand, fields, isProvisioned, -1, -1, false,
                    new List<string> { exception.Message }, "",
                    LocalConfigNoteOf(repositoryRoot));
            }
        }

        /// <summary>
        /// 本机配置文件缺失时的说明；文件在就给空串。「文件不存在」与「文件有但这项没填」是两支，
        /// 页面必须能区分（决策 42、77）：字段级判据统一把「文件不存在」当未配，但这一行说明告诉人原因。
        /// </summary>
        private static string LocalConfigNoteOf(string repositoryRoot)
        {
            return File.Exists(LocalConfigFilePath(repositoryRoot))
                ? ""
                : "本机配置文件不存在：Tools/CreationPipeline/Config/local.json（本行字段全部按「未配」处理）";
        }

        /// <summary>
        /// 读 driver.json 的配置 schema 明细与能力探测命令。密钥字段（配置 schema 里类型是 secret 的、
        /// 以及 driver.json 的「密钥字段」数组点名的）只判本机配置里键在不在，值一次都不取（决策 5）。
        /// </summary>
        private static (IReadOnlyList<PanelBridgeFieldRow> Fields, string ProbeCommand) ReadBridgeFields(
            string repositoryRoot,
            string driverName,
            BridgeDriverDescriptor descriptor)
        {
            var filePath = BridgeDriverDescriptor.DriverFile(repositoryRoot, driverName);
            var fields = new List<PanelBridgeFieldRow>();
            var probeCommand = "";
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(filePath)))
                {
                    var root = document.RootElement;
                    probeCommand = ReadStringOrEmpty(root, "能力探测");

                    var schemaFields = new List<(string Name, JsonElement Element)>();
                    if (root.TryGetProperty("配置schema", out var schema) && schema.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in schema.EnumerateObject())
                        {
                            schemaFields.Add((property.Name, property.Value));
                        }
                    }

                    schemaFields.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));

                    // 密钥字段数组里不在配置 schema 的字段名也补进表单（决策 5：密钥配没配必须可见），
                    // 排在 schema 字段之后，类型就是 secret——它没有其它控件信息。
                    foreach (var secretName in descriptor.SecretFieldNames)
                    {
                        if (!schemaFields.Any(candidate => string.Equals(candidate.Name, secretName, StringComparison.Ordinal)))
                        {
                            schemaFields.Add((secretName, default(JsonElement)));
                        }
                    }

                    var secretNames = new HashSet<string>(descriptor.SecretFieldNames, StringComparer.Ordinal);
                    foreach (var (name, element) in schemaFields)
                    {
                        var fieldType = "";
                        var isRequired = false;
                        var options = new List<string>();
                        var isSecretFieldType = false;
                        if (element.ValueKind == JsonValueKind.Object)
                        {
                            fieldType = ReadStringOrEmpty(element, "类型");
                            isRequired = element.TryGetProperty("必填", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.True;
                            options = ReadStringList(element, "选项");
                            isSecretFieldType = string.Equals(fieldType, "secret", StringComparison.Ordinal);
                        }

                        var isSecret = isSecretFieldType || secretNames.Contains(name);
                        if (element.ValueKind != JsonValueKind.Object && isSecret)
                        {
                            fieldType = "secret";
                        }

                        fields.Add(new PanelBridgeFieldRow(
                            name,
                            fieldType,
                            isRequired,
                            options,
                            isSecret,
                            isSecret ? SecretStateOf(repositoryRoot, name) : NonSecretFieldStateOf(repositoryRoot, driverName, name)));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // descriptor.Load 已经过了，这里再失败几乎不可能；字段与探测命令给默认值，不抛。
            }

            return (fields, probeCommand);
        }

        /// <summary>本机配置文件路径：Tools/CreationPipeline/Config/local.json（密钥与非密钥字段都从这里读）。</summary>
        private static string LocalConfigFilePath(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "local.json");
        }

        /// <summary>判一个密钥字段在 Tools/CreationPipeline/Config/local.json 里配没配。只判键在不在，一次都不取它的值（决策 5、78）。</summary>
        private static string SecretStateOf(string repositoryRoot, string secretFieldName)
        {
            var localFilePath = LocalConfigFilePath(repositoryRoot);
            if (!File.Exists(localFilePath))
            {
                return "未配";
            }

            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(localFilePath)))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return "未配";
                    }

                    // out _ 就是只判存在：密钥的值永远不落进任何返回、日志或文案。
                    return root.TryGetProperty(secretFieldName, out _) ? "已配" : "未配";
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return "未配";
            }
        }

        /// <summary>
        /// 判一个非密钥字段在 Tools/CreationPipeline/Config/local.json 的「下游配置.&lt;driver&gt;.&lt;字段名&gt;」里配没配。
        /// 判据是「键在不在、且不是空串」：缺失、空串、文件不存在统一算「未配」（决策 78 的精神——
        /// 非密钥字段也只报配没配，值不显示、不出现在任何返回字段）。「文件不存在」与「没填」两支
        /// 由 LocalConfigNoteOf 的说明行区分，这里不合并成同一个原因。
        /// </summary>
        private static string NonSecretFieldStateOf(string repositoryRoot, string driverName, string fieldName)
        {
            var localFilePath = LocalConfigFilePath(repositoryRoot);
            if (!File.Exists(localFilePath))
            {
                return "未配";
            }

            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(localFilePath)))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("下游配置", out var downstream) || downstream.ValueKind != JsonValueKind.Object
                        || !downstream.TryGetProperty(driverName, out var driver) || driver.ValueKind != JsonValueKind.Object
                        || !driver.TryGetProperty(fieldName, out var value))
                    {
                        return "未配";
                    }

                    // 值只判「非空串」：数字/布尔转成字符串判，空串与键缺失同判「未配」。
                    // 判完即弃，值本身绝不放进任何返回、日志或文案。
                    var text = value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString() ?? "",
                        JsonValueKind.Number => value.ToString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => ""
                    };
                    return string.IsNullOrEmpty(text) ? "未配" : "已配";
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return "未配";
            }
        }

        /// <summary>读一份设计文档：解析成 JSON 对象成功则取名称/标题、版本与时间；定稿行额外取色板、数字版本与参考图。时间取不到「时间」字段时退化成文件最后写入时间（ISO 8601，UTC），并置 MomentFromFileTime。失败照样产一行不可读（决策 43）。</summary>
        private static PanelDesignRow ReadDesignRow(string category, string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            if (string.Equals(name, "final", StringComparison.Ordinal))
            {
                // 一稿一目录时文件名恒是 final.json，那个名字对人没有意义；目录名才是这份定稿的名字。
                // （文件名从「定稿.json」改成 final.json 时这里跟着改了——
                //  漏改的表现是定稿行的名字变成「final」，不是报错。）
                var parentName = Path.GetFileName(Path.GetDirectoryName(filePath));
                if (!string.IsNullOrEmpty(parentName))
                {
                    name = parentName;
                }
            }

            var title = "";
            var version = "";
            var moment = "";
            var isReadable = false;
            var paletteColors = new List<string>();
            var finalVersion = 0;
            var referenceImages = new List<string>();
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
                            // 「创建时间」是本页原本就认的第二来源，别在加文件时间兜底时把它挤掉：
                            // 作者写下的创建时间比「这个文件上次被谁碰过」准得多。
                            moment = ReadStringOrEmpty(root, "创建时间");
                        }
                        if (string.Equals(category, "定稿", StringComparison.Ordinal))
                        {
                            paletteColors = ReadHexColorList(root, "色板");
                            finalVersion = ReadInt(root, "版本", 0);
                            referenceImages = ReadStringList(root, "参考图");
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 解析不了照样产一行：IsReadable 为 false、其余字段空串，让页面看见这个坏文件。
            }

            var momentFromFileTime = false;
            if (string.IsNullOrEmpty(moment))
            {
                try
                {
                    // 「时间」字段取不到时退化成文件最后写入时间：JSON 里的时间是文档作者写的，
                    // 文件时间是这行数据在盘上的事实，两个来源语义不同，必须用标志位标出来。
                    moment = File.GetLastWriteTimeUtc(filePath).ToString("o", CultureInfo.InvariantCulture);
                    momentFromFileTime = true;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    // 文件时间也读不到时保持空串，不抛。
                }
            }

            return new PanelDesignRow(category, name, title, version, moment, isReadable, momentFromFileTime, paletteColors, finalVersion, referenceImages);
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

        /// <summary>
        /// 读单个需求的结构化详情：需求字段 + 验收标准 + 任务状态 + 工作项清单。
        /// 需求文件不存在时返回 null（路由层据此回 404 文案）；任务目录不存在时「有任务」为 false。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        public static PanelTaskDetail ReadTaskDetailData(string repositoryRoot, string poolRoot, string requirementIdentifier)
        {
            var filePath = PoolPaths.RequirementFile(poolRoot, requirementIdentifier);
            if (!File.Exists(filePath))
            {
                return null;
            }

            string title = "", kind = "", status = "", epic = "", description = "";
            var locked = false;
            var acceptanceCriteria = new List<string>();
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(filePath));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    title = ReadStringOrEmpty(root, "标题");
                    kind = ReadStringOrEmpty(root, "类型");
                    status = ReadStringOrEmpty(root, "状态");
                    epic = ReadStringOrEmpty(root, "专项");
                    description = ReadStringOrEmpty(root, "描述");
                    locked = root.TryGetProperty("锁定", out var lockedElement) && lockedElement.ValueKind == JsonValueKind.True;
                    if (root.TryGetProperty("验收标准", out var criteria) && criteria.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in criteria.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                acceptanceCriteria.Add(item.GetString() ?? "");
                            }
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return null;
            }

            var hasTask = TaskState.TryLoad(repositoryRoot, requirementIdentifier, out var state, out _);
            var workItems = new List<PanelWorkItemRow>();
            var workItemsDirectory = Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "20-work-items");
            if (Directory.Exists(workItemsDirectory))
            {
                foreach (var workItemFile in Directory.GetFiles(workItemsDirectory, "*.json").OrderBy(path => path, StringComparer.Ordinal))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(workItemFile));
                        var root = document.RootElement;
                        if (root.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var name = ReadStringOrEmpty(root, "id");
                        if (name.Length == 0)
                        {
                            name = Path.GetFileNameWithoutExtension(workItemFile);
                        }

                        workItems.Add(new PanelWorkItemRow(name, ReadStringOrEmpty(root, "状态")));
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                    {
                        // 单个工作项文件坏了跳过，别让一份坏文件把整页详情读没。
                    }
                }
            }

            return new PanelTaskDetail(
                requirementIdentifier,
                title,
                kind,
                status,
                epic,
                description,
                locked,
                acceptanceCriteria,
                hasTask,
                hasTask ? state.Stage : "",
                hasTask ? state.SubState : "",
                hasTask ? state.PendingGate ?? "" : "",
                hasTask ? state.CurrentWorkItem ?? "" : "",
                workItems);
        }

        /// <summary>从 Pools/Requirements/&lt;id&gt;/requirement.json 读「标题」，取不到留空串。</summary>
        private static string ReadRequirementTitle(string poolRoot, string requirementIdentifier)
        {
            var filePath = PoolPaths.RequirementFile(poolRoot, requirementIdentifier);
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

        /// <summary>读字符串数组；缺失、类型不对或元素不是字符串时跳过该元素。</summary>
        private static List<string> ReadStringList(JsonElement element, string propertyName)
        {
            var values = new List<string>();
            if (!element.TryGetProperty(propertyName, out var listElement) || listElement.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in listElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    values.Add(item.GetString() ?? "");
                }
            }

            return values;
        }

        /// <summary>读色板数组：只收合法的十六进制串（#RRGGBB 或 RRGGBB），非法的跳过。</summary>
        private static List<string> ReadHexColorList(JsonElement element, string propertyName)
        {
            var colors = new List<string>();
            if (!element.TryGetProperty(propertyName, out var listElement) || listElement.ValueKind != JsonValueKind.Array)
            {
                return colors;
            }

            foreach (var item in listElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var hex = item.GetString() ?? "";
                    if (SrgbColor.TryParseHex(hex, out _))
                    {
                        colors.Add(hex);
                    }
                }
            }

            return colors;
        }

        /// <summary>分类的固定排序下标：定稿 0、汇总 1、记录 2，其余排最后。</summary>
        private static int CategoryIndex(string category)
        {
            for (var i = 0; i < DesignCategories.Length; i++)
            {
                if (string.Equals(DesignCategories[i].Label, category, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return DesignCategories.Length;
        }

        /// <summary>把绝对路径转成仓库相对路径，分隔符统一成正斜杠。</summary>
        private static string RepositoryRelative(string repositoryRoot, string fullPath)
        {
            return Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(fullPath)).Replace('\\', '/');
        }

        /// <summary>读资产请求「风格锚点.定稿」的值并剥掉 JSON 引号；没有给空串。</summary>
        private static string ReadStyleAnchorFinalName(AssetRequest request)
        {
            if (request.StyleAnchors.TryGetValue("定稿", out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                return StripJsonStringQuotes(raw);
            }

            return "";
        }
    }
}
