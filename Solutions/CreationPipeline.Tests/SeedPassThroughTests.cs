using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Bridges.Comfyui;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 种子穿透测试：给了种子 → 载荷里带；没给 → 载荷里是桥产的非空值；
    /// 同一个种子传两次 → 产出的载荷逐字节相同（重生成的前提，决策 26）。
    /// 全走 Program.BuildGenerateWorkflow 这条纯函数路径——它和 RunGenerate 共用同一段代码，
    /// 测的就是真跑的载荷，不碰网络、不碰磁盘、不需要临时目录。
    /// </summary>
    public class SeedPassThroughTests
    {
        /// <summary>给了种子 → 载荷里 KSampler 的 seed 就是它，且 out 的种子文本原样。</summary>
        [Fact]
        public void GivenSeed_PutsItIntoPayload()
        {
            var (workflow, recipe, assetRequest) = BuildFixture();

            var payload = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "12345", out var seedText, out var reason);

            Assert.NotNull(payload);
            Assert.Equal("", reason);
            Assert.Equal("12345", seedText);
            Assert.Equal(12345UL, payload["7"]["inputs"]["seed"].GetValue<ulong>());
        }

        /// <summary>没给种子（空串）→ 载荷里是桥产的非空种子，且真落进了 KSampler。</summary>
        [Fact]
        public void WithoutSeed_ProducesNonEmptySeed()
        {
            var (workflow, recipe, assetRequest) = BuildFixture();

            var payload = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "", out var seedText, out var reason);

            Assert.NotNull(payload);
            Assert.Equal("", reason);
            Assert.False(string.IsNullOrWhiteSpace(seedText), "没给种子时桥必须自己产一个非空种子");
            Assert.True(ulong.TryParse(payload["7"]["inputs"]["seed"].GetValue<ulong>().ToString(), out _), "载荷里 KSampler 的 seed 必须是数字");
        }

        /// <summary>同一个种子传两次 → 产出的载荷逐字节相同。这是「变体可由边车重生成」的前提。</summary>
        [Fact]
        public void SameSeedTwice_YieldsByteIdenticalPayload()
        {
            var (workflow, recipe, assetRequest) = BuildFixture();

            var first = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "987654321", out _, out _);
            var second = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "987654321", out _, out _);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first.ToJsonString(), second.ToJsonString());
        }

        /// <summary>给了种子时不许偷偷加偏移：种子原样进载荷，不做「种子 + 变体序号」之类的算术。</summary>
        [Fact]
        public void GivenSeed_NoOffsetIsAdded()
        {
            var (workflow, recipe, assetRequest) = BuildFixture();

            var payload = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "42", out var seedText, out _);

            Assert.NotNull(payload);
            Assert.Equal("42", seedText);
            Assert.Equal(42UL, payload["7"]["inputs"]["seed"].GetValue<ulong>());
        }

        /// <summary>测试夹具：一个最小的含 KSampler 的 workflow + 一条映射的配方 + 一个资产请求。</summary>
        private static (JsonObject Workflow, RecipeDefinition Recipe, JsonElement AssetRequest) BuildFixture()
        {
            var workflow = JsonNode.Parse(
                "{\"1\":{\"类型\":\"CheckpointLoaderSimple\",\"参数\":{\"ckpt_name\":\"sd_xl_base_1.0.safetensors\"}},"
                + "\"2\":{\"类型\":\"CLIPTextEncode\",\"参数\":{\"text\":\"\"}},"
                + "\"7\":{\"类型\":\"KSampler\",\"参数\":{\"seed\":0,\"steps\":30,\"cfg\":7.0}}}") as JsonObject;

            var recipe = new RecipeDefinition(
                "测试配方",
                "图标",
                "1.0.0",
                new[] { "1", "2", "7" },
                new[] { new RecipeMappingEntry("描述", "2", "text") },
                Array.Empty<RecipeAnchorSlot>(),
                Array.Empty<string>());

            var assetRequest = JsonDocument.Parse("{\"id\":\"a\",\"描述\":\"金币袋\",\"变体数\":3}").RootElement;
            return (workflow, recipe, assetRequest);
        }
    }
}
