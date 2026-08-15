using System;
using System.Linq;
using HSGFrame.WorldSpaceUI;
using Xunit;

namespace HSGFrame.WorldSpaceUI.Tests
{
    /// <summary>世界空间锚点登记表的登记、覆盖、注销与批量解析测试。</summary>
    public class WorldAnchorRegistryTests
    {
        private static WorldAnchorRegistry CreateRegistry()
        {
            return new WorldAnchorRegistry(new WorldAnchorPolicy(30f, 1f, 2f));
        }

        [Fact]
        public void RegisterIncrementsAnchorCount()
        {
            var registry = CreateRegistry();

            registry.Register("alpha", new WorldPoint(0f, 0f, 0f));
            registry.Register("bravo", new WorldPoint(0f, 0f, 0f));

            Assert.Equal(2, registry.AnchorCount);
        }

        [Fact]
        public void RegisteringSameIdOverwritesWithoutGrowingCount()
        {
            var registry = CreateRegistry();

            registry.Register("alpha", new WorldPoint(0f, 0f, 0f));
            registry.Register("alpha", new WorldPoint(0f, 0f, 10f));

            Assert.Equal(1, registry.AnchorCount);

            // 旧位置 (0,0,0) 点积为 0 会被隐藏，覆盖后的新位置 (0,0,10) 在相机前方、可见。
            var result = registry.Resolve(new WorldPoint(0f, 0f, 0f), new WorldPoint(0f, 0f, 1f));
            Assert.Single(result);
            Assert.True(result[0].Value.IsVisible);
        }

        [Fact]
        public void UpdatePositionTakesEffect()
        {
            var registry = CreateRegistry();
            registry.Register("alpha", new WorldPoint(0f, 0f, 1f));

            registry.UpdatePosition("alpha", new WorldPoint(0f, 0f, 50f));

            var result = registry.Resolve(new WorldPoint(0f, 0f, 0f), new WorldPoint(0f, 0f, 1f));
            Assert.Single(result);
            Assert.False(result[0].Value.IsVisible);
            Assert.Equal("超出可见距离", result[0].Value.HiddenReason);
        }

        [Fact]
        public void UpdatePositionOnUnknownIdReturnsFalse()
        {
            var registry = CreateRegistry();

            Assert.False(registry.UpdatePosition("missing", new WorldPoint(0f, 0f, 0f)));
        }

        [Fact]
        public void DisposingHandleDecrementsCount()
        {
            var registry = CreateRegistry();
            var handle = registry.Register("alpha", new WorldPoint(0f, 0f, 0f));

            Assert.Equal(1, registry.AnchorCount);

            handle.Dispose();

            Assert.Equal(0, registry.AnchorCount);
        }

        [Fact]
        public void DisposingHandleTwiceIsSafe()
        {
            var registry = CreateRegistry();
            var handle = registry.Register("alpha", new WorldPoint(0f, 0f, 0f));

            handle.Dispose();
            handle.Dispose();

            Assert.Equal(0, registry.AnchorCount);
        }

        [Fact]
        public void ResolveReturnsEntriesSortedByAnchorId()
        {
            var registry = CreateRegistry();
            registry.Register("charlie", new WorldPoint(0f, 0f, 1f));
            registry.Register("alpha", new WorldPoint(0f, 0f, 1f));
            registry.Register("bravo", new WorldPoint(0f, 0f, 1f));

            var result = registry.Resolve(new WorldPoint(0f, 0f, 0f), new WorldPoint(0f, 0f, 1f));

            Assert.Equal(new[] { "alpha", "bravo", "charlie" }, result.Select(pair => pair.Key).ToArray());
        }

        [Fact]
        public void ResolveVisibleIdsReturnsOnlyVisible()
        {
            var registry = CreateRegistry();
            registry.Register("alpha", new WorldPoint(0f, 0f, 5f));
            registry.Register("bravo", new WorldPoint(0f, 0f, -5f));
            registry.Register("charlie", new WorldPoint(0f, 0f, 50f));

            var visible = registry.ResolveVisibleIds(new WorldPoint(0f, 0f, 0f), new WorldPoint(0f, 0f, 1f));

            Assert.Equal(new[] { "alpha" }, visible.ToArray());
        }

        [Fact]
        public void ResolveOnEmptyRegistryReturnsEmptyList()
        {
            var registry = CreateRegistry();

            Assert.Empty(registry.Resolve(new WorldPoint(0f, 0f, 0f), new WorldPoint(0f, 0f, 1f)));
        }

        [Fact]
        public void RegisteringEmptyIdThrows()
        {
            var registry = CreateRegistry();

            Assert.Throws<ArgumentException>(() => registry.Register(string.Empty, new WorldPoint(0f, 0f, 0f)));
            Assert.Throws<ArgumentException>(() => registry.Register((string)null, new WorldPoint(0f, 0f, 0f)));
        }

        [Fact]
        public void ClearAllResetsCountToZero()
        {
            var registry = CreateRegistry();
            registry.Register("alpha", new WorldPoint(0f, 0f, 0f));
            registry.Register("bravo", new WorldPoint(0f, 0f, 0f));

            registry.ClearAll();

            Assert.Equal(0, registry.AnchorCount);
        }
    }
}
