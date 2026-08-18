using System;
using System.Collections.Generic;
using System.ComponentModel;
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

    /// <summary>
    /// 入站/出站/队列/状态五条命令的 CLI 入口：
    /// pool.pull 跑一轮入站、pool.push 按出站事件生成意图信封、
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
    }
}
