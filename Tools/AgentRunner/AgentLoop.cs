using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.AgentRunner
{
    /// <summary>一次工具循环的结果。</summary>
    public sealed class AgentLoopResult
    {
        /// <summary>
        /// 构造一份结果。
        /// </summary>
        /// <param name="finalText">模型的最终正文；被打断时可能为空串。</param>
        /// <param name="rounds">实际跑了几轮（一轮 = 一次 HTTP 调用）。</param>
        /// <param name="totalTokens">全部轮次的 token 总数。</param>
        /// <param name="toolCallCount">执行过的工具调用总数。</param>
        /// <param name="abortReason">被打断的原因；正常收尾时为空串。</param>
        public AgentLoopResult(string finalText, int rounds, int totalTokens, int toolCallCount, string abortReason)
        {
            FinalText = finalText ?? "";
            Rounds = rounds;
            TotalTokens = totalTokens;
            ToolCallCount = toolCallCount;
            AbortReason = abortReason ?? "";
        }

        /// <summary>模型的最终正文；被打断时可能为空串。</summary>
        public string FinalText { get; }

        /// <summary>实际跑了几轮（一轮 = 一次 HTTP 调用）。</summary>
        public int Rounds { get; }

        /// <summary>全部轮次的 token 总数。</summary>
        public int TotalTokens { get; }

        /// <summary>执行过的工具调用总数。</summary>
        public int ToolCallCount { get; }

        /// <summary>被打断的原因；正常收尾时为空串。</summary>
        public string AbortReason { get; }
    }

    /// <summary>
    /// 函数调用工具循环：发消息 → 模型回 tool_calls 就逐个执行、把结果追加回消息数组 →
    /// 直到模型回纯正文或到达轮数上限。
    /// 转录逐行落 JSONL（每轮的助手消息、每次工具调用与结果摘要），密钥永不进转录。
    /// </summary>
    public sealed class AgentLoop
    {
        private readonly IChatTransport _transport;
        private readonly AgentToolbox _toolbox;
        private readonly string _transcriptPath;

        /// <summary>
        /// 构造一个循环。
        /// </summary>
        /// <param name="transport">chat 传输。</param>
        /// <param name="toolbox">本地工具箱。</param>
        /// <param name="transcriptPath">转录 JSONL 文件路径；空串表示不落转录。</param>
        public AgentLoop(IChatTransport transport, AgentToolbox toolbox, string transcriptPath)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _toolbox = toolbox ?? throw new ArgumentNullException(nameof(toolbox));
            _transcriptPath = transcriptPath ?? "";
        }

        /// <summary>
        /// 跑完一次任务：系统提示 + 任务书起步，循环到模型给出最终正文或到达轮数上限。
        /// 传输层抛错（连不上、重试后仍限流）会转成带原因的打断，不向上抛。
        /// </summary>
        /// <param name="systemText">系统提示（角色档案 + 工具协议）。</param>
        /// <param name="taskText">任务书全文。</param>
        /// <param name="maxRounds">轮数上限。</param>
        public AgentLoopResult Run(string systemText, string taskText, int maxRounds)
        {
            var messages = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemText ?? "" },
                new JsonObject { ["role"] = "user", ["content"] = taskText ?? "" }
            };
            var tools = _toolbox.BuildToolDefinitions();

            var totalTokens = 0;
            var toolCallCount = 0;
            for (var round = 1; round <= Math.Max(1, maxRounds); round++)
            {
                ChatTurn turn;
                try
                {
                    turn = _transport.Complete(messages, tools);
                }
                catch (InvalidOperationException exception)
                {
                    AppendTranscript(new JsonObject { ["轮次"] = round, ["事件"] = "传输失败", ["原因"] = exception.Message });
                    return new AgentLoopResult("", round, totalTokens, toolCallCount, "传输失败：" + exception.Message);
                }

                totalTokens += turn.TotalTokens;
                messages.Add(turn.AssistantMessage);

                var contentText = ReadContent(turn.AssistantMessage);
                if (turn.AssistantMessage["tool_calls"] is not JsonArray toolCalls || toolCalls.Count == 0)
                {
                    AppendTranscript(new JsonObject { ["轮次"] = round, ["事件"] = "最终正文", ["字符数"] = contentText.Length });
                    return new AgentLoopResult(contentText, round, totalTokens, toolCallCount, "");
                }

                AppendTranscript(new JsonObject
                {
                    ["轮次"] = round,
                    ["事件"] = "助手消息",
                    ["工具调用数"] = toolCalls.Count,
                    ["正文摘要"] = Truncate(contentText, 200)
                });

                // 先把每个调用抄出来再执行：执行结果要按 tool_call_id 逐个回填。
                var calls = new List<(string Identifier, string Name, string Arguments)>();
                foreach (var node in toolCalls)
                {
                    if (node is not JsonObject call)
                    {
                        continue;
                    }

                    var identifier = call["id"]?.GetValue<string>() ?? "";
                    var name = (call["function"] as JsonObject)?["name"]?.GetValue<string>() ?? "";
                    var argumentsText = (call["function"] as JsonObject)?["arguments"]?.GetValue<string>() ?? "{}";
                    calls.Add((identifier, name, argumentsText));
                }

                foreach (var call in calls)
                {
                    var result = _toolbox.Execute(call.Name, call.Arguments);
                    toolCallCount++;
                    AppendTranscript(new JsonObject
                    {
                        ["轮次"] = round,
                        ["事件"] = "工具",
                        ["工具"] = call.Name,
                        ["参数摘要"] = Truncate(call.Arguments, 300),
                        ["结果摘要"] = Truncate(result, 300)
                    });
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = call.Identifier,
                        ["content"] = result
                    });
                }
            }

            // 轮数用完不空手而归：追加收尾指令，发一次不带工具的调用，把已有进展压成回报。
            AppendTranscript(new JsonObject { ["事件"] = "轮数上限", ["上限"] = maxRounds });
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = "轮数已用完。立即停止使用工具，把你目前的进展按角色档案「返回什么」的形状输出成最终回报；没查完、没做完的部分如实标注，不许编。"
            });
            try
            {
                var wrapUpTurn = _transport.Complete(messages, null);
                totalTokens += wrapUpTurn.TotalTokens;
                var wrapUpText = ReadContent(wrapUpTurn.AssistantMessage);
                AppendTranscript(new JsonObject { ["事件"] = "收尾轮", ["字符数"] = wrapUpText.Length });
                return new AgentLoopResult(wrapUpText, maxRounds, totalTokens, toolCallCount,
                    $"到达轮数上限（{maxRounds}），回报是收尾轮压出来的进展，不是完整收尾");
            }
            catch (InvalidOperationException exception)
            {
                AppendTranscript(new JsonObject { ["事件"] = "收尾轮失败", ["原因"] = exception.Message });
                return new AgentLoopResult("", maxRounds, totalTokens, toolCallCount, $"到达轮数上限（{maxRounds}），任务没有收尾");
            }
        }

        private static string ReadContent(JsonObject assistantMessage)
        {
            return assistantMessage["content"] is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : "";
        }

        private void AppendTranscript(JsonObject line)
        {
            if (_transcriptPath.Length == 0)
            {
                return;
            }

            line["时间"] = DateTimeOffset.Now.ToString("O");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_transcriptPath) ?? ".");
                File.AppendAllText(
                    _transcriptPath,
                    line.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default)
                    {
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    }) + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 转录写不进不打断任务：转录是留痕，不是任务本体。
            }
        }

        private static string Truncate(string text, int limit)
        {
            var content = text ?? "";
            return content.Length <= limit ? content : content.Substring(0, limit) + "…";
        }
    }
}
