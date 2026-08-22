using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>助手常驻会话命令 assist.serve 的参数。</summary>
    public sealed class AssistantServeArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对仓库根。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>跑几轮后退出；0 表示不自己停（要靠停止文件或 Ctrl+C）。</summary>
        [Summary("跑几轮后退出；0 表示不自己停")]
        [DefaultValue(1)]
        public int MaxRounds { get; set; }

        /// <summary>两轮之间歇多少毫秒。</summary>
        [Summary("两轮之间歇多少毫秒")]
        [DefaultValue(2000)]
        public int RoundDelayMilliseconds { get; set; }

        /// <summary>停止文件路径：这个文件出现就停下来；空串表示不看停止文件。</summary>
        [Summary("停止文件路径：这个文件出现就停；空串表示不看")]
        [DefaultValue("")]
        public string StopFilePath { get; set; }

        /// <summary>只组装不发：不调执行后端、不回话、不写下游。默认 true——真跑要花钱又会真发消息。</summary>
        [Summary("只组装不发：不调执行后端、不回话、不写下游。默认 true，要真跑显式传 false")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }

        /// <summary>校验过了要不要真写下游草稿表。默认 false：真回话与真写表分成两级开关。</summary>
        [Summary("校验过了要不要真写下游草稿表。默认 false")]
        [DefaultValue(false)]
        public bool WriteDownstream { get; set; }

        /// <summary>执行后端调用超时秒数。</summary>
        [Summary("执行后端调用超时秒数")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>
    /// 助手 port 的 B 形态：常驻会话（子文档 05 §一「助手：package / serve」）。
    ///
    /// 一轮干四件事，顺序不许换：
    /// 1. 从会话目录取一条消息（下游旁路投进来的，已归一成「会话」块）；
    /// 2. 调执行后端把这句话变成「回话 + 需求草稿」；
    /// 3. **现场跑 req.validate**，不过就不写表，把校验发现翻成人话；
    /// 4. 回话；校验过且开了写表开关，才真写下游草稿，并**往唤醒目录投一个信号**叫醒引擎。
    ///
    /// 与引擎守护一样是**有限轮**的（决策 81）：跑满 N 轮自己退出，无限只是 N=0 的特例——
    /// 常驻进程在门禁里没法验，有限轮把这条前提解掉了。
    ///
    /// 信号在**这一轮全部跑完之后**才消费（决策 82）：中途崩了信号要留着，
    /// 否则消息丢了而账上什么都没有。
    /// </summary>
    public static class AssistantCommands
    {
        /// <summary>同一条会话信号最多试着回几次；超了就隔离，不许把循环堵死。</summary>
        private const int MaxReplyAttempts = 3;

        /// <summary>空转心跳大约隔多久报一条「还活着，在等」。</summary>
        private const int HeartbeatIntervalMilliseconds = 5 * 60 * 1000;

        /// <summary>轮询间隔为 0（不歇）时的心跳轮数兜底：这时用时间折算会除出个荒唐的大数。</summary>
        private const int HeartbeatFallbackRounds = 150;

        /// <summary>写 JSON 的选项：本机是 .NET 10 preview SDK，必须从 Default 复制着构造。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 跑助手常驻会话：循环取会话消息、过执行后端、校验、回话。
        /// </summary>
        /// <param name="arguments">常驻会话命令参数。</param>
        [EditorCommand("assist.serve")]
        [Summary("助手常驻会话：取消息 → 执行后端 → 现场校验 → 回话（默认只组装不发）")]
        public static CommandResult Serve(AssistantServeArguments arguments)
        {
            if (arguments == null)
            {
                return CommandResult.Failure("参数为空");
            }

            string repositoryRoot;
            string poolRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
                poolRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.PoolRoot) ? "Pools" : arguments.PoolRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"路径参数无法解析为绝对路径：{exception.Message}");
            }

            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"仓库根目录不存在：{repositoryRoot}");
            }

            var routeTable = BridgeRouteTable.Load(repositoryRoot);
            if (!routeTable.Loaded)
            {
                return CommandResult.Failure($"路由表错误：{routeTable.LoadFailureReason}");
            }

            if (!routeTable.TryResolvePort("助手", out var assistantDriver, out var assistantReason))
            {
                return CommandResult.Failure($"助手 port 没有可用的 driver：{assistantReason}");
            }

            if (!routeTable.TryResolvePort("执行后端", out var backendDriver, out var backendReason))
            {
                return CommandResult.Failure($"执行后端没有可用的 driver：{backendReason}");
            }

            PoolSchema schema;
            try
            {
                schema = PoolSchemaLoader.Load(poolRoot, "需求");
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException || exception is InvalidOperationException)
            {
                return CommandResult.Failure($"需求 schema 读不动：{exception.Message}");
            }

            var lines = new List<string>();

            // 一行日志的去处。宿主接上实时流时当场交出去——常驻循环跑着就能在日志里看见，
            // 而且行不留在内存里；没接流的调用方（单测、进程内宿主）照旧收进 OutputLines。
            // 两条路只走一条，不会同一行打两遍。
            void Record(string line)
            {
                if (CommandLogStream.IsAttached)
                {
                    CommandLogStream.Write(line);
                }
                else
                {
                    lines.Add(line);
                }
            }

            var handledCount = 0;
            var writtenCount = 0;
            var failedCount = 0;
            var round = 0;
            var stopReason = "跑满轮数";

            // 连着空转了多少轮。空转**不逐轮记**：2 秒一轮逐轮记就是一天四万行，
            // 内存与磁盘白涨不说，真正有用的那几行全被淹掉。改成隔一阵报一次。
            var idleRounds = 0;
            var heartbeatEveryRounds = arguments.RoundDelayMilliseconds > 0
                ? Math.Max(1, HeartbeatIntervalMilliseconds / arguments.RoundDelayMilliseconds)
                : HeartbeatFallbackRounds;

            // 起来先报一句。以前这里一声不吭，日志文件要等进程退出才有内容，
            // 于是「助手在不在跑」这件事从日志上完全看不出来——只能去翻进程表。
            Record($"助手常驻会话起来了：仓库 {repositoryRoot}，轮询间隔 {arguments.RoundDelayMilliseconds} 毫秒，" +
                $"写下游 {(arguments.WriteDownstream ? "开" : "关")}，" +
                $"{(arguments.MaxRounds <= 0 ? $"靠停止文件退出（{arguments.StopFilePath}）" : $"跑满 {arguments.MaxRounds} 轮退出")}");

            // 同一条信号在本进程里重试了几次。回话失败且可重试时把信号留在原地，
            // 但不许无限留——留到第 MaxReplyAttempts 次还送不出去就隔离，
            // 否则一条发不出去的消息会把常驻循环永远堵在这里。
            var attemptsBySignal = new Dictionary<string, int>(StringComparer.Ordinal);

            while (arguments.MaxRounds <= 0 || round < arguments.MaxRounds)
            {
                if (!string.IsNullOrWhiteSpace(arguments.StopFilePath) && File.Exists(arguments.StopFilePath))
                {
                    stopReason = "看到停止文件";
                    break;
                }

                round++;
                var poll = ConversationSignalSource.Poll(repositoryRoot);
                if (!poll.HasSignal)
                {
                    idleRounds++;

                    // 第一轮空转报一句「开始等了」，之后按心跳间隔报，其余的轮次一个字都不写。
                    if (idleRounds == 1 || idleRounds % heartbeatEveryRounds == 0)
                    {
                        Record($"轮次 {round}　在等消息（已连续空转 {idleRounds} 轮，{poll.Reason}）");
                    }

                    if (arguments.MaxRounds <= 0 || round < arguments.MaxRounds)
                    {
                        Thread.Sleep(Math.Max(0, arguments.RoundDelayMilliseconds));
                    }

                    continue;
                }

                idleRounds = 0;
                var signalName = Path.GetFileName(poll.SignalFilePath);
                attemptsBySignal.TryGetValue(signalName, out var attempts);
                attempts++;
                attemptsBySignal[signalName] = attempts;

                var turnLines = RunOneTurn(
                    repositoryRoot,
                    poolRoot,
                    assistantDriver,
                    backendDriver,
                    schema,
                    poll.SignalFilePath,
                    arguments,
                    out var wroteDownstream,
                    out var replyDelivered,
                    out var replyRetryable);
                handledCount++;
                if (wroteDownstream)
                {
                    writtenCount++;
                }

                foreach (var line in turnLines)
                {
                    Record($"轮次 {round}　{line}");
                }

                // 判定全跑完才消费信号（决策 82）。三路处置，依据是**回话到底送没送出去**：
                // 送到了才算「已处理」；没送到就绝不许进 processed——那等于账面上说回过了，
                // 而用户那头一个字都没收到。
                if (replyDelivered)
                {
                    var archived = ConversationSignalSource.Consume(repositoryRoot, poll.SignalFilePath);
                    Record($"轮次 {round}　信号归档：{(archived.Length == 0 ? "移动失败，信号留在原地下一轮还会取到" : archived)}");
                }
                else if (replyRetryable && attempts < MaxReplyAttempts)
                {
                    failedCount++;
                    Record($"轮次 {round}　回话没送出去（第 {attempts}/{MaxReplyAttempts} 次），信号留在原地，下一轮重试");
                    if (arguments.MaxRounds <= 0 || round < arguments.MaxRounds)
                    {
                        Thread.Sleep(Math.Max(0, arguments.RoundDelayMilliseconds));
                    }
                }
                else
                {
                    failedCount++;
                    var quarantined = ConversationSignalSource.Quarantine(repositoryRoot, poll.SignalFilePath);
                    var why = replyRetryable ? $"重试 {attempts} 次仍失败" : "不可重试";
                    Record($"轮次 {round}　回话没送出去（{why}），信号隔离：{(quarantined.Length == 0 ? "移动失败" : quarantined)}");
                }
            }

            var summary = $"助手会话跑了 {round} 轮（处理消息 {handledCount} 条，写下游草稿 {writtenCount} 条，回话失败 {failedCount} 次）；停止原因：{stopReason}";

            // 回话失败过就不许报「成功」——这条链路的产出就是「用户收到了回复」，
            // 没收到而账上是绿的，正是这次翻车的根因。
            return failedCount > 0
                ? CommandResult.Failure(summary, lines)
                : CommandResult.Success(summary, lines);
        }

        /// <summary>
        /// 跑一轮：读消息 → 组提示 → 调执行后端 → 处置 → 回话 → 写下游。
        /// 一轮里任何一步失败都只影响这一轮，绝不把整个常驻循环带崩（决策 83 同源）。
        /// </summary>
        private static IReadOnlyList<string> RunOneTurn(
            string repositoryRoot,
            string poolRoot,
            string assistantDriver,
            string backendDriver,
            PoolSchema schema,
            string signalFilePath,
            AssistantServeArguments arguments,
            out bool wroteDownstream,
            out bool replyDelivered,
            out bool replyRetryable)
        {
            wroteDownstream = false;
            // 默认「没送出去、可重试」：任何一条没走到回话那一步就返回的路径，
            // 都不该被当成「回过了」而归档进 processed。
            replyDelivered = false;
            replyRetryable = true;
            var lines = new List<string>();

            if (!AssistantConversationMessage.TryReadFile(signalFilePath, out var message, out var readReason))
            {
                lines.Add($"这条消息读不了：{readReason}");
                AppendLedger(repositoryRoot, new JsonObject
                {
                    ["时间"] = DateTimeOffset.Now.ToString("o"),
                    ["信号"] = Path.GetFileName(signalFilePath),
                    ["结果"] = "读不了",
                    ["原因"] = readReason
                });
                // 读不动的信号重投多少次都还是读不动，判成不可重试，直接进隔离目录。
                replyRetryable = false;
                return lines;
            }

            lines.Add($"收到消息：会话={message.ConversationIdentifier}　类型={message.MessageKind}　字数={message.Text.Length}");

            if (!message.IsHandleableText)
            {
                var text = "我这边现在只认文字消息，这一条是「" + (message.MessageKind.Length == 0 ? "未知类型" : message.MessageKind) + "」，没法处理。";
                lines.Add("不是可处理的文字消息，回一句说明");
                var kindReply = SendReply(repositoryRoot, assistantDriver, message, text, arguments, lines);
                replyDelivered = kindReply.Delivered;
                replyRetryable = kindReply.Retryable;
                AppendLedger(repositoryRoot, new JsonObject
                {
                    ["时间"] = DateTimeOffset.Now.ToString("o"),
                    ["信号"] = Path.GetFileName(signalFilePath),
                    ["结果"] = "非文字消息",
                    ["消息类型"] = message.MessageKind
                });
                return lines;
            }

            var prompt = AssistantServePrompt.Build(repositoryRoot, assistantDriver, message.Text);
            lines.Add($"提示词：{prompt.PromptText.Length} 字，版本 {prompt.PromptVersion}，知识文件 {prompt.KnowledgeFileCount} 份");
            if (prompt.DegradedReason.Length > 0)
            {
                lines.Add($"知识降级：{prompt.DegradedReason}");
            }

            if (arguments.DryRun)
            {
                lines.Add("干跑：没有调执行后端、没有回话、没有写下游");
                replyDelivered = true;
                return lines;
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["提示"] = prompt.PromptText,
                ["上下文"] = AssistantServePrompt.SystemContextText
            });

            var call = BridgeInvoker.Invoke(repositoryRoot, backendDriver, "complete", payload, arguments.TimeoutSeconds);
            if (!call.Succeeded)
            {
                var text = "我这边调执行后端失败了（" + call.ErrorCode + "），这一轮没建任何东西。原因：" + call.HumanText;
                lines.Add($"执行后端调用失败（{call.ErrorCode}）：{call.HumanText}");
                var backendReply = SendReply(repositoryRoot, assistantDriver, message, text, arguments, lines);
                replyDelivered = backendReply.Delivered;
                replyRetryable = backendReply.Retryable;
                AppendLedger(repositoryRoot, new JsonObject
                {
                    ["时间"] = DateTimeOffset.Now.ToString("o"),
                    ["信号"] = Path.GetFileName(signalFilePath),
                    ["结果"] = "执行后端失败",
                    ["错误码"] = call.ErrorCode
                });
                return lines;
            }

            var modelText = ReadPayloadString(call.Payload, "文本");
            var modelName = ReadPayloadString(call.Payload, "模型");
            AssistantServeReply.TryParse(modelText, out var reply);
            lines.Add($"执行后端回答：模型={modelName}　读懂了={reply.Parsed}　要建需求={reply.WantsRequirement}");

            var outcome = AssistantServeTurn.Decide(repositoryRoot, poolRoot, message, reply, schema, DateTimeOffset.Now);
            if (outcome.BlockedFields.Count > 0)
            {
                lines.Add($"越权字段已挡：{string.Join("、", outcome.BlockedFields)}");
            }

            foreach (var finding in outcome.Findings)
            {
                lines.Add($"校验发现：{finding.Reason}");
            }

            if (outcome.ShouldWriteDownstream && arguments.WriteDownstream)
            {
                var draftPath = AssistantServeTurn.SaveDraft(repositoryRoot, outcome.RequirementIdentifier, outcome.Draft);
                lines.Add($"草稿留底：{(draftPath.Length == 0 ? "写失败" : draftPath)}");

                var records = new JsonArray { JsonNode.Parse(outcome.Draft.ToJsonString(WriteOptions)) };
                var pushPayload = JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["干跑"] = false,
                    ["记录"] = records,
                    ["幂等键字段"] = "id"
                });

                var pushCall = BridgeInvoker.Invoke(repositoryRoot, assistantDriver, "push", pushPayload, arguments.TimeoutSeconds);
                if (pushCall.Succeeded)
                {
                    wroteDownstream = true;
                    lines.Add($"已写下游草稿：{outcome.RequirementIdentifier}");

                    // 真建了东西才叫醒引擎——没建就叫醒等于让引擎白跑一轮。
                    var wakePath = WakeSignalSource.Emit(
                        repositoryRoot,
                        "助手产出草稿",
                        new JsonObject { ["需求id"] = outcome.RequirementIdentifier, ["来自会话"] = message.ConversationIdentifier },
                        DateTimeOffset.Now);
                    lines.Add($"已投唤醒信号：{(wakePath.Length == 0 ? "写失败" : Path.GetFileName(wakePath))}");
                }
                else
                {
                    lines.Add($"写下游失败（{pushCall.ErrorCode}）：{pushCall.HumanText}");
                }
            }
            else if (outcome.ShouldWriteDownstream)
            {
                lines.Add($"校验通过但没开写表开关（--write-downstream false），草稿 {outcome.RequirementIdentifier} 只在回话里说了");
            }

            var finalReply = SendReply(repositoryRoot, assistantDriver, message, outcome.ReplyText, arguments, lines);
            replyDelivered = finalReply.Delivered;
            replyRetryable = finalReply.Retryable;
            AppendLedger(repositoryRoot, new JsonObject
            {
                ["时间"] = DateTimeOffset.Now.ToString("o"),
                ["信号"] = Path.GetFileName(signalFilePath),
                ["结果"] = outcome.ShouldWriteDownstream ? "校验通过" : "没建需求",
                ["回话送出"] = finalReply.Delivered,
                ["需求id"] = outcome.RequirementIdentifier,
                ["模型"] = modelName,
                ["提示词版本"] = prompt.PromptVersion,
                ["写了下游"] = wroteDownstream,
                ["回话"] = outcome.ReplyText,
                ["校验发现数"] = outcome.Findings.Count
            });

            return lines;
        }

        /// <summary>
        /// 一次回话的结果：送没送出去、失败的话值不值得重试。
        /// 这个返回值是**信号处置的依据**——回不出话的消息不许当成「已处理」归档。
        /// </summary>
        private sealed class ReplyOutcome
        {
            /// <summary>构造一次回话结果。</summary>
            /// <param name="delivered">回话是否真送出去了；干跑视为送到（干跑本就不发）。</param>
            /// <param name="retryable">没送出去时，这个失败值不值得下一轮再试。</param>
            public ReplyOutcome(bool delivered, bool retryable)
            {
                Delivered = delivered;
                Retryable = retryable;
            }

            /// <summary>回话是否真送出去了。</summary>
            public bool Delivered { get; }

            /// <summary>没送出去时，这个失败值不值得重试。</summary>
            public bool Retryable { get; }
        }

        /// <summary>回一句话：干跑时只打印，真跑时经助手 driver 的 reply 动作发出去。</summary>
        private static ReplyOutcome SendReply(
            string repositoryRoot,
            string assistantDriver,
            AssistantConversationMessage message,
            string text,
            AssistantServeArguments arguments,
            List<string> lines)
        {
            if (arguments.DryRun)
            {
                lines.Add("干跑：本该回的话是「" + Shorten(text) + "」");
                return new ReplyOutcome(true, false);
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = false,
                ["会话标识"] = message.ConversationIdentifier,
                ["文本"] = text
            });

            var call = BridgeInvoker.Invoke(repositoryRoot, assistantDriver, "reply", payload, arguments.TimeoutSeconds);
            lines.Add(call.Succeeded
                ? "已回话：" + Shorten(text)
                : $"回话失败（{call.ErrorCode}）：{call.HumanText}");
            return new ReplyOutcome(call.Succeeded, call.Retryable);
        }

        /// <summary>把一轮记进会话流水：&lt;仓库根&gt;/_Tasks/conversations/ledger.jsonl，一行一条，只追加。</summary>
        private static void AppendLedger(string repositoryRoot, JsonObject record)
        {
            try
            {
                var directory = Path.Combine(repositoryRoot, "_Tasks", "conversations");
                Directory.CreateDirectory(directory);
                var filePath = Path.Combine(directory, "ledger.jsonl");
                File.AppendAllText(filePath, record.ToJsonString(WriteOptions) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>读响应载荷里字符串键的值；缺失或类型不对给空串。</summary>
        private static string ReadPayloadString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        /// <summary>回话文本的单行预览，超过 80 字截断。</summary>
        private static string Shorten(string text)
        {
            var single = (text ?? "").Replace("\r", " ").Replace("\n", " ");
            return single.Length <= 80 ? single : single.Substring(0, 80) + "…";
        }
    }
}
