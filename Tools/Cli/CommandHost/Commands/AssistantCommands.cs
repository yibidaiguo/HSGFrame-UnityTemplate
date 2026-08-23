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

            if (string.Equals(message.ActionName, AssistantCard.NewTopicAction, StringComparison.Ordinal))
            {
                return StartNewTopic(repositoryRoot, assistantDriver, signalFilePath, message, arguments, lines, out replyDelivered, out replyRetryable);
            }

            if (string.Equals(message.ActionName, AssistantCard.CutAction, StringComparison.Ordinal))
            {
                return HandleCut(
                    repositoryRoot, assistantDriver, backendDriver, signalFilePath, message, arguments, lines,
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
                var findings = AssistantServeTurn.Validate(draft, identifier, schema);
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
                    // 一路做完：写池子 → 出文档 → 推知识库 → 任务表加一行。
                    // 池子是第一步也是最要紧的一步——它是事实源，后面几步都是它的视图，
                    // 哪一步挂了都不影响「这条需求已经立住了」。
                    if (!TryLandRequirement(poolRoot, identifier, draft, lines, out var landFailure))
                    {
                        replyText = identifier + " 没建成：写需求池失败——" + landFailure + "。再点一次可以重试。";
                    }
                    else
                    {
                        wroteDownstream = true;
                        result = "已建需求";

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

                        var wakePath = WakeSignalSource.Emit(
                            repositoryRoot,
                            "助手产出草稿",
                            new JsonObject { ["需求id"] = identifier, ["来自会话"] = message.ConversationIdentifier },
                            DateTimeOffset.Now);
                        lines.Add($"已投唤醒信号：{(wakePath.Length == 0 ? "写失败" : Path.GetFileName(wakePath))}");

                        var documentLink = PublishDocument(repositoryRoot, poolRoot, identifier, arguments, lines, out var documentFailure);
                        var rowFailure = AddTaskRow(repositoryRoot, assistantDriver, identifier, draft, documentLink, arguments, lines);

                        replyText = DescribeCreation(identifier, documentLink, documentFailure, rowFailure);
                    }
                }
            }

            var reply = SendReply(repositoryRoot, assistantDriver, message, replyText, arguments, lines, null);
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
                + (referenceImagePath.Length > 0 ? "，照着你给的参考图" : "") + "）。"
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

            var variantDirectory = AssetPaths.VariantDirectory(
                repositoryRoot, AssetRequest.UnownedRequirementIdentifier, assetIdentifier);
            var images = ListVariantImages(variantDirectory);

            var card = AssistantCard.ForGeneratedImages(statusText, images, assetIdentifier, canCut: withButtons);
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
                    builder.Append("\n（还有 ").Append(lines.Count - shown).Append(" 条，看日志）");
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
            out bool replyRetryable)
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
                        withButtons: false, arguments: arguments, lines: lines));
                result = cut ? "已拆图" : "拆图失败";

                if (!cut)
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
            Action<string> progress = null)
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
            var prompt = feedback.Length > 0 && previousLayers.Count > 0
                ? UiLayerCutter.BuildRecutPrompt(previousLayers, feedback)
                : UiLayerCutter.LayerPrompt;

            if (feedback.Length > 0)
            {
                lines.Add($"重拆：带上一次 {previousLayers.Count} 层 + 意见「{Shorten(feedback)}」");
            }

            // 问视觉模型要每个元素的框。图以 data: URL 内联发过去，不经任何第三方图床。
            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["提示"] = prompt,
                ["上下文"] = "你是给游戏 UI 切图的助手，只回 JSON，不回别的。宁可多框一个，也别漏。",
                ["图片"] = new JsonArray { sourcePath }
            });

            var call = BridgeInvoker.Invoke(repositoryRoot, backendDriver, "complete", payload, Math.Max(arguments.TimeoutSeconds, 300));
            if (!call.Succeeded)
            {
                lines.Add($"视觉模型调用失败（{call.ErrorCode}）：{call.HumanText}");
                return "拆图失败：视觉模型没能看这张图（" + call.ErrorCode + "）：" + call.HumanText;
            }

            var layers = UiLayerCutter.ParseLayers(ReadPayloadString(call.Payload, "文本"), out var parseFailure);
            if (layers.Count == 0)
            {
                lines.Add($"层解析失败：{parseFailure}");
                return "拆图失败：" + parseFailure;
            }

            // 标框那一步是最慢的，过了就报一次进度——人问过「不知道有没有拆完」，
            // 一条「标到 N 个」比一直沉默有用得多，还顺带说清这一趟打算切几片。
            progress?.Invoke($"标到 {layers.Count} 个元素，正在逐个裁切并落盘…");

            var decoded = PngDecoder.DecodeFile(sourcePath);
            if (!decoded.Succeeded)
            {
                return "拆图失败：读不动那张整屏图（" + decoded.FailureReason + "）";
            }

            var catalog = AssetSpecCatalog.Load(repositoryRoot, "");
            var spec = catalog.Find(UiElementAssetType);
            var destination = spec?.Destination ?? "Assets/Game/Art/Texture/Ui/";
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

            foreach (var layer in layers)
            {
                if (!layer.IsUsable)
                {
                    skipped.Add(layer.Name.Length == 0 ? "（没名字的一层）" : layer.Name);
                    continue;
                }

                var piece = UiLayerCutter.Cut(decoded.Image, layer);
                if (piece == null)
                {
                    skipped.Add(layer.Name);
                    continue;
                }

                var naming = AssetNamingNormalizer.Normalize(layer.Name, spec?.NamingPattern ?? "").Naming;
                var filePath = Path.Combine(outputRoot, naming + ".png");
                if (!PngEncoder.EncodeToFile(piece, filePath, out var encodeReason))
                {
                    lines.Add($"写不出 {naming}.png：{encodeReason}");
                    skipped.Add(layer.Name);
                    continue;
                }

                // 切下来的那块也要按 UI元素 规格走一遍：抠透明。**尺寸不缩**——
                // 每个元素本来就大小不一，硬塞进 512×512 会把它拉变形。
                if (needsTransparency)
                {
                    var normalized = AssetImageNormalizer.Normalize(filePath, 0, 0, needsTransparency: true);
                    foreach (var note in normalized.Remaining)
                    {
                        lines.Add($"{naming} 还差：{note}");
                    }
                }

                layer.ToPixels(decoded.Image.Width, decoded.Image.Height, out var x, out var y, out var w, out var h);
                elements.Add(new UiPanelElement(
                    layer.Name,
                    UiPanelDefinitionWriter.GuessIdentifier(layer.Name),
                    UiPanelDefinitionWriter.GuessElementType(layer.Name),
                    destination.TrimEnd('/') + "/" + naming + ".png",
                    x, y, w, h));
                written.Add(filePath);
                lines.Add($"拆出 {naming}.png（{w}×{h}）");
            }

            if (written.Count == 0)
            {
                return "拆图失败：一层都没能拆出来（模型给的框全都不合法）。可以再点一次重试。";
            }

            var panelIdentifier = UiPanelDefinitionWriter.GuessIdentifier(assetIdentifier) + "Panel";
            var definitionPath = UiPanelDefinitionWriter.Write(
                repositoryRoot, assetIdentifier + " 界面", panelIdentifier, elements);
            lines.Add($"面板定义：{(definitionPath.Length == 0 ? "写失败" : definitionPath)}");

            // 留底：下一次人说「那层框大了」时，要靠它把上一次的框喂回给模型。
            if (!AssistantServeTurn.SaveCut(repositoryRoot, conversationIdentifier, assetIdentifier, sourcePath, layers))
            {
                lines.Add("拆图留底写失败——下次说「改一改」时接不上上一次的框");
            }

            cut = true;
            var builder = new StringBuilder();
            builder.Append("拆出 ").Append(written.Count).Append(" 层，已经落进正式环境：\n")
                .Append(destination).Append('\n');
            foreach (var element in elements)
            {
                builder.Append("· ").Append(element.DisplayName).Append("　")
                    .Append(element.Width).Append('×').Append(element.Height)
                    .Append("　→ ").Append(element.ElementType).Append('\n');
            }

            if (skipped.Count > 0)
            {
                builder.Append("没拆出来的：").Append(string.Join("、", skipped)).Append("（框不合法）\n");
            }

            builder.Append("\n哪层框得不对、漏了什么、多切了什么，直接说，我在这一版基础上改。\n");
            builder.Append(definitionPath.Length == 0
                ? "面板定义没写成，得人看一眼磁盘。"
                : "面板定义写好了：UI/Definitions/" + panelIdentifier + ".uidef.json。\n"
                    + "跑一次 ui.scaffold 就出 UXML/USS/C#——程序侧读那份 UXML 就知道这个界面怎么用，不用读图。\n"
                    + "元素类型是按层名猜的，不对改 uidef 一行再重跑。");

            card = AssistantCard.ForGeneratedImages(builder.ToString(), written);
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
