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
            var handledCount = 0;
            var writtenCount = 0;
            var round = 0;
            var stopReason = "跑满轮数";

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
                    lines.Add($"轮次 {round}　没有待回的消息（{poll.Reason}）");
                    if (arguments.MaxRounds <= 0 || round < arguments.MaxRounds)
                    {
                        Thread.Sleep(Math.Max(0, arguments.RoundDelayMilliseconds));
                    }

                    continue;
                }

                var turnLines = RunOneTurn(
                    repositoryRoot,
                    poolRoot,
                    assistantDriver,
                    backendDriver,
                    schema,
                    poll.SignalFilePath,
                    arguments,
                    out var wroteDownstream);
                handledCount++;
                if (wroteDownstream)
                {
                    writtenCount++;
                }

                foreach (var line in turnLines)
                {
                    lines.Add($"轮次 {round}　{line}");
                }

                // 判定全跑完才消费信号（决策 82）。
                var archived = ConversationSignalSource.Consume(repositoryRoot, poll.SignalFilePath);
                lines.Add($"轮次 {round}　信号归档：{(archived.Length == 0 ? "移动失败，信号留在原地下一轮还会取到" : archived)}");
            }

            return CommandResult.Success(
                $"助手会话跑了 {round} 轮（处理消息 {handledCount} 条，写下游草稿 {writtenCount} 条）；停止原因：{stopReason}",
                lines);
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
            out bool wroteDownstream)
        {
            wroteDownstream = false;
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
                return lines;
            }

            lines.Add($"收到消息：会话={message.ConversationIdentifier}　类型={message.MessageKind}　字数={message.Text.Length}");

            if (!message.IsHandleableText)
            {
                var text = "我这边现在只认文字消息，这一条是「" + (message.MessageKind.Length == 0 ? "未知类型" : message.MessageKind) + "」，没法处理。";
                lines.Add("不是可处理的文字消息，回一句说明");
                SendReply(repositoryRoot, assistantDriver, message, text, arguments, lines);
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
                SendReply(repositoryRoot, assistantDriver, message, text, arguments, lines);
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

            SendReply(repositoryRoot, assistantDriver, message, outcome.ReplyText, arguments, lines);
            AppendLedger(repositoryRoot, new JsonObject
            {
                ["时间"] = DateTimeOffset.Now.ToString("o"),
                ["信号"] = Path.GetFileName(signalFilePath),
                ["结果"] = outcome.ShouldWriteDownstream ? "校验通过" : "没建需求",
                ["需求id"] = outcome.RequirementIdentifier,
                ["模型"] = modelName,
                ["提示词版本"] = prompt.PromptVersion,
                ["写了下游"] = wroteDownstream,
                ["回话"] = outcome.ReplyText,
                ["校验发现数"] = outcome.Findings.Count
            });

            return lines;
        }

        /// <summary>回一句话：干跑时只打印，真跑时经助手 driver 的 reply 动作发出去。</summary>
        private static void SendReply(
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
                return;
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
