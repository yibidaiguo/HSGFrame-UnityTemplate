using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// 人点了「一键建需求」之后，要不要真写下游草稿表。默认 false：真回话与真写表分成两级开关。
        /// **这个开关不再决定「什么时候写」**——什么时候写由人点按钮决定，它只决定「许不许写」。
        /// </summary>
        [Summary("人点了创建按钮之后要不要真写下游草稿表。默认 false")]
        [DefaultValue(false)]
        public bool WriteDownstream { get; set; }

        /// <summary>执行后端调用超时秒数。</summary>
        [Summary("执行后端调用超时秒数")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }

        /// <summary>
        /// 出功能图这一步钉死用哪个模型；留空走本机配置那一档。
        ///
        /// **单给这一步留一个口子是有来由的**：聊天一轮几百字，轻量档模型又快又便宜；
        /// 而出功能图要一口气吐出一份几十个字段的 JSON——轻量档在这种长结构化输出上
        /// 会把预算花在推理里、回一段空 content，报出来是「执行后端回了空文本」，
        /// 指不到「这个模型干不了这活」上。为这一步单独换个强档，比把整条会话都抬上去省。
        /// </summary>
        [Summary("出功能图这一步钉死用哪个模型；留空走本机配置")]
        [DefaultValue("")]
        public string InterfaceDraftModel { get; set; }
    }

    /// <summary>
    /// 助手 port 的 B 形态：常驻会话（子文档 05 §一「助手：package / serve」）。
    ///
    /// 一轮干五件事，顺序不许换：
    /// 1. 从会话目录取一条消息（下游旁路投进来的，已归一成「会话」块）；
    /// 2. **按钮点击走自己的分支**，不过执行后端——它带的是明确动作，问模型只会多一次不确定；
    /// 3. 文字消息：读这条会话的历史 → 连同这句话一起交给执行后端 → 变成「回话 + 需求草稿」；
    /// 4. **现场跑 req.validate**，不过就不摆确认卡，把校验发现翻成人话；
    /// 5. 回一张卡（至少带「开新话题」按钮）。草稿齐了只留底等人点，
    ///    **建发生在人点「一键建需求」那一刻**：写需求池 → 投唤醒信号 → 出需求文档并推成
    ///    知识库节点 → 任务表加一行挂上文档链接，一路做完才算「一键」。不派活——那是 PM 的事。
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

        /// <summary>写进池子的 JSON 选项：**缩进**。池子里那份要给人读、也要能看 git diff。</summary>
        private static readonly JsonSerializerOptions PoolWriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

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
                poolRoot = ResolvePoolRoot(arguments);
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

            // 按钮点击走自己的分支，**不过执行后端**：它带的是一个明确的动作，
            // 再去问一遍模型只会平白多一次不确定与一次花销。
            if (message.IsCardAction)
            {
                return HandleCardAction(
                    repositoryRoot,
                    poolRoot,
                    assistantDriver,
                    backendDriver,
                    schema,
                    signalFilePath,
                    message,
                    arguments,
                    lines,
                    out wroteDownstream,
                    out replyDelivered,
                    out replyRetryable);
            }

            if (!message.IsHandleable)
            {
                // 走到这儿的是**真的什么都没有**：正文空、附件也空（表情包、语音落在这儿）。
                // 带图带文件的消息不在这一支——那些有话要说，归一那一步已经把附件摆出来了。
                var text = "这一条我没读到任何内容（类型「"
                    + (message.MessageKind.Length == 0 ? "未知" : message.MessageKind)
                    + "」，既没有文字也没有图片或文件），没法处理。直接打字告诉我要什么就行。";
                lines.Add("消息里既没有正文也没有附件，回一句说明");
                var kindReply = SendReply(repositoryRoot, assistantDriver, message, text, arguments, lines, null);
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

            // 打字说「开新话题」与点按钮等价：回调链路没通、卡片被折叠时，人只会打字。
            if (AssistantServeTurn.LooksLikeNewTopic(message.Text))
            {
                return StartNewTopic(repositoryRoot, assistantDriver, signalFilePath, message, arguments, lines, out replyDelivered, out replyRetryable);
            }

            // **模型一开始想，上一张卡的按钮就撤掉。**
            // 飞书的卡片点完不会消失、翻上去还能点：聊到第十轮时第三轮那张「一键建需求」
            // 还亮着，手一滑点下去，建出来的是三轮之前那份早就聊废了的草稿；
            // 出图那种更贵——翻上去点一下就是又一批图，真花钱。
            // 撤在这里而不是等这一轮跑完：人等回复的那几十秒，恰恰最容易去点上面那张旧卡。
            RetireStaleCard(repositoryRoot, assistantDriver, message.ConversationIdentifier, arguments, lines);

            // 人发的图片与文件先取回本地：那张参考图**就是这句话的一半意思**，
            // 少了它，模型看到的只有「再出一张，可以参考」，参考什么无从谈起。
            var attachments = FetchAttachments(repositoryRoot, assistantDriver, message, arguments, lines);

            var history = AssistantConversationHistory.Read(repositoryRoot, message.ConversationIdentifier);
            var historyText = AssistantConversationHistory.RenderForPrompt(history);
            if (history.Count > 0)
            {
                lines.Add($"带上下文：{history.Count} 轮，{historyText.Length} 字");
            }

            // 用户这句话先进历史再组提示：这一轮崩在半路时，人说过的话也已经留住了。
            // 提示词里那句话来自 message.Text 本身，不来自历史，所以先写不会重一遍。
            AssistantConversationHistory.Append(
                repositoryRoot,
                message.ConversationIdentifier,
                AssistantHistoryTurn.UserRole,
                message.Text,
                DateTimeOffset.Now);

            // 图片直接喂给模型看，别的文件只能以文字交代——那条链路吃的是「多模态内容数组」，
            // 一份 psd 塞进去下游只会报一句看不懂的错。不交代的话，人甩过来一份配置表配一句
            // 「按这个做」，模型看到的只有那句话，那份表等于没发过。
            var userText = message.Text;
            if (attachments.FileNotes.Count > 0)
            {
                userText = (userText.Length > 0 ? userText + "\n" : "")
                    + "（他还发了这些文件，我看不了里面的内容，只知道存在哪：\n- "
                    + string.Join("\n- ", attachments.FileNotes)
                    + "\n要用到里面的内容就直说，让人贴出来或者转成图。）";
            }

            var prompt = AssistantServePrompt.Build(repositoryRoot, assistantDriver, userText, historyText);
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

            var payloadObject = new JsonObject
            {
                ["提示"] = prompt.PromptText,
                ["上下文"] = AssistantServePrompt.SystemContextText
            };

            // 图片喂给模型看。只喂图，不喂别的文件：那条链路是「多模态内容数组」，
            // 一份 psd 塞进去下游只会报一句看不懂的错。别的文件在提示词里以文字交代。
            if (attachments.ImagePaths.Count > 0)
            {
                var imageArray = new JsonArray();
                foreach (var imagePath in attachments.ImagePaths)
                {
                    imageArray.Add(imagePath);
                }

                payloadObject["图片"] = imageArray;
                lines.Add($"随消息带了 {attachments.ImagePaths.Count} 张图，一并给模型看");
            }

            var payload = JsonSerializer.SerializeToElement(payloadObject);

            var call = BridgeInvoker.Invoke(repositoryRoot, backendDriver, "complete", payload, arguments.TimeoutSeconds);
            if (!call.Succeeded)
            {
                var text = "我这边调执行后端失败了（" + call.ErrorCode + "），这一轮没建任何东西。原因：" + call.HumanText;
                lines.Add($"执行后端调用失败（{call.ErrorCode}）：{call.HumanText}");
                var backendReply = SendReply(repositoryRoot, assistantDriver, message, text, arguments, lines, null);
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

            // **读代码是一次中途取材，不是一种产出。** 模型点名要几个文件，
            // 引擎读完贴回提示词再问一遍，最终产出还是需求 / 图 / 策划案那几种之一。
            //
            // **只追问一次**：模型拿到代码还说要读，就如实告诉它一轮只读一次，
            // 让它照手上的材料回答。不设这道闸的话，一句「我再看看那个文件」
            // 能把一轮聊成十几次调用，而人在飞书那头只看到助手一直不吭声。
            if (reply.WantsReadCode)
            {
                var read = ProjectCodeReader.Read(repositoryRoot, reply.ReadCodeFiles);
                lines.Add($"读代码：要 {reply.ReadCodeFiles.Count} 个，读到 {read.ReadPaths.Count} 个");
                foreach (var note in read.Notes)
                {
                    lines.Add("  " + note);
                }

                payloadObject["提示"] = AssistantServePrompt.AppendCodeReading(
                    payloadObject["提示"]?.GetValue<string>() ?? "", read.Text, read.Notes);

                var second = BridgeInvoker.Invoke(
                    repositoryRoot,
                    backendDriver,
                    "complete",
                    JsonSerializer.SerializeToElement(payloadObject),
                    arguments.TimeoutSeconds);

                if (!second.Succeeded)
                {
                    lines.Add($"读完代码再问那一次失败（{second.ErrorCode}）：{second.HumanText}");
                }
                else
                {
                    var secondText = ReadPayloadString(second.Payload, "文本");
                    if (AssistantServeReply.TryParse(secondText, out var secondReply))
                    {
                        reply = secondReply;
                        lines.Add($"读完代码再答：读懂了={reply.Parsed}　要建需求={reply.WantsRequirement}");

                        if (reply.WantsReadCode)
                        {
                            lines.Add("模型拿到代码还要再读——一轮只读一次，按手上的材料往下走");
                        }
                    }
                    else
                    {
                        lines.Add("读完代码再答那一份读不懂，沿用第一次的回答");
                    }
                }
            }

            // 「上次拆得不对」这一支不走需求也不走出图：它改的是已经拆出来的那些层。
            if (reply.WantsRecut)
            {
                return HandleRecut(
                    repositoryRoot, assistantDriver, backendDriver, signalFilePath, message, reply,
                    arguments, lines, out replyDelivered, out replyRetryable);
            }

            // 参考图钉进出图请求本身，**必须赶在算请求 key 之前**：
            // key 是请求内容的哈希，换一张参考图就该是另一个请求。
            // 事后再塞的话，同样一句「照这个再出一张」配两张不同的参考图会撞成同一个 key，
            // 第二次直接被幂等挡掉——人只会看到「这张图刚才出过了」，而他给的是新图。
            if (reply.WantsImage && reply.ImageRequest != null && attachments.ImagePaths.Count > 0)
            {
                reply.ImageRequest["参考图"] = attachments.ImagePaths[0];
                lines.Add($"出图请求带参考图：{attachments.ImagePaths[0]}");
            }

            // 「要一份模块策划案」这一支既不走需求也不走出图：策划案是模块的正本，
            // 内容全是从正本投影出来的，模型只需要说清是哪个模块。
            // **不必先有需求**——逼人先建一条需求才能拿到模块正本，
            // 等于为了记录现状先编一件事出来。
            if (reply.WantsPlan)
            {
                var planCard = AssistantCard.ForPlanRequest(reply.PlanModule, reply.ReplyText);
                var planSent = SendReply(
                    repositoryRoot, assistantDriver, message, planCard.ToPlainText(), arguments, lines, planCard);
                replyDelivered = planSent.Delivered;
                replyRetryable = planSent.Retryable;

                AssistantConversationHistory.Append(
                    repositoryRoot, message.ConversationIdentifier, AssistantHistoryTurn.AssistantRole,
                    reply.ReplyText, DateTimeOffset.Now);

                AppendLedger(repositoryRoot, new JsonObject
                {
                    ["时间"] = DateTimeOffset.Now.ToString("o"),
                    ["信号"] = Path.GetFileName(signalFilePath),
                    ["结果"] = "策划案待确认",
                    ["模块"] = reply.PlanModule,
                    ["回话送出"] = planSent.Delivered
                });

                return lines;
            }

            var outcome = AssistantServeTurn.Decide(repositoryRoot, poolRoot, message, reply, schema, DateTimeOffset.Now);
            if (outcome.BlockedFields.Count > 0)
            {
                lines.Add($"越权字段已挡：{string.Join("、", outcome.BlockedFields)}");
            }

            foreach (var finding in outcome.Findings)
            {
                lines.Add($"校验发现：{finding.Reason}");
            }

            // 出图请求也留底：按钮点下去要按 id 读回「画什么」，与需求草稿同一套路。
            if (outcome.ImageRequestReady)
            {
                var imagePath = AssistantServeTurn.SaveDraft(
                    repositoryRoot, outcome.ImageRequestIdentifier, outcome.ImageRequest);
                lines.Add($"出图请求留底待确认：{(imagePath.Length == 0 ? "写失败" : imagePath)}");
            }

            // 草稿齐了只留底、不写表：留底那份就是卡片上那一版，人点按钮时按 id 读回它。
            // 「什么时候写」从此归人管，引擎只管「许不许写」（--write-downstream）。
            if (outcome.DraftReady)
            {
                var draftPath = AssistantServeTurn.SaveDraft(repositoryRoot, outcome.RequirementIdentifier, outcome.Draft);
                lines.Add($"草稿留底待确认：{(draftPath.Length == 0 ? "写失败" : draftPath)}");
                if (draftPath.Length == 0)
                {
                    // 留底写不下去，按钮就找不回这份草稿。与其发一张点了会失败的卡，不如当场说清楚。
                    lines.Add("草稿留底写失败，这一轮不发确认卡");
                }
            }

            var finalReply = SendReply(
                repositoryRoot,
                assistantDriver,
                message,
                outcome.ReplyText,
                arguments,
                lines,
                outcome.Card);
            replyDelivered = finalReply.Delivered;
            replyRetryable = finalReply.Retryable;

            AssistantConversationHistory.Append(
                repositoryRoot,
                message.ConversationIdentifier,
                AssistantHistoryTurn.AssistantRole,
                outcome.ReplyText,
                DateTimeOffset.Now);

            AppendLedger(repositoryRoot, new JsonObject
            {
                ["时间"] = DateTimeOffset.Now.ToString("o"),
                ["信号"] = Path.GetFileName(signalFilePath),
                ["结果"] = outcome.DraftReady ? "草稿待确认" : "还在聊",
                ["回话送出"] = finalReply.Delivered,
                ["需求id"] = outcome.RequirementIdentifier,
                ["模型"] = modelName,
                ["提示词版本"] = prompt.PromptVersion,
                ["写了下游"] = wroteDownstream,
                ["回话"] = outcome.ReplyText,
                ["带了几轮上下文"] = history.Count,
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

        /// <summary>
        /// 处理一次卡片按钮点击。两个动作：
        /// - **开新话题**：往历史里插一条分隔线，之后的轮次读不到分隔线之前的内容。
        /// - **创建需求**：按 id 读回当初摆在卡上的草稿 → 再校验一遍 → 写下游 → 叫醒引擎 → 记台账。
        ///
        /// 草稿从留底读、不从按钮携带读：按钮携带是从客户端回来的数据，改得动；
        /// 留底那份是引擎自己写的，才是当时给人看的那一版。
        /// </summary>
        private static IReadOnlyList<string> HandleCardAction(
            string repositoryRoot,
            string poolRoot,
            string assistantDriver,
            string backendDriver,
            PoolSchema schema,
            string signalFilePath,
            AssistantConversationMessage message,
            AssistantServeArguments arguments,
            List<string> lines,
            out bool wroteDownstream,
            out bool replyDelivered,
            out bool replyRetryable)
        {
            wroteDownstream = false;
            lines.Add($"卡片按钮：动作={message.ActionName}");

            // 点过的那张卡也算翻篇：**按钮点完不会自己消失**，留着就还能再点一次。
            // 出图与拆图那两支自己会换卡（要报进度），这里撤的是别的动作留下的按钮。
            // 撤的是台账里记的那张——正常情况下就是人刚点的这张。
            RetireStaleCard(repositoryRoot, assistantDriver, message.ConversationIdentifier, arguments, lines);

            if (string.Equals(message.ActionName, AssistantCard.NewTopicAction, StringComparison.Ordinal))
            {
                return StartNewTopic(repositoryRoot, assistantDriver, signalFilePath, message, arguments, lines, out replyDelivered, out replyRetryable);
            }

            if (string.Equals(message.ActionName, AssistantCard.PlanAction, StringComparison.Ordinal))
            {
                return HandlePlanDraft(
                    repositoryRoot, poolRoot, assistantDriver, signalFilePath, message, arguments, lines,
                    out replyDelivered, out replyRetryable);
            }

            if (string.Equals(message.ActionName, AssistantCard.CutAction, StringComparison.Ordinal)
                || string.Equals(message.ActionName, AssistantCard.ConfirmCutAction, StringComparison.Ordinal))
            {
                // 确认那一支走同一条路，只是这一次不再问「要花这些钱，确定吗」。
                return HandleCut(
                    repositoryRoot, assistantDriver, backendDriver, signalFilePath, message, arguments, lines,
                    out replyDelivered, out replyRetryable,
                    confirmed: string.Equals(message.ActionName, AssistantCard.ConfirmCutAction, StringComparison.Ordinal));
            }

            if (string.Equals(message.ActionName, AssistantCard.InterfaceAction, StringComparison.Ordinal))
            {
                return HandleInterfaceDraft(
                    repositoryRoot, poolRoot, assistantDriver, signalFilePath, message, arguments, lines,
                    out replyDelivered, out replyRetryable);
            }

            if (string.Equals(message.ActionName, AssistantCard.GenerateAction, StringComparison.Ordinal))
            {
                return HandleGenerate(
                    repositoryRoot, assistantDriver, signalFilePath, message, arguments, lines, out replyDelivered, out replyRetryable);
            }

            if (!string.Equals(message.ActionName, AssistantCard.CreateAction, StringComparison.Ordinal))
            {
                var unknownText = "这个按钮我不认识（动作「" + message.ActionName + "」），什么都没做。";
                var unknownReply = SendReply(repositoryRoot, assistantDriver, message, unknownText, arguments, lines, null);
                replyDelivered = unknownReply.Delivered;
                replyRetryable = unknownReply.Retryable;
                AppendLedger(repositoryRoot, new JsonObject
                {
                    ["时间"] = DateTimeOffset.Now.ToString("o"),
                    ["信号"] = Path.GetFileName(signalFilePath),
                    ["结果"] = "不认识的按钮",
                    ["动作"] = message.ActionName
                });
                return lines;
            }

            var identifier = message.ReadActionValue("需求id");
            string replyText;
            AssistantCard card = null;
            var result = "建需求失败";

            if (AssistantServeTurn.IsConfirmed(repositoryRoot, identifier))
            {
                // 卡片会一直挂在聊天记录里，隔天再点一次是常事。
                replyText = identifier + " 之前已经建过了，这次没有重复建。要改它就直接说要改哪里。";
                result = "已建过";
            }
            else if (!AssistantServeTurn.TryLoadDraft(repositoryRoot, identifier, out var draft, out var loadReason))
            {
                replyText = "这条我建不了：" + loadReason;
            }
            else
            {
                // 建之前再校验一遍。留底是这个进程写的没错，但中间可能换了 schema 版本——
                // 拿一份过不了校验的记录去写下游，下游收下了才是真麻烦。
                // **拿占位号去校验，不拿草稿 key**：草稿 key 是内容哈希（REQ-draft-xxxxxx），
                // 校验器要比对「id 字段」与「所在目录名」，拿它当目录名的话必然对不上——
                // 而草稿里的 id 一直是占位号 REQ-0000。真号在下面落池子那一刻才发。
                // 整理草稿那一步早就这么做了，点按钮这一次漏了，于是每次点都被判「过不了校验」。
                var findings = AssistantServeTurn.Validate(
                    draft, AssistantServeTurn.ValidationPlaceholderIdentifier, schema);
                if (findings.Count > 0)
                {
                    var builder = new StringBuilder();
                    builder.Append("这条现在过不了校验，没建：\n");
                    foreach (var finding in findings)
                    {
                        builder.Append("· ").Append(finding.Reason).Append("　修复：").Append(finding.FixAction).Append('\n');
                        lines.Add($"确认时校验发现：{finding.Reason}");
                    }

                    replyText = builder.ToString().TrimEnd();
                }
                else if (arguments.DryRun || !arguments.WriteDownstream)
                {
                    var why = arguments.DryRun ? "--dry-run true" : "--write-downstream false";
                    replyText = "本机是只读模式（" + why + "），" + identifier + " 没有真建。开了写表开关再点一次。";
                    result = "只读模式没写";
                    lines.Add($"只读模式（{why}），按钮点了但什么都没写");
                }
                else
                {
                    // **真正的 REQ 号在这一刻才发**：`identifier` 是草稿 key（内容哈希），
                    // 只用来按钮携带与留底归档；号要等这条需求真进池子时才有意义。
                    // 从前在整理草稿那一刻就发号，于是聊十轮发十个号、池子里一条都没有，
                    // 人看到 REQ-0002 会以为前面还有个 REQ-0001，去池子里翻却翻不着。
                    var poolIdentifier = AssistantServeTurn.AllocatePoolIdentifier(poolRoot);
                    lines.Add($"发号：{poolIdentifier}（草稿 key {identifier}）");

                    // 一路做完：写池子 → 出文档 → 推知识库 → 任务表加一行。
                    // 池子是第一步也是最要紧的一步——它是事实源，后面几步都是它的视图，
                    // 哪一步挂了都不影响「这条需求已经立住了」。
                    if (!TryLandRequirement(poolRoot, poolIdentifier, draft, lines, out var landFailure))
                    {
                        replyText = poolIdentifier + " 没建成：写需求池失败——" + landFailure + "。再点一次可以重试。";
                    }
                    else
                    {
                        wroteDownstream = true;
                        result = "已建需求";

                        // 记一笔「这条会话在做哪条需求」：**拆图靠它认出该照哪份界面规格切**。
                        // 资产那边挂的是无主哨兵号（助手聊出来的图本来就不挂需求），
                        // 所以这条线索只能从会话这一侧留。记不上不影响这一步，
                        // 只是拆图那会儿会退回「看图猜元素」那条老路。
                        if (!AssistantServeTurn.RememberConversationRequirement(
                            repositoryRoot, message.ConversationIdentifier, poolIdentifier))
                        {
                            lines.Add("会话需求留底写失败——拆图那步会退回看图猜元素");
                        }

                        // 台账先记：记不上就等于下次点还会再来一遍。
                        var recorded = AssistantServeTurn.RecordConfirmed(
                            repositoryRoot,
                            identifier,
                            message.ConversationIdentifier,
                            message.SenderIdentifier,
                            DateTimeOffset.Now);
                        if (!recorded)
                        {
                            lines.Add("已确认台账写失败——再点一次会重复建，需要人看一眼磁盘");
                        }

                        // 下游一律用**真号**：文档节点名、任务行的需求 id、回话里报的号，
                        // 都是人往后拿来查这条需求的东西。只有幂等台账继续用草稿 key——
                        // 它要回答的是「这张卡片点过没有」，那跟池子里叫什么号无关。
                        var wakePath = WakeSignalSource.Emit(
                            repositoryRoot,
                            "助手产出草稿",
                            new JsonObject { ["需求id"] = poolIdentifier, ["来自会话"] = message.ConversationIdentifier },
                            DateTimeOffset.Now);
                        lines.Add($"已投唤醒信号：{(wakePath.Length == 0 ? "写失败" : Path.GetFileName(wakePath))}");

                        var documentLink = PublishDocument(repositoryRoot, poolRoot, poolIdentifier, arguments, lines, out var documentFailure);
                        var rowFailure = AddTaskRow(repositoryRoot, assistantDriver, poolIdentifier, draft, documentLink, arguments, lines);

                        replyText = DescribeCreation(poolIdentifier, documentLink, documentFailure, rowFailure);

                        // 建成之后问一句「要不要顺手出一份功能图」。**做成按钮不做成自动跑**：
                        // 它要调执行后端（花钱、要等），而且不是每条需求都有界面——
                        // 「把某个系统的现状纳入知识库」这种就没有。
                        card = AssistantCard.ForCreatedRequirement(replyText, poolIdentifier);
                    }
                }
            }

            var reply = SendReply(repositoryRoot, assistantDriver, message, replyText, arguments, lines, card);
            replyDelivered = reply.Delivered;
            replyRetryable = reply.Retryable;

            AssistantConversationHistory.Append(
                repositoryRoot,
                message.ConversationIdentifier,
                AssistantHistoryTurn.AssistantRole,
                replyText,
                DateTimeOffset.Now);

            AppendLedger(repositoryRoot, new JsonObject
            {
                ["时间"] = DateTimeOffset.Now.ToString("o"),
                ["信号"] = Path.GetFileName(signalFilePath),
                ["结果"] = result,
                ["回话送出"] = reply.Delivered,
                ["需求id"] = identifier,
                ["写了下游"] = wroteDownstream,
                ["回话"] = replyText
            });

            return lines;
        }

        /// <summary>
        /// 把这条需求落进池子：<c>Pools/Requirements/&lt;id&gt;/requirement.json</c>。
        ///
        /// **直接写，不绕下游**。上一版是「写下游表 → 再 pull 回来入池」，
        /// 那是因为当时下游有一张结构化的需求表、而人会在表里改它，所以「下游收下了什么」才有意义。
        /// 现在需求在下游只是一份**文档**（人看的，不回流），池子才是唯一事实源——
        /// 绕一圈只会平白多两次网络调用与两处能失败的地方。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="identifier">需求 id。</param>
        /// <param name="draft">补全后的草稿。</param>
        /// <param name="lines">这一轮的日志行。</param>
        /// <param name="failureReason">失败原因；成功时为空串。</param>
        private static bool TryLandRequirement(
            string poolRoot,
            string identifier,
            JsonObject draft,
            List<string> lines,
            out string failureReason)
        {
            failureReason = "";
            try
            {
                // **把占位号换成真号**：草稿里那个 id 是给校验器看的占位（REQ-0000），
                // 落池子这一刻才有真号。不覆盖的话，池子里那份的 id 与目录名对不上，
                // 池子校验门禁当场判红——而红的原因跟「建需求」看着毫无关系。
                draft["id"] = identifier;

                var directory = PoolPaths.RequirementDirectory(poolRoot, identifier);
                Directory.CreateDirectory(directory);
                var filePath = PoolPaths.RequirementFile(poolRoot, identifier);
                File.WriteAllText(filePath, draft.ToJsonString(PoolWriteOptions), new UTF8Encoding(false));
                lines.Add($"已写进需求池：{filePath}");
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                failureReason = exception.Message;
                lines.Add($"写需求池失败：{exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// 出文档并推上去：<c>doc.render</c> 生成 index.md，<c>doc.push</c> 推成知识库里的一个节点，
        /// 回来的链接挂到任务行上。
        ///
        /// 推失败**不算这条需求没建成**——需求已经在池子里了，文档只是它的一个视图。
        /// 所以这里不返回成败，只把「有没有链接」与「为什么没有」分别交出去，由回话如实说。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="identifier">需求 id。</param>
        /// <param name="arguments">常驻会话命令参数。</param>
        /// <param name="lines">这一轮的日志行。</param>
        /// <param name="failureReason">没推成时的原因；推成了为空串。</param>
        private static string PublishDocument(
            string repositoryRoot,
            string poolRoot,
            string identifier,
            AssistantServeArguments arguments,
            List<string> lines,
            out string failureReason)
        {
            failureReason = "";

            // **推之前先确保下游对象在**：这是「第一次触发时没有就自动建、有就沿用」那条规矩
            // （子文档 02 §五之二）。不先跑这一步的话，人在飞书里删过一次东西之后，
            // 这条链会拿着台账里那个死 id 去推文档，回一句指不到真因的下游报错。
            //
            // ensure 自己是幂等的：对象还在就只验一下、什么都不建，所以每次都跑不亏。
            // 它失败**不打断**这一轮——文档推不推得上下面会如实报，
            // 而把「补建对象失败」当成「需求没建成」是两回事。
            EnsureDownstreamObjects(repositoryRoot, arguments, lines);

            var render = RequirementDocCommands.Render(new RequirementDocRenderArguments
            {
                RequirementIdentifier = identifier,
                RepositoryRoot = repositoryRoot,
                PoolRoot = poolRoot,
                DryRun = false
            });
            lines.Add($"出需求文档：{render.Message}");
            if (!render.IsSuccess)
            {
                failureReason = render.Message;
                return "";
            }

            var push = RequirementDocCommands.Push(new RequirementDocPushArguments
            {
                RequirementIdentifier = identifier,
                RepositoryRoot = repositoryRoot,
                PoolRoot = poolRoot,
                DryRun = false,
                TimeoutSeconds = arguments.TimeoutSeconds
            });
            lines.Add($"推需求文档：{push.Message}");
            if (!push.IsSuccess)
            {
                failureReason = push.Message;
                return "";
            }

            // 链接由推送回写进 index.md 的 frontmatter「同步」块，从那儿读回来最可靠——
            // 它是「真推上去的那一份」的地址，不是我们拼出来的。
            var link = ReadDocumentLink(poolRoot, identifier, repositoryRoot);
            if (link.Length == 0)
            {
                failureReason = "推上去了但没读回文档链接";
            }

            return link;
        }

        /// <summary>
        /// 池子根目录：参数给了用参数的，没给用当前目录下的 Pools。
        ///
        /// 单独抽出来是因为**这一处解析有两个调用点**（起会话时、拆图时），
        /// 各写一遍的话，哪天有人只改了其中一处，拆图就会去另一个池子里找需求，
        /// 而那种错找起来极难——两边都「有池子」，只是不是同一个。
        /// </summary>
        /// <param name="arguments">常驻会话命令参数。</param>
        private static string ResolvePoolRoot(AssistantServeArguments arguments)
        {
            return Path.GetFullPath(
                string.IsNullOrWhiteSpace(arguments?.PoolRoot) ? "Pools" : arguments.PoolRoot);
        }

        /// <summary>从需求文档的同步块里读回文档链接；读不到给空串。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="identifier">需求 id。</param>
        /// <param name="repositoryRoot">仓库根目录，读文档规范要用。</param>
        private static string ReadDocumentLink(string poolRoot, string identifier, string repositoryRoot)
        {
            try
            {
                var documentPath = PoolPaths.RequirementDocument(poolRoot, identifier);
                if (!File.Exists(documentPath))
                {
                    return "";
                }

                var specification = RequirementDocumentSpec.Load(repositoryRoot);
                if (!RequirementDocument.TryParse(File.ReadAllText(documentPath), specification, out var parsed, out _))
                {
                    return "";
                }

                return RequirementDocumentSyncState.Read(parsed).Link;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                return "";
            }
        }

        /// <summary>往任务表加一行，等 PM 派。失败只报原因，不影响「需求已经建成」这件事。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="assistantDriver">助手 port 路由到的 driver 名。</param>
        /// <param name="identifier">需求 id。</param>
        /// <param name="draft">补全后的草稿，取标题当任务描述。</param>
        /// <param name="documentLink">需求文档链接；没有就不挂。</param>
        /// <param name="arguments">常驻会话命令参数。</param>
        /// <param name="lines">这一轮的日志行。</param>
        private static string AddTaskRow(
            string repositoryRoot,
            string assistantDriver,
            string identifier,
            JsonObject draft,
            string documentLink,
            AssistantServeArguments arguments,
            List<string> lines)
        {
            var title = draft != null && draft.TryGetPropertyValue("标题", out var value) && value is JsonValue titleValue
                && titleValue.TryGetValue<string>(out var text)
                ? text
                : identifier;

            var payload = new JsonObject
            {
                ["干跑"] = false,
                ["需求id"] = identifier,
                ["任务描述"] = title
            };

            if (documentLink.Length > 0)
            {
                payload["需求文档链接"] = documentLink;
            }

            var call = BridgeInvoker.Invoke(
                repositoryRoot,
                assistantDriver,
                "task-row",
                JsonSerializer.SerializeToElement(payload),
                arguments.TimeoutSeconds);
            if (!call.Succeeded)
            {
                lines.Add($"加任务行失败（{call.ErrorCode}）：{call.HumanText}");
                return call.ErrorCode + "：" + call.HumanText;
            }

            lines.Add("已加任务行：" + identifier);
            return "";
        }

        /// <summary>
        /// 把「建到哪一步」翻成一句给人的话。三段分开说：需求在池子里了（这条一定成立，
        /// 因为不成立就不会走到这里）、文档推没推上去、任务行加没加上。
        /// 哪一段没成就说哪一段，不许一句「建好了」盖过去。
        /// </summary>
        /// <param name="identifier">需求 id。</param>
        /// <param name="documentLink">需求文档链接；空表示没推成。</param>
        /// <param name="documentFailure">文档那一步的失败原因。</param>
        /// <param name="rowFailure">任务行那一步的失败原因。</param>
        private static string DescribeCreation(string identifier, string documentLink, string documentFailure, string rowFailure)
        {
            var builder = new StringBuilder();
            builder.Append("建好了：").Append(identifier).Append("，已经写进需求池。");

            if (documentLink.Length > 0)
            {
                builder.Append("\n需求文档：").Append(documentLink);
            }
            else
            {
                builder.Append("\n需求文档没推上去：").Append(documentFailure.Length == 0 ? "原因不明" : documentFailure);
            }

            if (rowFailure.Length == 0)
            {
                builder.Append("\n任务表已经加了一行，等 PM 派。");
            }
            else
            {
                builder.Append("\n任务行没加上：").Append(rowFailure);
            }

            return builder.ToString();
        }

        /// <summary>
        /// 把刚写进下游表的那条需求拉回池子：读水位 → 下游 pull 落信封 → 入站 → 进水位。
        ///
        /// 走 pull 而不是拿手里那份草稿直接写池子：**池子是事实源，但「下游到底收下了什么」
        /// 只有下游说了算**。拿本地草稿绕过去，等于把「我以为写进去的」当成「真写进去的」。
        ///
        /// 带水位是为了别把整张表重拉一遍——那会把别人几个月前的旧记录一起翻出来重新入站。
        /// 水位只在入站真跑完之后才前进：中途崩了宁可下次多拉一点，也不许漏掉这一段。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="assistantDriver">助手 port 路由到的 driver 名。</param>
        /// <param name="schema">合并后的需求 schema。</param>
        /// <param name="identifier">这次要看的需求 id。</param>
        /// <param name="arguments">常驻会话命令参数。</param>
        /// <param name="lines">这一轮的日志行。</param>
        /// <param name="failureReason">整步失败的原因；成功时为空串。</param>
        private static IntakeDecision? LandInPool(
            string repositoryRoot,
            string poolRoot,
            string assistantDriver,
            PoolSchema schema,
            string identifier,
            AssistantServeArguments arguments,
            List<string> lines,
            out string failureReason)
        {
            failureReason = "";

            var watermark = SyncWatermark.Load(repositoryRoot);
            watermark.Entries.TryGetValue(assistantDriver, out var entry);
            var since = entry?.Moment ?? "";

            var pullPayload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = false,
                ["水位"] = since,
                ["输出目录"] = PoolPaths.InboxDirectory(poolRoot)
            });

            var pullCall = BridgeInvoker.Invoke(repositoryRoot, assistantDriver, "pull", pullPayload, arguments.TimeoutSeconds);
            if (!pullCall.Succeeded)
            {
                failureReason = pullCall.ErrorCode + "：" + pullCall.HumanText;
                lines.Add($"入站拉取失败（{pullCall.ErrorCode}）：{pullCall.HumanText}");
                return null;
            }

            lines.Add($"入站拉取完成：拉到 {ReadPayloadInt(pullCall.Payload, "拉到")} 条");

            IReadOnlyList<IntakeOutcome> outcomes;
            try
            {
                outcomes = RequirementIntake.Run(repositoryRoot, poolRoot, schema, DateTimeOffset.Now);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                failureReason = "入站跑失败：" + exception.Message;
                lines.Add(failureReason);
                return null;
            }

            IntakeDecision? mine = null;
            foreach (var outcome in outcomes)
            {
                lines.Add($"入站：{outcome.ToDisplayText()}");
                if (string.Equals(outcome.RequirementIdentifier, identifier, StringComparison.Ordinal))
                {
                    mine = outcome.Decision;
                }
            }

            // 水位放在入站之后前进：拉到了却没入站成，下次还得把这一段再拉一遍。
            var newWatermark = ReadPayloadString(pullCall.Payload, "新水位");
            if (newWatermark.Length > 0)
            {
                var advance = SyncWatermark.Advance(repositoryRoot, assistantDriver, newWatermark, identifier);
                lines.Add(advance.Succeeded
                    ? $"水位{(advance.Advanced ? "前进到 " + newWatermark : "没动（幂等重放）")}"
                    : $"水位没写成：{advance.FailureReason}");
            }

            return mine;
        }

        /// <summary>
        /// 人点了「出图」：读回那份出图请求 → 建资产请求 → 真去下游生图 → 把变体贴回聊天。
        ///
        /// **这一支是助手存在的另一半**：人明说要一张图时，该真去出图，
        /// 而不是把「要图」这件事整理成一条需求文档——那等于把下面整条生图链挡在门外。
        ///
        /// 图不挂需求（落进无主那一档）：人在聊天里说「先出张图看看」时往往连需求都还没有，
        /// 硬要一条需求才让出图，等于把「试一张」挡在门外。事后要认领给某条需求是挪目录的事。
        /// </summary>
        private static IReadOnlyList<string> HandleGenerate(
            string repositoryRoot,
            string assistantDriver,
            string signalFilePath,
            AssistantConversationMessage message,
            AssistantServeArguments arguments,
            List<string> lines,
            out bool replyDelivered,
            out bool replyRetryable)
        {
            var identifier = message.ReadActionValue("出图请求id");
            string replyText;
            AssistantCard card = null;
            var result = "出图失败";

            if (AssistantServeTurn.IsGenerated(repositoryRoot, identifier))
            {
                // 卡片挂在聊天记录里，人隔天再点一次是常事；出图一次是真花钱。
                replyText = "这份出图请求已经出过了，没有重出。要换个方向就直说，我按新描述再出一版。";
                result = "已出过";
                lines.Add($"出图请求 {identifier} 已在台账里，挡掉重复出图");
            }
            else if (!AssistantServeTurn.TryLoadDraft(repositoryRoot, identifier, out var request, out var loadReason))
            {
                replyText = "这张图我出不了：" + loadReason;
            }
            else if (arguments.DryRun || !arguments.WriteDownstream)
            {
                var why = arguments.DryRun ? "--dry-run true" : "--write-downstream false";
                replyText = "本机是只读模式（" + why + "），没有真去出图。开了开关再点一次。";
                result = "只读模式没出";
                lines.Add($"只读模式（{why}），按钮点了但没出图");
            }
            else
            {
                var assetType = ReadDraftString(request, "资产类型");

                // 显式点名生图 driver，不让域路由的失败转移替我们挑。
                // 转移的前提是候选之间吃同一份调用参数，而**配方名恰恰不通用**——
                // 一家的配方名转到另一家就是「找不到预设文件」。
                // 点名之后配方查得准，转移这件事留给人显式换 driver。
                var routeTable = BridgeRouteTable.Load(repositoryRoot);
                if (!routeTable.TryResolvePort("生图", out var imageDriver, out var driverReason))
                {
                    lines.Add($"生图 driver 取不到：{driverReason}");
                    replyText = "出不了图：生图这一域没有可用的下游（" + driverReason + "）";
                    result = "没有生图下游";
                }
                else
                {
                    // 参考图在留底那份请求里——按钮是隔了一会儿才点的，
                    // 当时那条消息早处理完了，只有留底还记着他给过哪张图。
                    var referenceImagePath = ReadDraftString(request, "参考图");
                    if (referenceImagePath.Length > 0 && !File.Exists(referenceImagePath))
                    {
                        // 图没了就**如实说**，不许当没给过参考图接着跑：
                        // 那样出来的图跟他给的那张没关系，钱照花，人还以为是模型不听话。
                        lines.Add($"参考图不在了：{referenceImagePath}");
                        referenceImagePath = "";
                    }

                    var route = AssetRecipeRouteTable.Load(repositoryRoot);
                    if (!route.TryResolve(imageDriver, assetType, referenceImagePath.Length > 0, out var recipeName, out var routeReason))
                    {
                        // 配方缺了就**如实说**，不许回落到别的配方——拿图标的配方去出界面底图，
                        // 出来的东西既不对又花了钱，而人还以为链路是通的。
                        lines.Add($"配方查不到：{routeReason}");
                        replyText = "这张图还出不了：" + routeReason;
                        result = "缺配方";
                    }
                    else
                    {
                        // 真跑之前先把原卡的按钮撤掉。出图要跑几十秒，那期间按钮还亮着，
                        // 连点几下就是连着出好几批——**这是会真花钱的**，不是体验问题。
                        UpdateImageCard(
                            repositoryRoot, assistantDriver, message, identifier, request,
                            "出图请求　正在出图…", "已经开始出了，跑完我把图贴上来。这期间不用再点。",
                            withButton: false, arguments: arguments, lines: lines);

                        replyText = RunGeneration(
                            repositoryRoot, request, assetType, imageDriver, recipeName, arguments, lines,
                            out card, out var generated, out var assetIdentifier, referenceImagePath);
                        result = generated ? "已出图" : "出图失败";

                        if (generated)
                        {
                            // 成功才记台账：失败那次不记，人才点得了第二次。
                            if (!AssistantServeTurn.RecordGenerated(repositoryRoot, identifier, assetIdentifier, DateTimeOffset.Now))
                            {
                                lines.Add("已出图台账写失败——再点一次会重出一批，需要人看一眼磁盘");
                            }

                            UpdateImageCard(
                                repositoryRoot, assistantDriver, message, identifier, request,
                                "出图请求　已出图", "图在下一条里（" + assetIdentifier + "）。",
                                withButton: false, arguments: arguments, lines: lines);
                        }
                        else
                        {
                            // 失败了把按钮换回来——这正是人要重试的时候。
                            UpdateImageCard(
                                repositoryRoot, assistantDriver, message, identifier, request,
                                "出图请求　没出成，可以再点", "上一次没出成，原因见下一条。改好了再点一次。",
                                withButton: true, arguments: arguments, lines: lines);
                        }
                    }
                }
            }

            var reply = SendReply(repositoryRoot, assistantDriver, message, replyText, arguments, lines, card);
            replyDelivered = reply.Delivered;
            replyRetryable = reply.Retryable;

            AssistantConversationHistory.Append(
                repositoryRoot,
                message.ConversationIdentifier,
                AssistantHistoryTurn.AssistantRole,
                replyText,
                DateTimeOffset.Now);

            AppendLedger(repositoryRoot, new JsonObject
            {
                ["时间"] = DateTimeOffset.Now.ToString("o"),
                ["信号"] = Path.GetFileName(signalFilePath),
                ["结果"] = result,
                ["回话送出"] = reply.Delivered,
                ["出图请求id"] = identifier,
                ["回话"] = replyText
            });

            return lines;
        }

        /// <summary>
        /// 真跑一次生图：art.request 建资产请求 → bridge.generate 出变体 → 把图的路径收出来。
        /// 失败一律回一句能照做的话，不吞。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="request">出图请求：资产类型 / 命名 / 描述 / 变体数。</param>
        /// <param name="assetType">资产类型。</param>
        /// <param name="imageDriver">生图用哪个下游。</param>
        /// <param name="recipeName">配方名。</param>
        /// <param name="arguments">常驻会话命令参数。</param>
        /// <param name="lines">这一轮的日志行。</param>
        /// <param name="card">出成了时带图的卡片；没出成为 null。</param>
        /// <param name="generated">真出了图没有。</param>
        /// <param name="assetIdentifier">出来的资产 id；没出成为空串。</param>
        /// <param name="referenceImagePath">参考图的本地路径；空串表示这趟不走图生图。</param>
        private static string RunGeneration(
            string repositoryRoot,
            JsonObject request,
            string assetType,
            string imageDriver,
            string recipeName,
            AssistantServeArguments arguments,
            List<string> lines,
            out AssistantCard card,
            out bool generated,
            out string assetIdentifier,
            string referenceImagePath = "")
        {
            card = null;
            generated = false;
            assetIdentifier = "";

            var naming = ReadDraftString(request, "命名");
            var description = ReadDraftString(request, "描述");
            var variantCount = ReadDraftInt(request, "变体数", 6);

            // **接上设计库**：不接的话，每出一张图都是从零理解风格——
            // 配方里那几句提示词就是模型知道的全部，第 5 个界面和第 1 个之间没有任何联系。
            // 这一步模块还不知道（模块名要等拆图那步才有），所以取到的是项目级那一层：
            // 总设计层、项目色板、负面清单，外加 Shared/ 里的通用件当参考图。
            var anchor = StyleAnchorResolver.Resolve(
                repositoryRoot, "", assetType,
                referenceImagePath.Length > 0 ? 0 : StyleAnchorResolver.DefaultReferenceImageCount);

            foreach (var note in anchor.Notes)
            {
                lines.Add("锚点：" + note);
            }

            var anchorFragment = StyleAnchorResolver.ToPromptFragment(anchor);
            if (anchorFragment.Length > 0)
            {
                description = description.Length > 0 ? description + "。" + anchorFragment : anchorFragment;
            }

            // 人自己给了参考图时**不拿库里的顶替**——他给的那张才是这次要照的。
            if (referenceImagePath.Length == 0 && anchor.ReferenceImages.Count > 0)
            {
                referenceImagePath = anchor.ReferenceImages[0];
                lines.Add($"拿库里的同类当参考图：{referenceImagePath}");
            }

            var made = ArtCommands.Request(new ArtRequestArguments
            {
                RepositoryRoot = repositoryRoot,
                PoolRoot = Path.Combine(repositoryRoot, "Pools"),
                AssetType = assetType,
                NamingText = naming,
                Description = description,
                VariantCount = variantCount,
                Width = ReadDraftInt(request, "宽", 0),
                Height = ReadDraftInt(request, "高", 0)
            });
            lines.Add($"建资产请求：{made.Message}");
            if (!made.IsSuccess)
            {
                // **把门禁到底判了哪一条带出来**。只说「资产规格门禁未通过」，
                // 人对着这句话什么也改不了——真正有用的是「宽被从 1080 放宽成 1920，
                // 只许收紧不许放宽」那一句，它就在输出行里。
                return "资产请求没建成：" + made.Message + Detail(made) + "\n这张图没出，改完再点一次。";
            }

            assetIdentifier = ExtractAssetIdentifier(made);
            if (assetIdentifier.Length == 0)
            {
                return "资产请求建出来了，但没读回它的 id，没法接着出图。看一眼 _Tasks/REQ-0000/ 下面。";
            }

            var requestPath = AssetPaths.AssetRequestFile(
                repositoryRoot, AssetRequest.UnownedRequirementIdentifier, assetIdentifier);

            var generate = BridgeCommands.Generate(new BridgeGenerateArguments
            {
                Driver = imageDriver,
                RequestPath = requestPath,
                RecipeName = recipeName,
                RepositoryRoot = repositoryRoot,
                ReferenceImagePath = referenceImagePath,
                TimeoutSeconds = Math.Max(arguments.TimeoutSeconds, 600)
            });
            lines.Add($"生图：{generate.Message}");
            if (!generate.IsSuccess)
            {
                return "出图失败：" + generate.Message + Detail(generate) + "\n再点一次可以重试。";
            }

            var variantDirectory = AssetPaths.VariantDirectory(
                repositoryRoot, AssetRequest.UnownedRequirementIdentifier, assetIdentifier);
            var images = ListVariantImages(variantDirectory);
            lines.Add($"变体：{images.Count} 张，落在 {variantDirectory}");

            if (images.Count == 0)
            {
                return "生图说成了，但变体目录里一张图都没有（" + variantDirectory + "）。这一步得人看一眼。";
            }

            // 按规格归一：下游按自己的档位出图（要 256×256，回来的常是 1024 往上、且不透明），
            // 而资产规格是硬的。少这一步，出多少张都入不了库——机检会逐张判红。
            var normalizeNotes = NormalizeVariants(repositoryRoot, assetType, images, lines);

            generated = true;
            var body = "出来了 " + images.Count + " 张（" + assetIdentifier + "，" + imageDriver + " 的 " + recipeName + " 配方"
                + (referenceImagePath.Length > 0 ? "，照着参考图" : "") + "）。"
                + (anchor.IsColdStart
                    ? "\n这一批**没有风格锚点**（还没定过总设计与定稿）。挑中之后我把它定成第一版风格，"
                        + "往后同类就有得参考了。"
                    : "")
                + "\n挑中哪张就直说，我把其余的弃掉；都不行就说改哪儿，我重出。"
                + "\n本体在 " + variantDirectory;

            if (normalizeNotes.Length > 0)
            {
                body += "\n" + normalizeNotes;
            }

            // 能不能拆**由资产规格声明**，不按类型名猜。
            // 按名字前缀猜过一次：判据写的是「界面」开头，而「PC界面底图」以 PC 开头，
            // 当场漏掉，人拿到的卡上根本没有拆图按钮。
            var canCut = AssetSpecCatalog.Load(repositoryRoot, "").Find(assetType)?.IsCuttable ?? false;
            card = AssistantCard.ForGeneratedImages(body, images, assetIdentifier, canCut);
            return card.ToPlainText();
        }

        /// <summary>
        /// 就地把一张元素图四周的透明边裁掉。裁不动只记一笔，不打断这一个元素——
        /// 没裁成最多是这张图边距大一点，把整趟拆图判死才是坏事。
        /// </summary>
        /// <param name="filePath">元素图路径。</param>
        /// <param name="lines">执行流水。</param>
        /// <param name="naming">这个元素的命名，用在流水里。</param>
        private static void TrimElement(string filePath, List<string> lines, string naming)
        {
            var decoded = PngDecoder.DecodeFile(filePath);
            if (!decoded.Succeeded)
            {
                lines.Add($"{naming} 读不回来，没裁透明边：{decoded.FailureReason}");
                return;
            }

            var trimmed = AssetImageNormalizer.TrimTransparentBorder(decoded.Image, out var trimNote);
            if (trimNote.Length > 0)
            {
                lines.Add($"{naming} {trimNote}");
            }

            if (ReferenceEquals(trimmed, decoded.Image))
            {
                return;
            }

            if (!PngEncoder.EncodeToFile(trimmed, filePath, out var encodeReason))
            {
                lines.Add($"{naming} 裁完写不回去：{encodeReason}");
            }
        }

        /// <summary>
        /// 让模型照着一小块参考片段，重画一张干净的透明底单元素图。返回重绘出来那张图的路径；没成给空串。
        ///
        /// 走的是**正经的资产请求 → 生图**那条路，不是抄近道：每个 UI 元素本来就是一份独立资产，
        /// 该有自己的 id、自己的请求留痕、自己能被重出一次。抄近道的话，
        /// 「这个按钮当初是照什么出的」下次没人答得上来。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="imageDriver">生图 driver 名。</param>
        /// <param name="recipeName">图生图配方名。</param>
        /// <param name="naming">这个元素归一之后的命名。</param>
        /// <param name="displayName">这个元素在设计图上叫什么，进提示词。</param>
        /// <param name="moduleName">这一屏属于哪个模块，用来挑就近的规格覆盖；空串表示不分模块。</param>
        /// <param name="destination">落点（已经带上模块目录）。</param>
        /// <param name="referencePath">裁下来的参考片段路径。</param>
        /// <param name="width">元素在设计图上的像素宽。</param>
        /// <param name="height">元素在设计图上的像素高。</param>
        /// <param name="arguments">命令参数。</param>
        /// <param name="lines">执行流水。</param>
        /// <param name="failure">没成时的原因；成了为空串。</param>
        private static string RedrawElement(
            string repositoryRoot,
            string imageDriver,
            string recipeName,
            string naming,
            string displayName,
            string moduleName,
            string destination,
            string referencePath,
            int width,
            int height,
            AssistantServeArguments arguments,
            List<string> lines,
            out string failure)
        {
            failure = "";

            // 重绘这一步**模块是已知的**，所以取得到模块级定稿——
            // 「背包偏冷、商店偏暖」这种差异只有在这里才用得上。
            var anchor = StyleAnchorResolver.Resolve(repositoryRoot, moduleName, UiElementAssetType, referenceImageCount: 0);
            var anchorFragment = StyleAnchorResolver.ToPromptFragment(anchor);
            var describedWithStyle = anchorFragment.Length > 0
                ? displayName + "。" + anchorFragment
                : displayName;

            var made = ArtCommands.Request(new ArtRequestArguments
            {
                RepositoryRoot = repositoryRoot,
                PoolRoot = Path.Combine(repositoryRoot, "Pools"),
                AssetType = UiElementAssetType,
                Module = moduleName,
                // 落点显式给带模块的那一版：Module 只用来挑就近的规格覆盖，
                // 它不会自己拼进落点里。
                Destination = destination,
                NamingText = naming,
                Description = describedWithStyle,
                VariantCount = 1,
                Width = width,
                Height = height
            });

            if (!made.IsSuccess)
            {
                failure = made.Message + Detail(made);
                return "";
            }

            var elementIdentifier = ExtractAssetIdentifier(made);
            if (elementIdentifier.Length == 0)
            {
                failure = "资产请求建出来了，但没读回它的 id";
                return "";
            }

            var requestPath = AssetPaths.AssetRequestFile(
                repositoryRoot, AssetRequest.UnownedRequirementIdentifier, elementIdentifier);

            // **请求文件必须真落盘**。资产 id 是按「请求目录里最大编号 + 1」发的——
            // 请求没落盘，下一个元素就会拿到同一个 id，于是整批元素共用一个 id、
            // 共用一个变体目录。真炸过：一趟拆图 73 次生成全落在 ASSET-0000-10 上。
            if (!File.Exists(requestPath))
            {
                failure = "资产请求说建成了，但文件不在 " + requestPath + "——"
                    + "id 是按请求目录里的最大编号发的，请求不落盘的话下一个元素会拿到同一个 id，整批共用一份产物";
                return "";
            }

            var variantDirectory = AssetPaths.VariantDirectory(
                repositoryRoot, AssetRequest.UnownedRequirementIdentifier, elementIdentifier);

            // 生成前先记下目录里已经有什么。**下面只认这一趟新冒出来的那张**——
            // 从前取的是「目录里的第一张」，目录里但凡有旧文件（换过配方、重跑过、
            // 或者 id 被复用），拿回来的就是别人的图，再按这个元素的名字复制出去。
            // 那正是「每个元素拆出来长得一模一样」的成因，而且一处都不报错。
            var before = new HashSet<string>(ListVariantImages(variantDirectory), StringComparer.OrdinalIgnoreCase);

            var generate = BridgeCommands.Generate(new BridgeGenerateArguments
            {
                Driver = imageDriver,
                RequestPath = requestPath,
                RecipeName = recipeName,
                RepositoryRoot = repositoryRoot,
                ReferenceImagePath = referencePath,
                TimeoutSeconds = Math.Max(arguments.TimeoutSeconds, 600)
            });

            if (!generate.IsSuccess)
            {
                failure = generate.Message + Detail(generate);
                return "";
            }

            var fresh = new List<string>();
            foreach (var path in ListVariantImages(variantDirectory))
            {
                if (!before.Contains(path))
                {
                    fresh.Add(path);
                }
            }

            if (fresh.Count == 0)
            {
                failure = "生图说成了，但变体目录里没有这一趟新出的图（" + variantDirectory + "）——"
                    + "宁可这一个元素缺图，也不能把目录里别人的图当成它的";
                return "";
            }

            return fresh[0];
        }

        /// <summary>
        /// 就地跑一次 ui.scaffold，把这份面板定义变成 UXML + USS + C#。
        ///
        /// 产物落在 <c>Game.View</c> 的源码树里而不是仓库根：落在仓库根时 Unity 编译不到，
        /// 而 Logic.Core 又因为零 UnityEngine 铁律链接不了，那条管线是断的。
        /// 这个落点跟门禁里那一段是同一个，改一处就得改两处——**它们必须一致**，
        /// 否则拆图写到 A、门禁去 B 校验，永远对不上。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="definitionPath">面板定义文件路径。</param>
        /// <param name="lines">执行流水。</param>
        private static bool RunScaffold(string repositoryRoot, string definitionPath, List<string> lines)
        {
            var outputDirectory = Path.Combine(
                repositoryRoot, "UnityProject", "Assets", "Game", "Scripts", "View", "_Generated");

            var result = UiScaffoldCommand.Execute(new UiScaffoldArguments
            {
                DefinitionPath = definitionPath,
                OutputDirectory = outputDirectory,
                TemplateRoot = repositoryRoot,
                VerifyOnly = false
            });

            lines.Add($"生成三件套：{result.Message}");
            return result.IsSuccess;
        }

        /// <summary>
        /// 裁参考图时四周各留多少（按框自身宽高的比例）。
        /// 12% 够把「框标小了半圈」这种常见偏差兜住，又不至于把邻居整个带进来。
        /// </summary>
        private const double ReferencePaddingRatio = 0.12;

        /// <summary>结果卡上最多逐条列几个元素；再多就只报个数——完整清单在面板定义里。</summary>
        private const int ElementListLimit = 10;

        /// <summary>
        /// 标到多少个元素就得先问一句。
        /// 12 是「一屏正常的可交互件」那个量级；再多通常是模型把每个格子、每个小图标都框了一遍。
        /// </summary>
        private const int CutConfirmThreshold = 12;

        /// <summary>一次重绘大概几秒，只用来给人一个量级，不做任何判断。</summary>
        private const double SecondsPerRedraw = 20.0;

        /// <summary>
        /// 确保下游对象都在：没有的建出来，有的沿用，新建的 id 回填台账。
        ///
        /// 幂等，所以每次推文档前跑一遍不亏；失败只记一笔，不打断这一轮——
        /// 「补建对象失败」与「需求没建成」是两回事，混成一句会让人去查错方向。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="arguments">常驻会话命令参数。</param>
        /// <param name="lines">这一轮的日志行。</param>
        private static void EnsureDownstreamObjects(
            string repositoryRoot, AssistantServeArguments arguments, List<string> lines)
        {
            var routeTable = BridgeRouteTable.Load(repositoryRoot);
            if (!routeTable.TryResolvePort("需求文档端", out var driverName, out var reason))
            {
                lines.Add($"补建下游对象跳过：需求文档端取不到（{reason}）");
                return;
            }

            var ensured = BridgeEnsureCommands.Ensure(new BridgeEnsureArguments
            {
                Driver = driverName,
                RepositoryRoot = repositoryRoot,
                DryRun = false,
                TimeoutSeconds = arguments.TimeoutSeconds
            });

            lines.Add($"下游对象：{ensured.Message}");
            if (ensured.IsSuccess)
            {
                return;
            }

            foreach (var line in Detail(ensured).Split('\n'))
            {
                if (line.Trim().Length > 0)
                {
                    lines.Add("  " + line.Trim());
                }
            }
        }

        /// <summary>
        /// 把上一张还带着按钮的卡换成不带按钮的。
        ///
        /// 撤不掉只记一笔，不打断这一轮——**那张卡的按钮多留一会儿，
        /// 总比因为改不动一张旧卡就不回人的话强**。
        /// 幂等：撤过就把记录忘掉，下一轮不会重复撤。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="assistantDriver">助手 port 路由到的 driver 名。</param>
        /// <param name="conversationIdentifier">会话标识。</param>
        /// <param name="arguments">命令参数。</param>
        /// <param name="lines">执行流水。</param>
        private static void RetireStaleCard(
            string repositoryRoot,
            string assistantDriver,
            string conversationIdentifier,
            AssistantServeArguments arguments,
            List<string> lines)
        {
            if (arguments.DryRun)
            {
                return;
            }

            var staleIdentifier = LiveCardRegistry.Read(repositoryRoot, conversationIdentifier);
            if (staleIdentifier.Length == 0)
            {
                return;
            }

            // **只去掉按钮，正文原样留着。**
            // 从前这里换的是一张写着「已翻篇」的替身卡，那等于把聊天记录抹了——
            // 人翻上去想看之前聊到哪，看到的是一句没有信息的占位话。
            // 拿的是那张卡真发出去的 JSON：图在里面已经是 image_key，
            // 重拼一份的话 card-update 不传图，图会当场消失。
            var stripped = LiveCardRegistry.StripActions(
                LiveCardRegistry.ReadCardJson(repositoryRoot, conversationIdentifier));
            if (stripped.Length == 0)
            {
                // 解析不动、或那张卡本来就没有按钮：放弃这次撤。
                // **宁可按钮多留一会儿，也不许推一份残缺的卡上去。**
                LiveCardRegistry.Forget(repositoryRoot, conversationIdentifier);
                return;
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = false,
                ["消息标识"] = staleIdentifier,
                ["卡片JSON"] = stripped
            });

            var call = BridgeInvoker.Invoke(repositoryRoot, assistantDriver, "card-update", payload, arguments.TimeoutSeconds);
            lines.Add(call.Succeeded
                ? "上一张卡的按钮已撤"
                : $"上一张卡撤不掉（{call.ErrorCode}）：{call.HumanText}（不影响这一轮）");

            // 撤成了才忘：没撤成的话下一轮再试一次。
            if (call.Succeeded)
            {
                LiveCardRegistry.Forget(repositoryRoot, conversationIdentifier);
            }
        }

        /// <summary>
        /// 照一条需求出界面规格与白块功能图。
        ///
        /// 这一步**会调执行后端**，所以只在人点了按钮时才跑（与出图同一规矩）。
        /// 产出三样：界面规格（功能契约）、布局图（白块，给策划确认功能位、给美术当底稿）、
        /// 资产清单（真要出几张图）。规格产出后立刻校验——
        /// 草案是模型写的，不校验就发给人看，等于把「模型编了个不合规的东西」当成结论。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="assistantDriver">助手 port 路由到的 driver 名。</param>
        /// <param name="signalFilePath">这一轮的信号文件。</param>
        /// <param name="message">这次按钮点击的消息。</param>
        /// <param name="arguments">命令参数。</param>
        /// <param name="lines">执行流水。</param>
        /// <param name="replyDelivered">回话送出了没有。</param>
        /// <param name="replyRetryable">回话失败能不能重试。</param>
        private static IReadOnlyList<string> HandleInterfaceDraft(
            string repositoryRoot,
            string poolRoot,
            string assistantDriver,
            string signalFilePath,
            AssistantConversationMessage message,
            AssistantServeArguments arguments,
            List<string> lines,
            out bool replyDelivered,
            out bool replyRetryable)
        {
            var requirementIdentifier = message.ReadActionValue("需求id");
            string replyText;
            var result = "出功能图失败";

            // 按钮带着需求 id 来，顺手把「这条会话在做哪条需求」记上——
            // 人可能是在新会话里翻出旧卡片点的这个按钮，那时建需求那一步没经过这条会话。
            AssistantServeTurn.RememberConversationRequirement(
                repositoryRoot, message.ConversationIdentifier, requirementIdentifier);

            if (requirementIdentifier.Length == 0)
            {
                replyText = "按钮没带需求 id，不知道照哪条需求出。";
            }
            else if (arguments.DryRun || !arguments.WriteDownstream)
            {
                var why = arguments.DryRun ? "--dry-run true" : "--write-downstream false";
                replyText = "本机是只读模式（" + why + "），没有真出。开了开关再点一次。";
                result = "只读模式没出";
            }
            else
            {
                var drafted = InterfaceCommands.Draft(new InterfaceSpecDraftArguments
                {
                    RepositoryRoot = repositoryRoot,
                    PoolRoot = poolRoot,
                    Requirement = requirementIdentifier,

                    // **面板名留空，由模型定**：它正读着这条需求，比这里更清楚是哪一屏。
                    Panel = "",
                    TimeoutSeconds = Math.Max(arguments.TimeoutSeconds, 300),
                    Model = arguments.InterfaceDraftModel ?? "",
                    DryRun = false
                });

                lines.Add($"出界面规格：{drafted.Message}");
                foreach (var line in drafted.OutputLines ?? Array.Empty<string>())
                {
                    lines.Add("  " + line);
                }

                // **草案落没落盘，与校验过不过，是两件事。**
                // ui.spec.draft 在有校验发现时回 Failure，但那时规格已经写进池子了——
                // 照着 IsSuccess 说「没出成」，人会以为要重出一次，
                // 于是再点一次，再花一次钱，再得到同一份草案。
                // 落没落盘按「池子里现在有没有这条需求名下的规格」认，不解析回话文本——
                // 文案会改，而这条判据的正确性不该跟着文案走。
                var landed = InterfaceSpec.FindByRequirement(
                    repositoryRoot, requirementIdentifier, out _).Count > 0;
                result = drafted.IsSuccess ? "已出功能图" : landed ? "出了功能图但校验没过" : "出功能图失败";

                var moduleName = ModulePlanRefresher.ReadEpic(poolRoot, requirementIdentifier);
                var documentLink = "";
                if (landed)
                {
                    // **校验没过也照渲。** 规格已经是池子里的事实，模块策划案是它的投影——
                    // 投影落后于事实，人看策划案时会以为这一屏还没出。
                    // 校验那几条单独报，改的是规格本身，不是这份投影。
                    documentLink = PublishDocument(
                        repositoryRoot, poolRoot, requirementIdentifier, arguments, lines, out var republishFailure);
                    if (republishFailure.Length > 0)
                    {
                        lines.Add("需求案重推失败：" + republishFailure);
                    }

                    ModulePlanRefresher.RefreshForRequirement(
                        repositoryRoot, poolRoot, requirementIdentifier, out var planNotes,
                        alsoPush: true, timeoutSeconds: arguments.TimeoutSeconds);
                    lines.AddRange(planNotes);
                }

                replyText = landed
                    ? (drafted.IsSuccess ? "功能图出好了：" : "功能图出好了，但有几条要改：") + drafted.Message
                        + "\n" + Detail(drafted)
                        + "\n" + "元素行为表与白块布局图已经写进模块策划案"
                        + (moduleName.Length > 0 ? "（" + moduleName + "）" : "")
                        + (documentLink.Length > 0 ? "；需求案：" + documentLink : "")
                        + "\n" + (drafted.IsSuccess
                            ? "哪个功能位不对、少了什么，直接说。"
                            : "**别再点一次**——草案已经落盘了，再点是重出一份。"
                                + "改上面那几条，或者直接跟我说哪儿不对。")
                    : "功能图没出成：" + drafted.Message + Detail(drafted) + "\n" + "再点一次可以重试。";
            }

            var reply = SendReply(repositoryRoot, assistantDriver, message, replyText, arguments, lines, null);
            replyDelivered = reply.Delivered;
            replyRetryable = reply.Retryable;

            AssistantConversationHistory.Append(
                repositoryRoot, message.ConversationIdentifier, AssistantHistoryTurn.AssistantRole, replyText, DateTimeOffset.Now);

            AppendLedger(repositoryRoot, new JsonObject
            {
                ["时间"] = DateTimeOffset.Now.ToString("o"),
                ["信号"] = Path.GetFileName(signalFilePath),
                ["结果"] = result,
                ["需求id"] = requirementIdentifier,
                ["回话送出"] = reply.Delivered
            });

            return lines;
        }

        /// <summary>
        /// 人点了「出策划案」：给这个模块建/刷新一份策划案，然后推知识库。
        ///
        /// **没有就冷启动、有就只刷新生成区。** 这两件事的区别不在难易，在**碰不碰人写区**：
        /// 冷启动那次要照代码与已有需求产人写区草案，往后每次都只动生成区——
        /// 人写区一旦有人动过，重渲染就再也不许碰它。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="assistantDriver">助手 port 路由到的 driver 名。</param>
        /// <param name="signalFilePath">这一轮的信号文件。</param>
        /// <param name="message">这次按钮点击的消息。</param>
        /// <param name="arguments">命令参数。</param>
        /// <param name="lines">执行流水。</param>
        /// <param name="replyDelivered">回话送出了没有。</param>
        /// <param name="replyRetryable">回话失败能不能重试。</param>
        private static IReadOnlyList<string> HandlePlanDraft(
            string repositoryRoot,
            string poolRoot,
            string assistantDriver,
            string signalFilePath,
            AssistantConversationMessage message,
            AssistantServeArguments arguments,
            List<string> lines,
            out bool replyDelivered,
            out bool replyRetryable)
        {
            var moduleName = message.ReadActionValue("模块");
            string replyText;
            var result = "出策划案失败";

            if (moduleName.Length == 0)
            {
                replyText = "按钮没带模块名，不知道给哪个模块出。";
            }
            else if (arguments.DryRun || !arguments.WriteDownstream)
            {
                var why = arguments.DryRun ? "--dry-run true" : "--write-downstream false";
                replyText = "本机是只读模式（" + why + "），没有真出。开了开关再点一次。";
                result = "只读模式没出";
            }
            else
            {
                var documentPath = PoolPaths.ModulePlanDocument(poolRoot, moduleName);
                var isColdStart = !File.Exists(documentPath);

                if (isColdStart)
                {
                    var drafted = PlanningDocCommands.Draft(new PlanDraftArguments
                    {
                        RepositoryRoot = repositoryRoot,
                        PoolRoot = poolRoot,
                        Module = moduleName,
                        TimeoutSeconds = Math.Max(arguments.TimeoutSeconds, 300),
                        Model = arguments.InterfaceDraftModel ?? "",
                        DryRun = false
                    });

                    lines.Add($"冷启动出草案：{drafted.Message}");
                    foreach (var line in drafted.OutputLines ?? Array.Empty<string>())
                    {
                        lines.Add("  " + line);
                    }

                    if (!File.Exists(documentPath))
                    {
                        replyText = moduleName + " 的策划案没出成：" + drafted.Message
                            + Detail(drafted) + "\n再点一次可以重试。";
                        return FinishPlanDraft(
                            repositoryRoot, assistantDriver, signalFilePath, message, arguments, lines,
                            moduleName, result, replyText, out replyDelivered, out replyRetryable);
                    }
                }

                // 冷启动那一趟已经渲过一次生成区，但**这儿再渲一次不亏**：
                // 它是幂等的，没变化就什么都不写，而少这一次的代价是
                // 「有策划案」那条路完全没渲过。
                ModulePlanRefresher.Refresh(
                    repositoryRoot, poolRoot, moduleName, lines, out var pushed,
                    alsoPush: true, timeoutSeconds: arguments.TimeoutSeconds);

                result = isColdStart ? "已出策划案" : "已刷新策划案";

                // **推没推上去照实说**，不许照着「渲成了」就写「已推知识库」。
                // 假的成功比失败难查得多：人去知识库里找不到，会先怀疑自己看错了地方。
                var pushNote = pushed == null
                    ? "这一趟没推知识库。"
                    : pushed.Link.Length > 0
                        ? "已推知识库：" + pushed.Link
                        : pushed.FailureReason.Length > 0
                            ? "**没推上知识库**：" + pushed.FailureReason
                            : pushed.Note;

                replyText = (isColdStart
                        ? moduleName + " 的策划案出好了（第一版是照代码与已有需求产的草案）。"
                        : moduleName + " 的策划案刷新了。")
                    + "\n" + "落点 Pools/Designs/Modules/" + moduleName + "/index.md。"
                    + "\n" + pushNote
                    + (isColdStart
                        ? "\n" + "**「往后要做成什么样」那一节留给你写**——那是人的判断，代码里没有依据。"
                        : "");
            }

            return FinishPlanDraft(
                repositoryRoot, assistantDriver, signalFilePath, message, arguments, lines,
                moduleName, result, replyText, out replyDelivered, out replyRetryable);
        }

        /// <summary>出策划案那一支的收尾：回话、记历史、记台账。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="assistantDriver">助手 driver 名。</param>
        /// <param name="signalFilePath">这一轮的信号文件。</param>
        /// <param name="message">这次按钮点击的消息。</param>
        /// <param name="arguments">命令参数。</param>
        /// <param name="lines">执行流水。</param>
        /// <param name="moduleName">模块名。</param>
        /// <param name="result">台账上记什么结果。</param>
        /// <param name="replyText">回话正文。</param>
        /// <param name="replyDelivered">回话送出了没有。</param>
        /// <param name="replyRetryable">回话失败能不能重试。</param>
        private static IReadOnlyList<string> FinishPlanDraft(
            string repositoryRoot,
            string assistantDriver,
            string signalFilePath,
            AssistantConversationMessage message,
            AssistantServeArguments arguments,
            List<string> lines,
            string moduleName,
            string result,
            string replyText,
            out bool replyDelivered,
            out bool replyRetryable)
        {
            var reply = SendReply(repositoryRoot, assistantDriver, message, replyText, arguments, lines, null);
            replyDelivered = reply.Delivered;
            replyRetryable = reply.Retryable;

            AssistantConversationHistory.Append(
                repositoryRoot, message.ConversationIdentifier, AssistantHistoryTurn.AssistantRole,
                replyText, DateTimeOffset.Now);

            AppendLedger(repositoryRoot, new JsonObject
            {
                ["时间"] = DateTimeOffset.Now.ToString("o"),
                ["信号"] = Path.GetFileName(signalFilePath),
                ["结果"] = result,
                ["模块"] = moduleName,
                ["回话送出"] = reply.Delivered
            });

            return lines;
        }

        /// <summary>取回来的附件：图片一组，别的文件一组。</summary>
        /// <param name="ImagePaths">图片的本地路径，按消息里的先后。</param>
        /// <param name="FileNotes">别的文件的一句话说明，进提示词用。</param>
        private sealed record FetchedAttachments(IReadOnlyList<string> ImagePaths, IReadOnlyList<string> FileNotes);

        /// <summary>
        /// 把这条消息里带的图片与文件取回本地。
        ///
        /// 为什么要落成本地文件而不是只留 key：下游的资源 key **是按消息取的**，
        /// 过一会儿人点「出图」时那条消息早处理完了，拿 key 已经取不回来。
        /// 存成文件，路径就能一路带进出图请求的留底里。
        ///
        /// 取失败只记一笔、不打断这一轮：图没取到最多是这次少一张参考图，
        /// 而人打的那行字还在——把整轮判死才是坏事。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="assistantDriver">助手 port 路由到的 driver 名。</param>
        /// <param name="message">这一轮的会话消息。</param>
        /// <param name="arguments">命令参数。</param>
        /// <param name="lines">执行流水。</param>
        private static FetchedAttachments FetchAttachments(
            string repositoryRoot,
            string assistantDriver,
            AssistantConversationMessage message,
            AssistantServeArguments arguments,
            List<string> lines)
        {
            var imagePaths = new List<string>();
            var fileNotes = new List<string>();
            if (message.Attachments.Count == 0)
            {
                return new FetchedAttachments(imagePaths, fileNotes);
            }

            var directory = Path.Combine(
                repositoryRoot,
                "_Tasks",
                "conversations",
                "attachments",
                AssistantConversationHistory.SafeFileName(message.ConversationIdentifier));

            for (var index = 0; index < message.Attachments.Count; index++)
            {
                var attachment = message.Attachments[index];
                var fileName = AssistantConversationHistory.SafeFileName(message.MessageIdentifier)
                    + "-" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + attachment.FileExtension;
                var destination = Path.Combine(directory, fileName);

                // 同一条消息重来一次（重试、重投）时不重下：省一次往返，也保证路径稳定。
                if (!File.Exists(destination))
                {
                    var payload = JsonSerializer.SerializeToElement(new JsonObject
                    {
                        ["干跑"] = false,
                        ["消息标识"] = message.MessageIdentifier,
                        ["资源key"] = attachment.Key,
                        ["资源类型"] = attachment.IsImage ? "image" : "file",
                        ["存到"] = destination
                    });

                    var call = BridgeInvoker.Invoke(repositoryRoot, assistantDriver, "fetch", payload, arguments.TimeoutSeconds);
                    if (!call.Succeeded)
                    {
                        lines.Add($"附件取不回来（{call.ErrorCode}）：{call.HumanText}");
                        continue;
                    }
                }

                if (attachment.IsImage)
                {
                    imagePaths.Add(destination);
                }
                else
                {
                    var shownName = string.IsNullOrWhiteSpace(attachment.FileName)
                        ? Path.GetFileName(destination)
                        : attachment.FileName;
                    fileNotes.Add(shownName + "（已存到 " + destination + "）");
                }
            }

            if (imagePaths.Count > 0 || fileNotes.Count > 0)
            {
                lines.Add($"附件取回：图 {imagePaths.Count} 张、文件 {fileNotes.Count} 个");
            }

            return new FetchedAttachments(imagePaths, fileNotes);
        }

        /// <summary>
        /// 就地把那张出图结果卡换掉（撤按钮 / 换文案 / 把按钮换回来）。
        ///
        /// 与出图那张状态卡分开写是因为**这张卡上带着图**：出图的请求卡只有文字，
        /// 而结果卡的图就是人正在看的那几张候选——换卡时把图丢了，
        /// 人对着一行「正在拆」根本不知道在拆哪张。
        /// 换不动只记一笔，不影响拆图本身：卡片没换掉最多是按钮还亮着。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="assistantDriver">助手 port 路由到的 driver 名。</param>
        /// <param name="message">这次按钮点击的消息（要用它的消息标识定位原卡）。</param>
        /// <param name="assetIdentifier">这张卡对应的资产 id。</param>
        /// <param name="statusText">换上去的状态文案。</param>
        /// <param name="withButtons">给不给拆图按钮：跑之前与跑成了都不给，失败才换回来。</param>
        /// <param name="arguments">命令参数。</param>
        /// <param name="lines">执行流水。</param>
        private static void UpdateCutCard(
            string repositoryRoot,
            string assistantDriver,
            AssistantConversationMessage message,
            string assetIdentifier,
            string statusText,
            bool withButtons,
            AssistantServeArguments arguments,
            List<string> lines)
        {
            if (message.MessageIdentifier.Length == 0)
            {
                lines.Add("原卡没有消息标识，换不了卡（不影响拆图）");
                return;
            }

            // **换上去的卡不带图**：card-update 那条路不传图（uploadImages: false），
            // 传了也是白传——换完人看到的就是一张没有图的卡。
            // 所以状态文案里得自己把「在拆哪一份」说清楚，不能指望旁边那几张图替它交代。
            var card = AssistantCard.ForGeneratedImages(
                assetIdentifier + "　" + statusText,
                Array.Empty<string>(),
                assetIdentifier,
                canCut: withButtons);
            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = false,
                ["消息标识"] = message.MessageIdentifier,
                ["卡片"] = card.ToJson()
            });

            var call = BridgeInvoker.Invoke(repositoryRoot, assistantDriver, "card-update", payload, arguments.TimeoutSeconds);
            lines.Add(call.Succeeded
                ? $"原卡已换：{Shorten(statusText)}"
                : $"原卡换不动（{call.ErrorCode}）：{call.HumanText}（不影响拆图）");
        }

        /// <summary>
        /// 就地把原来那张出图卡换掉（改按钮与状态）。改不动只记一笔，不影响出图本身——
        /// 卡片没换掉最多是按钮还亮着，而幂等那一层已经挡住了重复出图。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="assistantDriver">助手 port 路由到的 driver 名。</param>
        /// <param name="message">这次按钮点击的消息（要用它的消息标识定位原卡）。</param>
        /// <param name="identifier">出图请求 key。</param>
        /// <param name="request">出图请求。</param>
        /// <param name="title">新标题。</param>
        /// <param name="bodyText">新正文。</param>
        /// <param name="withButton">换成的这张给不给「出图」按钮。</param>
        /// <param name="arguments">常驻会话命令参数。</param>
        /// <param name="lines">这一轮的日志行。</param>
        private static void UpdateImageCard(
            string repositoryRoot,
            string assistantDriver,
            AssistantConversationMessage message,
            string identifier,
            JsonObject request,
            string title,
            string bodyText,
            bool withButton,
            AssistantServeArguments arguments,
            List<string> lines)
        {
            if (arguments.DryRun || message.MessageIdentifier.Length == 0)
            {
                return;
            }

            var card = AssistantCard.ForImageRequestStatus(identifier, request, title, bodyText, withButton);
            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = false,
                ["消息标识"] = message.MessageIdentifier,
                ["卡片"] = card.ToJson()
            });

            var call = BridgeInvoker.Invoke(repositoryRoot, assistantDriver, "card-update", payload, arguments.TimeoutSeconds);
            lines.Add(call.Succeeded
                ? "原卡已换成：" + title
                : $"原卡没换成（{call.ErrorCode}）：{call.HumanText}");
        }

        /// <summary>
        /// 把刚出的变体按资产规格归一（缩放，以及背景是纯色时抠透明）。
        /// 返回一句给人看的话：动了什么、还差什么。
        ///
        /// **还差什么一定要说**：抠不成透明的图看着跟成了的一样，
        /// 不说的话人要到把它摆进游戏里才发现少了一层。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="assetType">资产类型，规格从它查。</param>
        /// <param name="images">刚出的变体路径。</param>
        /// <param name="lines">这一轮的日志行。</param>
        private static string NormalizeVariants(
            string repositoryRoot, string assetType, IReadOnlyList<string> images, List<string> lines)
        {
            var catalog = AssetSpecCatalog.Load(repositoryRoot, "");
            var spec = catalog.Find(assetType);
            if (spec == null)
            {
                return "";
            }

            spec.Values.TryGetValue("规格.宽", out var widthText);
            spec.Values.TryGetValue("规格.高", out var heightText);
            spec.Values.TryGetValue("规格.需要透明", out var transparentText);

            var width = int.TryParse(widthText, out var parsedWidth) ? parsedWidth : 0;
            var height = int.TryParse(heightText, out var parsedHeight) ? parsedHeight : 0;
            var needsTransparency = string.Equals(transparentText, "true", StringComparison.OrdinalIgnoreCase);

            var changedCount = 0;
            var remaining = new List<string>();
            foreach (var image in images)
            {
                var outcome = AssetImageNormalizer.Normalize(image, width, height, needsTransparency);
                foreach (var note in outcome.Notes)
                {
                    lines.Add($"规格归一 {Path.GetFileName(image)}：{note}");
                }

                foreach (var note in outcome.Remaining)
                {
                    lines.Add($"规格没达标 {Path.GetFileName(image)}：{note}");
                    if (!remaining.Contains(note))
                    {
                        remaining.Add(note);
                    }
                }

                if (outcome.Changed)
                {
                    changedCount++;
                }
            }

            var text = "";
            if (changedCount > 0)
            {
                text = "已按规格归一（" + changedCount + " 张，" + width + "×" + height + "）。";
            }

            if (remaining.Count > 0)
            {
                text += "还差：" + string.Join("；", remaining);
            }

            return text;
        }

        /// <summary>
        /// 把命令结果里的输出行拼成一段细节，跟在那句总结后面。
        ///
        /// **总结那句话往往不够用**：门禁只说「资产规格门禁未通过」，
        /// 而「宽被从 1080 放宽成 1920，只许收紧不许放宽」在输出行里——
        /// 少了它，人对着回话什么都改不了。行太多时截断并说明还有几条。
        /// </summary>
        /// <param name="result">命令结果。</param>
        private static string Detail(CommandResult result)
        {
            var lines = result?.OutputLines;
            if (lines == null || lines.Count == 0)
            {
                return "";
            }

            var builder = new StringBuilder();
            var shown = 0;
            foreach (var line in lines)
            {
                if (shown >= MaxDetailLines)
                {
                    // 说清楚数的是**流水行数**，不是问题条数。上一句往往刚说完
                    // 「校验有 4 条问题」，紧跟一句「还有 7 条」——两个数说的不是一回事，
                    // 摆在一起读起来像自相矛盾，而人只会记住后一个数。
                    builder.Append("\n（执行流水还有 ").Append(lines.Count - shown).Append(" 行，看日志）");
                    break;
                }

                builder.Append('\n').Append(line);
                shown++;
            }

            return builder.ToString();
        }

        /// <summary>回话里最多摆几条细节；再多就该去看日志，堆在聊天里没人读。</summary>
        private const int MaxDetailLines = 4;

        /// <summary>把变体目录里的 PNG 按文件名序列出来；目录不在给空表。</summary>
        /// <param name="variantDirectory">变体目录。</param>
        private static IReadOnlyList<string> ListVariantImages(string variantDirectory)
        {
            try
            {
                if (!Directory.Exists(variantDirectory))
                {
                    return Array.Empty<string>();
                }

                return Directory
                    .GetFiles(variantDirectory, "*.png", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>从 art.request 的输出行里认出资产 id（ASSET-xxxx-xx）；认不出给空串。</summary>
        /// <param name="result">art.request 的结果。</param>
        private static string ExtractAssetIdentifier(CommandResult result)
        {
            var texts = new List<string> { result.Message ?? "" };
            if (result.OutputLines != null)
            {
                texts.AddRange(result.OutputLines);
            }

            foreach (var text in texts)
            {
                var match = System.Text.RegularExpressions.Regex.Match(text, @"ASSET-\d{4}-\d{2}");
                if (match.Success)
                {
                    return match.Value;
                }
            }

            return "";
        }

        /// <summary>读出图请求里的字符串键；缺失给空串。</summary>
        private static string ReadDraftString(JsonObject draft, string name)
        {
            return draft != null
                && draft.TryGetPropertyValue(name, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var text)
                ? text
                : "";
        }

        /// <summary>读出图请求里的整数键；缺失或不是数字给缺省值。</summary>
        private static int ReadDraftInt(JsonObject draft, string name, int fallback)
        {
            return draft != null
                && draft.TryGetPropertyValue(name, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<int>(out var number)
                ? number
                : fallback;
        }

        /// <summary>
        /// 人在聊天里说「上次拆得不对」：带着上一次的框与他的意见重拆一遍。
        ///
        /// **不必再点一次按钮**：他刚看完那批图，接着说话是最自然的动作；
        /// 让他回去翻聊天记录找那张卡再点一下，只会把一句「关闭按钮框大了」变成三步操作。
        /// 改的是哪一份靠会话最近一次拆图的留底认——认不出来就如实说，不瞎猜一个资产去拆。
        /// </summary>
        private static IReadOnlyList<string> HandleRecut(
            string repositoryRoot,
            string assistantDriver,
            string backendDriver,
            string signalFilePath,
            AssistantConversationMessage message,
            AssistantServeReply reply,
            AssistantServeArguments arguments,
            List<string> lines,
            out bool replyDelivered,
            out bool replyRetryable)
        {
            var assetIdentifier = AssistantServeTurn.ReadLastCutAsset(repositoryRoot, message.ConversationIdentifier);
            string replyText;
            AssistantCard card = null;
            var result = "重拆失败";

            if (assetIdentifier.Length == 0)
            {
                replyText = "我这边没记着这条会话最近拆的是哪一张，接不上你说的「上次」。"
                    + "把那张出图完成的卡翻出来点一次「拆图」，之后再说改哪儿。";
            }
            else if (arguments.DryRun || !arguments.WriteDownstream)
            {
                var why = arguments.DryRun ? "--dry-run true" : "--write-downstream false";
                replyText = "本机是只读模式（" + why + "），没有真重拆。";
                result = "只读模式没拆";
            }
            else
            {
                replyText = RunCut(
                    repositoryRoot, backendDriver, assetIdentifier, arguments, lines, out card, out var cut,
                    message.ConversationIdentifier, reply.CutFeedback);
                result = cut ? "已重拆" : "重拆失败";
            }

            var replyOutcome = SendReply(repositoryRoot, assistantDriver, message, replyText, arguments, lines, card);
            replyDelivered = replyOutcome.Delivered;
            replyRetryable = replyOutcome.Retryable;

            AssistantConversationHistory.Append(
                repositoryRoot, message.ConversationIdentifier, AssistantHistoryTurn.AssistantRole, replyText, DateTimeOffset.Now);

            AppendLedger(repositoryRoot, new JsonObject
            {
                ["时间"] = DateTimeOffset.Now.ToString("o"),
                ["信号"] = Path.GetFileName(signalFilePath),
                ["结果"] = result,
                ["回话送出"] = replyOutcome.Delivered,
                ["资产id"] = assetIdentifier,
                ["拆图意见"] = reply.CutFeedback,
                ["回话"] = replyText
            });

            return lines;
        }

        /// <summary>
        /// 人点了「拆图」：把那张整屏设计图按元素拆成一张张透明底单图，落进正式环境，
        /// 顺带写一份面板定义，让程序侧读 UXML 就懂这个界面怎么用。
        ///
        /// **拆是裁，不是重新生成**：每层重生一次，十层就是十种画风，拼回去不像一个界面。
        /// 框由视觉模型标——**框准不准是模型的事**，这里只保证不合法的框一律不用，
        /// 并且把每层的名字与框都摆到卡上，人看得见、不对能重来。
        /// </summary>
        private static IReadOnlyList<string> HandleCut(
            string repositoryRoot,
            string assistantDriver,
            string backendDriver,
            string signalFilePath,
            AssistantConversationMessage message,
            AssistantServeArguments arguments,
            List<string> lines,
            out bool replyDelivered,
            out bool replyRetryable,
            bool confirmed = false)
        {
            var assetIdentifier = message.ReadActionValue("资产id");
            var variantText = message.ReadActionValue("变体序号");
            var variantIndex = int.TryParse(variantText, out var parsedIndex) && parsedIndex > 0 ? parsedIndex : 1;
            string replyText;
            AssistantCard card = null;
            var result = "拆图失败";

            if (assetIdentifier.Length == 0)
            {
                replyText = "按钮没带资产 id，不知道拆哪张图。";
            }
            else if (arguments.DryRun || !arguments.WriteDownstream)
            {
                var why = arguments.DryRun ? "--dry-run true" : "--write-downstream false";
                replyText = "本机是只读模式（" + why + "），没有真拆。开了开关再点一次。";
                result = "只读模式没拆";
            }
            else
            {
                // 真跑之前先把原卡的按钮撤掉、文案换成「正在拆」。
                // 拆图跟出图一样要跑几十秒，那期间按钮还亮着——连点几下就是连着拆好几趟，
                // 后一趟还会把前一趟的落点文件覆盖掉。人也确实反馈过「不知道有没有拆完」。
                UpdateCutCard(
                    repositoryRoot, assistantDriver, message, assetIdentifier,
                    "正在标框…　这一步要让视觉模型把整张图看一遍，几十秒。这期间不用再点。",
                    withButtons: false, arguments: arguments, lines: lines);

                replyText = RunCut(
                    repositoryRoot, backendDriver, assetIdentifier, arguments, lines, out card, out var cut,
                    message.ConversationIdentifier, "", variantIndex,
                    progress: text => UpdateCutCard(
                        repositoryRoot, assistantDriver, message, assetIdentifier, text,
                        withButtons: false, arguments: arguments, lines: lines),
                    confirmed: confirmed);
                result = cut ? "已拆图" : (card != null ? "等确认" : "拆图失败");

                // 停在「要花这些钱吗」这一步时，卡片自己带着确认按钮，
                // 不该再把「拆图」按钮换回原卡——那会变成两个入口，人点哪个都对不上。
                if (!cut && card == null)
                {
                    // 没拆成才把按钮换回来——拆成了就不该再有「拆图」这个动作留在原卡上，
                    // 结果卡自己带着「哪层不对直接说」那条路。
                    UpdateCutCard(
                        repositoryRoot, assistantDriver, message, assetIdentifier,
                        "这一趟没拆成，按钮给你留着，可以再点一次。",
                        withButtons: true, arguments: arguments, lines: lines);
                }
            }

            var reply = SendReply(repositoryRoot, assistantDriver, message, replyText, arguments, lines, card);
            replyDelivered = reply.Delivered;
            replyRetryable = reply.Retryable;

            AssistantConversationHistory.Append(
                repositoryRoot, message.ConversationIdentifier, AssistantHistoryTurn.AssistantRole, replyText, DateTimeOffset.Now);

            AppendLedger(repositoryRoot, new JsonObject
            {
                ["时间"] = DateTimeOffset.Now.ToString("o"),
                ["信号"] = Path.GetFileName(signalFilePath),
                ["结果"] = result,
                ["回话送出"] = reply.Delivered,
                ["资产id"] = assetIdentifier,
                ["回话"] = replyText
            });

            return lines;
        }

        /// <summary>真跑一次拆图：问视觉模型要框 → 裁 → 按 UI元素 规格归一 → 落正式环境 → 写面板定义。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="backendDriver">执行后端 driver 名（要它看图）。</param>
        /// <param name="assetIdentifier">要拆的那份资产 id。</param>
        /// <param name="arguments">常驻会话命令参数。</param>
        /// <param name="lines">这一轮的日志行。</param>
        /// <param name="card">拆成了时带图的卡片；没拆成为 null。</param>
        /// <param name="cut">真拆出东西了没有。</param>
        /// <param name="conversationIdentifier">会话标识，拆图留底按它归档。</param>
        /// <param name="feedback">重拆意见；空串表示头一次拆。</param>
        /// <param name="variantIndex">拆第几张变体，从 1 起。</param>
        /// <param name="progress">报进度用；给 null 就不报。标框那一步几十秒，中间不吭声人会以为卡死了。</param>
        /// <param name="confirmed">人有没有点过那颗确认按钮；框标得太多时靠它放行。</param>
        private static string RunCut(
            string repositoryRoot,
            string backendDriver,
            string assetIdentifier,
            AssistantServeArguments arguments,
            List<string> lines,
            out AssistantCard card,
            out bool cut,
            string conversationIdentifier = "",
            string feedback = "",
            int variantIndex = 1,
            Action<string> progress = null,
            bool confirmed = false)
        {
            card = null;
            cut = false;

            var variantDirectory = AssetPaths.VariantDirectory(
                repositoryRoot, AssetRequest.UnownedRequirementIdentifier, assetIdentifier);
            var images = ListVariantImages(variantDirectory);
            if (images.Count == 0)
            {
                return "拆不了：" + assetIdentifier + " 的变体目录里一张图都没有（" + variantDirectory + "）。";
            }

            // 拆哪一张由按钮带回来的序号决定——出了几张就有几个候选，人挑哪张拆哪张。
            // 序号越界时退回第一张并说一句，不静默拆错一张。
            var pickIndex = variantIndex - 1;
            if (pickIndex < 0 || pickIndex >= images.Count)
            {
                lines.Add($"变体序号 {variantIndex} 越界（共 {images.Count} 张），退回第 1 张");
                pickIndex = 0;
            }

            var sourcePath = images[pickIndex];

            // 改拆图时把上一次的框原样喂回去，只动人说的那几处——从头再标一遍等于把
            // 已经标对的也重掷一次骰子，人明明只说了一句「关闭按钮框大了」，结果整套框全变。
            var previousLayers = Array.Empty<UiLayer>() as IReadOnlyList<UiLayer>;
            if (feedback.Length > 0)
            {
                // 改的是上一次拆的**那一张**，不是重新挑一张——人说的「关闭按钮框大了」
                // 指的是他刚看到的那批图，换一张源图就对不上了。
                previousLayers = AssistantServeTurn.ReadCut(repositoryRoot, assetIdentifier, out var previousSource);
                if (previousSource.Length > 0 && File.Exists(previousSource))
                {
                    sourcePath = previousSource;
                }
            }
            // 这条会话在做哪条需求 → 那条需求有没有出过功能图。有就照清单切，没有才看图猜。
            // 两条路的区别不在准不准，在**谁说了算**：清单是策划审过的功能契约，
            // 猜出来的是视觉模型看图看出来的。从前一屏猜出上百个、跟需求对不上、
            // 通用件认不出来——三样都是从这一点上错的（子文档 08 §六）。
            var plan = InterfaceCutPlanner.Resolve(
                repositoryRoot, ResolvePoolRoot(arguments), conversationIdentifier, feedback);
            lines.AddRange(plan.Notes);
            if (plan.Blocker.Length > 0)
            {
                return plan.Blocker;
            }

            var interfaceSpec = plan.Spec;
            var requestedElements = plan.Requests;

            string prompt;
            if (feedback.Length > 0 && previousLayers.Count > 0)
            {
                prompt = UiLayerCutter.BuildRecutPrompt(previousLayers, feedback);
                lines.Add($"重拆：带上一次 {previousLayers.Count} 层 + 意见「{Shorten(feedback)}」");
            }
            else if (requestedElements.Count > 0)
            {
                prompt = UiLayerCutter.BuildManifestPrompt(requestedElements);
            }
            else
            {
                prompt = UiLayerCutter.LayerPrompt;
            }

            // 问视觉模型要每个元素的框。图以 data: URL 内联发过去，不经任何第三方图床。
            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["提示"] = prompt,
                ["上下文"] = requestedElements.Count > 0
                    ? "你是给游戏 UI 找框的助手，只回 JSON，不回别的。清单之外的东西一概不要框。"
                    : "你是给游戏 UI 切图的助手，只回 JSON，不回别的。宁可多框一个，也别漏。",
                ["图片"] = new JsonArray { sourcePath }
            });

            var call = BridgeInvoker.Invoke(repositoryRoot, backendDriver, "complete", payload, Math.Max(arguments.TimeoutSeconds, 300));
            if (!call.Succeeded)
            {
                lines.Add($"视觉模型调用失败（{call.ErrorCode}）：{call.HumanText}");
                return "拆图失败：视觉模型没能看这张图（" + call.ErrorCode + "）：" + call.HumanText;
            }

            var layers = UiLayerCutter.ParseLayers(
                ReadPayloadString(call.Payload, "文本"), out var parseFailure, out var guessedModule);
            if (layers.Count == 0)
            {
                lines.Add($"层解析失败：{parseFailure}");
                return "拆图失败：" + parseFailure;
            }

            // 模块名：有规格时取规格的面板名，没有才退回让模型顺带猜的那个。
            // 面板名是确定的（规格里写着，还决定 uidef 名与图集），猜出来的每趟都可能不一样。
            var moduleName = interfaceSpec != null
                ? UiLayerCutter.SafeModuleName(interfaceSpec.PanelName)
                : guessedModule;

            // 照清单切时把清单外的框就地丢掉，找不到的如实报。
            // **丢掉不是可惜，正是这一步的意义**：一屏画面上能框出一百多个，
            // 而真正要出的只有那十几二十个，差额全是钱与后面没人认领的碎图。
            if (requestedElements.Count > 0)
            {
                var beforeCount = layers.Count;
                layers = UiLayerCutter.FilterToManifest(
                    layers, requestedElements, out var missingElements, out var unexpectedElements);

                lines.Add($"照 {interfaceSpec.Identifier} 的清单切：要 {requestedElements.Count} 个，"
                    + $"模型标了 {beforeCount} 个，对上 {layers.Count} 个");
                if (unexpectedElements.Count > 0)
                {
                    lines.Add($"清单外的框已丢掉（{unexpectedElements.Count} 个）：{string.Join("、", unexpectedElements)}");
                }

                if (missingElements.Count > 0)
                {
                    lines.Add($"清单里这几个没在图上找到（{missingElements.Count} 个）：{string.Join("、", missingElements)}");
                }

                if (layers.Count == 0)
                {
                    return "拆图失败：模型标的框没有一个对得上 " + interfaceSpec.Identifier + " 的清单。"
                        + "多半是这张图不是这一屏——换一张再点，或者先说清这是哪个界面。";
                }
            }

            // 标框那一步是最慢的，过了就报一次进度——人问过「不知道有没有拆完」，
            // 一条「标到 N 个」比一直沉默有用得多，还顺带说清这一趟打算切几片。
            progress?.Invoke((requestedElements.Count > 0
                    ? $"照 {interfaceSpec.Identifier} 的清单对上 {layers.Count} 个元素。"
                    : $"标到 {layers.Count} 个元素。")
                + "接下来逐个重绘成透明底单图——"
                + "每个元素一次生图调用，这一趟大概要几分钟，跑完我把结果贴上来。");

            var decoded = PngDecoder.DecodeFile(sourcePath);
            if (!decoded.Succeeded)
            {
                return "拆图失败：读不动那张整屏图（" + decoded.FailureReason + "）";
            }

            // 重绘要用的下游与配方**先查**，查不到就当场停：
            // 拆到第 8 个元素才发现没配方，前 7 次调用的钱已经花出去了。
            var routeTable = BridgeRouteTable.Load(repositoryRoot);
            if (!routeTable.TryResolvePort("生图", out var imageDriver, out var driverReason))
            {
                return "拆不了：生图这一域没有可用的下游（" + driverReason + "）";
            }

            var recipeRoute = AssetRecipeRouteTable.Load(repositoryRoot);
            if (!recipeRoute.TryResolve(imageDriver, UiElementAssetType, withReferenceImage: true, out var elementRecipe, out var recipeReason))
            {
                return "拆不了：" + recipeReason;
            }

            var catalog = AssetSpecCatalog.Load(repositoryRoot, "");
            var spec = catalog.Find(UiElementAssetType);
            // 落点要带**模块目录**：《结构规范-资源》的层级公式是「类型 → 功能 → 模块 → 内容」，
            // 例子就写着 Art/Texture/Ui/Inventory/T_背包格子.png。全堆在 Ui/ 根下的话，
            // 几个界面拆下来就是几百张平铺的图，而图集是按模块建的（一个模块一图集），
            // 分不出模块就分不出图集。模块名由标框那一步的模型顺带给——它正看着整张设计图，
            // 最清楚这是哪个功能；给不出就退回不分模块的落点，并说一句。
            var destination = spec?.Destination ?? "Assets/Game/Art/Texture/Ui/";
            if (moduleName.Length > 0)
            {
                destination = destination.TrimEnd('/') + "/" + moduleName + "/";
                lines.Add($"模块目录：{moduleName}");
            }
            else
            {
                lines.Add("模型没给出模块名，这一批平铺在功能层下（该由人挪进模块目录）");
            }
            var outputRoot = Path.Combine(repositoryRoot, "UnityProject", destination.Replace('/', Path.DirectorySeparatorChar));

            spec?.Values.TryGetValue("规格.需要透明", out var transparentText);
            var needsTransparency = spec != null
                && spec.Values.TryGetValue("规格.需要透明", out var transparent)
                && string.Equals(transparent, "true", StringComparison.OrdinalIgnoreCase);

            var written = new List<string>();
            var elements = new List<UiPanelElement>();
            var skipped = new List<string>();

            try
            {
                Directory.CreateDirectory(outputRoot);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return "拆图失败：建不了落点目录 " + outputRoot + "（" + exception.Message + "）";
            }

            // **点拆图就是选中的动作**——人从几张候选里挑一张点下去，那一下已经表达了「这张是对的」。
            // 所以顺手把这张原稿收进设计库，不必再另做一个「选片」步骤。
            // 收的是**原稿**不是重绘产物：拿重绘产物当风格锚点会世代退化——
            // 模型参考自己的输出，下一轮再参考「参考自己输出的输出」，
            // 几轮之后离原稿越来越远，而每一步看着都合理。
            var imported = DesignLibraryImporter.Import(repositoryRoot, moduleName, sourcePath, assetIdentifier);
            foreach (var note in imported.Notes)
            {
                lines.Add("设计库：" + note);
            }

            var usable = new List<UiLayer>();
            foreach (var layer in layers)
            {
                if (layer.IsUsable)
                {
                    usable.Add(layer);
                }
                else
                {
                    skipped.Add(layer.Name.Length == 0 ? "（没名字的一层）" : layer.Name);
                }
            }

            // 重绘是**一个元素一次生图调用**，而视觉模型标框很舍得——
            // 一张背包界面标出过 86 个框，那就是 86 次调用。这种量级不该由机器替人决定，
            // 所以超过门槛就停下来问一句；少于门槛的直接跑，不拿一个多余的确认去烦人。
            if (!confirmed && usable.Count > CutConfirmThreshold)
            {
                var minutes = Math.Max(1, (int)Math.Round(usable.Count * SecondsPerRedraw / 60.0));
                card = AssistantCard.ForCutConfirmation(
                    assetIdentifier,
                    variantIndex,
                    usable.Count,
                    "视觉模型在这张图上标了 " + usable.Count + " 个元素。\n"
                        + "每个元素都要单独调一次生图重画成透明底单图——这一趟就是 "
                        + usable.Count + " 次调用，大概 " + minutes + " 分钟，按次计费。\n"
                        + "\n框标多了的话，先跟我说「合并掉那些小图标」「只要按钮和面板底」之类的，我重标一遍再拆，比直接跑省。\n"
                        + "确定就点下面。");
                lines.Add($"标到 {usable.Count} 个元素，超过 {CutConfirmThreshold}，停下来等确认");
                return card.ToPlainText();
            }

            // 参考图片段落在临时区，不进正式落点：它带着邻居与面板底色，是**给模型看的**，
            // 不是成品。混进 Art/Texture/ 的话，图集里会多出一堆没人认领的脏图。
            var pieceRoot = Path.Combine(
                repositoryRoot, "_Tasks", "cut-pieces", AssistantConversationHistory.SafeFileName(assetIdentifier));

            for (var index = 0; index < usable.Count; index++)
            {
                var layer = usable[index];
                progress?.Invoke($"正在重绘第 {index + 1}/{usable.Count} 个元素（{layer.Name}）…");

                // 带一圈留白裁：这一刀出来的是**给模型看的参考图**，不是成品。
                // 贴着框裁的话，框标歪一点元素边缘就没了，模型照着残件抠出来的也是残的。
                var piece = UiLayerCutter.Cut(decoded.Image, layer, ReferencePaddingRatio);
                if (piece == null)
                {
                    skipped.Add(layer.Name);
                    continue;
                }

                var naming = AssetNamingNormalizer.Normalize(layer.Name, spec?.NamingPattern ?? "").Naming;

                // 坐标取**没留白的那个框**：那才是元素在界面上的真实位置，
                // 留白只是给模型多看一圈上下文，不该跟着写进面板定义。
                layer.ToPixels(decoded.Image.Width, decoded.Image.Height, out var x, out var y, out var w, out var h);

                // 裁下来的片段先落临时区当参考图。
                var piecePath = Path.Combine(pieceRoot, naming + ".png");
                try
                {
                    Directory.CreateDirectory(pieceRoot);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    return "拆图失败：建不了临时目录 " + pieceRoot + "（" + exception.Message + "）";
                }

                if (!PngEncoder.EncodeToFile(piece, piecePath, out var encodeReason))
                {
                    lines.Add($"{naming} 的参考片段写不出：{encodeReason}");
                    skipped.Add(layer.Name);
                    continue;
                }

                // 让模型照着这一小块**重画**一张透明底单图。
                // 为什么不直接用裁下来的那块：元素互相压叠时一刀必然带上邻居，
                // 而白底上的白件抠底会顺着主体灌进去打洞——这两条本地都解不了。
                var redrawn = RedrawElement(
                    repositoryRoot, imageDriver, elementRecipe, naming, layer.Name, moduleName, destination, piecePath, w, h,
                    arguments, lines, out var redrawFailure);

                if (redrawn.Length == 0)
                {
                    lines.Add($"{naming} 重绘没成：{redrawFailure}");
                    skipped.Add(layer.Name + "（重绘失败）");
                    continue;
                }

                var filePath = Path.Combine(outputRoot, naming + ".png");
                try
                {
                    File.Copy(redrawn, filePath, overwrite: true);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    lines.Add($"{naming} 落点写不进去：{exception.Message}");
                    skipped.Add(layer.Name);
                    continue;
                }

                // **先裁透明边，再缩**。下游只出它自己那几档尺寸，而 UI 元素什么长宽比都有：
                // 一条 1565×54 的长条会被画在 1536×1024 的画布中间、四周全透明，
                // 不裁就直接缩的话那条长条会被压成几个像素高，整张图作废。
                TrimElement(filePath, lines, naming);

                // 再按框的实际尺寸缩回去。透明这一步交给规格：
                // 模型应当已经给了透明底，归一只在没给时兜底。
                var normalized = AssetImageNormalizer.Normalize(filePath, w, h, needsTransparency);
                foreach (var note in normalized.Remaining)
                {
                    lines.Add($"{naming} 还差：{note}");
                }

                elements.Add(new UiPanelElement(
                    layer.Name,
                    UiPanelDefinitionWriter.GuessIdentifier(layer.Name),
                    UiPanelDefinitionWriter.GuessElementType(layer.Name),
                    destination.TrimEnd('/') + "/" + naming + ".png",
                    x, y, w, h));
                written.Add(filePath);
                lines.Add($"重绘出 {naming}.png（{w}×{h}）");
            }

            if (written.Count == 0)
            {
                return "拆图失败：一个元素都没出来。"
                    + (skipped.Count > 0 ? "卡在这些上：" + string.Join("、", skipped) + "。" : "")
                    + "可以再点一次重试。";
            }

            var panelIdentifier = UiPanelDefinitionWriter.GuessIdentifier(assetIdentifier) + "Panel";
            var definitionPath = UiPanelDefinitionWriter.Write(
                repositoryRoot, assetIdentifier + " 界面", panelIdentifier, elements);
            lines.Add($"面板定义：{(definitionPath.Length == 0 ? "写失败" : definitionPath)}");

            // 三件套**当场生成**，不留给人手跑。
            // 两个理由：一是「接上项目的 UI 工作流」本来就是这条链的目的，
            // 停在一份 uidef 上等于只做了一半；二是生成物幂等门禁扫 UI/Definitions/ 下每一份定义，
            // 写了定义却不生成产物，下一次跑门禁必红——而红的原因跟拆图看着毫无关系，
            // 人要翻半天才找到是这儿留的尾巴。
            var scaffolded = definitionPath.Length > 0 && RunScaffold(repositoryRoot, definitionPath, lines);

            // 留底：下一次人说「那层框大了」时，要靠它把上一次的框喂回给模型。
            if (!AssistantServeTurn.SaveCut(repositoryRoot, conversationIdentifier, assetIdentifier, sourcePath, layers))
            {
                lines.Add("拆图留底写失败——下次说「改一改」时接不上上一次的框");
            }

            cut = true;
            var builder = new StringBuilder();
            if (imported.FinalCreated)
            {
                builder.Append("顺带把这张定成了 ").Append(moduleName)
                    .Append(" 的第一版风格（主色从图上算的，参考图就是它本身）——")
                    .Append("往后这个模块的图都以它为锚点。\n\n");
            }

            builder.Append("拆出 ").Append(written.Count).Append(" 个元素，每个都是模型照着设计图重画的透明底单图。")
                .Append("\n\n全在这儿了，去引擎里看：\n").Append(destination).Append('\n');

            // **清单封顶**：一屏能拆出几十个元素，几十行清单刷下来聊天框根本没法看
            // （真被这么抱怨过）。完整的名字与坐标都在面板定义里，图本体在引擎的正式落点里——
            // 在 Project 面板里扫一眼，比在聊天里一条条翻快得多。
            for (var index = 0; index < elements.Count && index < ElementListLimit; index++)
            {
                var element = elements[index];
                builder.Append("· ").Append(element.DisplayName).Append("　")
                    .Append(element.Width).Append('×').Append(element.Height)
                    .Append("　→ ").Append(element.ElementType).Append('\n');
            }

            if (elements.Count > ElementListLimit)
            {
                builder.Append("· …另外 ").Append(elements.Count - ElementListLimit)
                    .Append(" 个，名字与坐标都在下面那份面板定义里。").Append('\n');
            }

            if (skipped.Count > 0)
            {
                builder.Append("没拆出来的：").Append(string.Join("、", skipped)).Append("（框不合法）\n");
            }

            builder.Append("\n哪个框得不对、漏了什么、多切了什么，直接说，我在这一版基础上改。\n")
                .Append("（重绘是照着框里那一小块画的，所以框歪了元素也会歪——先说框。）\n");
            if (definitionPath.Length == 0)
            {
                builder.Append("面板定义没写成，得人看一眼磁盘。");
            }
            else if (scaffolded)
            {
                builder.Append("面板定义与 UXML/USS/C# 都出好了：UI/Definitions/").Append(panelIdentifier).Append(".uidef.json\n")
                    .Append("程序侧读那份 UXML 就知道这个界面怎么用，不用读图。\n")
                    .Append("元素类型是按层名猜的，不对就改 uidef 一行，再说一声我重生成。");
            }
            else
            {
                builder.Append("面板定义写好了：UI/Definitions/").Append(panelIdentifier).Append(".uidef.json，\n")
                    .Append("但三件套没生成成（原因在日志里）。得人跑一次 ui.scaffold，\n")
                    .Append("不然下次跑门禁会因为「生成物幂等」判红。");
            }

            // **贴图张数也封顶**，且贴的是缩略图（桥那边 compact_width，点开能放大）。
            // 几十张满宽大图刷下来，人要滑半天才划得完；前几张够看出拆成了什么样，
            // 剩下的报个路径就行——它们已经在引擎里了。
            var shown = written.Count <= AssistantCard.MaximumImagesOnCard
                ? written
                : written.GetRange(0, AssistantCard.MaximumImagesOnCard);
            if (written.Count > shown.Count)
            {
                builder.Append("\n（下面只贴了前 ").Append(shown.Count).Append(" 张缩略图，点开能放大；其余 ")
                    .Append(written.Count - shown.Count).Append(" 张去上面那个目录看。）").Append('\n');
            }

            card = AssistantCard.ForGeneratedImages(builder.ToString(), shown);
            return card.ToPlainText();
        }

        /// <summary>拆出来的单个 UI 元素在资产规格里叫什么。</summary>
        private const string UiElementAssetType = "UI元素";

        /// <summary>
        /// 开一个新话题：往历史里插一条分隔线，并回一句说明。
        /// 按钮与打字两条入口都走这里，免得两处各写一遍、行为再慢慢分叉。
        /// </summary>
        private static IReadOnlyList<string> StartNewTopic(
            string repositoryRoot,
            string assistantDriver,
            string signalFilePath,
            AssistantConversationMessage message,
            AssistantServeArguments arguments,
            List<string> lines,
            out bool replyDelivered,
            out bool replyRetryable)
        {
            var started = AssistantConversationHistory.StartNewTopic(
                repositoryRoot,
                message.ConversationIdentifier,
                message.IsCardAction ? "按了开新话题" : "打字说开新话题",
                DateTimeOffset.Now);

            var text = started
                ? "好，前面聊的我不再带上了，从这儿重新开始。你想做什么？"
                : "我这边记不下这条分隔（磁盘写不动），上下文没能清掉——先跟人说一声再继续。";
            lines.Add(started ? "已开新话题，上下文从此处断开" : "开新话题失败：历史写不动");

            var reply = SendReply(repositoryRoot, assistantDriver, message, text, arguments, lines, null);
            replyDelivered = reply.Delivered;
            replyRetryable = reply.Retryable;

            AppendLedger(repositoryRoot, new JsonObject
            {
                ["时间"] = DateTimeOffset.Now.ToString("o"),
                ["信号"] = Path.GetFileName(signalFilePath),
                ["结果"] = started ? "开新话题" : "开新话题失败",
                ["回话送出"] = reply.Delivered,
                ["会话"] = message.ConversationIdentifier
            });

            return lines;
        }

        /// <summary>
        /// 回一句话：干跑时只打印，真跑时经助手 driver 的 reply 动作发出去。
        /// 带卡片时把归一的卡片数据一并塞进载荷——**文本照旧要给**：
        /// 下游发不了卡片时要能退回纯文本，不许因为卡片发不成就什么都不回。
        /// </summary>
        private static ReplyOutcome SendReply(
            string repositoryRoot,
            string assistantDriver,
            AssistantConversationMessage message,
            string text,
            AssistantServeArguments arguments,
            List<string> lines,
            AssistantCard card)
        {
            if (arguments.DryRun)
            {
                lines.Add("干跑：本该回的话是「" + Shorten(text) + "」"
                    + (card == null ? "" : "（带卡片，按钮 " + string.Join("、", card.Buttons.Select(button => button.Label)) + "）"));
                return new ReplyOutcome(true, false);
            }

            var body = new JsonObject
            {
                ["干跑"] = false,
                ["会话标识"] = message.ConversationIdentifier,
                ["文本"] = text
            };

            if (card != null)
            {
                body["卡片"] = card.ToJson();
            }

            var payload = JsonSerializer.SerializeToElement(body);

            var call = BridgeInvoker.Invoke(repositoryRoot, assistantDriver, "reply", payload, arguments.TimeoutSeconds);
            lines.Add(call.Succeeded
                ? "已回话：" + Shorten(text)
                : $"回话失败（{call.ErrorCode}）：{call.HumanText}");

            // 记下这张带按钮的卡，下一轮开头把它的按钮撤掉。
            // **只记带按钮的**：没有按钮的卡不会被误点，记了反而多一次白跑的 card-update。
            if (call.Succeeded && card != null && card.Buttons.Count > 0)
            {
                var sentIdentifier = ReadPayloadString(call.Payload, "消息标识");
                if (sentIdentifier.Length > 0)
                {
                    LiveCardRegistry.Remember(
                        repositoryRoot,
                        message.ConversationIdentifier,
                        sentIdentifier,
                        ReadPayloadString(call.Payload, "卡片JSON"));
                }
            }

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

        /// <summary>读响应载荷里整数键的值；缺失或类型不对给 0。</summary>
        private static int ReadPayloadInt(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number))
            {
                return number;
            }

            return 0;
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
