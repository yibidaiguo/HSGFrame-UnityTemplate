using System;
using System.Collections.Generic;
using System.Linq;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 拆图这一层：解析视觉模型给的框、按框裁、以及「改一改」时怎么带上一次的框。
    ///
    /// 盯的是**框准不准是模型的事，合不合法是我们的事**：
    /// 越界、零面积、没名字的框一律不用，绝不裁出一张空图当成果。
    /// </summary>
    public class UiLayerCutterTests
    {
        /// <summary>正常的一份回答解析得出层清单。</summary>
        [Fact]
        public void LayersAreParsedFromModelAnswer()
        {
            var text = "{\"层\":[{\"名字\":\"panel_bg\",\"左\":0,\"上\":0,\"右\":1,\"下\":1},"
                + "{\"名字\":\"btn_close\",\"左\":0.9,\"上\":0.02,\"右\":0.98,\"下\":0.08}]}";

            var layers = UiLayerCutter.ParseLayers(text, out var failure);

            Assert.Equal("", failure);
            Assert.Equal(2, layers.Count);
            Assert.Equal("btn_close", layers[1].Name);
            Assert.All(layers, layer => Assert.True(layer.IsUsable));
        }

        /// <summary>包在闲话与代码块里的 JSON 也要抠得出来——模型常这么回。</summary>
        [Fact]
        public void JsonWrappedInChatterIsStillParsed()
        {
            var text = "好的，我框好了：\n```json\n{\"层\":[{\"名字\":\"icon_coin\",\"左\":0.1,\"上\":0.1,\"右\":0.2,\"下\":0.2}]}\n```";

            var layers = UiLayerCutter.ParseLayers(text, out var failure);

            Assert.Equal("", failure);
            Assert.Equal("icon_coin", Assert.Single(layers).Name);
        }

        /// <summary>读不懂与「一层都没有」是两支，原因要说得出来。</summary>
        [Theory]
        [InlineData("", "空文本")]
        [InlineData("我觉得这张图很好看", "找不到")]
        [InlineData("{\"别的\":1}", "没有「层」数组")]
        [InlineData("{\"层\":[]}", "空的")]
        public void UnparseableAnswersReportWhy(string text, string expectedFragment)
        {
            var layers = UiLayerCutter.ParseLayers(text, out var failure);

            Assert.Empty(layers);
            Assert.Contains(expectedFragment, failure);
        }

        /// <summary>越界、零面积、没名字的框一律判成不可用。</summary>
        [Theory]
        [InlineData("btn", -0.1, 0, 0.5, 0.5)]
        [InlineData("btn", 0, 0, 1.2, 0.5)]
        [InlineData("btn", 0.5, 0.5, 0.5, 0.9)]
        [InlineData("", 0, 0, 1, 1)]
        public void IllegalBoxesAreRejected(string name, double left, double top, double right, double bottom)
        {
            Assert.False(new UiLayer(name, left, top, right, bottom).IsUsable);
        }

        /// <summary>裁出来的那块尺寸对得上框换算的像素。</summary>
        [Fact]
        public void CutProducesTheBoxedRegion()
        {
            var pixels = new byte[40 * 40 * 4];
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = 200;
            }

            var source = new PngImage(40, 40, pixels);
            var piece = UiLayerCutter.Cut(source, new UiLayer("btn", 0.25, 0.25, 0.75, 0.5));

            Assert.NotNull(piece);
            Assert.Equal(20, piece.Width);
            Assert.Equal(10, piece.Height);
        }

        /// <summary>框不合法时**不许裁出一张空图**，返回 null。</summary>
        [Fact]
        public void IllegalBoxCutsNothing()
        {
            var source = new PngImage(10, 10, new byte[10 * 10 * 4]);

            Assert.Null(UiLayerCutter.Cut(source, new UiLayer("x", 0, 0, 2, 2)));
            Assert.Null(UiLayerCutter.Cut(source, null));
        }

        /// <summary>
        /// 重拆的提示词要**把上一次的框原样带上**，并明说「没提到的原样保留」——
        /// 不然模型会从头再标一遍，人只说了一处，整套框全变。
        /// </summary>
        [Fact]
        public void RecutPromptCarriesPreviousBoxesAndTheAskToKeepTheRest()
        {
            var previous = new List<UiLayer>
            {
                new UiLayer("panel_bg", 0, 0, 1, 1),
                new UiLayer("btn_close", 0.9, 0.02, 0.98, 0.08)
            };

            var prompt = UiLayerCutter.BuildRecutPrompt(previous, "关闭按钮框大了，贴着按钮本身切");

            Assert.Contains("panel_bg", prompt);
            Assert.Contains("btn_close", prompt);
            Assert.Contains("关闭按钮框大了", prompt);
            Assert.Contains("原样保留", prompt);
        }
    }
}
