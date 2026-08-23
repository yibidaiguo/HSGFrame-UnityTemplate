using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 拆图照不照清单切的判定测试。
    ///
    /// 这一层要盯住的是**别静默走错路**：照清单切与看图猜是两条路，
    /// 走了哪条要在流水里说得出来；有歧义时宁可停下来问，也不许挑一屏赌。
    /// </summary>
    public class InterfaceCutPlannerTests
    {
        private const string Conversation = "oc_test";

        /// <summary>往工作区写一份界面规格。</summary>
        /// <param name="workspace">测试工作区。</param>
        /// <param name="identifier">界面 id。</param>
        /// <param name="title">标题。</param>
        /// <param name="panel">面板名。</param>
        /// <param name="requirementIdentifier">来源需求 id。</param>
        /// <param name="elements">元素数组。</param>
        private static void WriteSpec(
            PoolTestWorkspace workspace,
            string identifier,
            string title,
            string panel,
            string requirementIdentifier,
            JsonArray elements)
        {
            var directory = InterfaceSpec.Directory(workspace.RepositoryRoot);
            Directory.CreateDirectory(directory);

            var spec = new JsonObject
            {
                ["id"] = identifier,
                ["面板"] = panel,
                ["标题"] = title,
                ["来源需求"] = new JsonArray { requirementIdentifier },
                ["画布"] = new JsonObject { ["宽"] = 1280, ["高"] = 720 },
                ["状态"] = "草稿",
                ["元素"] = elements
            };

            File.WriteAllText(Path.Combine(directory, identifier + ".json"), spec.ToJsonString());
        }

        /// <summary>一个元素。</summary>
        /// <param name="identifier">元素 id。</param>
        /// <param name="elementType">元素类型。</param>
        private static JsonObject Element(string identifier, string elementType)
        {
            return new JsonObject
            {
                ["id"] = identifier,
                ["名称"] = identifier,
                ["类型"] = elementType,
                ["布局"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["宽"] = 100, ["高"] = 40 },
                ["复用"] = "新建",
                ["交互"] = "点一下",
                ["成功"] = "成了",
                ["失败"] = "砸了",
                ["状态"] = "常态",
                ["边界"] = "无",
                ["验收"] = "能点"
            };
        }

        /// <summary>会话没留需求底时退回看图猜，并在流水里说清走的是哪条路。</summary>
        [Fact]
        public void FallsBackToGuessingWhenTheConversationHasNoRequirement()
        {
            using var workspace = new PoolTestWorkspace();

            var plan = InterfaceCutPlanner.Resolve(workspace.RepositoryRoot, Conversation, "");

            Assert.Null(plan.Spec);
            Assert.Empty(plan.Requests);
            Assert.Equal("", plan.Blocker);
            Assert.Contains(plan.Notes, note => note.Contains("按看图猜元素拆"));
        }

        /// <summary>需求在、但还没出过功能图时同样退回看图猜。</summary>
        [Fact]
        public void FallsBackToGuessingWhenThereIsNoInterfaceSpecYet()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyUiElementTemplateBaseline();
            AssistantServeTurn.RememberConversationRequirement(workspace.RepositoryRoot, Conversation, "REQ-0042");

            var plan = InterfaceCutPlanner.Resolve(workspace.RepositoryRoot, Conversation, "");

            Assert.Null(plan.Spec);
            Assert.Empty(plan.Requests);
            Assert.Equal("", plan.Blocker);
            Assert.Contains(plan.Notes, note => note.Contains("REQ-0042 还没出过功能图"));
        }

        /// <summary>一份规格时照它的清单切，且不出图的那几类不进清单。</summary>
        [Fact]
        public void UsesTheManifestAndDropsElementsThatNeedNoImage()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyUiElementTemplateBaseline();
            AssistantServeTurn.RememberConversationRequirement(workspace.RepositoryRoot, Conversation, "REQ-0042");
            WriteSpec(workspace, "UI-0001", "背包主界面", "Inventory", "REQ-0042", new JsonArray
            {
                Element("领取按钮", "Button"),
                Element("格子容器", "Container"),
                Element("标题文案", "Label")
            });

            var plan = InterfaceCutPlanner.Resolve(workspace.RepositoryRoot, Conversation, "");

            Assert.NotNull(plan.Spec);
            Assert.Equal("UI-0001", plan.Spec.Identifier);
            Assert.Equal("", plan.Blocker);
            Assert.Single(plan.Requests);
            Assert.Equal("领取按钮", plan.Requests[0].Identifier);
        }

        /// <summary>一屏全是不出图的那几类时不切，并说清为什么——不是「失败」，是「不用拆」。</summary>
        [Fact]
        public void SaysNothingToCutWhenNoElementNeedsAnImage()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyUiElementTemplateBaseline();
            AssistantServeTurn.RememberConversationRequirement(workspace.RepositoryRoot, Conversation, "REQ-0042");
            WriteSpec(workspace, "UI-0001", "背包主界面", "Inventory", "REQ-0042", new JsonArray
            {
                Element("格子容器", "Container"),
                Element("标题文案", "Label")
            });

            var plan = InterfaceCutPlanner.Resolve(workspace.RepositoryRoot, Conversation, "");

            Assert.Empty(plan.Requests);
            Assert.Contains("这张图不用拆", plan.Blocker);
        }

        /// <summary>一条需求动了两屏时停下来问，不挑一屏赌——猜错要花一整趟钱。</summary>
        [Fact]
        public void AsksWhichScreenWhenTheRequirementTouchesMoreThanOne()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyUiElementTemplateBaseline();
            AssistantServeTurn.RememberConversationRequirement(workspace.RepositoryRoot, Conversation, "REQ-0042");
            WriteSpec(workspace, "UI-0001", "背包主界面", "Inventory", "REQ-0042",
                new JsonArray { Element("领取按钮", "Button") });
            WriteSpec(workspace, "UI-0002", "商店主界面", "Shop", "REQ-0042",
                new JsonArray { Element("购买按钮", "Button") });

            var plan = InterfaceCutPlanner.Resolve(workspace.RepositoryRoot, Conversation, "");

            Assert.Null(plan.Spec);
            Assert.Empty(plan.Requests);
            Assert.Contains("UI-0001「背包主界面」", plan.Blocker);
            Assert.Contains("UI-0002「商店主界面」", plan.Blocker);
        }

        /// <summary>问出来的问题要答得上：人回一句标题里的词就认得出是哪一屏。</summary>
        [Fact]
        public void ResolvesTheAmbiguityFromWhatThePersonSaid()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyUiElementTemplateBaseline();
            AssistantServeTurn.RememberConversationRequirement(workspace.RepositoryRoot, Conversation, "REQ-0042");
            WriteSpec(workspace, "UI-0001", "背包主界面", "Inventory", "REQ-0042",
                new JsonArray { Element("领取按钮", "Button") });
            WriteSpec(workspace, "UI-0002", "商店主界面", "Shop", "REQ-0042",
                new JsonArray { Element("购买按钮", "Button") });

            var byTitle = InterfaceCutPlanner.Resolve(workspace.RepositoryRoot, Conversation, "这张是商店那屏");
            Assert.Equal("UI-0002", byTitle.Spec.Identifier);

            var byIdentifier = InterfaceCutPlanner.Resolve(workspace.RepositoryRoot, Conversation, "照 UI-0001 切");
            Assert.Equal("UI-0001", byIdentifier.Spec.Identifier);
        }

        /// <summary>两屏都被提到时当作没认出：挑哪一屏都是猜。</summary>
        [Fact]
        public void StillAsksWhenTheHintMentionsBothScreens()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyUiElementTemplateBaseline();
            AssistantServeTurn.RememberConversationRequirement(workspace.RepositoryRoot, Conversation, "REQ-0042");
            WriteSpec(workspace, "UI-0001", "背包主界面", "Inventory", "REQ-0042",
                new JsonArray { Element("领取按钮", "Button") });
            WriteSpec(workspace, "UI-0002", "商店主界面", "Shop", "REQ-0042",
                new JsonArray { Element("购买按钮", "Button") });

            var plan = InterfaceCutPlanner.Resolve(
                workspace.RepositoryRoot, Conversation, "背包主界面和商店主界面都要");

            Assert.Null(plan.Spec);
            Assert.NotEqual("", plan.Blocker);
        }

        /// <summary>会话需求底写得进也读得回；没写过时读回空串。</summary>
        [Fact]
        public void RemembersWhichRequirementTheConversationIsWorkingOn()
        {
            using var workspace = new PoolTestWorkspace();

            Assert.Equal("", AssistantServeTurn.ReadConversationRequirement(workspace.RepositoryRoot, Conversation));
            Assert.True(AssistantServeTurn.RememberConversationRequirement(
                workspace.RepositoryRoot, Conversation, "REQ-0042"));
            Assert.Equal(
                "REQ-0042", AssistantServeTurn.ReadConversationRequirement(workspace.RepositoryRoot, Conversation));
        }

        /// <summary>需求 id 为空时不写留底：写一个空串下去，下次读回来会当成「有底」。</summary>
        [Fact]
        public void DoesNotRememberAnEmptyRequirement()
        {
            using var workspace = new PoolTestWorkspace();

            Assert.False(AssistantServeTurn.RememberConversationRequirement(
                workspace.RepositoryRoot, Conversation, ""));
            Assert.Equal("", AssistantServeTurn.ReadConversationRequirement(workspace.RepositoryRoot, Conversation));
        }
    }
}
