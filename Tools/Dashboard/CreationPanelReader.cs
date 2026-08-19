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

    /// <summary>
    /// 面板五页的数据读取器：每页只读磁盘文件，返回可直接序列化成 JSON 的对象。
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
