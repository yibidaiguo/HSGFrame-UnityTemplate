using System;
using HSGFrame.WorldSpaceUI;
using Xunit;

namespace HSGFrame.WorldSpaceUI.Tests
{
    /// <summary>世界空间锚点呈现策略的显示判定、缩放插值与参数校验测试。</summary>
    public class WorldAnchorPolicyTests
    {
        [Fact]
        public void AnchorInFrontWithinDistanceIsVisible()
        {
            var policy = new WorldAnchorPolicy(30f, 1f, 2f);

            var result = policy.Resolve(
                new WorldPoint(0f, 0f, 5f),
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 1f));

            Assert.True(result.IsVisible);
            Assert.Equal(string.Empty, result.HiddenReason);
        }

        [Fact]
        public void AnchorBeyondMaximumDistanceIsHiddenWithReason()
        {
            var policy = new WorldAnchorPolicy(30f, 1f, 2f);

            var result = policy.Resolve(
                new WorldPoint(0f, 0f, 31f),
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 1f));

            Assert.False(result.IsVisible);
            Assert.Equal("超出可见距离", result.HiddenReason);
        }

        [Fact]
        public void AnchorBehindCameraIsHiddenWithReason()
        {
            var policy = new WorldAnchorPolicy(30f, 1f, 2f);

            var result = policy.Resolve(
                new WorldPoint(0f, 0f, -5f),
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 1f));

            Assert.False(result.IsVisible);
            Assert.Equal("在相机背后", result.HiddenReason);
        }

        [Fact]
        public void AnchorExactlyAtMaximumDistanceIsVisible()
        {
            // 距离恰好等于上限时算可见（边界取闭区间），所以这一条必须通过。
            var policy = new WorldAnchorPolicy(30f, 1f, 2f);

            var result = policy.Resolve(
                new WorldPoint(0f, 0f, 30f),
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 1f));

            Assert.True(result.IsVisible);
        }

        [Fact]
        public void AnchorWithZeroDotIsHidden()
        {
            // 相机到锚点的向量与朝向垂直时点积恰好为 0，按「≤ 0 隐藏」处理，避免正侧方的锚点残留。
            var policy = new WorldAnchorPolicy(30f, 1f, 2f);

            var result = policy.Resolve(
                new WorldPoint(5f, 0f, 0f),
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 1f));

            Assert.False(result.IsVisible);
            Assert.Equal("在相机背后", result.HiddenReason);
        }

        [Fact]
        public void ZeroDistanceYieldsMinimumScale()
        {
            var policy = new WorldAnchorPolicy(30f, 1f, 2f);

            var result = policy.Resolve(
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 1f));

            Assert.Equal(1f, result.Scale, 3);
        }

        [Fact]
        public void DistanceAtMaximumYieldsMaximumScale()
        {
            var policy = new WorldAnchorPolicy(30f, 1f, 2f);

            var result = policy.Resolve(
                new WorldPoint(0f, 0f, 30f),
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 1f));

            Assert.Equal(2f, result.Scale, 3);
        }

        [Fact]
        public void HalfDistanceYieldsMidpointScale()
        {
            var policy = new WorldAnchorPolicy(30f, 1f, 2f);

            var result = policy.Resolve(
                new WorldPoint(0f, 0f, 15f),
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 1f));

            Assert.Equal(1.5f, result.Scale, 3);
        }

        [Fact]
        public void EqualScaleRangeYieldsSameScaleAtAnyDistance()
        {
            var policy = new WorldAnchorPolicy(30f, 1.5f, 1.5f);

            var near = policy.Resolve(
                new WorldPoint(0f, 0f, 3f),
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 1f));
            var far = policy.Resolve(
                new WorldPoint(0f, 0f, 27f),
                new WorldPoint(0f, 0f, 0f),
                new WorldPoint(0f, 0f, 1f));

            Assert.Equal(1.5f, near.Scale, 3);
            Assert.Equal(1.5f, far.Scale, 3);
        }

        [Fact]
        public void MaximumVisibleDistanceNotPositiveThrows()
        {
            var exception = Assert.Throws<ArgumentException>(() => new WorldAnchorPolicy(0f, 1f, 2f));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }

        [Fact]
        public void MinimumScaleGreaterThanMaximumScaleThrows()
        {
            var exception = Assert.Throws<ArgumentException>(() => new WorldAnchorPolicy(30f, 2f, 1f));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }

        [Fact]
        public void WorldPointDistanceMeasuresEuclideanDistance()
        {
            var left = new WorldPoint(0f, 0f, 0f);
            var right = new WorldPoint(3f, 4f, 0f);

            Assert.Equal(5f, WorldPoint.Distance(left, right), 3);
        }

        [Fact]
        public void WorldPointSubtractComputesComponentDifference()
        {
            var left = new WorldPoint(5f, 4f, 3f);
            var right = new WorldPoint(2f, 1f, 0f);

            var difference = WorldPoint.Subtract(left, right);

            Assert.Equal(3f, difference.X, 3);
            Assert.Equal(3f, difference.Y, 3);
            Assert.Equal(3f, difference.Z, 3);
        }

        [Fact]
        public void WorldPointDotComputesDotProduct()
        {
            var left = new WorldPoint(1f, 2f, 3f);
            var right = new WorldPoint(4f, 5f, 6f);

            Assert.Equal(32f, WorldPoint.Dot(left, right), 3);
        }
    }
}
