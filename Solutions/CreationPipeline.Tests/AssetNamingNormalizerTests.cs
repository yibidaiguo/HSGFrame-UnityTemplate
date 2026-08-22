using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 命名归一：按资产类型的命名模式把名字补成合规的。
    ///
    /// 盯两件事：**该补的补上并说出来**（名字要进仓库、进 .meta、被别处引用，
    /// 悄悄改掉等于让人手上那个名字与仓库里那个对不上），
    /// **补不出来的别硬猜**（那时该让规格门禁去判红，而不是编一个能过的名字）。
    /// </summary>
    public class AssetNamingNormalizerTests
    {
        /// <summary>本来就合规的一个字都不动。</summary>
        [Fact]
        public void AlreadyMatchingNamingIsLeftAlone()
        {
            var outcome = AssetNamingNormalizer.Normalize("icon_bag", "^icon_[a-z0-9_]+$");

            Assert.Equal("icon_bag", outcome.Naming);
            Assert.False(outcome.Changed);
            Assert.Equal("", outcome.Note);
        }

        /// <summary>缺前缀的补上前缀，并且**要说出来**改了什么。</summary>
        [Fact]
        public void MissingPrefixIsAddedAndReported()
        {
            var outcome = AssetNamingNormalizer.Normalize("bag", "^icon_[a-z0-9_]+$");

            Assert.Equal("icon_bag", outcome.Naming);
            Assert.True(outcome.Changed);
            Assert.Contains("icon_bag", outcome.Note);
        }

        /// <summary>名字里已经含着前缀那个词时先摘掉，免得补出「ui_bag_ui_effect」这种叠词。</summary>
        [Fact]
        public void PrefixWordInsideNamingIsNotDuplicated()
        {
            var outcome = AssetNamingNormalizer.Normalize("bag_ui_effect", "^ui_[a-z0-9_]+$");

            Assert.Equal("ui_bag_effect", outcome.Naming);
            Assert.True(outcome.Changed);
        }

        /// <summary>中文与大写一律归一成小写下划线。</summary>
        [Fact]
        public void NonAsciiAndUpperCaseAreSlugged()
        {
            var outcome = AssetNamingNormalizer.Normalize("Bag Main 界面", "^ui_[a-z0-9_]+$");

            Assert.Equal("ui_bag_main", outcome.Naming);
            Assert.True(outcome.Changed);
        }

        /// <summary>模式里抠不出确定前缀时**不许硬猜**：原样返回并说清为什么没敢改。</summary>
        [Fact]
        public void PatternWithoutLiteralPrefixIsNotGuessed()
        {
            var outcome = AssetNamingNormalizer.Normalize("bag", "^[a-z]+_[0-9]+$");

            Assert.Equal("bag", outcome.Naming);
            Assert.False(outcome.Changed);
            Assert.Contains("没敢替你改", outcome.Note);
        }

        /// <summary>去掉不合规字符后什么都不剩时也不许硬造一个名字。</summary>
        [Fact]
        public void NamingThatSlugsToNothingIsNotInvented()
        {
            var outcome = AssetNamingNormalizer.Normalize("背包", "^icon_[a-z0-9_]+$");

            Assert.False(outcome.Changed);
            Assert.Contains("没敢替你改", outcome.Note);
        }

        /// <summary>没有命名模式时一个字都不动——没有规则就没有该补的东西。</summary>
        [Fact]
        public void EmptyPatternLeavesNamingAlone()
        {
            var outcome = AssetNamingNormalizer.Normalize("bag", "");

            Assert.Equal("bag", outcome.Naming);
            Assert.False(outcome.Changed);
        }

        /// <summary>字面前缀就抠到第一个正则元字符为止。</summary>
        [Theory]
        [InlineData("^icon_[a-z0-9_]+$", "icon_")]
        [InlineData("^ui_[a-z0-9_]+$", "ui_")]
        [InlineData("[a-z]+", "")]
        [InlineData("^[a-z]+", "")]
        public void LiteralPrefixStopsAtFirstMetaCharacter(string pattern, string expected)
        {
            Assert.Equal(expected, AssetNamingNormalizer.LiteralPrefix(pattern));
        }
    }
}
