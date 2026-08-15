using System;

namespace HSGFrame.WorldSpaceUI
{
    /// <summary>一个世界空间锚点这一帧的呈现结论。</summary>
    public readonly struct WorldAnchorPresentation
    {
        /// <summary>这一帧该不该显示。</summary>
        public bool IsVisible { get; }

        /// <summary>缩放系数，用来补偿透视让远处的字不至于看不清。</summary>
        public float Scale { get; }

        /// <summary>不显示时的原因，显示时是空串。</summary>
        public string HiddenReason { get; }

        /// <summary>用可见性、缩放与隐藏原因构造一份呈现结论。</summary>
        public WorldAnchorPresentation(bool isVisible, float scale, string hiddenReason)
        {
            IsVisible = isVisible;
            Scale = scale;
            HiddenReason = hiddenReason;
        }
    }

    /// <summary>世界空间锚点的呈现策略：按距离与朝向判定显示与否，并算出缩放。</summary>
    public sealed class WorldAnchorPolicy
    {
        private readonly float _maximumVisibleDistance;
        private readonly float _minimumScale;
        private readonly float _maximumScale;

        /// <summary>用可见距离上限与缩放区间构造。</summary>
        public WorldAnchorPolicy(float maximumVisibleDistance = 30f, float minimumScale = 1f, float maximumScale = 2f)
        {
            if (maximumVisibleDistance <= 0f)
            {
                throw new ArgumentException(
                    "位置：WorldAnchorPolicy 构造函数；原因：maximumVisibleDistance 小于等于 0；修复：传入大于 0 的可见距离上限；参考：参见 maximumVisibleDistance 参数说明");
            }

            if (minimumScale > maximumScale)
            {
                throw new ArgumentException(
                    "位置：WorldAnchorPolicy 构造函数；原因：minimumScale 大于 maximumScale；修复：让最小缩放小于等于最大缩放；参考：参见 minimumScale 与 maximumScale 参数说明");
            }

            _maximumVisibleDistance = maximumVisibleDistance;
            _minimumScale = minimumScale;
            _maximumScale = maximumScale;
        }

        /// <summary>超过这个距离就隐藏。</summary>
        public float MaximumVisibleDistance => _maximumVisibleDistance;

        /// <summary>最近处的缩放系数。</summary>
        public float MinimumScale => _minimumScale;

        /// <summary>最远处的缩放系数。</summary>
        public float MaximumScale => _maximumScale;

        /// <summary>算一个锚点这一帧该怎么呈现。</summary>
        public WorldAnchorPresentation Resolve(WorldPoint anchorPosition, WorldPoint cameraPosition, WorldPoint cameraForward)
        {
            var toAnchor = WorldPoint.Subtract(anchorPosition, cameraPosition);
            var distance = WorldPoint.Distance(anchorPosition, cameraPosition);

            // 缩放始终按距离在 [MinimumScale, MaximumScale] 之间线性插值，与是否可见正交：
            // 即使隐藏，调用方也能拿到「若显示该多大」。距离超出上限时比例钳到 1，不放大过头。
            var ratio = Clamp01(distance / _maximumVisibleDistance);
            var scale = _minimumScale + (_maximumScale - _minimumScale) * ratio;

            // 距离超过上限就隐藏。恰好等于上限时算可见（边界取闭区间），所以这里用严格大于。
            if (distance > _maximumVisibleDistance)
            {
                return new WorldAnchorPresentation(false, scale, "超出可见距离");
            }

            // 相机到锚点的向量与朝向点积 ≤ 0 说明锚点在相机背后或正侧方，隐藏。
            // 只判距离的话，转身之后背后的血条会继续飘在屏幕上。
            if (WorldPoint.Dot(toAnchor, cameraForward) <= 0f)
            {
                return new WorldAnchorPresentation(false, scale, "在相机背后");
            }

            return new WorldAnchorPresentation(true, scale, string.Empty);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }
    }
}
