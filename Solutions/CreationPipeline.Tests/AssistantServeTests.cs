using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 助手 B 形态（常驻会话）的纯逻辑测试：会话消息解析、模型回答解析、字段所有权闸门、
    /// 一轮处置的判定。全部脱离网络——真调用的证据在批次日志里，这里管的是判定不会说谎。
    ///
    /// 重点盯三件事：
    /// 1. **解析失败不许冒充正常结论**（决策 42）：读不懂就说读不懂，不能变成「没什么要建的」。
    /// 2. **模型没有分配 id、决定状态的权力**：工程侧字段一律挡掉并报出来。
    /// 3. **校验不过就不写下游**，且回话里要说清为什么。
    /// </summary>
    public class AssistantServeTests
    {
        /// <summary>正常的会话信号能解析出会话标识、发件人与正文。</summary>
        [Fact]
        public void ConversationSignalParsesNormalizedBlock()
        {
            var signal = @"{
              ""来源"": ""某个下游"",
              ""事件"": ""收到消息"",
              ""收到时间"": ""2026-08-21T02:00:00Z"",
              ""会话"": {
                ""会话标识"": ""c-1"",
                ""发件人标识"": ""u-1"",
                ""消息标识"": ""m-1"",
                ""消息类型"": ""text"",
                ""文本"": ""想加个排序""
              },
              ""载荷"": {}
            }";

            Assert.True(AssistantConversationMessage.TryParse(signal, out var message, out _));
            Assert.Equal("c-1", message.ConversationIdentifier);
            Assert.Equal("u-1", message.SenderIdentifier);
            Assert.Equal("m-1", message.MessageIdentifier);
            Assert.Equal("想加个排序", message.Text);
            Assert.True(message.IsHandleable);
        }

        /// <summary>
        /// 带图的富文本消息：正文与图片 key 都要读出来，且算「能处理」。
        /// 从前判据是「类型是不是 text」，人发一张参考图配一句话（post）当场被回一句
        /// 「我只认文字消息」——而他明明已经把要说的说完了。
        /// </summary>
        [Fact]
        public void MessageWithImageAttachmentIsHandleable()
        {
            var signal = @"{
              ""会话"": {
                ""会话标识"": ""c-1"",
                ""消息标识"": ""m-1"",
                ""消息类型"": ""post"",
                ""文本"": ""再出一张 可以参考"",
                ""附件"": [{ ""类型"": ""image"", ""key"": ""img_v3_abc"", ""文件名"": """" }]
              }
            }";

            Assert.True(AssistantConversationMessage.TryParse(signal, out var message, out _));
            Assert.True(message.IsHandleable);
            var attachment = Assert.Single(message.Attachments);
            Assert.True(attachment.IsImage);
            Assert.Equal("img_v3_abc", attachment.Key);
            Assert.Equal(".png", attachment.FileExtension);
        }

        /// <summary>
        /// 一句话都没有、只甩了个文件过来，照样算能处理——那也是他在说话。
        /// 文件的扩展名跟着原名走，取不到才退回 .bin。
        /// </summary>
        [Fact]
        public void MessageWithOnlyFileAttachmentIsHandleable()
        {
            var signal = @"{
              ""会话"": {
                ""会话标识"": ""c-1"",
                ""消息标识"": ""m-1"",
                ""消息类型"": ""file"",
                ""文本"": """",
                ""附件"": [{ ""类型"": ""file"", ""key"": ""file_x"", ""文件名"": ""参考.psd"" }]
              }
            }";

            Assert.True(AssistantConversationMessage.TryParse(signal, out var message, out _));
            Assert.True(message.IsHandleable);
            Assert.False(message.Attachments[0].IsImage);
            Assert.Equal(".psd", message.Attachments[0].FileExtension);
        }

        /// <summary>缺 key 的附件跳过，但整条消息照样读得出来——少一张图不该把这一轮判死。</summary>
        [Fact]
        public void AttachmentWithoutKeyIsSkippedWithoutFailingTheMessage()
        {
            var signal = @"{
              ""会话"": {
                ""会话标识"": ""c-1"",
                ""文本"": ""看这个"",
                ""附件"": [{ ""类型"": ""image"", ""key"": """" }, { ""类型"": ""image"", ""key"": ""img_ok"" }]
              }
            }";

            Assert.True(AssistantConversationMessage.TryParse(signal, out var message, out _));
            Assert.Equal("img_ok", Assert.Single(message.Attachments).Key);
        }

        /// <summary>没有「会话」块的信号解析失败，且原因要指出归一该由下游旁路做。</summary>
        [Fact]
        public void ConversationSignalWithoutNormalizedBlockFails()
        {
            var signal = @"{""事件"": ""收到消息"", ""载荷"": {""event"": {}}}";

            Assert.False(AssistantConversationMessage.TryParse(signal, out var message, out var reason));
            Assert.Null(message);
            Assert.Contains("会话", reason);
        }

        /// <summary>会话标识为空要判失败——回话没有去处，比编一个标识强。</summary>
        [Fact]
        public void ConversationSignalWithoutIdentifierFails()
        {
            var signal = @"{""会话"": {""会话标识"": """", ""文本"": ""喂""}}";

            Assert.False(AssistantConversationMessage.TryParse(signal, out _, out var reason));
            Assert.Contains("会话标识", reason);
        }

        /// <summary>
        /// 真正处理不了的是**什么都没有**：正文空白、附件也空（表情包、语音落在这儿）。
        ///
        /// 「类型不是 text」不再是判据——类型空但有正文的那一条现在算能处理：
        /// 归一那一步取不到类型是常有的事，凭它把一句实打实的话扔掉没道理。
        /// </summary>
        [Theory]
        [InlineData("sticker", "")]
        [InlineData("text", "   ")]
        [InlineData("audio", "")]
        public void MessagesWithNeitherTextNorAttachmentAreNotHandleable(string kind, string text)
        {
            var message = new AssistantConversationMessage("c", "u", "m", kind, text, "");

            Assert.False(message.IsHandleable);
        }

        /// <summary>类型取不到但有正文，照样处理——凭一个空类型把话扔掉没道理。</summary>
        [Fact]
        public void MessageWithUnknownKindButRealTextIsHandleable()
        {
            var message = new AssistantConversationMessage("c", "u", "m", "", "有字但类型空", "");

            Assert.True(message.IsHandleable);
        }

        /// <summary>模型回答包在代码块里、前后有闲话，照样能抠出那份 JSON。</summary>
        [Fact]
        public void ReplyParsesJsonWrappedInCodeFence()
        {
            var text = "好的，我理解了。\n```json\n{\"回话\":\"我懂了\",\"要不要建需求\":false,\"还缺什么\":[\"验收标准\"]}\n```\n就这样。";

            Assert.True(AssistantServeReply.TryParse(text, out var reply));
            Assert.True(reply.Parsed);
            Assert.Equal("我懂了", reply.ReplyText);
            Assert.False(reply.WantsRequirement);
            Assert.Equal(new[] { "验收标准" }, reply.MissingItems);
        }

        /// <summary>回答里没有 JSON、没有「回话」、整个空，都算解析失败——不许当成「没什么要建的」。</summary>
        [Theory]
        [InlineData("")]
        [InlineData("我觉得这个需求挺好的，不过还得再想想。")]
        [InlineData("{\"要不要建需求\": true}")]
        public void UnreadableReplyIsNotParsedAndNeverBuilds(string modelText)
        {
            Assert.False(AssistantServeReply.TryParse(modelText, out var reply));
            Assert.False(reply.Parsed);
            Assert.False(reply.WantsRequirement);
            Assert.Null(reply.Draft);
            Assert.NotEqual("", reply.ParseFailureReason);
            Assert.Contains("没能读懂", reply.ReplyText);
        }

        /// <summary>说要建需求却没给草稿，是自相矛盾：按不建处理，并在回话里说清楚。</summary>
        [Fact]
        public void WantsRequirementWithoutDraftDowngradesAndSaysSo()
        {
            var text = "{\"回话\":\"建好了\",\"要不要建需求\":true}";

            Assert.True(AssistantServeReply.TryParse(text, out var reply));
            Assert.False(reply.WantsRequirement);
            Assert.Null(reply.Draft);
            Assert.Contains("没给草稿", reply.ReplyText);
        }

        /// <summary>所有权闸门只留白名单里的字段，挡掉的要报出来而不是静默丢。</summary>
        [Fact]
        public void OwnershipGateReportsBlockedFields()
        {
            var source = new JsonObject
            {
                ["标题"] = "背包排序",
                ["状态"] = "已完成",
                ["锁定"] = true
            };

            var result = RequirementFieldOwnership.KeepOnly(source, new[] { "标题" });

            Assert.True(result.Kept.ContainsKey("标题"));
            Assert.False(result.Kept.ContainsKey("状态"));
            Assert.Equal(new[] { "状态", "锁定" }, result.BlockedFields);
        }

        /// <summary>专项入站只放行认领与来源（决策 33），别的一字不动。</summary>
        [Fact]
        public void EpicInboundOnlyAllowsClaimAndSource()
        {
            var record = new JsonObject
            {
                ["id"] = "EPIC-0001",
                ["认领"] = "甲",
                ["来源"] = "下游",
                ["名称"] = "被下游改了的名字"
            };

            var result = RequirementFieldOwnership.FilterInboundEpicClaim(record, "id");

            Assert.Equal(new[] { "名称" }, result.BlockedFields);
        }

        /// <summary>需求入站放行策划端字段与分类型必填字段，挡住工程侧的状态机字段。</summary>
        [Fact]
        public void RequirementInboundBlocksEngineOwnedFields()
        {
            var schema = BuildRequirementSchema();
            var record = new JsonObject
            {
                ["id"] = "REQ-0001",
                ["标题"] = "策划改的标题",
                ["目标"] = "策划写的目标",
                ["状态"] = "已完成",
                ["锁定"] = true
            };

            var result = RequirementFieldOwnership.FilterInboundRequirement(record, schema, "id");

            Assert.True(result.Kept.ContainsKey("标题"));
            Assert.True(result.Kept.ContainsKey("目标"));
            Assert.Equal(new[] { "状态", "锁定" }, result.BlockedFields);
        }

        /// <summary>提示词版本由内容算出：知识变了版本必须跟着变，否则决策 90 的缓存键就在说谎。</summary>
        [Fact]
        public void PromptVersionFollowsKnowledgeContent()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var packageDirectory = ProvisionPaths.AssistantPackageDirectory(root, "某下游");
                var knowledgeDirectory = ProvisionPaths.AssistantKnowledgeDirectory(root, "某下游");
                Directory.CreateDirectory(knowledgeDirectory);
                File.WriteAllText(Path.Combine(packageDirectory, "system-prompt.md"), "你是助手");
                File.WriteAllText(Path.Combine(knowledgeDirectory, "术语.md"), "第一版知识");

                var first = AssistantServePrompt.Build(root, "某下游", "随便说一句");

                File.WriteAllText(Path.Combine(knowledgeDirectory, "术语.md"), "第二版知识");
                var second = AssistantServePrompt.Build(root, "某下游", "随便说一句");

                Assert.NotEqual(first.PromptVersion, second.PromptVersion);
                Assert.Equal(1, first.KnowledgeFileCount);
                Assert.Equal("", first.DegradedReason);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>换一句用户输入不该换版本——版本说的是提示词，不是这一轮的内容。</summary>
        [Fact]
        public void PromptVersionIgnoresUserText()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var packageDirectory = ProvisionPaths.AssistantPackageDirectory(root, "某下游");
                Directory.CreateDirectory(packageDirectory);
                File.WriteAllText(Path.Combine(packageDirectory, "system-prompt.md"), "你是助手");

                var first = AssistantServePrompt.Build(root, "某下游", "第一句");
                var second = AssistantServePrompt.Build(root, "某下游", "完全不同的第二句");

                Assert.Equal(first.PromptVersion, second.PromptVersion);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>知识读不到时照样能跑，但必须把降级说出来——不许静默降级。</summary>
        [Fact]
        public void MissingKnowledgeIsReportedNotSwallowed()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var prompt = AssistantServePrompt.Build(root, "某下游", "你好");

                Assert.NotEqual("", prompt.DegradedReason);
                Assert.Equal(0, prompt.KnowledgeFileCount);
                Assert.Contains("bridge.provision", prompt.DegradedReason);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>一轮处置：模型给了合格草稿 → 补齐工程字段、校验通过、摆成等人点的确认卡。</summary>
        [Fact]
        public void TurnCompletesDraftAndOffersConfirmCard()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var schema = BuildRequirementSchema();
                var message = new AssistantConversationMessage("c-1", "u-1", "m-1", "text", "加个排序", "");
                var reply = new AssistantServeReply(
                    parsed: true,
                    replyText: "我懂了",
                    wantsRequirement: true,
                    missingItems: Array.Empty<string>(),
                    draft: new JsonObject
                    {
                        ["类型"] = "系统",
                        ["标题"] = "背包一键排序",
                        ["验收标准"] = new JsonArray { "点排序后顺序正确" },
                        ["目标"] = "少拖拽",
                        ["玩法"] = "点一下按品质排",
                        ["状态"] = "已完成"
                    },
                    parseFailureReason: "");

                var outcome = AssistantServeTurn.Decide(root, Path.Combine(root, "Pools"), message, reply, schema, DateTimeOffset.Parse("2026-08-21T10:00:00+09:00"));

                Assert.True(outcome.DraftReady);
                Assert.Empty(outcome.Findings);
                // 草稿拿到的是内容哈希，不是编号——号在落池子那一刻才发。
                Assert.StartsWith("REQ-草稿-", outcome.RequirementIdentifier);
                Assert.Equal(AssistantServeTurn.ValidationPlaceholderIdentifier, outcome.Draft["id"].GetValue<string>());
                Assert.Contains("状态", outcome.BlockedFields);
                Assert.Equal("草稿", outcome.Draft["状态"].GetValue<string>());
                Assert.False(outcome.Draft["锁定"].GetValue<bool>());
                Assert.Equal("助手会话", outcome.Draft["来源"]["渠道"].GetValue<string>());
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>校验不过就不摆确认卡，且回话里要把原因说给提需求的人听。</summary>
        [Fact]
        public void TurnRefusesToOfferCardWhenValidationFails()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var schema = BuildRequirementSchema();
                var message = new AssistantConversationMessage("c-1", "u-1", "m-1", "text", "加个排序", "");
                var reply = new AssistantServeReply(
                    parsed: true,
                    replyText: "我懂了",
                    wantsRequirement: true,
                    missingItems: Array.Empty<string>(),
                    draft: new JsonObject
                    {
                        ["类型"] = "系统",
                        ["标题"] = "背包一键排序"
                    },
                    parseFailureReason: "");

                var outcome = AssistantServeTurn.Decide(root, Path.Combine(root, "Pools"), message, reply, schema, DateTimeOffset.Parse("2026-08-21T10:00:00+09:00"));

                Assert.False(outcome.DraftReady);
                Assert.NotEmpty(outcome.Findings);
                Assert.Contains("立不住", outcome.ReplyText);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>模型读不懂时这一轮什么都不建，回话如实说。</summary>
        [Fact]
        public void TurnBuildsNothingWhenReplyUnparsed()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var outcome = AssistantServeTurn.Decide(
                    root,
                    Path.Combine(root, "Pools"),
                    new AssistantConversationMessage("c", "u", "m", "text", "话", ""),
                    AssistantServeReply.NotParsed("回答不是 JSON"),
                    BuildRequirementSchema(),
                    DateTimeOffset.Parse("2026-08-21T10:00:00+09:00"));

                Assert.False(outcome.DraftReady);
                Assert.Equal("", outcome.RequirementIdentifier);
                Assert.Contains("没能读懂", outcome.ReplyText);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>
        /// **草稿不占号**：整理十份草稿，池子里的下一个号还是 REQ-0001。
        ///
        /// 从前草稿也发号，于是聊十轮发十个号、池子里一条都没有——
        /// 人看到 REQ-0002 会以为前面还有个 REQ-0001，去池子里翻却翻不着。
        /// </summary>
        [Fact]
        public void DraftsDoNotConsumeRequirementNumbers()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var poolRoot = Path.Combine(root, "Pools");

                for (var index = 0; index < 10; index++)
                {
                    var draft = new JsonObject { ["标题"] = "第 " + index + " 条" };
                    AssistantServeTurn.SaveDraft(root, AssistantServeTurn.DraftKey(draft), draft);
                }

                Assert.Equal("REQ-0001", AssistantServeTurn.AllocatePoolIdentifier(poolRoot));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>同一份草稿聊两遍是同一个 key——哈希顺带去了重，不会多出一份留底。</summary>
        [Fact]
        public void SameDraftGetsSameKey()
        {
            var draft = new JsonObject { ["标题"] = "背包整理" };
            var again = new JsonObject { ["标题"] = "背包整理" };

            Assert.Equal(AssistantServeTurn.DraftKey(draft), AssistantServeTurn.DraftKey(again));
        }

        /// <summary>内容不一样就是两份草稿，各自一个 key。</summary>
        [Fact]
        public void DifferentDraftsGetDifferentKeys()
        {
            var one = new JsonObject { ["标题"] = "背包整理" };
            var other = new JsonObject { ["标题"] = "商店" };

            Assert.NotEqual(AssistantServeTurn.DraftKey(one), AssistantServeTurn.DraftKey(other));
        }

        /// <summary>草稿 key 不长得像编号——摆到人眼前时不会被当成 REQ 号。</summary>
        [Fact]
        public void DraftKeyIsNotShapedLikeARequirementNumber()
        {
            var key = AssistantServeTurn.DraftKey(new JsonObject { ["标题"] = "背包整理" });

            Assert.StartsWith("REQ-草稿-", key);
            Assert.DoesNotMatch(@"^REQ-\d{4}$", key);
        }

        /// <summary>唤醒信号能被自己投出来，且落的是唤醒目录、带得上明细。</summary>
        [Fact]
        public void WakeSignalCanBeEmittedByEngineItself()
        {
            var root = NewTemporaryDirectory();
            try
            {
                var path = WakeSignalSource.Emit(
                    root,
                    "助手产出草稿",
                    new JsonObject { ["需求id"] = "REQ-0001" },
                    DateTimeOffset.Parse("2026-08-21T10:00:00+00:00"));

                Assert.NotEqual("", path);
                Assert.StartsWith(WakeSignalSource.SignalDirectory(root), path);

                var poll = WakeSignalSource.Poll(root);
                Assert.True(poll.HasSignal);

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                Assert.Equal("助手产出草稿", document.RootElement.GetProperty("事件").GetString());
                Assert.Equal("REQ-0001", document.RootElement.GetProperty("载荷").GetProperty("需求id").GetString());
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>会话目录与唤醒目录是两条独立的队列，互相取不到对方的信号（决策 95）。</summary>
        [Fact]
        public void ConversationQueueIsSeparateFromWakeQueue()
        {
            var root = NewTemporaryDirectory();
            try
            {
                Directory.CreateDirectory(ConversationSignalSource.SignalDirectory(root));
                File.WriteAllText(Path.Combine(ConversationSignalSource.SignalDirectory(root), "a.json"), "{}");

                Assert.True(ConversationSignalSource.Poll(root).HasSignal);
                Assert.False(WakeSignalSource.Poll(root).HasSignal);

                var archived = ConversationSignalSource.Consume(root, Path.Combine(ConversationSignalSource.SignalDirectory(root), "a.json"));
                Assert.NotEqual("", archived);
                Assert.False(ConversationSignalSource.Poll(root).HasSignal);
                Assert.True(File.Exists(archived));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>造一份需求 schema：字段、所有权、分类型必填与状态机都要有，够校验器用。</summary>
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

        /// <summary>开一个临时目录。</summary>
        private static string NewTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "助手会话测试-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>删临时目录，删不掉不报错。</summary>
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

        /// <summary>
        /// 记下最新那张带按钮的卡，下一轮好把它撤掉。
        /// 飞书的卡片点完不会消失、翻上去还能点——第三轮那张「一键建需求」亮到第十轮，
        /// 手一滑建出来的是早就聊废了的草稿。
        /// </summary>
        [Fact]
        public void LiveCardIsRememberedPerConversation()
        {
            var root = NewTemporaryDirectory();
            try
            {
                Assert.Equal("", LiveCardRegistry.Read(root, "c-1"));

                Assert.True(LiveCardRegistry.Remember(root, "c-1", "om_first"));
                Assert.Equal("om_first", LiveCardRegistry.Read(root, "c-1"));

                // 后一张顶掉前一张：只有最新那张的按钮算数。
                Assert.True(LiveCardRegistry.Remember(root, "c-1", "om_second"));
                Assert.Equal("om_second", LiveCardRegistry.Read(root, "c-1"));

                // 会话之间互不干扰。
                Assert.Equal("", LiveCardRegistry.Read(root, "c-2"));

                LiveCardRegistry.Forget(root, "c-1");
                Assert.Equal("", LiveCardRegistry.Read(root, "c-1"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>会话标识或消息标识为空时不记——记了也撤不动，还多一次白跑的往返。</summary>
        [Theory]
        [InlineData("", "om_1")]
        [InlineData("c-1", "")]
        public void EmptyIdentifiersAreNotRemembered(string conversation, string messageIdentifier)
        {
            var root = NewTemporaryDirectory();
            try
            {
                Assert.False(LiveCardRegistry.Remember(root, conversation, messageIdentifier));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        /// <summary>撤过的替身卡一个按钮都没有——留一个就等于没撤。</summary>
        [Fact]
        public void RetiredCardHasNoButtons()
        {
            var card = AssistantCard.ForRetired();

            Assert.Empty(card.Buttons);
            Assert.NotEqual("", card.BodyText);
        }
    }
}
