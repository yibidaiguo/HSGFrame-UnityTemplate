using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Bridges.Comfyui;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 锚点槽穿透测试（图生图那条路的纯函数部分）。
    ///
    /// 要点全都指向同一件事：**接口在、语义不在，是最坏的一种缺**。
    /// - 给了锚点值而配方没有那个槽 → **报错**，不静默忽略；
    /// - 给了值就必须真的落进 workflow 的对应节点参数上；
    /// - 锚点值不参与种子逻辑，也不影响没给锚点时的产出（老路径一个字不变）。
    ///
    /// 走的是 Program.BuildGenerateWorkflow 这条纯函数路径——与 RunGenerate 同一段代码，
    /// 不碰网络也不碰磁盘。真出图那部分的证据在批次日志里。
    /// </summary>
    public class AnchorSlotTests
    {
        /// <summary>给了「参考图」，配方也有那个槽 → 值落到槽指的节点参数上。</summary>
        [Fact]
        public void AnchorValueLandsOnTheSlotNode()
        {
            var (workflow, recipe, assetRequest) = BuildFixture(withAnchorSlot: true);
            var anchors = new Dictionary<string, string>(StringComparer.Ordinal) { ["参考图"] = "Suzanne_BaseColor.png" };

            var payload = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "123", anchors, out _, out var reason);

            Assert.NotNull(payload);
            Assert.Equal("", reason);
            Assert.Equal("Suzanne_BaseColor.png", payload["5"]["inputs"]["image"].GetValue<string>());
        }

        /// <summary>配方没有那个槽却给了值 → 报错，绝不静默忽略（那正是孤立锚点槽骗人的方式）。</summary>
        [Fact]
        public void AnchorValueWithoutSlotIsAnError()
        {
            var (workflow, recipe, assetRequest) = BuildFixture(withAnchorSlot: false);
            var anchors = new Dictionary<string, string>(StringComparer.Ordinal) { ["参考图"] = "a.png" };

            var payload = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "123", anchors, out _, out var reason);

            Assert.Null(payload);
            Assert.Contains("没有名叫「参考图」的锚点槽", reason);
        }

        /// <summary>不给锚点值时，产出与老路径逐字节相同——加锚点不许改变原来的行为。</summary>
        [Fact]
        public void NoAnchorValuesMatchesTheOldPath()
        {
            var (workflow, recipe, assetRequest) = BuildFixture(withAnchorSlot: true);

            var withoutAnchors = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "777", out _, out _);
            var withNullAnchors = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "777", null, out _, out _);

            Assert.NotNull(withoutAnchors);
            Assert.NotNull(withNullAnchors);
            Assert.Equal(withoutAnchors.ToJsonString(), withNullAnchors.ToJsonString());
        }

        /// <summary>同一个锚点值传两次，产出的载荷逐字节相同（重生成的前提，决策 26）。</summary>
        [Fact]
        public void SameAnchorTwiceProducesIdenticalPayload()
        {
            var (workflow, recipe, assetRequest) = BuildFixture(withAnchorSlot: true);
            var anchors = new Dictionary<string, string>(StringComparer.Ordinal) { ["参考图"] = "x.png" };

            var first = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "555", anchors, out _, out _);
            var second = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "555", anchors, out _, out _);

            Assert.Equal(first.ToJsonString(), second.ToJsonString());
        }

        /// <summary>槽指向的节点不在 workflow 里 → 翻译时报错，不产一张断图。</summary>
        [Fact]
        public void SlotPointingAtMissingNodeIsAnError()
        {
            var (workflow, _, assetRequest) = BuildFixture(withAnchorSlot: true);
            var recipe = new RecipeDefinition(
                "坏配方",
                "图标",
                "1.0.0",
                new[] { "1", "2", "7" },
                new[] { new RecipeMappingEntry("描述", "2", "text") },
                new[] { new RecipeAnchorSlot("参考图", "999", "image") },
                Array.Empty<string>());
            var anchors = new Dictionary<string, string>(StringComparer.Ordinal) { ["参考图"] = "x.png" };

            var payload = Program.BuildGenerateWorkflow(workflow, recipe, assetRequest, "1", anchors, out _, out var reason);

            Assert.Null(payload);
            Assert.Contains("999", reason);
        }

        /// <summary>造一份夹具；withAnchorSlot 决定配方带不带「参考图」槽。</summary>
        private static (JsonObject Workflow, RecipeDefinition Recipe, JsonElement AssetRequest) BuildFixture(bool withAnchorSlot)
        {
            var workflow = JsonNode.Parse(
                "{\"1\":{\"类型\":\"CheckpointLoaderSimple\",\"参数\":{\"ckpt_name\":\"sd_xl_base_1.0.safetensors\"}},"
                + "\"2\":{\"类型\":\"CLIPTextEncode\",\"参数\":{\"text\":\"\"}},"
                + "\"5\":{\"类型\":\"LoadImage\",\"参数\":{\"image\":\"\"}},"
                + "\"7\":{\"类型\":\"KSampler\",\"参数\":{\"seed\":0,\"steps\":30,\"cfg\":7.0}}}") as JsonObject;

            var anchors = withAnchorSlot
                ? new[] { new RecipeAnchorSlot("参考图", "5", "image") }
                : Array.Empty<RecipeAnchorSlot>();

            var recipe = new RecipeDefinition(
                "测试配方",
                "图标",
                "1.0.0",
                new[] { "1", "2", "5", "7" },
                new[] { new RecipeMappingEntry("描述", "2", "text") },
                anchors,
                Array.Empty<string>());

            var assetRequest = JsonDocument.Parse("{\"id\":\"a\",\"描述\":\"金币袋\",\"变体数\":1}").RootElement;
            return (workflow, recipe, assetRequest);
        }
    }
}
