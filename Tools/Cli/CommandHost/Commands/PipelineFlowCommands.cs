using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
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

    /// <summary>AI 对抗预审命令 task.prereview 的参数。</summary>
    public sealed class TaskPreReviewArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>需求 id，形如 REQ-0042；预审报告落 _Tasks/&lt;id&gt;/预审报告.json。</summary>
        [Summary("需求 id，形如 REQ-0042；预审报告落 _Tasks/<id>/预审报告.json")]
        public string RequirementIdentifier { get; set; }

        /// <summary>变更 diff 的文件路径，内容作为预审输入。</summary>
        [Summary("变更 diff 的文件路径，内容作为预审输入")]
        public string DiffPath { get; set; }

        /// <summary>执行后端调用超时秒数，缺省 120。</summary>
        [Summary("执行后端调用超时秒数，缺省 120")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }

        /// <summary>试跑：只组装提示词、不发请求，打印提示词统计。</summary>
        [Summary("试跑：只组装提示词、不发请求，打印提示词统计")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }
    }

    /// <summary>影响评估命令 task.impact 的参数。</summary>
    public sealed class TaskImpactArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>需求 id，形如 REQ-0042；影响评估报告落 _Tasks/&lt;id&gt;/影响评估.json。</summary>
        [Summary("需求 id，形如 REQ-0042；影响评估报告落 _Tasks/<id>/影响评估.json")]
        public string RequirementIdentifier { get; set; }

        /// <summary>变更 diff 的文件路径，内容作为评估输入。</summary>
        [Summary("变更 diff 的文件路径，内容作为评估输入")]
        public string DiffPath { get; set; }

        /// <summary>未命中工作项的 JSON 列表文件路径（字符串数组）。</summary>
        [Summary("未命中工作项的 JSON 列表文件路径（字符串数组）")]
        public string WorkItemsPath { get; set; }

        /// <summary>执行后端调用超时秒数，缺省 120。</summary>
        [Summary("执行后端调用超时秒数，缺省 120")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }

        /// <summary>试跑：只组装提示词、不发请求，打印提示词统计。默认 true——真调花用户的钱。</summary>
        [Summary("试跑：只组装提示词、不发请求，打印提示词统计。默认 true")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }
    }

    /// <summary>语义冲突比对命令 conflict.semantic 的参数。</summary>
    public sealed class ConflictSemanticArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>执行后端调用超时秒数，缺省 120。</summary>
        [Summary("执行后端调用超时秒数，缺省 120")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }

        /// <summary>试跑：只组装提示词、不发请求，打印提示词统计。默认 true——真调花用户的钱。</summary>
        [Summary("试跑：只组装提示词、不发请求，打印提示词统计。默认 true")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }
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

        /// <summary>业务模块名，用于取 Specifications/Business/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 Specifications/Business/<模块>/ 的就近覆盖")]
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

        /// <summary>业务模块名，用于取 Specifications/Business/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 Specifications/Business/<模块>/ 的就近覆盖")]
        [DefaultValue("")]
        public string ModuleName { get; set; }

        /// <summary>门禁是否全绿，缺省 false。</summary>
        [Summary("门禁是否全绿，缺省 false")]
        [DefaultValue(false)]
        public bool AllGatesGreen { get; set; }

        /// <summary>池子根目录，相对当前工作目录；不给就不查未决冲突。</summary>
        [Summary("池子根目录，相对当前工作目录；不给就不查未决冲突")]
        [DefaultValue("")]
        public string PoolRoot { get; set; }
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

    /// <summary>引擎守护命令 engine.daemon 的参数。</summary>
    public sealed class EngineDaemonArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>最多跑几轮；0 表示无限。</summary>
        [Summary("最多跑几轮；0 表示无限")]
        [DefaultValue(1)]
        public int MaxRounds { get; set; }

        /// <summary>轮间延迟毫秒数。</summary>
        [Summary("轮间延迟毫秒数")]
        [DefaultValue(1000)]
        public int RoundDelayMilliseconds { get; set; }

        /// <summary>停止文件路径；非空且存在时守护在下一轮开头退出。</summary>
        [Summary("停止文件路径；非空且存在时守护在下一轮开头退出")]
        [DefaultValue("")]
        public string StopFilePath { get; set; }

        /// <summary>池子根目录，相对当前工作目录；留空取仓库根下的 Pools。</summary>
        [Summary("池子根目录，相对当前工作目录；留空取仓库根下的 Pools")]
        [DefaultValue("")]
        public string PoolRoot { get; set; }
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

        /// <summary>业务模块名，用于取 Specifications/Business/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 Specifications/Business/<模块>/ 的就近覆盖")]
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

        /// <summary>业务模块名，用于取 Specifications/Business/&lt;模块&gt;/ 的就近覆盖与意见库模块名。</summary>
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

    /// <summary>冲突自动探测命令 conflict.detect 的参数。</summary>
    public sealed class ConflictDetectArguments
    {
        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>需求 id，形如 REQ-0042。</summary>
        [Summary("需求 id，形如 REQ-0042")]
        public string RequirementIdentifier { get; set; }
    }

    /// <summary>同步水位命令 sync.watermark 的参数。</summary>
    public sealed class SyncWatermarkArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>动作：查看 / 前进 / 回退（ASCII 别名 show / advance / rewind）。</summary>
        [Summary("动作：查看 / 前进 / 回退（ASCII 别名 show / advance / rewind）")]
        public string Action { get; set; }

        /// <summary>driver 名，前进 / 回退时必填。</summary>
        [Summary("driver 名，前进 / 回退时必填")]
        [DefaultValue("")]
        public string DriverName { get; set; }

        /// <summary>水位时刻，ISO 8601；前进 / 回退时必填。</summary>
        [Summary("水位时刻，ISO 8601；前进 / 回退时必填")]
        [DefaultValue("")]
        public string Moment { get; set; }

        /// <summary>最后记录 id，可选。</summary>
        [Summary("最后记录 id，可选")]
        [DefaultValue("")]
        public string RecordIdentifier { get; set; }
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

        /// <summary>是否真的落地（快照 + 标脏 + 回方案关）；不给就是只出计划。</summary>
        [Summary("是否真的落地（快照 + 标脏 + 回方案关）；不给就是只出计划")]
        [DefaultValue(false)]
        public bool Apply { get; set; }

        /// <summary>人改权威文件时人的确认标志；不确认就落地会被拒绝。</summary>
        [Summary("人改权威文件时人的确认标志；不确认就落地会被拒绝")]
        [DefaultValue(false)]
        public bool HumanConfirmed { get; set; }

        /// <summary>需求原文的路径；Apply 为 true 时必填，快照要写的是需求原文。</summary>
        [Summary("需求原文的路径；Apply 为 true 时必填，快照要写的是需求原文")]
        [DefaultValue("")]
        public string RequirementFile { get; set; }
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
        /// AI 对抗预审：执行后端按生效规范 + 历史打回意见库审查变更 diff，产物是预审报告。
        /// 报告是产物不是判定（决策 89）：命令返回值永远是 Success，哪怕有阻断级发现。
        /// 按判定键缓存（决策 90）：同输入同模型同提示词版本不重判，命中标「来自缓存」。
        /// driver 名只走运行时数据（路由表解析），本文件不出现任何 driver 名字面量。
        /// </summary>
        /// <param name="arguments">预审命令参数。</param>
        [EditorCommand("task.prereview")]
        [Summary("AI 对抗预审：执行后端审查变更 diff，产物是预审报告")]
        public static CommandResult PreReview(TaskPreReviewArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RequirementIdentifier))
            {
                return CommandResult.Failure("参数 RequirementIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.DiffPath))
            {
                return CommandResult.Failure("参数 DiffPath 为必填项");
            }

            var repositoryRoot = ResolveRoot(arguments.RepositoryRoot, ".", "RepositoryRoot", "仓库根", out var repositoryFailure);
            if (repositoryFailure.Length > 0)
            {
                return CommandResult.Failure(repositoryFailure);
            }

            string diffText;
            try
            {
                diffText = File.ReadAllText(Path.GetFullPath(arguments.DiffPath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            {
                return CommandResult.Failure($"读 diff 文件失败：{exception.Message}");
            }

            try
            {
                var opinions = ReviewOpinionBook.Load(Path.Combine(repositoryRoot, "Pools"));
                var prompt = PreReviewPrompt.Build(repositoryRoot, diffText, null, opinions, PreReviewPrompt.DefaultFewShotLimit);

                if (arguments.DryRun)
                {
                    return CommandResult.Success("预审试跑完成：只组装了提示词，未发任何请求", new[]
                    {
                        $"提示词字符数：{prompt.PromptText.Length}",
                        $"提示词版本：{prompt.PromptVersion}"
                    });
                }

                var routeTable = BridgeRouteTable.Load(repositoryRoot);
                if (!routeTable.Loaded)
                {
                    return CommandResult.Failure($"路由表错误：{routeTable.LoadFailureReason}");
                }

                if (!routeTable.TryResolvePort("执行后端", out var driverName, out var routeReason))
                {
                    return CommandResult.Failure($"执行后端没有可用的 driver：{routeReason}");
                }

                var localSettings = LocalBridgeSettings.Load(repositoryRoot);
                if (!localSettings.Loaded)
                {
                    return CommandResult.Failure($"本机配置错误：{localSettings.LoadFailureReason}");
                }

                var modelName = ReadConfiguredModelName(localSettings, driverName);
                if (modelName.Length == 0)
                {
                    return CommandResult.Failure($"driver「{driverName}」的本机配置里没有「模型」键");
                }

                // 缓存键必须含模型名与提示词版本（决策 90）：换了模型还命中旧缓存，报告就在说谎。
                var cacheKey = PreReviewCache.ComputeKey(prompt.PromptText, modelName, prompt.PromptVersion);

                PreReviewReport report;
                bool fromCache = false;
                if (PreReviewCache.TryLoad(repositoryRoot, cacheKey, out var cached))
                {
                    report = cached.AsStamped(DateTimeOffset.Now.ToString("o"), fromCache: true);
                    fromCache = true;
                }
                else
                {
                    var payload = JsonSerializer.SerializeToElement(new JsonObject
                    {
                        ["提示"] = prompt.PromptText,
                        ["上下文"] = PreReviewSystemContext
                    });

                    var result = BridgeInvoker.Invoke(repositoryRoot, driverName, "complete", payload, arguments.TimeoutSeconds);
                    if (!result.Succeeded)
                    {
                        return CommandResult.Failure($"执行后端调用失败（{result.ErrorCode}）：{result.HumanText}");
                    }

                    var modelText = ReadPayloadString(result.Payload, "文本");
                    var returnedModel = ReadPayloadString(result.Payload, "模型");
                    if (!PreReviewReport.TryParse(modelText, out report, out var parseReason))
                    {
                        // 解析失败绝不许当成零发现（决策 42）：判成了=false、零发现、原因写清。
                        report = PreReviewReport.NotParsed(returnedModel, prompt.PromptVersion, cacheKey, parseReason);
                    }
                    else
                    {
                        report = new PreReviewReport(
                            parsed: report.Parsed,
                            model: returnedModel,
                            promptVersion: prompt.PromptVersion,
                            decisionKey: cacheKey,
                            findings: report.Findings,
                            blockingCount: report.BlockingCount,
                            suggestionCount: report.SuggestionCount,
                            fromCache: false,
                            parseReason: "",
                            timestamp: DateTimeOffset.Now.ToString("o"));
                    }

                    // 判没判成都缓存：同输入同模型同版本不再重判。
                    PreReviewCache.Save(repositoryRoot, cacheKey, report);
                }

                var reportPath = report.WriteReport(repositoryRoot, arguments.RequirementIdentifier);
                var outputLines = new List<string>
                {
                    $"判成了：{report.Parsed}",
                    $"阻断级：{report.BlockingCount} 条",
                    $"建议级：{report.SuggestionCount} 条",
                    $"模型：{report.Model}",
                    $"提示词版本：{report.PromptVersion}",
                    $"判定键：{report.DecisionKey}",
                    $"来自缓存：{fromCache}",
                    $"报告：{reportPath}"
                };
                return CommandResult.Success("预审完成，报告已落盘", outputLines);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"预审失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 影响评估：执行后端对未被 diff 直接命中的工作项逐个判脏/净并给理由，产物是影响评估报告。
        /// 报告是产物不是判定（决策 89）：命令返回值永远是 Success，哪怕报告里全是「脏」。
        /// 模型漏答的项进「漏判的工作项」，绝不默认成「净」（决策 42）。
        /// 按判定键缓存（决策 90）：同输入同模型同提示词版本不重判，命中标「来自缓存」。
        /// 报告落盘之后**合并写进 05-change-impact.md**（子文档 03 §三）：只加「执行后端评估（建议，不是判定）」
        /// 那一节，不动重规划算出来的那几节；重复跑覆盖上一次，不越堆越多。
        /// 那份文档不存在时不新建，只如实报一句——没有它说明还没重规划过。
        /// driver 名只走运行时数据（路由表解析），本文件不出现任何 driver 名字面量。
        /// </summary>
        /// <param name="arguments">影响评估命令参数。</param>
        [EditorCommand("task.impact")]
        [Summary("影响评估：执行后端对未命中工作项判脏/净，产物是影响评估报告")]
        public static CommandResult Impact(TaskImpactArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RequirementIdentifier))
            {
                return CommandResult.Failure("参数 RequirementIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.DiffPath))
            {
                return CommandResult.Failure("参数 DiffPath 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.WorkItemsPath))
            {
                return CommandResult.Failure("参数 WorkItemsPath 为必填项");
            }

            var repositoryRoot = ResolveRoot(arguments.RepositoryRoot, ".", "RepositoryRoot", "仓库根", out var repositoryFailure);
            if (repositoryFailure.Length > 0)
            {
                return CommandResult.Failure(repositoryFailure);
            }

            string diffText;
            try
            {
                diffText = File.ReadAllText(Path.GetFullPath(arguments.DiffPath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            {
                return CommandResult.Failure($"读 diff 文件失败：{exception.Message}");
            }

            IReadOnlyList<string> workItems;
            try
            {
                workItems = ReadWorkItemList(Path.GetFullPath(arguments.WorkItemsPath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException || exception is JsonException)
            {
                return CommandResult.Failure($"读工作项列表失败：{exception.Message}");
            }

            try
            {
                var prompt = ImpactAssessPrompt.Build(repositoryRoot, diffText, workItems, ImpactAssessPrompt.PromptVersion);

                if (arguments.DryRun)
                {
                    return CommandResult.Success("影响评估试跑完成：只组装了提示词，未发任何请求", new[]
                    {
                        $"提示词字符数：{prompt.PromptText.Length}",
                        $"提示词版本：{prompt.PromptVersion}",
                        $"待评估工作项：{workItems.Count} 个"
                    });
                }

                var routeTable = BridgeRouteTable.Load(repositoryRoot);
                if (!routeTable.Loaded)
                {
                    return CommandResult.Failure($"路由表错误：{routeTable.LoadFailureReason}");
                }

                if (!routeTable.TryResolvePort("执行后端", out var driverName, out var routeReason))
                {
                    return CommandResult.Failure($"执行后端没有可用的 driver：{routeReason}");
                }

                var localSettings = LocalBridgeSettings.Load(repositoryRoot);
                if (!localSettings.Loaded)
                {
                    return CommandResult.Failure($"本机配置错误：{localSettings.LoadFailureReason}");
                }

                var modelName = ReadConfiguredModelName(localSettings, driverName);
                if (modelName.Length == 0)
                {
                    return CommandResult.Failure($"driver「{driverName}」的本机配置里没有「模型」键");
                }

                // 缓存键必须含模型名与提示词版本（决策 90）：换了模型还命中旧缓存，报告就在说谎。
                var cacheKey = PreReviewCache.ComputeKey(prompt.PromptText, modelName, prompt.PromptVersion);

                ImpactAssessReport report;
                bool fromCache = false;
                var cacheFilePath = PreReviewCache.CacheFile(repositoryRoot, cacheKey);
                if (File.Exists(cacheFilePath))
                {
                    var cached = ImpactAssessReport.TryFromJson(File.ReadAllText(cacheFilePath, Encoding.UTF8));
                    if (cached != null)
                    {
                        report = cached.AsStamped(DateTimeOffset.Now.ToString("o"), fromCache: true);
                        fromCache = true;
                    }
                    else
                    {
                        report = null;
                    }
                }
                else
                {
                    report = null;
                }

                if (report == null)
                {
                    var payload = JsonSerializer.SerializeToElement(new JsonObject
                    {
                        ["提示"] = prompt.PromptText,
                        ["上下文"] = ImpactAssessSystemContext
                    });

                    var result = BridgeInvoker.Invoke(repositoryRoot, driverName, "complete", payload, arguments.TimeoutSeconds);
                    if (!result.Succeeded)
                    {
                        return CommandResult.Failure($"执行后端调用失败（{result.ErrorCode}）：{result.HumanText}");
                    }

                    var modelText = ReadPayloadString(result.Payload, "文本");
                    var returnedModel = ReadPayloadString(result.Payload, "模型");
                    if (!ImpactAssessReport.TryParse(modelText, workItems, out report, out var parseReason))
                    {
                        // 解析失败绝不许当成零结论（决策 42）：判成了=false、零结论、原因写清。
                        report = ImpactAssessReport.NotParsed(returnedModel, prompt.PromptVersion, cacheKey, parseReason);
                    }
                    else
                    {
                        report = new ImpactAssessReport(
                            parsed: report.Parsed,
                            model: returnedModel,
                            promptVersion: prompt.PromptVersion,
                            decisionKey: cacheKey,
                            verdicts: report.Verdicts,
                            missingWorkItems: report.MissingWorkItems,
                            dirtyCount: report.DirtyCount,
                            cleanCount: report.CleanCount,
                            fromCache: false,
                            parseReason: "",
                            timestamp: DateTimeOffset.Now.ToString("o"));
                    }

                    // 判没判成都缓存：同输入同模型同版本不再重判。复用 PreReviewCache 的目录与键（决策 90）。
                    Directory.CreateDirectory(PreReviewCache.CacheDirectory(repositoryRoot));
                    File.WriteAllText(cacheFilePath, report.ToJson(), new UTF8Encoding(false));
                }

                var reportPath = report.WriteReport(repositoryRoot, arguments.RequirementIdentifier);
                var outputLines = new List<string>
                {
                    $"判成了：{report.Parsed}",
                    $"脏：{report.DirtyCount} 项",
                    $"净：{report.CleanCount} 项",
                    $"漏判的工作项：{report.MissingWorkItems.Count} 个",
                    $"模型：{report.Model}",
                    $"提示词版本：{report.PromptVersion}",
                    $"判定键：{report.DecisionKey}",
                    $"来自缓存：{fromCache}",
                    $"报告：{reportPath}"
                };

                var merge = ChangeImpactMerger.Merge(repositoryRoot, arguments.RequirementIdentifier, report);
                outputLines.Add(merge.Merged
                    ? $"已合并写：{merge.FilePath}{(merge.ReplacedExistingSection ? "（覆盖了上一次的评估小节）" : "")}"
                    : $"没合并写：{merge.Reason}");

                return CommandResult.Success("影响评估完成，报告已落盘", outputLines);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"影响评估失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 语义冲突比对：执行后端把设计池汇总与存量需求做语义比对，输出冲突候选 + 置信度，产物是语义冲突报告。
        /// 报告是产物不是判定（决策 89）：命令返回值永远是 Success，哪怕报告里全是高置信度冲突。
        /// 照决策 66：只产报告，**一个字都不写进协作层账本**——不写 ConflictList、不调 Append。
        /// 照决策 67：同一对需求命中多条判据时，每条判据各产一条候选，不合并、不取最大（提示词里已明确要求）。
        /// 按判定键缓存（决策 90）：同输入同模型同提示词版本不重判，命中标「来自缓存」。
        /// driver 名只走运行时数据（路由表解析），本文件不出现任何 driver 名字面量。
        /// </summary>
        /// <param name="arguments">语义冲突比对命令参数。</param>
        [EditorCommand("conflict.semantic")]
        [Summary("语义冲突比对：执行后端对设计池汇总与存量需求做比对，产物是语义冲突报告")]
        public static CommandResult SemanticConflict(ConflictSemanticArguments arguments)
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
                var designSummary = ReadDesignPoolSummary(poolRoot);
                var existingRequirements = ReadExistingRequirements(poolRoot);
                var prompt = SemanticConflictPrompt.Build(repositoryRoot, designSummary, existingRequirements, SemanticConflictPrompt.PromptVersion);

                if (arguments.DryRun)
                {
                    return CommandResult.Success("语义冲突比对试跑完成：只组装了提示词，未发任何请求", new[]
                    {
                        $"提示词字符数：{prompt.PromptText.Length}",
                        $"提示词版本：{prompt.PromptVersion}",
                        $"存量需求：{existingRequirements.Count} 份"
                    });
                }

                var routeTable = BridgeRouteTable.Load(repositoryRoot);
                if (!routeTable.Loaded)
                {
                    return CommandResult.Failure($"路由表错误：{routeTable.LoadFailureReason}");
                }

                if (!routeTable.TryResolvePort("执行后端", out var driverName, out var routeReason))
                {
                    return CommandResult.Failure($"执行后端没有可用的 driver：{routeReason}");
                }

                var localSettings = LocalBridgeSettings.Load(repositoryRoot);
                if (!localSettings.Loaded)
                {
                    return CommandResult.Failure($"本机配置错误：{localSettings.LoadFailureReason}");
                }

                var modelName = ReadConfiguredModelName(localSettings, driverName);
                if (modelName.Length == 0)
                {
                    return CommandResult.Failure($"driver「{driverName}」的本机配置里没有「模型」键");
                }

                // 缓存键必须含模型名与提示词版本（决策 90）：换了模型还命中旧缓存，报告就在说谎。
                var cacheKey = PreReviewCache.ComputeKey(prompt.PromptText, modelName, prompt.PromptVersion);

                SemanticConflictReport report;
                bool fromCache = false;
                var cacheFilePath = PreReviewCache.CacheFile(repositoryRoot, cacheKey);
                if (File.Exists(cacheFilePath))
                {
                    var cached = SemanticConflictReport.TryFromJson(File.ReadAllText(cacheFilePath, Encoding.UTF8));
                    if (cached != null)
                    {
                        report = cached.AsStamped(DateTimeOffset.Now.ToString("o"), fromCache: true);
                        fromCache = true;
                    }
                    else
                    {
                        report = null;
                    }
                }
                else
                {
                    report = null;
                }

                if (report == null)
                {
                    var payload = JsonSerializer.SerializeToElement(new JsonObject
                    {
                        ["提示"] = prompt.PromptText,
                        ["上下文"] = SemanticConflictSystemContext
                    });

                    var result = BridgeInvoker.Invoke(repositoryRoot, driverName, "complete", payload, arguments.TimeoutSeconds);
                    if (!result.Succeeded)
                    {
                        return CommandResult.Failure($"执行后端调用失败（{result.ErrorCode}）：{result.HumanText}");
                    }

                    var modelText = ReadPayloadString(result.Payload, "文本");
                    var returnedModel = ReadPayloadString(result.Payload, "模型");
                    if (!SemanticConflictReport.TryParse(modelText, out report, out var parseReason))
                    {
                        // 解析失败绝不许当成零候选（决策 42）：判成了=false、零候选、原因写清。
                        report = SemanticConflictReport.NotParsed(returnedModel, prompt.PromptVersion, cacheKey, parseReason);
                    }
                    else
                    {
                        report = new SemanticConflictReport(
                            parsed: report.Parsed,
                            model: returnedModel,
                            promptVersion: prompt.PromptVersion,
                            decisionKey: cacheKey,
                            candidates: report.Candidates,
                            highCount: report.HighCount,
                            mediumCount: report.MediumCount,
                            lowCount: report.LowCount,
                            fromCache: false,
                            parseReason: "",
                            timestamp: DateTimeOffset.Now.ToString("o"));
                    }

                    // 判没判成都缓存：同输入同模型同版本不再重判。复用 PreReviewCache 的目录与键（决策 90）。
                    Directory.CreateDirectory(PreReviewCache.CacheDirectory(repositoryRoot));
                    File.WriteAllText(cacheFilePath, report.ToJson(), new UTF8Encoding(false));
                }

                var reportPath = report.WriteReport(repositoryRoot);
                var outputLines = new List<string>
                {
                    $"判成了：{report.Parsed}",
                    $"冲突候选：{report.Candidates.Count} 条（高 {report.HighCount} / 中 {report.MediumCount} / 低 {report.LowCount}）",
                    $"模型：{report.Model}",
                    $"提示词版本：{report.PromptVersion}",
                    $"判定键：{report.DecisionKey}",
                    $"来自缓存：{fromCache}",
                    $"报告：{reportPath}"
                };
                return CommandResult.Success("语义冲突比对完成，报告已落盘", outputLines);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"语义冲突比对失败：{exception.Message}");
            }
        }

        /// <summary>执行后端调用的系统上下文：只定位角色，具体的审查要求全在提示词里。</summary>
        private const string PreReviewSystemContext = "你是创作管线的 AI 对抗预审员。你只对给定的变更 diff 输出审查发现，输出必须是严格的 JSON，不要输出任何其他内容。";

        /// <summary>影响评估调用的系统上下文：只定位角色，具体的评估要求全在提示词里。</summary>
        private const string ImpactAssessSystemContext = "你是创作管线的影响评估员。你只对给定的变更 diff 与工作项列表逐个判定脏/净，输出必须是严格的 JSON，不要输出任何其他内容。";

        /// <summary>语义冲突比对调用的系统上下文：只定位角色，具体的比对要求全在提示词里。</summary>
        private const string SemanticConflictSystemContext = "你是创作管线的语义冲突比对员。你只对设计池汇总与存量需求做语义比对并输出冲突候选，输出必须是严格的 JSON，不要输出任何其他内容。";

        /// <summary>从 JSON 文件读未命中工作项列表：数组元素是字符串直接取；是对象则取「id」字符串键；其余形状报错。</summary>
        /// <param name="filePath">工作项列表 JSON 文件路径。</param>
        private static IReadOnlyList<string> ReadWorkItemList(string filePath)
        {
            var root = JsonNode.Parse(File.ReadAllText(filePath, Encoding.UTF8));
            if (root is not JsonArray array)
            {
                throw new JsonException("工作项列表的顶层必须是 JSON 数组");
            }

            var result = new List<string>();
            foreach (var item in array)
            {
                if (item is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String)
                {
                    result.Add(jsonValue.GetValue<string>() ?? "");
                    continue;
                }

                if (item is JsonObject obj && obj.TryGetPropertyValue("id", out var idNode) && idNode is JsonValue idValue && idValue.GetValueKind() == JsonValueKind.String)
                {
                    result.Add(idValue.GetValue<string>() ?? "");
                    continue;
                }

                throw new JsonException("工作项列表的每个元素必须是字符串或带「id」字符串键的对象");
            }

            return result;
        }

        /// <summary>读设计池汇总：&lt;池根&gt;/Designs/Digest/*.md 按文件名序数序，每份一节；目录不存在或没有文件给占位文案。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        private static string ReadDesignPoolSummary(string poolRoot)
        {
            var directory = PoolPaths.DesignSummaryDirectory(poolRoot);
            if (!Directory.Exists(directory))
            {
                return "暂无设计汇总。";
            }

            var files = Directory.GetFiles(directory, "*.md").ToList();
            files.Sort(StringComparer.Ordinal);
            if (files.Count == 0)
            {
                return "暂无设计汇总。";
            }

            var builder = new StringBuilder();
            for (var i = 0; i < files.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendLine();
                }

                var fileName = Path.GetFileName(files[i]);
                builder.AppendLine("### 文件：" + fileName);
                builder.AppendLine();
                builder.AppendLine(File.ReadAllText(files[i], Encoding.UTF8));
            }

            return builder.ToString();
        }

        /// <summary>读存量需求：&lt;池根&gt;/Requirements/REQ-*.json 按文件名序数序，每份 JSON 原文一节；目录不存在给空列表。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        private static IReadOnlyList<string> ReadExistingRequirements(string poolRoot)
        {
            var result = new List<string>();
            var directory = PoolPaths.RequirementsDirectory(poolRoot);
            if (!Directory.Exists(directory))
            {
                return result;
            }

            var files = Directory.GetFiles(directory, "REQ-*.json").ToList();
            files.Sort(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                result.Add("### 需求：" + fileName + Environment.NewLine + File.ReadAllText(file, Encoding.UTF8));
            }

            return result;
        }

        /// <summary>从本机配置里读某 driver 的模型名（只读「模型」键，密钥不经这里）。</summary>
        private static string ReadConfiguredModelName(LocalBridgeSettings localSettings, string driverName)
        {
            if (localSettings.TryGetDriverConfiguration(driverName, out var configuration)
                && configuration.ValueKind == JsonValueKind.Object
                && configuration.TryGetProperty("模型", out var modelElement)
                && modelElement.ValueKind == JsonValueKind.String)
            {
                return modelElement.GetString() ?? "";
            }

            return "";
        }

        /// <summary>从桥返回载荷里读字符串键；缺失给空串。</summary>
        private static string ReadPayloadString(JsonElement payload, string key)
        {
            if (payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? "";
            }

            return "";
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

            var history = ConflictDecisionLedger.Load(poolRoot).FindByConflict(result.Entry.Identifier);
            lines.Add($"裁决流水：本条冲突累计 {history.Count} 次裁决（本次是第 {history.Count} 次）");

            return CommandResult.Success($"冲突 {result.Entry.Identifier} 裁决完成", lines);
        }

        /// <summary>
        /// 冲突自动探测：把一条新需求与池子存量需求比对，产出冲突候选与置信度。
        /// 本命令一个字都不写盘——挂账仍然只能靠 conflict.list / conflict.resolve 那条路。
        /// 「没扫成」与「未发现候选」是两个分支（决策 42）：探测没跑成时结论行必须写清原因。
        /// </summary>
        /// <param name="arguments">冲突探测命令参数。</param>
        [EditorCommand("conflict.detect")]
        [Summary("冲突自动探测：新需求 vs 存量需求，产出候选不写盘")]
        public static CommandResult DetectConflict(ConflictDetectArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.RequirementIdentifier))
            {
                return CommandResult.Failure("参数 RequirementIdentifier 为必填项");
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            ConflictDetectionReport report;
            try
            {
                report = ConflictDetector.Detect(poolRoot, arguments.RequirementIdentifier);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"冲突探测失败：{exception.Message}");
            }

            var lines = new List<string>();
            if (!report.Scanned)
            {
                return CommandResult.Success($"冲突探测没跑成：{report.LoadFailureReason}", lines);
            }

            if (report.LoadFailureReason.Length > 0)
            {
                lines.Add($"注意：{report.LoadFailureReason}");
            }

            if (report.Candidates.Count == 0)
            {
                return CommandResult.Success(
                    $"冲突探测（比对 {report.ScannedCount} 条存量需求）：未发现候选",
                    lines);
            }

            var raiseCount = report.Candidates.Count(candidate => candidate.ShouldRaiseCard);
            foreach (var candidate in report.Candidates)
            {
                var star = candidate.ShouldRaiseCard ? "★" : "";
                lines.Add($"{star}[{candidate.Confidence}] {candidate.OldIdentifier} ← {candidate.Reason} ({candidate.Score.ToString("0.000")}) {candidate.Detail}");
            }

            return CommandResult.Success(
                $"冲突探测（比对 {report.ScannedCount} 条存量需求）：候选 {report.Candidates.Count} 条，建议发卡 {raiseCount} 条",
                lines);
        }

        /// <summary>
        /// 同步水位：查看 / 前进 / 回退。查看列出全部 driver 的水位；前进只许前进（时间相同
        /// 是幂等重放不算前进）；回退是显式重拉的正门，不做前进检查直接写。
        /// </summary>
        /// <param name="arguments">同步水位命令参数。</param>
        [EditorCommand("sync.watermark")]
        [Summary("同步水位：查看 / 前进 / 回退")]
        public static CommandResult ManageWatermark(SyncWatermarkArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            var action = NormalizeWatermarkAction(arguments.Action);
            if (action.Length == 0)
            {
                return CommandResult.Failure($"动作「{arguments.Action}」不合法；合法值是：查看、前进、回退（ASCII 别名 show、advance、rewind）");
            }

            if (string.Equals(action, "查看", StringComparison.Ordinal))
            {
                SyncWatermark watermark;
                try
                {
                    watermark = SyncWatermark.Load(repositoryRoot);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    return CommandResult.Failure($"同步水位读取失败：{exception.Message}");
                }

                var lines = new List<string>();
                if (watermark.LoadFailureReason.Length > 0)
                {
                    lines.Add($"注意：{watermark.LoadFailureReason}");
                }

                if (watermark.Entries.Count == 0)
                {
                    return CommandResult.Success("同步水位：还没有任何 driver 记过水位（全量拉）", lines);
                }

                foreach (var pair in watermark.Entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    lines.Add($"{pair.Key}　最后修改时间 {pair.Value.Moment}　最后记录id {pair.Value.RecordIdentifier}");
                }

                return CommandResult.Success($"同步水位 {watermark.Entries.Count} 个 driver", lines);
            }

            if (string.IsNullOrWhiteSpace(arguments.DriverName))
            {
                return CommandResult.Failure("参数 DriverName 为必填项（前进 / 回退时）");
            }

            if (string.IsNullOrWhiteSpace(arguments.Moment))
            {
                return CommandResult.Failure("参数 Moment 为必填项（前进 / 回退时）");
            }

            var recordIdentifier = arguments.RecordIdentifier ?? "";
            if (string.Equals(action, "前进", StringComparison.Ordinal))
            {
                WatermarkAdvanceResult result;
                try
                {
                    result = SyncWatermark.Advance(repositoryRoot, arguments.DriverName, arguments.Moment, recordIdentifier);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    return CommandResult.Failure($"同步水位前进失败：{exception.Message}");
                }

                if (!result.Succeeded)
                {
                    return CommandResult.Failure(result.FailureReason);
                }

                if (!result.Advanced)
                {
                    return CommandResult.Success("水位没动（给的时间与当前水位相同）");
                }

                return CommandResult.Success($"水位已前进到 {result.Entry.Moment}：下次从这里增量拉");
            }

            WatermarkAdvanceResult rewindResult;
            try
            {
                rewindResult = SyncWatermark.Rewind(repositoryRoot, arguments.DriverName, arguments.Moment, recordIdentifier);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"同步水位回退失败：{exception.Message}");
            }

            if (!rewindResult.Succeeded)
            {
                return CommandResult.Failure(rewindResult.FailureReason);
            }

            return CommandResult.Success($"水位已回退到 {rewindResult.Entry.Moment}：下次会重拉这之后的记录");
        }

        /// <summary>把 ASCII 别名归一成中文动作名；认不出的返回空串。</summary>
        private static string NormalizeWatermarkAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return "";
            }

            var trimmed = action.Trim();
            if (string.Equals(trimmed, "查看", StringComparison.Ordinal) || string.Equals(trimmed, "show", StringComparison.OrdinalIgnoreCase))
            {
                return "查看";
            }

            if (string.Equals(trimmed, "前进", StringComparison.Ordinal) || string.Equals(trimmed, "advance", StringComparison.OrdinalIgnoreCase))
            {
                return "前进";
            }

            if (string.Equals(trimmed, "回退", StringComparison.Ordinal) || string.Equals(trimmed, "rewind", StringComparison.OrdinalIgnoreCase))
            {
                return "回退";
            }

            return "";
        }

        /// <summary>
        /// 打断重规划：算脏项、净项与要问人的地方；带 --Apply 时把计划真的落地
        /// （需求快照成新基准 + 脏项标脏 + 回方案关）。重规划算完不算失败——它是一份计划，不是判决。
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

            if (!arguments.Apply)
            {
                return CommandResult.Success("重规划完成（计划，不是判决）", lines);
            }

            if (string.IsNullOrWhiteSpace(arguments.RequirementFile))
            {
                return CommandResult.Failure("落地需要 RequirementFile：快照要写的是需求原文");
            }

            string requirementJsonText;
            try
            {
                requirementJsonText = File.ReadAllText(Path.GetFullPath(arguments.RequirementFile));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommandResult.Failure($"需求原文读不了：{exception.Message}");
            }

            ReplanLandingResult landing;
            try
            {
                landing = ReplanLanding.Apply(
                    repositoryRoot,
                    arguments.RequirementIdentifier,
                    result,
                    graph,
                    requirementJsonText,
                    arguments.HumanConfirmed);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommandResult.Failure($"重规划落地失败：{exception.Message}");
            }

            if (!landing.Applied)
            {
                // 零脏项 / 有执行中 / 要人确认：都是「该停下」而不是命令失败，走 Success 但结论行明说没落地。
                lines.Add($"重规划未落地：{landing.RefusalReason}");
                return CommandResult.Success("重规划未落地", lines);
            }

            lines.Add(
                $"重规划已落地：新基准 00-requirement.v{landing.SnapshotVersion}.json，"
                + $"标脏 {landing.MarkedDirty.Count} 项，保留 {landing.KeptClean.Count} 项，已回方案关");
            foreach (var identifier in landing.MarkedDirty)
            {
                lines.Add($"标脏：{identifier}");
            }

            foreach (var finding in landing.Findings)
            {
                lines.Add($"注意：{finding}");
            }

            return CommandResult.Success("重规划已落地", lines);
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

            // 未决冲突只摆账不改放行结论（决策 51）；池子根目录没给就不查，如实写「没查成」。
            lines.Add(BuildConflictDebtLine(arguments));

            return CommandResult.Success("放行判定完成", lines);
        }

        /// <summary>组「未决冲突：」那一行输出：没查成 / 零未决 / 有未决三种文案。</summary>
        private static string BuildConflictDebtLine(TaskReleaseArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return "未决冲突：没查成（未给池子根目录）";
            }

            var poolRoot = Path.GetFullPath(arguments.PoolRoot);
            // task.release 不针对某个需求，需求 id 传空白 = 看全池未决。
            var report = ConflictDebtView.ForRequirement(ConflictList.Load(poolRoot), "");
            if (!report.Scanned)
            {
                return $"未决冲突：没查成（{report.LoadFailureReason}）";
            }

            if (report.Items.Count == 0)
            {
                return $"未决冲突：本需求 0 条（池子共 {report.TotalPending} 条）";
            }

            return $"未决冲突：本需求 {report.Items.Count} 条（池子共 {report.TotalPending} 条）";
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

        /// <summary>
        /// 引擎守护：拿单实例锁后按模式循环跑取活判定并记账，跑满指定轮数或收到停止信号后退出。
        /// 拿不到锁不算失败——那正是单实例该有的行为（决策 55 同源），照样返回 Success 并写清原因。
        /// </summary>
        /// <param name="arguments">引擎守护命令参数。</param>
        [EditorCommand("engine.daemon")]
        [Summary("引擎守护：循环跑取活判定并记账，跑满指定轮数退出")]
        public static CommandResult Daemon(EngineDaemonArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"仓库根目录不存在：{repositoryRoot}");
            }

            var options = new DaemonRunOptions
            {
                MaxRounds = arguments.MaxRounds,
                RoundDelayMilliseconds = arguments.RoundDelayMilliseconds,
                StopFilePath = arguments.StopFilePath,
                PoolRoot = arguments.PoolRoot
            };

            var summary = PollingDaemon.Run(repositoryRoot, options, () => DateTimeOffset.Now, Thread.Sleep);

            var lines = new List<string>();
            foreach (var record in summary.Records)
            {
                lines.Add($"轮次 {record.Round}　取活 {(record.ShouldTake ? "取" : "不取")}　原因 {record.Reason}");
            }

            if (summary.ReleaseFailureReason.Length > 0)
            {
                // 锁没释放掉不算这一轮失败（下一次启动能接管陈旧锁自愈），但必须说出来。
                lines.Add($"锁释放失败：{summary.ReleaseFailureReason}");
            }

            return CommandResult.Success(
                $"守护跑了 {summary.RoundsRun} 轮（取活 {summary.TakenCount} 次，消费唤醒 {summary.WakeConsumedCount} 次）；停止原因：{summary.StopReason}",
                lines);
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
