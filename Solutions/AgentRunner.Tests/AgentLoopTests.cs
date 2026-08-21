using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Template.Toolkit.AgentRunner;
using Xunit;

namespace Template.Toolkit.AgentRunnerTests
{
    /// <summary>工具循环测试：用假传输喂预置回合，验证工具执行、收尾、轮数上限与传输失败的打断。</summary>
    public class AgentLoopTests : IDisposable
    {
        private readonly string _root;

        /// <summary>建一棵临时树给工具箱活动。</summary>
        public AgentLoopTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentLoopTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        /// <summary>清掉临时树。</summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>假传输：按队列吐预置回合，耗尽后抛错。</summary>
        private sealed class QueueTransport : IChatTransport
        {
            private readonly Queue<ChatTurn> _turns;

            /// <summary>用预置回合建一个假传输。</summary>
            public QueueTransport(params ChatTurn[] turns)
            {
                _turns = new Queue<ChatTurn>(turns);
            }

            /// <summary>吐下一个预置回合。</summary>
            public ChatTurn Complete(JsonArray messages, JsonArray tools)
            {
                if (_turns.Count == 0)
                {
                    throw new InvalidOperationException("假传输的预置回合用完了");
                }

                return _turns.Dequeue();
            }
        }

        private static ChatTurn FinalTurn(string content)
        {
            return new ChatTurn(new JsonObject { ["role"] = "assistant", ["content"] = content }, 10, "test-model");
        }

        private static ChatTurn ToolCallTurn(string toolName, string argumentsJson)
        {
            return new ChatTurn(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = "",
                ["tool_calls"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "call-1",
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = toolName, ["arguments"] = argumentsJson }
                    }
                }
            }, 20, "test-model");
        }

        private AgentLoop MakeLoop(IChatTransport transport)
        {
            var policy = new AgentPolicy(
                new[] { "git status" }, Array.Empty<string>(), Array.Empty<string>(), 60, 1000, 1000);
            return new AgentLoop(transport, new AgentToolbox(_root, policy, allowWrite: true), transcriptPath: "");
        }

        /// <summary>先一轮工具调用（写文件）再一轮正文：文件真被写了、正文与轮数都对。</summary>
        [Fact]
        public void ToolCallThenFinalExecutesToolAndReturnsText()
        {
            var loop = MakeLoop(new QueueTransport(
                ToolCallTurn("write_file", "{\"path\":\"note.txt\",\"content\":\"hello\"}"),
                FinalTurn("干完了")));

            var result = loop.Run("system", "task", maxRounds: 5);

            Assert.Equal("", result.AbortReason);
            Assert.Equal("干完了", result.FinalText);
            Assert.Equal(2, result.Rounds);
            Assert.Equal(1, result.ToolCallCount);
            Assert.Equal(30, result.TotalTokens);
            Assert.Equal("hello", File.ReadAllText(Path.Combine(_root, "note.txt")));
        }

        /// <summary>一直回工具调用：到轮数上限后追加一轮不带工具的收尾调用，把进展压成回报。</summary>
        [Fact]
        public void RoundCapTriggersWrapUpTurn()
        {
            var loop = MakeLoop(new QueueTransport(
                ToolCallTurn("list_directory", "{\"path\":\".\"}"),
                ToolCallTurn("list_directory", "{\"path\":\".\"}"),
                FinalTurn("查到一半的进展")));

            var result = loop.Run("system", "task", maxRounds: 2);

            Assert.Contains("轮数上限", result.AbortReason);
            Assert.Equal("查到一半的进展", result.FinalText);
            Assert.Equal(2, result.Rounds);
        }

        /// <summary>传输抛错：转成带原因的打断，不向上抛。</summary>
        [Fact]
        public void TransportFailureBecomesAbortReason()
        {
            var loop = MakeLoop(new QueueTransport());

            var result = loop.Run("system", "task", maxRounds: 3);

            Assert.Contains("传输失败", result.AbortReason);
            Assert.Equal("", result.FinalText);
        }
    }
}
