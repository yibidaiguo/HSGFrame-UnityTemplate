using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 助手会话的**上下文与卡片**这一半：会话历史、开新话题、确认卡、按钮点击。
    ///
    /// 盯的是这次改动要立住的三条：
    /// 1. **助手不再健忘**：同一条会话的历史读得回来，且进得了提示词。
    /// 2. **上下文能主动丢**：开新话题之后读到的只有分隔线之后的内容，而账本还在。
    /// 3. **建不建由人点**：校验通过只产出一张带按钮的卡，回话里不许说「已经建了」。
    /// </summary>
    public class AssistantConversationTests
    {
        /// <summary>同一条会话按顺序追加的两轮，读得回来，顺序不乱。</summary>
        [Fact]
        public void HistoryKeepsTurnsInOrder()
        {
            var root = NewTemporaryDirectory();
            try
            {
                AssistantConversationHistory.Append(root, "c-1", AssistantHistoryTurn.UserRole, "要做个背包", At(1));
                AssistantConversationHistory.Append(root, "c-1", AssistantHistoryTurn.AssistantRole, "跟传统 RPG 一样吗", At(2));
                AssistantConversationHistory.Append(root, "c-1", AssistantHistoryTurn.UserRole, "对", At(3));

                var turns = AssistantConversationHistory.Read(root, "c-1");

                Assert.Equal(3, turns.Count);
                Assert.Equal("要做个背包", turns[0].Text);
                Assert.Equal(AssistantHistoryTurn.AssistantRole, turns[1].Role);
                Assert.Equal("对", turns[2].Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>两条会话各记各的，不许串味。</summary>
        [Fact]
        public void HistoryIsPerConversation()
        {
            var root = NewTemporaryDirectory();
            try
            {
                AssistantConversationHistory.Append(root, "c-1", AssistantHistoryTurn.UserRole, "背包", At(1));
                AssistantConversationHistory.Append(root, "c-2", AssistantHistoryTurn.UserRole, "技能树", At(1));

                Assert.Equal("背包", Assert.Single(AssistantConversationHistory.Read(root, "c-1")).Text);
                Assert.Equal("技能树", Assert.Single(AssistantConversationHistory.Read(root, "c-2")).Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>开新话题之后只读得到分隔线之后的内容——上下文真丢了。</summary>
        [Fact]
        public void NewTopicDropsEverythingBefore()
        {
            var root = NewTemporaryDirectory();
            try
            {
                AssistantConversationHistory.Append(root, "c-1", AssistantHistoryTurn.UserRole, "旧话题", At(1));
                AssistantConversationHistory.StartNewTopic(root, "c-1", "按了开新话题", At(2));
                AssistantConversationHistory.Append(root, "c-1", AssistantHistoryTurn.UserRole, "新话题", At(3));

                var turns = AssistantConversationHistory.Read(root, "c-1");

                Assert.Equal("新话题", Assert.Single(turns).Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>丢的是「读得到」，不是「留着」：分隔线之前那几行仍在文件里，事后查得到。</summary>
        [Fact]
        public void NewTopicKeepsLedgerOnDisk()
        {
            var root = NewTemporaryDirectory();
            try
            {
                AssistantConversationHistory.Append(root, "c-1", AssistantHistoryTurn.UserRole, "旧话题", At(1));
                AssistantConversationHistory.StartNewTopic(root, "c-1", "按了开新话题", At(2));

                var text = File.ReadAllText(AssistantConversationHistory.HistoryFilePath(root, "c-1"));

                Assert.Contains("旧话题", text);
                Assert.Contains(AssistantHistoryTurn.BreakRole, text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>轮数上限从尾部往回取：留下的是最近那几轮，不是最早的。</summary>
        [Fact]
        public void HistoryTakesRecentTurnsWhenOverLimit()
        {
            var root = NewTemporaryDirectory();
            try
            {
                for (var index = 1; index <= 6; index++)
                {
                    AssistantConversationHistory.Append(root, "c-1", AssistantHistoryTurn.UserRole, "第" + index + "句", At(index));
                }

                var turns = AssistantConversationHistory.Read(root, "c-1", maxTurns: 2);

                Assert.Equal(2, turns.Count);
                Assert.Equal("第5句", turns[0].Text);
                Assert.Equal("第6句", turns[1].Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>字数上限也从尾部裁；哪怕一轮都放不下，也要留住人刚说的那一句。</summary>
        [Fact]
        public void HistoryAlwaysKeepsTheLastTurnEvenWhenOverCharacterBudget()
        {
            var root = NewTemporaryDirectory();
            try
            {
                AssistantConversationHistory.Append(root, "c-1", AssistantHistoryTurn.UserRole, new string('a', 50), At(1));
                AssistantConversationHistory.Append(root, "c-1", AssistantHistoryTurn.UserRole, new string('b', 50), At(2));

                var turns = AssistantConversationHistory.Read(root, "c-1", maxTurns: 10, maxCharacters: 10);

                Assert.Equal(new string('b', 50), Assert.Single(turns).Text);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>历史文件路径不许被会话标识带出目录：下游给什么标识都只能落在 history 下面。</summary>
        [Theory]
        [InlineData("../../evil")]
        [InlineData("oc_/../x")]
        [InlineData("会话:1")]
        public void HistoryFileNameStaysInsideHistoryDirectory(string conversationIdentifier)
        {
            var root = NewTemporaryDirectory();
            try
            {
                var filePath = Path.GetFullPath(AssistantConversationHistory.HistoryFilePath(root, conversationIdentifier));
                var directory = Path.GetFullPath(AssistantConversationHistory.HistoryDirectory(root));

                Assert.StartsWith(directory, filePath, StringComparison.Ordinal);
                Assert.DoesNotContain("..", Path.GetFileName(filePath));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>历史进得了提示词，且**不进版本哈希**——版本只跟稳定的那几段走。</summary>
        [Fact]
        public void PromptCarriesHistoryWithoutChangingVersion()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var withoutHistory = AssistantServePrompt.Build(root, "feishu", "接着说", "");
                var withHistory = AssistantServePrompt.Build(root, "feishu", "接着说", "用户：要做个背包\n助手：跟传统 RPG 一样吗");

                Assert.DoesNotContain("要做个背包", withoutHistory.PromptText);
                Assert.Contains("要做个背包", withHistory.PromptText);
                Assert.Contains("之前聊过什么", withHistory.PromptText);
                Assert.Equal(withoutHistory.PromptVersion, withHistory.PromptVersion);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>「开新话题」认整句，不认「新话题我想聊背包」——后者是正经一句话，不是命令。</summary>
        [Theory]
        [InlineData("开新话题", true)]
        [InlineData("  新话题 ", true)]
        [InlineData("/reset", true)]
        [InlineData("新话题我想聊背包", false)]
        [InlineData("背包", false)]
        [InlineData("", false)]
        public void NewTopicPhraseIsRecognizedOnlyAsAWholeSentence(string text, bool expected)
        {
            Assert.Equal(expected, AssistantServeTurn.LooksLikeNewTopic(text));
        }

        /// <summary>卡片按钮的点击解析得出动作与携带值。</summary>
        [Fact]
        public void CardActionSignalParsesActionAndValue()
        {
            var signal = @"{
              ""事件"": ""卡片按钮"",
              ""会话"": {
                ""会话标识"": ""c-1"",
                ""发件人标识"": ""u-1"",
                ""消息标识"": ""m-1"",
                ""消息类型"": ""card_action"",
                ""文本"": """",
                ""按钮动作"": ""创建需求"",
                ""按钮携带"": { ""需求id"": ""REQ-0007"", ""动作"": ""创建需求"" }
              },
              ""载荷"": {}
            }";

            Assert.True(AssistantConversationMessage.TryParse(signal, out var message, out _));
            Assert.True(message.IsCardAction);
            Assert.False(message.IsHandleableText);
            Assert.Equal(AssistantCard.CreateAction, message.ActionName);
            Assert.Equal("REQ-0007", message.ReadActionValue("需求id"));
        }

        /// <summary>没有动作名的 card_action 不算可处理的按钮点击——不许拿空动作去猜。</summary>
        [Fact]
        public void CardActionWithoutActionNameIsNotHandleable()
        {
            var message = new AssistantConversationMessage("c", "u", "m", "card_action", "", "", "", new JsonObject());

            Assert.False(message.IsCardAction);
        }

        /// <summary>校验通过时产出的是一张**等人点**的卡：主按钮建需求，另有开新话题；回话里不许说「已经建了」。</summary>
        [Fact]
        public void ValidDraftProducesConfirmCardInsteadOfWriting()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var outcome = AssistantServeTurn.Decide(
                    root,
                    Path.Combine(root, "Pools"),
                    new AssistantConversationMessage("c-1", "u-1", "m-1", "text", "要做个背包", ""),
                    ValidReply(),
                    BuildRequirementSchema(),
                    At(1));

                Assert.True(outcome.DraftReady);
                Assert.NotNull(outcome.Card);

                var actions = outcome.Card.Buttons.Select(button => button.Action).ToList();
                Assert.Contains(AssistantCard.CreateAction, actions);
                Assert.Contains(AssistantCard.NewTopicAction, actions);

                var create = outcome.Card.Buttons.First(button => button.Action == AssistantCard.CreateAction);
                Assert.Equal(outcome.RequirementIdentifier, create.Value["需求id"].GetValue<string>());

                Assert.DoesNotContain("已建", outcome.ReplyText);
                Assert.DoesNotContain("已经建", outcome.ReplyText);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>确认卡上摆的是人关心的字段，工程侧字段（id / 状态 / 来源）一个都不上卡。</summary>
        [Fact]
        public void ConfirmCardShowsPlannerFieldsOnly()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var outcome = AssistantServeTurn.Decide(
                    root,
                    Path.Combine(root, "Pools"),
                    new AssistantConversationMessage("c-1", "u-1", "m-1", "text", "要做个背包", ""),
                    ValidReply(),
                    BuildRequirementSchema(),
                    At(1));

                var names = outcome.Card.Entries.Select(entry => entry.Key).ToList();

                Assert.Contains("标题", names);
                Assert.Contains("目标", names);
                Assert.Contains("玩法", names);
                Assert.DoesNotContain("id", names);
                Assert.DoesNotContain("状态", names);
                Assert.DoesNotContain("来源", names);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>还没聊够的那一轮也发卡：至少带一个「开新话题」，人才有出口。</summary>
        [Fact]
        public void UnfinishedTurnStillOffersNewTopicButton()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var reply = new AssistantServeReply(
                    parsed: true,
                    replyText: "我理解你要一版背包界面的图",
                    wantsRequirement: false,
                    missingItems: new[] { "这些图给哪个界面用？" },
                    draft: null,
                    parseFailureReason: "");

                var outcome = AssistantServeTurn.Decide(
                    root,
                    Path.Combine(root, "Pools"),
                    new AssistantConversationMessage("c-1", "u-1", "m-1", "text", "帮我出张图", ""),
                    reply,
                    BuildRequirementSchema(),
                    At(1));

                Assert.False(outcome.DraftReady);
                Assert.NotNull(outcome.Card);
                Assert.Equal(AssistantCard.NewTopicAction, Assert.Single(outcome.Card.Buttons).Action);

                // 问题进「待确认」，**不再拼成「还缺这些」跟在回话后面**。
                Assert.Contains("这些图给哪个界面用？", outcome.Card.OpenQuestions);
                Assert.DoesNotContain("还缺这些", outcome.ReplyText);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>留底的草稿按 id 读得回来——按钮点下去要建的就是卡上那一份。</summary>
        [Fact]
        public void PendingDraftIsLoadedBackByIdentifier()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var draft = new JsonObject { ["id"] = "REQ-0001", ["标题"] = "背包" };
                AssistantServeTurn.SaveDraft(root, "REQ-0001", draft);

                Assert.True(AssistantServeTurn.TryLoadDraft(root, "REQ-0001", out var loaded, out _));
                Assert.Equal("背包", loaded["标题"].GetValue<string>());
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>草稿不在（比如卡是重装前留下的）要说清楚，不许拿空草稿顶上去建一条空需求。</summary>
        [Fact]
        public void MissingPendingDraftFailsWithReason()
        {
            var root = NewTemporaryDirectory();
            try
            {
                Assert.False(AssistantServeTurn.TryLoadDraft(root, "REQ-0404", out var loaded, out var reason));
                Assert.Null(loaded);
                Assert.Contains("REQ-0404", reason);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>建过一次就记账，第二次点同一张卡时判得出来——卡片会一直挂在聊天记录里。</summary>
        [Fact]
        public void ConfirmedLedgerRemembersWhatWasCreated()
        {
            var root = NewTemporaryDirectory();
            try
            {
                Assert.False(AssistantServeTurn.IsConfirmed(root, "REQ-0001"));

                Assert.True(AssistantServeTurn.RecordConfirmed(root, "REQ-0001", "c-1", "u-1", At(1)));

                Assert.True(AssistantServeTurn.IsConfirmed(root, "REQ-0001"));
                Assert.False(AssistantServeTurn.IsConfirmed(root, "REQ-0002"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>一份能过校验的模型回答，几处测试共用。</summary>
        private static AssistantServeReply ValidReply()
        {
            return new AssistantServeReply(
                parsed: true,
                replyText: "我理解你要一个传统 RPG 那样的背包",
                wantsRequirement: true,
                missingItems: Array.Empty<string>(),
                draft: new JsonObject
                {
                    ["类型"] = "系统",
                    ["标题"] = "背包",
                    ["描述"] = "管理玩家物品",
                    ["验收标准"] = new JsonArray { "能增加物品", "能使用物品" },
                    ["目标"] = "玩家能方便查看自己拥有的物品",
                    ["玩法"] = "格子 + 拖拽，与传统 RPG 一致"
                },
                parseFailureReason: "",
                intentSummary: "做一个传统 RPG 背包");
        }

        /// <summary>造一个固定时刻，序号只用来把几轮排开。</summary>
        private static DateTimeOffset At(int index)
        {
            return new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.FromHours(9)).AddMinutes(index);
        }

        /// <summary>与助手会话测试同源的需求 schema：字段所有权与分类型必填都要真。</summary>
        private static PoolSchema BuildRequirementSchema()
        {
            var fields = new List<PoolSchemaField>
            {
                new("id", "string", true, Array.Empty<string>(), "", 0, "工程", false, true),
                new("类型", "enum", true, new[] { "系统", "修改", "缺陷" }, "", 0, "策划端", false, false),
                new("状态", "enum", true, new[] { "草稿", "已确认", "已完成" }, "", 0, "工程", false, true),
                new("标题", "string", true, Array.Empty<string>(), "", 0, "策划端", false, false),
                new("描述", "string", false, Array.Empty<string>(), "", 0, "策划端", false, false),
                new("验收标准", "数组", true, Array.Empty<string>(), "string", 1, "策划端", false, false),
                new("来源", "对象", true, Array.Empty<string>(), "", 0, "工程", false, true),
                new("关联设计记录", "数组", true, Array.Empty<string>(), "string", 0, "工程", false, true),
                new("依赖", "数组", true, Array.Empty<string>(), "string", 0, "工程", false, true),
                new("锁定", "bool", true, Array.Empty<string>(), "", 0, "工程", false, true),
                new("schema版本", "string", true, Array.Empty<string>(), "", 0, "工程", false, true)
            };

            var requiredByType = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["系统"] = new[] { "目标", "玩法" },
                ["修改"] = new[] { "现状", "期望" },
                ["缺陷"] = new[] { "复现步骤", "期望", "实际" }
            };

            var stateMachine = new PoolStateMachine("草稿", Array.Empty<PoolStateTransition>());
            return new PoolSchema("1.0.0", "需求", "^REQ-\\d{4}$", fields, requiredByType, stateMachine);
        }

        /// <summary>造一个临时目录当仓库根。</summary>
        private static string NewTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "助手上下文测试-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>删临时目录；删不掉就放着，不影响结论。</summary>
        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
            }
        }
    }
}
