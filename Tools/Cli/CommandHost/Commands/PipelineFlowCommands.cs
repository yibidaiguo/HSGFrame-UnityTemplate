using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>入站命令 pool.pull 的参数。</summary>
    public sealed class PoolPullArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }
    }

    /// <summary>出站命令 pool.push 的参数。</summary>
    public sealed class PoolPushArguments
    {
        /// <summary>要推的需求 id，形如 REQ-0042。</summary>
        [Summary("要推的需求 id，形如 REQ-0042")]
        public string RequirementIdentifier { get; set; }

        /// <summary>出站事件：待验收 / 已完成 / 拒收 / 冲突 / 停等。</summary>
        [Summary("出站事件：待验收 / 已完成 / 拒收 / 冲突 / 停等")]
        public string EventName { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }
    }

    /// <summary>任务状态命令 task.status 的参数。</summary>
    public sealed class TaskStatusArguments
    {
        /// <summary>要看的需求 id；留空则列出全部任务。</summary>
        [Summary("要看的需求 id；留空则列出全部任务")]
        [DefaultValue("")]
        public string RequirementIdentifier { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }
    }

    /// <summary>引擎模式命令 engine.mode 的参数。</summary>
    public sealed class EngineModeArguments
    {
        /// <summary>要切换到的模式：值守 / 轮询 / 唤醒；留空则只显示当前模式。</summary>
        [Summary("要切换到的模式：值守 / 轮询 / 唤醒；留空则只显示当前模式")]
        [DefaultValue("")]
        public string Mode { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }
    }

    /// <summary>引擎队列命令 engine.queue 的参数。</summary>
    public sealed class EngineQueueArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }
    }

    /// <summary>风险分级命令 task.risk 的参数。</summary>
    public sealed class TaskRiskArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>按换行分隔的改动路径文本。</summary>
        [Summary("按换行分隔的改动路径文本")]
        public string ChangedPathsText { get; set; }

        /// <summary>改动行数，缺省 0。</summary>
        [Summary("改动行数，缺省 0")]
        [DefaultValue(0)]
        public int ChangedLineCount { get; set; }

        /// <summary>阻断级发现数，缺省 0。</summary>
        [Summary("阻断级发现数，缺省 0")]
        [DefaultValue(0)]
        public int BlockingFindingCount { get; set; }

        /// <summary>建议级发现数，缺省 0。</summary>
        [Summary("建议级发现数，缺省 0")]
        [DefaultValue(0)]
        public int SuggestionFindingCount { get; set; }

        /// <summary>业务模块名，用于取 规范/业务/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 规范/业务/<模块>/ 的就近覆盖")]
        [DefaultValue("")]
        public string ModuleName { get; set; }
    }

    /// <summary>放行判定命令 task.release 的参数，在 task.risk 之上加门禁全绿开关。</summary>
    public sealed class TaskReleaseArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>按换行分隔的改动路径文本。</summary>
        [Summary("按换行分隔的改动路径文本")]
        public string ChangedPathsText { get; set; }

        /// <summary>改动行数，缺省 0。</summary>
        [Summary("改动行数，缺省 0")]
        [DefaultValue(0)]
        public int ChangedLineCount { get; set; }

        /// <summary>阻断级发现数，缺省 0。</summary>
        [Summary("阻断级发现数，缺省 0")]
        [DefaultValue(0)]
        public int BlockingFindingCount { get; set; }

        /// <summary>建议级发现数，缺省 0。</summary>
        [Summary("建议级发现数，缺省 0")]
        [DefaultValue(0)]
        public int SuggestionFindingCount { get; set; }

        /// <summary>业务模块名，用于取 规范/业务/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 规范/业务/<模块>/ 的就近覆盖")]
        [DefaultValue("")]
        public string ModuleName { get; set; }

        /// <summary>门禁是否全绿，缺省 false。</summary>
        [Summary("门禁是否全绿，缺省 false")]
        [DefaultValue(false)]
        public bool AllGatesGreen { get; set; }
    }

    /// <summary>引擎一轮命令 engine.tick 与 engine.wake 共用的参数。</summary>
    public sealed class EngineTickArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>上次取活时刻，ISO 8601；缺省按从未取过。</summary>
        [Summary("上次取活时刻，ISO 8601；缺省按从未取过")]
        [DefaultValue("")]
        public string LastTickMoment { get; set; }
    }

    /// <summary>意见库追加命令 task.opinion 的参数。</summary>
    public sealed class TaskOpinionArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>问题类别，如「空引用未防」。</summary>
        [Summary("问题类别，如「空引用未防」")]
        public string Category { get; set; }

        /// <summary>模块名，如「签到」。</summary>
        [Summary("模块名，如「签到」")]
        public string ModuleName { get; set; }

        /// <summary>可规则化性：可代码化 / 可提示词化 / 不可规则化。</summary>
        [Summary("可规则化性：可代码化 / 可提示词化 / 不可规则化")]
        public string Rulability { get; set; }

        /// <summary>原文引用，打回意见里的一句话。</summary>
        [Summary("原文引用，打回意见里的一句话")]
        public string Quotation { get; set; }
    }

    /// <summary>放行入账命令 task.release.record 的参数。</summary>
    public sealed class TaskReleaseRecordArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>需求 id，形如 REQ-0042。</summary>
        [Summary("需求 id，形如 REQ-0042")]
        public string RequirementIdentifier { get; set; }

        /// <summary>风险级：低 / 常规 / 高。</summary>
        [Summary("风险级：低 / 常规 / 高")]
        public string Grade { get; set; }

        /// <summary>本次改动涉及的范围，逗号分隔，如「业务,其他」。</summary>
        [Summary("本次改动涉及的范围，逗号分隔，如「业务,其他」")]
        public string Scopes { get; set; }

        /// <summary>放行时间，ISO 8601；不给就用当前时间的 ISO 8601 文本。</summary>
        [Summary("放行时间，ISO 8601；不给就用当前时间")]
        [DefaultValue("")]
        public string ReleasedMoment { get; set; }

        /// <summary>合并提交哈希；没记就留空。</summary>
        [Summary("合并提交哈希；没记就留空")]
        [DefaultValue("")]
        public string MergeCommit { get; set; }
    }

    /// <summary>放行流水查看命令 task.ledger 的参数。</summary>
    public sealed class TaskLedgerArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>业务模块名，用于取 规范/业务/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 规范/业务/<模块>/ 的就近覆盖")]
        [DefaultValue("")]
        public string ModuleName { get; set; }

        /// <summary>抽查比例；小于 0 视为不给，用放行策略目录的抽查比例。</summary>
        [Summary("抽查比例；小于 0 视为不给，用放行策略目录的抽查比例")]
        [DefaultValue(-1.0)]
        public double Ratio { get; set; }
    }

    /// <summary>抽查销账命令 task.spotcheck 的参数。</summary>
    public sealed class TaskSpotCheckArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>放行流水条目 id，形如 RL-0001。</summary>
        [Summary("放行流水条目 id，形如 RL-0001")]
        public string LedgerIdentifier { get; set; }

        /// <summary>抽查结论状态：合格 / 发现问题。</summary>
        [Summary("抽查结论状态：合格 / 发现问题")]
        public string Conclusion { get; set; }

        /// <summary>抽查结论正文；没写就留空。</summary>
        [Summary("抽查结论正文；没写就留空")]
        [DefaultValue("")]
        public string ConclusionText { get; set; }

        /// <summary>回滚提交哈希；合格时留空。</summary>
        [Summary("回滚提交哈希；合格时留空")]
        [DefaultValue("")]
        public string RevertCommit { get; set; }

        /// <summary>业务模块名，用于取 规范/业务/&lt;模块&gt;/ 的就近覆盖与意见库模块名。</summary>
        [Summary("业务模块名，用于就近覆盖与意见库模块名")]
        [DefaultValue("")]
        public string ModuleName { get; set; }

        /// <summary>可规则化性：可代码化 / 可提示词化 / 不可规则化。</summary>
        [Summary("可规则化性：可代码化 / 可提示词化 / 不可规则化")]
        [DefaultValue("不可规则化")]
        public string Rulability { get; set; }
    }

    /// <summary>晋升提案命令 task.promotion 的参数。</summary>
    public sealed class TaskPromotionArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>动作：列出 / 入库；不给默认列出。</summary>
        [Summary("动作：列出 / 入库；不给默认列出")]
        [DefaultValue("列出")]
        public string Action { get; set; }

        /// <summary>同类条数阈值，缺省 3。</summary>
        [Summary("同类条数阈值，缺省 3")]
        [DefaultValue(3)]
        public int Threshold { get; set; }

        /// <summary>提出时间，ISO 8601；不给用当前时间。</summary>
        [Summary("提出时间，ISO 8601；不给用当前时间")]
        [DefaultValue("")]
        public string ProposedMoment { get; set; }
    }

    /// <summary>晋升裁决命令 task.promotion.decide 的参数。</summary>
    public sealed class TaskPromotionDecideArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>提案 id，形如 PR-0001。</summary>
        [Summary("提案 id，形如 PR-0001")]
        public string ProposalIdentifier { get; set; }

        /// <summary>动作：批准 / 拒绝 / 落地。</summary>
        [Summary("动作：批准 / 拒绝 / 落地")]
        public string Action { get; set; }

        /// <summary>裁决人姓名；批准 / 拒绝时必填。</summary>
        [Summary("裁决人姓名；批准 / 拒绝时必填")]
        [DefaultValue("")]
        public string DeciderName { get; set; }

        /// <summary>裁决时间，ISO 8601；不给用当前时间。</summary>
        [Summary("裁决时间，ISO 8601；不给用当前时间")]
        [DefaultValue("")]
        public string DecidedMoment { get; set; }
    }

    /// <summary>冲突列表命令 conflict.list 的参数。</summary>
    public sealed class ConflictListArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>只看未销账（未决 + 强制推送），缺省 false。</summary>
        [Summary("只看未销账（未决 + 强制推送），缺省 false")]
        [DefaultValue(false)]
        public bool OnlyPending { get; set; }
    }

    /// <summary>冲突裁决命令 conflict.resolve 的参数。</summary>
    public sealed class ConflictResolveArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>冲突 id，形如 CF-0009。</summary>
        [Summary("冲突 id，形如 CF-0009")]
        public string ConflictIdentifier { get; set; }

        /// <summary>裁决人姓名。</summary>
        [Summary("裁决人姓名")]
        public string ResolverName { get; set; }

        /// <summary>三选一：改新的 / 改旧的 / 强制推送。</summary>
        [Summary("三选一：改新的 / 改旧的 / 强制推送")]
        public string Choice { get; set; }
    }

    /// <summary>打断重规划命令 task.replan 的参数。</summary>
    public sealed class TaskReplanArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>需求 id，形如 REQ-0042。</summary>
        [Summary("需求 id，形如 REQ-0042")]
        public string RequirementIdentifier { get; set; }

        /// <summary>按换行分隔的字段 diff 命中的需求字段名文本。</summary>
        [Summary("按换行分隔的字段 diff 命中的需求字段名文本")]
        public string ChangedFieldsText { get; set; }
    }

    /// <summary>专项认领入站命令 pool.claimpull 的参数。</summary>
    public sealed class PoolClaimPullArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }
    }

    /// <summary>专项认领写盘命令 pool.claim 的参数。</summary>
    public sealed class PoolClaimArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>专项 id，如「EP-0003」。</summary>
        [Summary("专项 id，如「EP-0003」")]
        public string EpicIdentifier { get; set; }

        /// <summary>职责名，只许 美术/程序/策划。</summary>
        [Summary("职责名，只许 美术/程序/策划")]
        public string Duty { get; set; }

        /// <summary>成员的 open_id。</summary>
        [Summary("成员的 open_id")]
        public string OpenIdentifier { get; set; }

        /// <summary>true 走隐式认领（仅限默认职责内），false 走显式认领。</summary>
        [Summary("true 走隐式认领（仅限默认职责内），false 走显式认领")]
        [DefaultValue(false)]
        public bool IsImplicit { get; set; }
    }

    /// <summary>
    /// 入站/出站/专项认领/队列/状态七条命令的 CLI 入口：
    /// pool.pull 跑一轮入站、pool.push 按出站事件生成意图信封、
    /// pool.claimpull 同步专项认领入站、pool.claim 显式或隐式记一次认领、
    /// task.status 看任务状态、engine.mode 看/切引擎模式、engine.queue 看队列与能否自动派活。
    /// </summary>
    public static class PipelineFlowCommands
    {
        /// <summary>
        /// 跑一轮入站：扫收件箱，把合格记录入池、不合格的拒收。
        /// </summary>
        /// <param name="arguments">入站命令参数。</param>
        [EditorCommand("pool.pull")]
        [Summary("跑一轮入站：扫收件箱，按需求 schema 入池或拒收")]
        public static CommandResult Pull(PoolPullArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments?.RepositoryRoot, ".", "RepositoryRoot", "仓库根", out var repositoryFailure);
            if (repositoryFailure.Length > 0)
            {
                return CommandResult.Failure(repositoryFailure);
            }

            var poolRoot = ResolveRoot(arguments?.PoolRoot, "Pools", "PoolRoot", "池子根", out var poolFailure);
            if (poolFailure.Length > 0)
            {
                return CommandResult.Failure(poolFailure);
            }

            try
            {
                var schema = PoolSchemaLoader.Load(poolRoot, "需求");
                var outcomes = RequirementIntake.Run(repositoryRoot, poolRoot, schema, DateTimeOffset.Now);
                return ToPullResult(outcomes);
            }
            catch (FileNotFoundException exception)
            {
                return CommandResult.Failure(exception.Message);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"入站失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 按出站事件生成一张卡片的出站意图信封：读需求、路由卡片、落信封文件。
        /// </summary>
        /// <param name="arguments">出站命令参数。</param>
        [EditorCommand("pool.push")]
        [Summary("按出站事件生成出站意图信封")]
        public static CommandResult Push(PoolPushArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RequirementIdentifier))
            {
                return CommandResult.Failure("参数 RequirementIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.EventName))
            {
                return CommandResult.Failure("参数 EventName 为必填项");
            }

            var repositoryRoot = ResolveRoot(arguments.RepositoryRoot, ".", "RepositoryRoot", "仓库根", out var repositoryFailure);
            if (repositoryFailure.Length > 0)
            {
                return CommandResult.Failure(repositoryFailure);
            }

            var poolRoot = ResolveRoot(arguments.PoolRoot, "Pools", "PoolRoot", "池子根", out var poolFailure);
            if (poolFailure.Length > 0)
            {
                return CommandResult.Failure(poolFailure);
            }

            try
            {
                var result = PoolPushPlanner.Plan(repositoryRoot, poolRoot, arguments.RequirementIdentifier, arguments.EventName, DateTimeOffset.Now);
                if (!result.IsPlanned)
                {
                    return CommandResult.Failure(result.FailureReason);
                }

                var routing = result.Envelope?.Routing;
                var recipients = routing == null || routing.Recipients.Count == 0
                    ? "无"
                    : string.Join(",", routing.Recipients);

                var lines = new List<string>
                {
                    $"需求：{result.Envelope.RequirementIdentifier}",
                    $"事件：{result.Envelope.Event}",
                    $"卡片类型：{routing?.CardType ?? "无"}",
                    $"收件人：{recipients}",
                    $"命中步骤：{(routing == null ? "无" : routing.Step.ToString())}",
                    $"路由理由：{routing?.Reason ?? "无"}"
                };

                return CommandResult.Success($"出站意图已生成：{result.FilePath}", lines);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"出站失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 查看一条或全部需求的任务状态文本树；只读命令，不写任何文件。
        /// </summary>
        /// <param name="arguments">任务状态命令参数。</param>
        [EditorCommand("task.status")]
        [Summary("查看一条或全部需求的任务状态")]
        public static CommandResult Status(TaskStatusArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments?.RepositoryRoot, ".", "RepositoryRoot", "仓库根", out var repositoryFailure);
            if (repositoryFailure.Length > 0)
            {
                return CommandResult.Failure(repositoryFailure);
            }

            var poolRoot = ResolveRoot(arguments?.PoolRoot, "Pools", "PoolRoot", "池子根", out var poolFailure);
            if (poolFailure.Length > 0)
            {
                return CommandResult.Failure(poolFailure);
            }

            var text = string.IsNullOrWhiteSpace(arguments?.RequirementIdentifier)
                ? TaskStatusReport.RenderAll(repositoryRoot, poolRoot)
                : TaskStatusReport.RenderOne(repositoryRoot, poolRoot, arguments.RequirementIdentifier);

            var lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
            return CommandResult.Success("任务状态", lines);
        }

        /// <summary>
        /// 显示或切换引擎工作模式：留空 Mode 只显示当前模式，非空则切换并写回配置。
        /// </summary>
        /// <param name="arguments">引擎模式命令参数。</param>
        [EditorCommand("engine.mode")]
        [Summary("显示或切换引擎工作模式：值守 / 轮询 / 唤醒")]
        public static CommandResult Mode(EngineModeArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments?.RepositoryRoot, ".", "RepositoryRoot", "仓库根", out var repositoryFailure);
            if (repositoryFailure.Length > 0)
            {
                return CommandResult.Failure(repositoryFailure);
            }

            var modeValue = arguments?.Mode;
            if (string.IsNullOrWhiteSpace(modeValue))
            {
                var settings = EngineSettings.Load(repositoryRoot);
                var lines = new List<string>
                {
                    $"轮询间隔：{settings.PollIntervalSeconds} 秒",
                    $"重试上限：{settings.RetryLimit}"
                };
                if (settings.LoadFailureReason.Length > 0)
                {
                    lines.Add($"配置加载失败：{settings.LoadFailureReason}");
                }

                return CommandResult.Success($"当前引擎模式：{EngineSettings.ToChineseName(settings.Mode)}", lines);
            }

            if (!EngineSettings.TryParseMode(modeValue, out var targetMode))
            {
                return CommandResult.Failure($"不认识的引擎模式「{modeValue}」，可用的是：值守、轮询、唤醒");
            }

            try
            {
                var current = EngineSettings.Load(repositoryRoot);
                var updated = current.WithMode(targetMode);
                EngineSettings.Save(repositoryRoot, updated);

                return CommandResult.Success(
                    $"引擎模式已切换：{EngineSettings.ToChineseName(current.Mode)} → {EngineSettings.ToChineseName(targetMode)}",
                    new[] { $"配置文件：{EngineSettings.SettingsFile(repositoryRoot)}" });
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommandResult.Failure($"引擎模式切换失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 查看引擎模式与执行队列，以及按当前模式能否自动派活；只读命令，不写任何文件。
        /// </summary>
        /// <param name="arguments">引擎队列命令参数。</param>
        [EditorCommand("engine.queue")]
        [Summary("查看引擎模式、执行队列与能否自动派活")]
        public static CommandResult Queue(EngineQueueArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments?.RepositoryRoot, ".", "RepositoryRoot", "仓库根", out var repositoryFailure);
            if (repositoryFailure.Length > 0)
            {
                return CommandResult.Failure(repositoryFailure);
            }

            var poolRoot = ResolveRoot(arguments?.PoolRoot, "Pools", "PoolRoot", "池子根", out var poolFailure);
            if (poolFailure.Length > 0)
            {
                return CommandResult.Failure(poolFailure);
            }

            var settings = EngineSettings.Load(repositoryRoot);
            var queue = ExecutionQueue.Load(poolRoot);

            var lines = new List<string>
            {
                $"引擎模式：{EngineSettings.ToChineseName(settings.Mode)}",
                $"队列条数：{queue.Entries.Count}"
            };

            var sequence = 1;
            foreach (var entry in queue.Entries)
            {
                lines.Add($"{sequence}. {entry.RequirementIdentifier}　入队 {entry.EnqueueTime}　理由：{entry.Reason}");
                sequence++;
            }

            // 只读命令绝不能把队首取走：TryTakeNext 只用来拿 reason 判断能不能自动派活。
            // 传一份新 Load 出来的队列对象，且调用之后一律不 Save，磁盘上的队列文件不会被改动。
            var probeQueue = ExecutionQueue.Load(poolRoot);
            var canTake = EngineDispatchRule.TryTakeNext(settings, probeQueue, out _, out var dispatchReason);
            lines.Add($"自动派活：{(canTake ? "可以" : "不可以")}（{dispatchReason}）");

            return CommandResult.Success("引擎队列", lines);
        }

        /// <summary>
        /// 跑一轮专项认领入站：扫专项收件箱，把下游同步来的认领字段写进专项文件。
        /// 有拒收判命令失败并逐条列出；全部通过则报处理与跳过条数。
        /// </summary>
        /// <param name="arguments">专项认领入站命令参数。</param>
        [EditorCommand("pool.claimpull")]
        [Summary("专项认领入站：从专项收件箱同步认领字段")]
        public static CommandResult ClaimPull(PoolClaimPullArguments arguments)
        {
            var poolRoot = ResolveRoot(arguments?.PoolRoot, "Pools", "PoolRoot", "池子根", out var poolFailure);
            if (poolFailure.Length > 0)
            {
                return CommandResult.Failure(poolFailure);
            }

            EpicClaimIntakeReport report;
            try
            {
                report = EpicClaimIntake.Process(poolRoot);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"专项认领入站失败：{exception.Message}");
            }

            var lines = report.Rejections.Select(rejection => rejection.ToDisplayText()).ToList();
            foreach (var finding in report.Findings)
            {
                lines.Add($"注意：{finding.ToDisplayText()}");
            }

            if (report.Rejections.Count > 0)
            {
                return CommandResult.Failure(
                    $"专项认领入站完成：处理 {report.ProcessedCount} 条、跳过 {report.SkippedCount} 条、拒收 {report.Rejections.Count} 条",
                    lines);
            }

            return CommandResult.Success(
                $"专项认领入站完成：处理 {report.ProcessedCount} 条、跳过 {report.SkippedCount} 条（幂等）",
                lines);
        }

        /// <summary>
        /// 显式或隐式记一次专项认领：显式可跨默认职责，隐式仅限默认职责内且该职责须无人。
        /// 没写不算失败——「已认领过」「该职责已有人」都是正常结果，文案里说清没写与原因。
        /// </summary>
        /// <param name="arguments">专项认领写盘命令参数。</param>
        [EditorCommand("pool.claim")]
        [Summary("专项认领：显式或隐式记一次认领")]
        public static CommandResult Claim(PoolClaimArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.EpicIdentifier))
            {
                return CommandResult.Failure("参数 EpicIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.Duty))
            {
                return CommandResult.Failure("参数 Duty 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.OpenIdentifier))
            {
                return CommandResult.Failure("参数 OpenIdentifier 为必填项");
            }

            var poolRoot = ResolveRoot(arguments.PoolRoot, "Pools", "PoolRoot", "池子根", out var poolFailure);
            if (poolFailure.Length > 0)
            {
                return CommandResult.Failure(poolFailure);
            }

            ClaimWriteResult writeResult;
            try
            {
                writeResult = arguments.IsImplicit
                    ? EpicClaimWriter.RecordImplicitClaim(poolRoot, arguments.EpicIdentifier, arguments.Duty, arguments.OpenIdentifier)
                    : EpicClaimWriter.RecordExplicitClaim(poolRoot, arguments.EpicIdentifier, arguments.Duty, arguments.OpenIdentifier);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"专项认领写盘失败：{exception.Message}");
            }

            var mode = arguments.IsImplicit ? "隐式" : "显式";
            if (writeResult.Written)
            {
                return CommandResult.Success(
                    $"{mode}认领已写入：{arguments.EpicIdentifier} 职责 {arguments.Duty}",
                    new[] { writeResult.Reason });
            }

            return CommandResult.Success(
                $"{mode}认领未写入（正常结果）：{writeResult.Reason}",
                new[] { writeResult.Reason });
        }

        /// <summary>
        /// 列出冲突列表：全部或只看未销账；空列表是正常状态不判失败，未销账数末尾一行。
        /// </summary>
        /// <param name="arguments">冲突列表命令参数。</param>
        [EditorCommand("conflict.list")]
        [Summary("冲突列表：列出全部冲突与未销账数")]
        public static CommandResult ListConflicts(ConflictListArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            ConflictList list;
            try
            {
                list = ConflictList.Load(poolRoot);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"冲突列表加载失败：{exception.Message}");
            }

            var lines = new List<string>();
            foreach (var entry in list.Entries)
            {
                if (arguments.OnlyPending && !IsPendingConflict(entry))
                {
                    continue;
                }

                var choice = entry.Choice.Length > 0 ? entry.Choice : "—";
                lines.Add($"{entry.Identifier}　旧 {entry.OldIdentifier}　新 {entry.NewIdentifier}　{entry.State}　选择 {choice}");
            }

            lines.Add($"未销账 {list.PendingCount()} 条");
            if (list.LoadFailureReason.Length > 0)
            {
                lines.Add($"注意：{list.LoadFailureReason}");
            }

            if (list.Entries.Count == 0)
            {
                return CommandResult.Success("冲突列表为空", lines);
            }

            return CommandResult.Success($"冲突 {list.Entries.Count} 条", lines);
        }

        /// <summary>
        /// 冲突裁决：三选一；裁决失败是真失败——id 打错、选项打错、重复裁决都要让人立刻看见。
        /// </summary>
        /// <param name="arguments">冲突裁决命令参数。</param>
        [EditorCommand("conflict.resolve")]
        [Summary("冲突裁决：改新的 / 改旧的 / 强制推送 三选一")]
        public static CommandResult ResolveConflict(ConflictResolveArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.ConflictIdentifier))
            {
                return CommandResult.Failure("参数 ConflictIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.ResolverName))
            {
                return CommandResult.Failure("参数 ResolverName 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.Choice))
            {
                return CommandResult.Failure("参数 Choice 为必填项");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            if (!Directory.Exists(poolRoot))
            {
                return CommandResult.Failure($"位置：{poolRoot}；原因：池子根目录不存在；修复：把 PoolRoot 指向池子根");
            }

            ConflictResolutionResult result;
            try
            {
                result = ConflictList.Resolve(
                    poolRoot,
                    arguments.ConflictIdentifier,
                    arguments.ResolverName,
                    arguments.Choice,
                    DateTimeOffset.Now.ToString("o"));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"冲突裁决失败：{exception.Message}");
            }

            if (!result.IsResolved)
            {
                return CommandResult.Failure(result.Reason);
            }

            var lines = new List<string>
            {
                $"{result.Entry.Identifier} 已裁决：{result.Entry.Choice}"
            };
            foreach (var action in result.SystemActions)
            {
                lines.Add($"动作：{action}");
            }

            return CommandResult.Success($"冲突 {result.Entry.Identifier} 裁决完成", lines);
        }

        /// <summary>
        /// 打断重规划：算脏项、净项与要问人的地方。重规划算完不算失败——它是一份计划，不是判决。
        /// </summary>
        /// <param name="arguments">打断重规划命令参数。</param>
        [EditorCommand("task.replan")]
        [Summary("打断重规划：算脏项、净项与要问人的地方")]
        public static CommandResult Replan(TaskReplanArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.RequirementIdentifier))
            {
                return CommandResult.Failure("参数 RequirementIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.ChangedFieldsText))
            {
                return CommandResult.Failure("参数 ChangedFieldsText 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            var graph = WorkItemGraph.Load(repositoryRoot, arguments.RequirementIdentifier);
            var changedFields = SplitChangedPaths(arguments.ChangedFieldsText);
            var result = ReplanPlanner.Plan(graph, changedFields, null);

            var lines = new List<string>();
            if (result.MustAskHuman)
            {
                lines.Add("** 停下问人 **：有「人改权威」文件落在脏集内，先问人再重跑");
            }

            lines.Add($"脏项（{result.PropagatedDirty.Count}）：{JoinOrNone(result.PropagatedDirty)}");
            lines.Add($"净项（{result.Clean.Count}）：{JoinOrNone(result.Clean)}");
            lines.Add($"要后端评估（{result.NeedsBackendEvaluation.Count}）：{JoinOrNone(result.NeedsBackendEvaluation)}");
            lines.Add($"要问人的（{result.AuthoritativeFilesInDirtySet.Count}）：{JoinOrNone(result.AuthoritativeFilesInDirtySet)}");
            foreach (var finding in result.Findings)
            {
                lines.Add($"注意：{finding}");
            }

            if (graph.LoadFailureReason.Length > 0)
            {
                lines.Add($"注意：{graph.LoadFailureReason}");
            }

            return CommandResult.Success("重规划完成（计划，不是判决）", lines);
        }

        /// <summary>该条目是否算未销账：状态=未决 或 选择=强制推送。</summary>
        private static bool IsPendingConflict(ConflictEntry entry)
        {
            return string.Equals(entry.State, ConflictEntry.PendingState, StringComparison.Ordinal)
                || string.Equals(entry.Choice, "强制推送", StringComparison.Ordinal);
        }

        /// <summary>列表拼成顿号分隔的中文串；空列表给「无」。</summary>
        private static string JoinOrNone(IReadOnlyList<string> identifiers)
        {
            return identifiers.Count == 0 ? "无" : string.Join("、", identifiers);
        }

        /// <summary>
        /// 风险分级：读放行策略目录取高危范围，按改动范围与规模给风险级。
        /// </summary>
        /// <param name="arguments">风险分级命令参数。</param>
        [EditorCommand("task.risk")]
        [Summary("风险分级：按改动范围与规模给风险级")]
        public static CommandResult Risk(TaskRiskArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.ChangedPathsText))
            {
                return CommandResult.Failure("参数 ChangedPathsText 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            var catalog = ReleasePolicyCatalog.Load(repositoryRoot, arguments.ModuleName);
            var changedPaths = SplitChangedPaths(arguments.ChangedPathsText);
            var risk = RiskGrader.Grade(
                changedPaths,
                arguments.ChangedLineCount,
                arguments.BlockingFindingCount,
                arguments.SuggestionFindingCount,
                catalog.HighRiskScopes);

            var lines = new List<string>
            {
                $"风险级：{risk.Grade}",
                $"范围：{(risk.Scopes.Count == 0 ? "无" : string.Join("、", risk.Scopes))}",
                $"理由：{risk.Reason}"
            };

            foreach (var finding in catalog.Findings)
            {
                lines.Add($"注意：{finding.ToDisplayText()}");
            }

            return CommandResult.Success("风险分级完成", lines);
        }

        /// <summary>
        /// 放行判定：四条判据全绿才自动放行；「要人审」是正常结论不是失败，无论放不放行都是 Success。
        /// </summary>
        /// <param name="arguments">放行判定命令参数。</param>
        [EditorCommand("task.release")]
        [Summary("放行判定：四条判据全绿才自动放行")]
        public static CommandResult Release(TaskReleaseArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.ChangedPathsText))
            {
                return CommandResult.Failure("参数 ChangedPathsText 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            var catalog = ReleasePolicyCatalog.Load(repositoryRoot, arguments.ModuleName);
            var changedPaths = SplitChangedPaths(arguments.ChangedPathsText);
            var risk = RiskGrader.Grade(
                changedPaths,
                arguments.ChangedLineCount,
                arguments.BlockingFindingCount,
                arguments.SuggestionFindingCount,
                catalog.HighRiskScopes);
            var decision = ReleaseDecider.Decide(
                catalog,
                risk,
                arguments.AllGatesGreen,
                arguments.BlockingFindingCount,
                arguments.SuggestionFindingCount);

            var lines = new List<string>
            {
                $"风险级：{risk.Grade}",
                $"范围：{(risk.Scopes.Count == 0 ? "无" : string.Join("、", risk.Scopes))}",
                $"放行结论：{(decision.IsAutomatic ? "自动放行" : "人审")}"
            };

            foreach (var reason in decision.Reasons)
            {
                lines.Add($"不满足：{reason}");
            }

            foreach (var finding in catalog.Findings)
            {
                lines.Add($"注意：{finding.ToDisplayText()}");
            }

            return CommandResult.Success("放行判定完成", lines);
        }

        /// <summary>
        /// 引擎一轮：按模式判定该不该取活。先拿单实例锁，拿不到不是失败——那正是单实例该有的行为。
        /// </summary>
        /// <param name="arguments">引擎一轮命令参数。</param>
        [EditorCommand("engine.tick")]
        [Summary("引擎一轮：按模式判定该不该取活")]
        public static CommandResult Tick(EngineTickArguments arguments)
        {
            return RunEngineTick(arguments, false);
        }

        /// <summary>
        /// 引擎唤醒：提前跑一轮，判定逻辑与轮询同一条，只跳过间隔检查（子文档 03 §五，防漏）。
        /// </summary>
        /// <param name="arguments">引擎唤醒命令参数。</param>
        [EditorCommand("engine.wake")]
        [Summary("引擎唤醒：提前跑一轮")]
        public static CommandResult Wake(EngineTickArguments arguments)
        {
            return RunEngineTick(arguments, true);
        }

        // engine.tick 与 engine.wake 共用：拿单实例锁 + 加载配置与队列 + 跑一轮取活判定。
        private static CommandResult RunEngineTick(EngineTickArguments arguments, bool isWakeUp)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"仓库根目录不存在：{repositoryRoot}");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            if (!Directory.Exists(poolRoot))
            {
                return CommandResult.Failure($"池子根目录不存在：{poolRoot}");
            }

            if (!SingleInstanceLock.TryAcquire(repositoryRoot, out var instanceLock, out var lockReason))
            {
                return CommandResult.Success("本轮跳过（单实例锁被占，不是失败）", new[] { lockReason });
            }

            using (instanceLock)
            {
                if (!TryParseLastTickMoment(arguments.LastTickMoment, out var lastTickMoment, out var parseFailure))
                {
                    return CommandResult.Failure(parseFailure);
                }

                var settings = EngineSettings.Load(repositoryRoot);
                var queue = ExecutionQueue.Load(poolRoot);
                var decision = PollingScheduler.Tick(settings, queue, DateTimeOffset.Now, lastTickMoment, isWakeUp);

                var lines = new List<string>
                {
                    $"引擎模式：{EngineSettings.ToChineseName(settings.Mode)}",
                    $"该不该取活：{(decision.ShouldTake ? "取" : "不取")}",
                    $"原因：{decision.Reason}",
                    $"下轮建议：{decision.NextTickSeconds} 秒后再来"
                };
                if (decision.Entry != null)
                {
                    lines.Add($"取到的条目：{decision.Entry.RequirementIdentifier}（入队 {decision.Entry.EnqueueTime}）");
                }

                if (instanceLock.ReleaseFailureReason.Length > 0)
                {
                    lines.Add($"注意：{instanceLock.ReleaseFailureReason}");
                }

                return CommandResult.Success($"引擎{(isWakeUp ? "唤醒" : "一轮")}完成", lines);
            }
        }

        // 把 ISO 8601 文本解析成取活时刻；空白按从未取过（MinValue），解析不了给失败原因。
        private static bool TryParseLastTickMoment(string text, out DateTimeOffset moment, out string failure)
        {
            failure = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                moment = DateTimeOffset.MinValue;
                return true;
            }

            if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out moment))
            {
                return true;
            }

            failure = $"参数 LastTickMoment「{text}」不是合法 ISO 8601 时间";
            return false;
        }

        /// <summary>
        /// 意见库追加一条终审打回意见；可规则化性非法时转成失败，文案列出三个合法值。
        /// </summary>
        /// <param name="arguments">意见库追加命令参数。</param>
        [EditorCommand("task.opinion")]
        [Summary("意见库：追加一条终审打回意见")]
        public static CommandResult Opinion(TaskOpinionArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.Category))
            {
                return CommandResult.Failure("参数 Category 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.ModuleName))
            {
                return CommandResult.Failure("参数 ModuleName 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.Rulability))
            {
                return CommandResult.Failure("参数 Rulability 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.Quotation))
            {
                return CommandResult.Failure("参数 Quotation 为必填项");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            if (!Directory.Exists(poolRoot))
            {
                return CommandResult.Failure($"池子根目录不存在：{poolRoot}");
            }

            try
            {
                var opinion = ReviewOpinionBook.Append(
                    poolRoot,
                    arguments.Category,
                    arguments.ModuleName,
                    arguments.Rulability,
                    arguments.Quotation,
                    DateTimeOffset.Now.ToString("o"));
                return CommandResult.Success($"意见已入库：{opinion.Identifier}", new[]
                {
                    $"问题类别：{opinion.Category}",
                    $"模块：{opinion.ModuleName}",
                    $"可规则化性：{opinion.Rulability}",
                    $"原文引用：{opinion.Quotation}"
                });
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommandResult.Failure($"意见入库失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 放行入账：把一次自动放行的合并记进放行流水。只追加，不改已有条目。
        /// </summary>
        /// <param name="arguments">放行入账命令参数。</param>
        [EditorCommand("task.release.record")]
        [Summary("放行入账：把一次自动放行记进放行流水")]
        public static CommandResult ReleaseRecord(TaskReleaseRecordArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.RequirementIdentifier))
            {
                return CommandResult.Failure("参数 RequirementIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.Grade))
            {
                return CommandResult.Failure("参数 Grade 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.Scopes))
            {
                return CommandResult.Failure("参数 Scopes 为必填项");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            if (!Directory.Exists(poolRoot))
            {
                return CommandResult.Failure($"位置：{poolRoot}；原因：池子根目录不存在；修复：把 PoolRoot 指向池子根");
            }

            var scopes = arguments.Scopes
                .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(scope => scope.Trim())
                .Where(scope => scope.Length > 0)
                .ToList();
            var moment = string.IsNullOrWhiteSpace(arguments.ReleasedMoment)
                ? DateTimeOffset.Now.ToString("o")
                : arguments.ReleasedMoment;

            try
            {
                var entry = ReleaseLedger.Append(
                    poolRoot,
                    arguments.RequirementIdentifier,
                    arguments.Grade,
                    scopes,
                    moment,
                    arguments.MergeCommit ?? "");
                return CommandResult.Success(
                    $"放行已入账：{entry.Identifier}",
                    new[]
                    {
                        $"需求：{entry.RequirementIdentifier}",
                        $"风险级：{entry.Grade}",
                        $"范围：{(entry.Scopes.Count == 0 ? "无" : string.Join("、", entry.Scopes))}",
                        $"抽查状态：{entry.SpotCheckState}"
                    });
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"放行入账失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 放行流水：列出全部自动放行记录与本轮抽查建议。流水读不动时是失败，
        /// 不许当成「零条」报——读不动的账本和空账本是两回事。
        /// </summary>
        /// <param name="arguments">放行流水查看命令参数。</param>
        [EditorCommand("task.ledger")]
        [Summary("放行流水：列出自动放行记录与本轮抽查建议")]
        public static CommandResult Ledger(TaskLedgerArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            if (!Directory.Exists(poolRoot))
            {
                return CommandResult.Failure($"位置：{poolRoot}；原因：池子根目录不存在；修复：把 PoolRoot 指向池子根");
            }

            try
            {
                var ledger = ReleaseLedger.Load(poolRoot);
                if (ledger.LoadFailureReason.Length > 0)
                {
                    return CommandResult.Failure($"放行流水读不动，拒绝当零条报：{ledger.LoadFailureReason}");
                }

                var catalog = ReleasePolicyCatalog.Load(repositoryRoot, arguments.ModuleName ?? "");
                var ratio = arguments.Ratio < 0.0 ? catalog.SpotCheckRatio : arguments.Ratio;
                var suggestions = SpotCheckSelector.Select(ledger, ratio);

                var lines = new List<string>
                {
                    $"放行流水 {ledger.Entries.Count} 条，未抽查 {ledger.UncheckedCount()} 条，发现问题 {ledger.ProblemCount()} 条"
                };

                foreach (var entry in ledger.Entries)
                {
                    lines.Add(
                        $"{entry.Identifier}　需求 {entry.RequirementIdentifier}　{entry.Grade}　"
                        + $"范围 {(entry.Scopes.Count == 0 ? "无" : string.Join("、", entry.Scopes))}　{entry.SpotCheckState}");
                }

                lines.Add($"本轮抽查建议（{ratio:0.##} 比例）：{suggestions.Count} 条");
                foreach (var entry in suggestions)
                {
                    lines.Add(
                        $"{entry.Identifier}　需求 {entry.RequirementIdentifier}　{entry.Grade}　"
                        + $"范围 {(entry.Scopes.Count == 0 ? "无" : string.Join("、", entry.Scopes))}");
                }

                return CommandResult.Success($"放行流水 {ledger.Entries.Count} 条", lines);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"放行流水读取失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 抽查销账：记抽查结论，发现问题就回落策略并记意见库。
        /// 顺序钉死：先记结论，合格到此为止；发现问题再做 revert 计划、策略回落、记意见库三件事。
        /// 一行 git 都不跑——revert 只出计划文案，不真起子进程。
        /// </summary>
        /// <param name="arguments">抽查销账命令参数。</param>
        [EditorCommand("task.spotcheck")]
        [Summary("抽查销账：记结论，发现问题就回落策略并记意见库")]
        public static CommandResult SpotCheck(TaskSpotCheckArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            if (arguments == null || string.IsNullOrWhiteSpace(arguments.LedgerIdentifier))
            {
                return CommandResult.Failure("参数 LedgerIdentifier 为必填项");
            }

            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Conclusion))
            {
                return CommandResult.Failure("参数 Conclusion 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            if (!Directory.Exists(poolRoot))
            {
                return CommandResult.Failure($"位置：{poolRoot}；原因：池子根目录不存在；修复：把 PoolRoot 指向池子根");
            }

            try
            {
                // 1. 找到那一条。
                var ledger = ReleaseLedger.Load(poolRoot);
                ReleaseLedgerEntry target = null;
                foreach (var entry in ledger.Entries)
                {
                    if (string.Equals(entry.Identifier, arguments.LedgerIdentifier, StringComparison.Ordinal))
                    {
                        target = entry;
                        break;
                    }
                }

                if (target == null)
                {
                    return CommandResult.Failure($"流水条目 {arguments.LedgerIdentifier} 不存在");
                }

                // 2. 记结论。
                if (!ReleaseLedger.RecordSpotCheck(
                    poolRoot,
                    arguments.LedgerIdentifier,
                    arguments.Conclusion,
                    arguments.ConclusionText ?? "",
                    arguments.RevertCommit ?? "",
                    out var recordReason))
                {
                    return CommandResult.Failure($"抽查销账失败：{recordReason}");
                }

                // 3. 合格：到此为止。
                if (string.Equals(arguments.Conclusion, "合格", StringComparison.Ordinal))
                {
                    return CommandResult.Success(
                        $"抽查合格，策略不动",
                        new[] { $"流水条目 {arguments.LedgerIdentifier} 已记为合格" });
                }

                // 4. 发现问题：revert 计划 + 策略回落 + 记意见库，每件的结果都要出现。
                var lines = new List<string> { $"流水条目 {arguments.LedgerIdentifier} 记为发现问题" };

                // 4a. revert 计划：只出计划，不起 git 子进程。
                if (target.MergeCommit.Length > 0)
                {
                    lines.Add($"revert 计划：合并提交 {target.MergeCommit} 需要被 revert，生成回滚 PR 后人工确认再合");
                }
                else
                {
                    lines.Add("revert 计划：这条流水没记合并提交，revert 目标要人工确认");
                }

                // 4b. 策略回落：grade 与 scopes 取自这条流水条目本身。
                var catalog = ReleasePolicyCatalog.Load(repositoryRoot, arguments.ModuleName ?? "");
                var plan = PolicyFallbackPlanner.Plan(catalog, target.Grade, target.Scopes);
                var appliedKeys = PolicyFallbackPlanner.Apply(repositoryRoot, plan);
                if (appliedKeys.Count > 0)
                {
                    lines.Add($"策略回落：{string.Join("、", appliedKeys)} 已改为人审");
                }
                else
                {
                    lines.Add("策略回落：没有需要改的键");
                }

                if (plan.AlreadyManualKeys.Count > 0)
                {
                    lines.Add($"本来就是人审：{string.Join("、", plan.AlreadyManualKeys)}");
                }

                // 4c. 记意见库。
                var moduleName = string.IsNullOrWhiteSpace(arguments.ModuleName) ? "未指定" : arguments.ModuleName;
                var quotation = string.IsNullOrWhiteSpace(arguments.ConclusionText) ? target.Identifier : arguments.ConclusionText;
                var opinion = ReviewOpinionBook.Append(
                    poolRoot,
                    "抽查发现问题",
                    moduleName,
                    string.IsNullOrWhiteSpace(arguments.Rulability) ? "不可规则化" : arguments.Rulability,
                    quotation,
                    DateTimeOffset.Now.ToString("o"));
                lines.Add($"意见库：{opinion.Identifier} 已记");

                return CommandResult.Success($"抽查销账完成：{arguments.LedgerIdentifier}", lines);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"抽查销账失败：{exception.Message}");
            }
        }

        // 按换行分隔的改动路径文本拆成路径列表：去掉空行与首尾空白。
        private static IReadOnlyList<string> SplitChangedPaths(string text)
        {
            return text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
        }

        // 五条命令共用的根目录解析：空白取默认值，转绝对路径，目录不存在即失败。
        // 成功时返回绝对路径、failureMessage 为空串；失败时返回 null、failureMessage 为中文原因。
        private static string ResolveRoot(string value, string fallback, string parameterName, string displayName, out string failureMessage)
        {
            failureMessage = "";
            var root = string.IsNullOrWhiteSpace(value) ? fallback : value;

            string absoluteRoot;
            try
            {
                absoluteRoot = Path.GetFullPath(root);
            }
            catch (Exception exception)
            {
                failureMessage = $"参数 {parameterName} 无法解析为绝对路径：{exception.Message}";
                return null;
            }

            if (!Directory.Exists(absoluteRoot))
            {
                failureMessage = $"{displayName}目录不存在：{absoluteRoot}";
                return null;
            }

            return absoluteRoot;
        }

        // 一组入站结果转命令结果：逐行输出 + 汇总行；有 Unreadable 才判命令失败，拒收是正常业务结论。
        private static CommandResult ToPullResult(IReadOnlyList<IntakeOutcome> outcomes)
        {
            var lines = outcomes.Select(outcome => outcome.ToDisplayText()).ToList();
            lines.Add(ComposeIntakeSummary(outcomes));

            var unreadableCount = outcomes.Count(outcome => outcome.Decision == IntakeDecision.Unreadable);
            return unreadableCount == 0
                ? CommandResult.Success("入站完成", lines)
                : CommandResult.Failure($"入站完成，但有 {unreadableCount} 条信封无法解析", lines);
        }

        // 入站汇总行：六种决策各计一条数。
        private static string ComposeIntakeSummary(IReadOnlyList<IntakeOutcome> outcomes)
        {
            return $"汇总：入池 {outcomes.Count(outcome => outcome.Decision == IntakeDecision.Accepted)} 条，"
                + $"更新 {outcomes.Count(outcome => outcome.Decision == IntakeDecision.Updated)} 条，"
                + $"跳过 {outcomes.Count(outcome => outcome.Decision == IntakeDecision.Skipped)} 条，"
                + $"拒收 {outcomes.Count(outcome => outcome.Decision == IntakeDecision.Rejected)} 条，"
                + $"转为变更请求 {outcomes.Count(outcome => outcome.Decision == IntakeDecision.Diverted)} 条，"
                + $"无法解析 {outcomes.Count(outcome => outcome.Decision == IntakeDecision.Unreadable)} 条";
        }

        /// <summary>
        /// 晋升提案：列出账本，或把意见库攒够阈值的意见入库成提案。
        /// 列出时账本读不动是真失败（锁定决策 42）；入库时被跳过的提案逐条报出原因（锁定决策 46）。
        /// </summary>
        /// <param name="arguments">晋升提案命令参数。</param>
        [EditorCommand("task.promotion")]
        [Summary("晋升提案：列出账本，或把够阈值的意见入库成提案")]
        public static CommandResult Promotion(TaskPromotionArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            if (!Directory.Exists(poolRoot))
            {
                return CommandResult.Failure($"位置：{poolRoot}；原因：池子根目录不存在；修复：把 PoolRoot 指向池子根");
            }

            var action = string.IsNullOrWhiteSpace(arguments.Action) ? "列出" : arguments.Action;
            if (string.Equals(action, "列出", StringComparison.Ordinal))
            {
                return ListPromotions(poolRoot);
            }

            if (string.Equals(action, "入库", StringComparison.Ordinal))
            {
                return PromoteFromOpinions(poolRoot, arguments.Threshold, arguments.ProposedMoment);
            }

            return CommandResult.Failure($"动作「{action}」不合法；合法值是：列出、入库");
        }

        /// <summary>列出晋升账本；账本读不动返回 Failure（把没查的说成查过是决策 42 的另一种长相）。</summary>
        private static CommandResult ListPromotions(string poolRoot)
        {
            PromotionLedger ledger;
            try
            {
                ledger = PromotionLedger.Load(poolRoot);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"晋升账本加载失败：{exception.Message}");
            }

            if (ledger.LoadFailureReason.Length > 0)
            {
                return CommandResult.Failure($"晋升账本读不动：{ledger.LoadFailureReason}");
            }

            var lines = new List<string>();
            foreach (var record in ledger.Records)
            {
                var decider = record.DeciderName.Length > 0 ? record.DeciderName : "—";
                lines.Add($"{record.Identifier}　{record.Category}　{record.TargetChannel}　{record.State}　裁决人 {decider}");
            }

            return CommandResult.Success($"提案 {ledger.Records.Count} 条，未关闭 {ledger.OpenCount()} 条", lines);
        }

        /// <summary>把意见库里攒够阈值的意见入库成提案；被跳过的逐条列出原因，不静默吞掉。</summary>
        private static CommandResult PromoteFromOpinions(string poolRoot, int threshold, string proposedMoment)
        {
            ReviewOpinionBook book;
            try
            {
                book = ReviewOpinionBook.Load(poolRoot);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"意见库加载失败：{exception.Message}");
            }

            if (book.LoadFailureReason.Length > 0)
            {
                return CommandResult.Failure($"意见库读不动，先修好再入库：{book.LoadFailureReason}");
            }

            var proposals = PromotionProposalBuilder.Build(book, threshold);
            var moment = string.IsNullOrWhiteSpace(proposedMoment) ? DateTimeOffset.Now.ToString("o") : proposedMoment;

            var entered = new List<PromotionRecord>();
            var skipped = new List<string>();
            foreach (var proposal in proposals)
            {
                var record = PromotionLedger.Append(poolRoot, proposal, moment, out var reason);
                if (record != null)
                {
                    entered.Add(record);
                }
                else
                {
                    skipped.Add($"提案「{proposal.Category}」跳过：{reason}");
                }
            }

            var lines = new List<string> { $"入库 {entered.Count} 条：" };
            foreach (var record in entered)
            {
                lines.Add($"{record.Identifier}　{record.Category}");
            }

            lines.Add($"跳过 {skipped.Count} 条：");
            lines.AddRange(skipped);

            return CommandResult.Success($"入库 {entered.Count} 条，跳过 {skipped.Count} 条", lines);
        }

        /// <summary>
        /// 晋升裁决：批准 / 拒绝 / 落地一条提案。落地先产产物再改状态——
        /// 产物写成了但状态没跟上，比状态先跳过去而产物没写要好查得多。
        /// </summary>
        /// <param name="arguments">晋升裁决命令参数。</param>
        [EditorCommand("task.promotion.decide")]
        [Summary("晋升裁决：批准 / 拒绝 / 落地一条提案")]
        public static CommandResult DecidePromotion(TaskPromotionDecideArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            if (arguments == null || string.IsNullOrWhiteSpace(arguments.ProposalIdentifier))
            {
                return CommandResult.Failure("参数 ProposalIdentifier 为必填项");
            }

            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Action))
            {
                return CommandResult.Failure("参数 Action 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            if (!Directory.Exists(poolRoot))
            {
                return CommandResult.Failure($"位置：{poolRoot}；原因：池子根目录不存在；修复：把 PoolRoot 指向池子根");
            }

            var identifier = arguments.ProposalIdentifier;
            var moment = string.IsNullOrWhiteSpace(arguments.DecidedMoment)
                ? DateTimeOffset.Now.ToString("o")
                : arguments.DecidedMoment;

            if (string.Equals(arguments.Action, "批准", StringComparison.Ordinal))
            {
                return DecideApprove(poolRoot, identifier, arguments.DeciderName, moment);
            }

            if (string.Equals(arguments.Action, "拒绝", StringComparison.Ordinal))
            {
                return DecideReject(poolRoot, identifier, arguments.DeciderName, moment);
            }

            if (string.Equals(arguments.Action, "落地", StringComparison.Ordinal))
            {
                return DecideLand(repositoryRoot, poolRoot, identifier);
            }

            return CommandResult.Failure($"动作「{arguments.Action}」不合法；合法值是：批准、拒绝、落地");
        }

        /// <summary>批准一条提案；成功后提醒下一步跑落地。</summary>
        private static CommandResult DecideApprove(string poolRoot, string identifier, string deciderName, string moment)
        {
            if (string.IsNullOrWhiteSpace(deciderName))
            {
                return CommandResult.Failure("参数 DeciderName 为必填项（批准 / 拒绝时）");
            }

            try
            {
                var ok = PromotionLedger.UpdateState(poolRoot, identifier, "已批准", deciderName, moment, "", out var reason);
                if (!ok)
                {
                    return CommandResult.Failure(reason);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"批准失败：{exception.Message}");
            }

            return CommandResult.Success($"提案 {identifier} 已批准", new[]
            {
                "下一步：跑 task.promotion.decide 落地 把产物真的写出来"
            });
        }

        /// <summary>拒绝一条提案。</summary>
        private static CommandResult DecideReject(string poolRoot, string identifier, string deciderName, string moment)
        {
            if (string.IsNullOrWhiteSpace(deciderName))
            {
                return CommandResult.Failure("参数 DeciderName 为必填项（批准 / 拒绝时）");
            }

            try
            {
                var ok = PromotionLedger.UpdateState(poolRoot, identifier, "已拒绝", deciderName, moment, "", out var reason);
                if (!ok)
                {
                    return CommandResult.Failure(reason);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"拒绝失败：{exception.Message}");
            }

            return CommandResult.Success($"提案 {identifier} 已拒绝");
        }

        /// <summary>落地一条提案：先产产物，成功后再改状态；产物写出但状态没跟上时明确说出来。</summary>
        private static CommandResult DecideLand(string repositoryRoot, string poolRoot, string identifier)
        {
            PromotionLedger ledger;
            try
            {
                ledger = PromotionLedger.Load(poolRoot);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"晋升账本加载失败：{exception.Message}");
            }

            if (ledger.LoadFailureReason.Length > 0)
            {
                // 账本读不动与账本空必须分开（决策 42）：坏文件里的提案是查不到的，
                // 不能把「文件损坏」误报成「提案不存在」。
                return CommandResult.Failure($"晋升账本读不动，先修好再落地：{ledger.LoadFailureReason}");
            }

            PromotionRecord record = null;
            foreach (var candidate in ledger.Records)
            {
                if (string.Equals(candidate.Identifier, identifier, StringComparison.Ordinal))
                {
                    record = candidate;
                    break;
                }
            }

            if (record == null)
            {
                return CommandResult.Failure($"提案 {identifier} 不存在");
            }

            PromotionLandingResult landing;
            try
            {
                landing = PromotionLandingPlanner.Land(repositoryRoot, record);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"落地失败：{exception.Message}");
            }

            if (!landing.Succeeded)
            {
                return CommandResult.Failure($"落地失败：{landing.Reason}");
            }

            try
            {
                var ok = PromotionLedger.UpdateState(poolRoot, identifier, "已落地", "", "", landing.ArtifactPath, out var reason);
                if (!ok)
                {
                    return CommandResult.Failure($"产物已经写出去了但状态没跟上：{landing.ArtifactPath}；{reason}");
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"产物已经写出去了但状态没跟上：{landing.ArtifactPath}；{exception.Message}");
            }

            return CommandResult.Success($"提案 {identifier} 已落地", new[] { $"产物：{landing.ArtifactPath}" });
        }
    }
}
