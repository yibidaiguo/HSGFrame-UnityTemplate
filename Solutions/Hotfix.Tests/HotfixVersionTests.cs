using HSGFrame.Hotfix;
using Xunit;

namespace HSGFrame.Hotfix.Tests
{
    /// <summary>热更版本号解析与比较的测试。</summary>
    public class HotfixVersionTests
    {
        [Fact]
        public void TryParse_ValidThreePartString_ReturnsVersion()
        {
            var parsed = HotfixVersion.TryParse("1.2.3", out var version);

            Assert.True(parsed);
            Assert.Equal(1, version.Major);
            Assert.Equal(2, version.Minor);
            Assert.Equal(3, version.Patch);
        }

        [Fact]
        public void Compare_TenPatch_IsGreaterThan_NinePatch()
        {
            var lower = new HotfixVersion(1, 2, 9);
            var higher = new HotfixVersion(1, 2, 10);

            Assert.True(higher > lower);
            Assert.True(lower < higher);
        }

        [Fact]
        public void TryParse_TwoParts_ReturnsFalse()
        {
            Assert.False(HotfixVersion.TryParse("1.2", out _));
        }

        [Fact]
        public void TryParse_NonNumericParts_ReturnsFalse()
        {
            Assert.False(HotfixVersion.TryParse("a.b.c", out _));
        }

        [Fact]
        public void TryParse_EmptyString_ReturnsFalse()
        {
            Assert.False(HotfixVersion.TryParse("", out _));
        }

        [Fact]
        public void Equal_Versions_AreEqual_And_ShareHashCode()
        {
            var first = new HotfixVersion(1, 2, 3);
            var second = new HotfixVersion(1, 2, 3);

            Assert.True(first == second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
        }

        [Fact]
        public void ToString_ReturnsDottedForm()
        {
            var version = new HotfixVersion(1, 2, 3);

            Assert.Equal("1.2.3", version.ToString());
        }

        [Fact]
        public void TryParse_FourParts_ReturnsFalse()
        {
            Assert.False(HotfixVersion.TryParse("1.2.3.4", out _));
        }
    }
}
