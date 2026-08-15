using System;
using System.Globalization;

namespace HSGFrame.Hotfix
{
    /// <summary>三段式热更版本号，按 Major → Minor → Patch 逐段比较。</summary>
    public readonly struct HotfixVersion : IComparable<HotfixVersion>, IEquatable<HotfixVersion>
    {
        /// <summary>主版本号。</summary>
        public int Major { get; }

        /// <summary>次版本号。</summary>
        public int Minor { get; }

        /// <summary>修订版本号。</summary>
        public int Patch { get; }

        /// <summary>以三段版本号构造。</summary>
        public HotfixVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        /// <summary>解析「1.2.3」形状的版本号，段数不足、含非数字或为空时返回 false。</summary>
        public static bool TryParse(string text, out HotfixVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var parts = text.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            // NumberStyles.None 只认纯数字：前导符号、空白、小数点都会被拒，避免「-1.2.3」这类脏输入漏进去。
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major))
            {
                return false;
            }

            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
            {
                return false;
            }

            if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            {
                return false;
            }

            version = new HotfixVersion(major, minor, patch);
            return true;
        }

        /// <summary>按 Major → Minor → Patch 逐段比较。</summary>
        public int CompareTo(HotfixVersion other)
        {
            // 逐段比数值而非字符串：1.2.10 必须大于 1.2.9，字符串比较会得出相反结果。
            if (Major != other.Major)
            {
                return Major.CompareTo(other.Major);
            }

            if (Minor != other.Minor)
            {
                return Minor.CompareTo(other.Minor);
            }

            return Patch.CompareTo(other.Patch);
        }

        /// <summary>回「1.2.3」形状的文本。</summary>
        public override string ToString() => $"{Major}.{Minor}.{Patch}";

        /// <summary>三段相等即相等。</summary>
        public bool Equals(HotfixVersion other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;

        /// <summary>与对象比较是否相等。</summary>
        public override bool Equals(object obj) => obj is HotfixVersion other && Equals(other);

        /// <summary>三段共同决定哈希。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Major;
                hashCode = (hashCode * 397) ^ Minor;
                hashCode = (hashCode * 397) ^ Patch;
                return hashCode;
            }
        }

        /// <summary>相等比较。</summary>
        public static bool operator ==(HotfixVersion left, HotfixVersion right) => left.Equals(right);

        /// <summary>不等比较。</summary>
        public static bool operator !=(HotfixVersion left, HotfixVersion right) => !left.Equals(right);

        /// <summary>小于比较。</summary>
        public static bool operator <(HotfixVersion left, HotfixVersion right) => left.CompareTo(right) < 0;

        /// <summary>大于比较。</summary>
        public static bool operator >(HotfixVersion left, HotfixVersion right) => left.CompareTo(right) > 0;

        /// <summary>小于等于比较。</summary>
        public static bool operator <=(HotfixVersion left, HotfixVersion right) => left.CompareTo(right) <= 0;

        /// <summary>大于等于比较。</summary>
        public static bool operator >=(HotfixVersion left, HotfixVersion right) => left.CompareTo(right) >= 0;
    }
}
