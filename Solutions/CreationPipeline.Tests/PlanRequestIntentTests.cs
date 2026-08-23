using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 「要一份模块策划案」这一支的测试。
    ///
    /// 盯两件事：**这一支认得出来**（否则模型只能把它捏成又一条需求），
    /// 以及**它不冒充需求**（要什么写了策划案却没给模块名时，不许当成有效请求）。
    /// </summary>
    public class PlanRequestIntentTests
    {
        private static string Reply(string wanted, JsonObject planRequest)
        {
            var root = new JsonObject
            {
                ["回话"] = "我理解你想要的是给背包出一份策划案。",
                ["我理解你想干的"] = "要背包这个模块的策划正本",
                ["要问的问题"] = new JsonArray(),
                ["要什么"] = wanted,
                ["要不要建需求"] = false
            };

            if (planRequest != null)
            {
                root["策划案请求"] = planRequest;
            }

            return root.ToJsonString();
        }

        /// <summary>「要什么」是策划案且带了模块名时认得出来。</summary>
        [Fact]
        public void RecognisesAPlanRequest()
        {
            Assert.True(AssistantServeReply.TryParse(
                Reply(AssistantServeReply.WantPlan, new JsonObject { ["模块"] = "Inventory" }), out var reply));

            Assert.True(reply.WantsPlan);
            Assert.Equal("Inventory", reply.PlanModule);
            Assert.False(reply.WantsRequirement);
        }

        /// <summary>
        /// 说要策划案却没给模块名时**不算有效请求**。
        ///
        /// 这一支唯一的入参就是模块名——没有它，按钮点下去不知道给谁出，
        /// 而发一张点了会失败的卡比不发更坏。
        /// </summary>
        [Fact]
        public void APlanRequestWithoutAModuleDoesNotCount()
        {
            Assert.True(AssistantServeReply.TryParse(
                Reply(AssistantServeReply.WantPlan, new JsonObject()), out var reply));

            Assert.False(reply.WantsPlan);
            Assert.Equal("", reply.PlanModule);
        }

        /// <summary>没写「策划案请求」时也一样不算。</summary>
        [Fact]
        public void APlanRequestWithoutThePayloadDoesNotCount()
        {
            Assert.True(AssistantServeReply.TryParse(Reply(AssistantServeReply.WantPlan, null), out var reply));

            Assert.False(reply.WantsPlan);
        }

        /// <summary>别的意图不会被误当成策划案——「功能」还是走需求那条路。</summary>
        [Fact]
        public void OtherIntentsAreNotMistakenForAPlanRequest()
        {
            Assert.True(AssistantServeReply.TryParse(
                Reply(AssistantServeReply.WantFeature, new JsonObject { ["模块"] = "Inventory" }), out var reply));

            Assert.False(reply.WantsPlan);
        }

        /// <summary>卡片带着模块名，按钮点下去引擎才知道给谁出。</summary>
        [Fact]
        public void ThePlanCardCarriesTheModuleName()
        {
            var card = AssistantCard.ForPlanRequest("Inventory", "给背包出一份策划案。");

            Assert.Contains(card.Buttons, button =>
                button.Action == AssistantCard.PlanAction
                && button.Value["模块"]?.GetValue<string>() == "Inventory");
        }

        /// <summary>
        /// 输出契约里要写着「策划案」这一档，以及「实现不归你」。
        ///
        /// 契约文本进版本哈希，改了它模型那侧的缓存就换一批——
        /// 这条断言盯的是**这两句话别在某次精简里被顺手删掉**：
        /// 少了前者，人要策划案时模型只能捏一条需求；
        /// 少了后者，人说「做一下 REQ-0003」时它会再建一条讲同一件事的需求。
        /// </summary>
        [Fact]
        public void TheOutputContractDeclaresBothTheNewIntentAndWhatIsNotItsJob()
        {
            Assert.Contains("策划案请求", AssistantServePrompt.OutputContract);
            Assert.Contains("不归你", AssistantServePrompt.OutputContract);
            Assert.Contains("不许把它整理成又一条需求", AssistantServePrompt.OutputContract);

            // 权限边界那两条是硬的：改项目代码、删项目资产会毁掉别人手上的东西，
            // 而这条链上没有人在中间看一眼。
            Assert.Contains("不能改项目代码", AssistantServePrompt.OutputContract);
            Assert.Contains("不能删项目资产", AssistantServePrompt.OutputContract);
        }
    }
}
