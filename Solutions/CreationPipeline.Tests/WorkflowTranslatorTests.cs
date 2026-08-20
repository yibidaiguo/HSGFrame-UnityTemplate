using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Template.Bridges.Comfyui;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 配方骨架翻译器测试：骨架 + 连线 → 下游 API 形状。翻译器是纯函数，不碰网络与磁盘，
    /// 必须能脱离下游单独测——这些测试里没有任何一个依赖真实下游。
    /// </summary>
    public class WorkflowTranslatorTests
    {
        /// <summary>骨架加连线翻成 API 形状：类型进 class_type、参数进 inputs、连线写成 [上游id, 下标]。</summary>
        [Fact]
        public void Translate_TurnsSkeletonAndConnectionsIntoApiShape()
        {
            var workflow = Parse(
                "{\"1\":{\"类型\":\"CheckpointLoaderSimple\",\"参数\":{\"ckpt_name\":\"sd_xl_base_1.0.safetensors\"}},"
                + "\"2\":{\"类型\":\"CLIPTextEncode\",\"参数\":{\"text\":\"\"},\"连线\":{\"clip\":[\"1\",1]}}}");

            var translated = WorkflowTranslator.Translate(workflow, null);

            Assert.Equal("CheckpointLoaderSimple", translated["1"]["class_type"].GetValue<string>());
            Assert.Equal("sd_xl_base_1.0.safetensors", translated["1"]["inputs"]["ckpt_name"].GetValue<string>());
            Assert.Equal("CLIPTextEncode", translated["2"]["class_type"].GetValue<string>());
            Assert.True(JsonNode.DeepEquals(
                JsonNode.Parse("[\"1\",1]"),
                translated["2"]["inputs"]["clip"]));
        }

        /// <summary>连线指向不存在的节点必须报错，绝不静默产一张断图。</summary>
        [Fact]
        public void Translate_ConnectionToMissingNode_Throws()
        {
            var workflow = Parse(
                "{\"2\":{\"类型\":\"CLIPTextEncode\",\"参数\":{\"text\":\"\"},\"连线\":{\"clip\":[\"9\",0]}}}");

            var exception = Assert.Throws<InvalidOperationException>(() => WorkflowTranslator.Translate(workflow, null));
            Assert.Contains("9", exception.Message);
        }

        /// <summary>连线形状不对（缺输出下标、下标不是数字）也报错，不是静默。</summary>
        [Fact]
        public void Translate_ConnectionShapeInvalid_Throws()
        {
            var workflow = Parse(
                "{\"2\":{\"类型\":\"CLIPTextEncode\",\"参数\":{\"text\":\"\"},\"连线\":{\"clip\":[\"1\"]}}}");

            Assert.Throws<InvalidOperationException>(() => WorkflowTranslator.Translate(workflow, null));
        }

        /// <summary>参数覆盖按映射生效：覆盖值压过骨架参数，没被覆盖的键原样保留。</summary>
        [Fact]
        public void Translate_ParameterOverridesApplyPerMapping()
        {
            var workflow = Parse(
                "{\"2\":{\"类型\":\"CLIPTextEncode\",\"参数\":{\"text\":\"\"}},"
                + "\"3\":{\"类型\":\"EmptyLatentImage\",\"参数\":{\"width\":256,\"height\":256,\"batch_size\":1}}}");

            var overrides = new Dictionary<string, IReadOnlyDictionary<string, JsonNode>>
            {
                ["2"] = new Dictionary<string, JsonNode> { ["text"] = JsonValue.Create("金币袋") },
                ["3"] = new Dictionary<string, JsonNode> { ["width"] = JsonValue.Create(512) }
            };

            var translated = WorkflowTranslator.Translate(workflow, overrides);

            Assert.Equal("金币袋", translated["2"]["inputs"]["text"].GetValue<string>());
            Assert.Equal(512, translated["3"]["inputs"]["width"].GetValue<int>());
            Assert.Equal(256, translated["3"]["inputs"]["height"].GetValue<int>());
            Assert.Equal(1, translated["3"]["inputs"]["batch_size"].GetValue<int>());
        }

        /// <summary>覆盖优先级：参数 → 连线 → 覆盖，覆盖永远压过参数与连线。</summary>
        [Fact]
        public void Translate_OverridesBeatParametersAndConnections()
        {
            var workflow = Parse(
                "{\"1\":{\"类型\":\"A\",\"参数\":{\"x\":\"参数值\"},\"连线\":{\"x\":[\"2\",0]}},"
                + "\"2\":{\"类型\":\"B\",\"参数\":{}}}");

            var overrides = new Dictionary<string, IReadOnlyDictionary<string, JsonNode>>
            {
                ["1"] = new Dictionary<string, JsonNode> { ["x"] = JsonValue.Create("覆盖值") }
            };

            var translated = WorkflowTranslator.Translate(workflow, overrides);

            Assert.Equal("覆盖值", translated["1"]["inputs"]["x"].GetValue<string>());
        }

        /// <summary>参数覆盖指向不存在的节点也报错，静默失效和断图一样糟。</summary>
        [Fact]
        public void Translate_OverridesForMissingNode_Throws()
        {
            var workflow = Parse("{\"2\":{\"类型\":\"CLIPTextEncode\",\"参数\":{\"text\":\"\"}}}");
            var overrides = new Dictionary<string, IReadOnlyDictionary<string, JsonNode>>
            {
                ["9"] = new Dictionary<string, JsonNode> { ["text"] = JsonValue.Create("") }
            };

            Assert.Throws<InvalidOperationException>(() => WorkflowTranslator.Translate(workflow, overrides));
        }

        /// <summary>缺「类型」的节点报错，不静默。</summary>
        [Fact]
        public void Translate_NodeWithoutClassType_Throws()
        {
            var workflow = Parse("{\"1\":{\"参数\":{\"x\":1}}}");
            Assert.Throws<InvalidOperationException>(() => WorkflowTranslator.Translate(workflow, null));
        }

        /// <summary>
        /// 图标@v5 配方的完整形状：8 个节点连成一条能跑的文生图链——
        /// checkpoint → 正/负提示、latent → KSampler → VAEDecode → SaveImage。
        /// 这一条验证的不是语法，是「配方真能翻成一张不破的图」。
        /// </summary>
        [Fact]
        public void Translate_IconRecipeShape_YieldsCompleteChain()
        {
            var workflow = Parse(
                "{\"1\":{\"类型\":\"CheckpointLoaderSimple\",\"参数\":{\"ckpt_name\":\"sd_xl_base_1.0.safetensors\"}},"
                + "\"2\":{\"类型\":\"CLIPTextEncode\",\"参数\":{\"text\":\"\"},\"连线\":{\"clip\":[\"1\",1]}},"
                + "\"3\":{\"类型\":\"EmptyLatentImage\",\"参数\":{\"width\":256,\"height\":256,\"batch_size\":1}},"
                + "\"4\":{\"类型\":\"SaveImage\",\"参数\":{\"filename_prefix\":\"\"},\"连线\":{\"images\":[\"8\",0]}},"
                + "\"5\":{\"类型\":\"LoadImage\",\"参数\":{\"image\":\"\"}},"
                + "\"6\":{\"类型\":\"CLIPTextEncode\",\"参数\":{\"text\":\"text, watermark\"},\"连线\":{\"clip\":[\"1\",1]}},"
                + "\"7\":{\"类型\":\"KSampler\",\"参数\":{\"seed\":0,\"steps\":30,\"cfg\":7.0,\"sampler_name\":\"euler\",\"scheduler\":\"normal\",\"denoise\":1.0},"
                + "\"连线\":{\"model\":[\"1\",0],\"positive\":[\"2\",0],\"negative\":[\"6\",0],\"latent_image\":[\"3\",0]}},"
                + "\"8\":{\"类型\":\"VAEDecode\",\"参数\":{},\"连线\":{\"samples\":[\"7\",0],\"vae\":[\"1\",2]}}}");

            var translated = WorkflowTranslator.Translate(workflow, null);

            Assert.Equal(8, translated.Count);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[\"1\",1]"), translated["2"]["inputs"]["clip"]));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[\"1\",1]"), translated["6"]["inputs"]["clip"]));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[\"1\",0]"), translated["7"]["inputs"]["model"]));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[\"2\",0]"), translated["7"]["inputs"]["positive"]));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[\"6\",0]"), translated["7"]["inputs"]["negative"]));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[\"3\",0]"), translated["7"]["inputs"]["latent_image"]));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[\"7\",0]"), translated["8"]["inputs"]["samples"]));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[\"1\",2]"), translated["8"]["inputs"]["vae"]));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[\"8\",0]"), translated["4"]["inputs"]["images"]));
            Assert.False(translated["5"]["inputs"].AsObject().ContainsKey("images"), "锚点槽没接进主链时不应有 images 输入");
        }

        /// <summary>解析 JSON 文本成 JsonObject；测试里用，解析失败直接抛。</summary>
        private static JsonObject Parse(string text)
        {
            return JsonNode.Parse(text) as JsonObject;
        }
    }
}
