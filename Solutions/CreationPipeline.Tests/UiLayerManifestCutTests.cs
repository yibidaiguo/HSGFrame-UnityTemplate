using System;
using System.Collections.Generic;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 照清单切图：提示词让模型「找」而不是「猜」，结果按清单严格筛。
    ///
    /// 这一层跟从前那条「看图猜元素」的区别在**谁说了算**——
    /// 元素清单是策划审过的功能契约。一屏猜出上百个、跟需求对不上、通用件认不出来，
    /// 三样都是从这一点上错的，不是切图算法的问题。
    /// </summary>
    public sealed class UiLayerManifestCutTests
    {
        /// <summary>清单里的 id、类型、尺寸、人话名都要摆进提示词——模型得认得出要找的是什么。</summary>
        [Fact]
        public void ManifestPromptListsWhatToLookFor()
        {
            var prompt = UiLayerCutter.BuildManifestPrompt(Requests());

            Assert.Contains("ButtonSort", prompt);
            Assert.Contains("96×96", prompt);
            Assert.Contains("排序", prompt);
        }

        /// <summary>四条硬规矩要在——尤其「找不到就别放进结果」那条。</summary>
        [Theory]
        [InlineData("原样抄")]
        [InlineData("清单之外")]
        [InlineData("别放进结果")]
        [InlineData("只框其中一个")]
        public void ManifestPromptCarriesTheHardRules(string fragment)
        {
            Assert.Contains(fragment, UiLayerCutter.BuildManifestPrompt(Requests()));
        }

        /// <summary>清单之外的框丢掉——从前正是这些「顺手多框的」把一屏撑到上百个。</summary>
        [Fact]
        public void LayersOutsideTheManifestAreDropped()
        {
            var layers = new List<UiLayer>
            {
                Layer("ButtonSort"),
                Layer("DecorationLeaf"),
                Layer("SlotItem")
            };

            var kept = UiLayerCutter.FilterToManifest(layers, Requests(), out var missing, out var unexpected);

            Assert.Equal(2, kept.Count);
            Assert.Contains("DecorationLeaf", unexpected);
            Assert.Empty(missing);
        }

        /// <summary>
        /// 清单里有、结果里没有的要报出来。
        /// **缺件不许静默**——少一个元素就是少一张图，而少的那张要到进 Unity 摆界面时才发现。
        /// </summary>
        [Fact]
        public void MissingElementsAreReported()
        {
            var kept = UiLayerCutter.FilterToManifest(
                new List<UiLayer> { Layer("ButtonSort") }, Requests(), out var missing, out _);

            Assert.Single(kept);
            Assert.Contains("SlotItem", missing);
        }

        /// <summary>同一个 id 给了两个框只认第一个——清单里它只有一条，收两个会让落点静默互相覆盖。</summary>
        [Fact]
        public void DuplicateBoxesForOneIdentifierKeepOnlyTheFirst()
        {
            var layers = new List<UiLayer> { Layer("ButtonSort", 0.1), Layer("ButtonSort", 0.5) };

            var kept = UiLayerCutter.FilterToManifest(layers, Requests(), out _, out _);

            Assert.Single(kept);
            Assert.Equal(0.1, kept[0].Left, 3);
        }

        /// <summary>
        /// 不做模糊匹配：名字对不上就是对不上。
        /// 模糊匹配会把 ButtonSort 和 ButtonSortDescending 认成一个，而这两个是两张不同的图。
        /// </summary>
        [Fact]
        public void SimilarNamesAreNotMatchedLoosely()
        {
            var kept = UiLayerCutter.FilterToManifest(
                new List<UiLayer> { Layer("ButtonSortDescending") }, Requests(), out var missing, out var unexpected);

            Assert.Empty(kept);
            Assert.Contains("ButtonSortDescending", unexpected);
            Assert.Contains("ButtonSort", missing);
        }

        /// <summary>清单是空的时候，模型给什么都算清单外。</summary>
        [Fact]
        public void EverythingIsUnexpectedWhenTheManifestIsEmpty()
        {
            var kept = UiLayerCutter.FilterToManifest(
                new List<UiLayer> { Layer("ButtonSort") }, Array.Empty<UiLayerRequest>(), out var missing, out var unexpected);

            Assert.Empty(kept);
            Assert.Empty(missing);
            Assert.Single(unexpected);
        }

        /// <summary>两条清单。</summary>
        private static IReadOnlyList<UiLayerRequest> Requests()
        {
            return new[]
            {
                new UiLayerRequest("ButtonSort", "Button", "排序", 96, 96),
                new UiLayerRequest("SlotItem", "Image", "物品格子", 120, 120)
            };
        }

        /// <summary>造一个框。</summary>
        private static UiLayer Layer(string name, double left = 0.1)
        {
            return new UiLayer(name, left, 0.1, left + 0.1, 0.2);
        }
    }
}
