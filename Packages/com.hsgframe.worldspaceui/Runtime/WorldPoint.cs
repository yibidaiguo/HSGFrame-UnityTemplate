using System;

namespace HSGFrame.WorldSpaceUI
{
    /// <summary>三分量点，纯 C# 结构，让这一层与引擎的向量类型解耦。</summary>
    public readonly struct WorldPoint
    {
        /// <summary>X 分量。</summary>
        public float X { get; }

        /// <summary>Y 分量。</summary>
        public float Y { get; }

        /// <summary>Z 分量。</summary>
        public float Z { get; }

        /// <summary>用三个分量构造一个点。</summary>
        public WorldPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>两点之间的欧氏距离。</summary>
        public static float Distance(WorldPoint left, WorldPoint right)
        {
            var difference = Subtract(left, right);
            return (float)Math.Sqrt(
                difference.X * difference.X
                + difference.Y * difference.Y
                + difference.Z * difference.Z);
        }

        /// <summary>两点相减得到的分量差（left 减去 right）。</summary>
        public static WorldPoint Subtract(WorldPoint left, WorldPoint right)
        {
            return new WorldPoint(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        /// <summary>与另一个点的点积。</summary>
        public static float Dot(WorldPoint left, WorldPoint right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }
    }
}
