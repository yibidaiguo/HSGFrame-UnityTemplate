using System.Collections.Generic;

namespace HSGFrame.Hotfix
{
    /// <summary>一份热更版本清单：版本号与该版本包含的全部包。属性名即 JSON 键名、保持英文——这份清单是给程序和 CDN 解析的，不是给策划读的。</summary>
    public sealed class HotfixManifest
    {
        /// <summary>版本号原文，形如「1.2.3」。</summary>
        public string VersionText { get; }

        /// <summary>该版本包含的全部包条目。</summary>
        public IReadOnlyList<HotfixPackageEntry> Packages { get; }

        /// <summary>以版本号原文与包列表构造。</summary>
        public HotfixManifest(string versionText, IReadOnlyList<HotfixPackageEntry> packages)
        {
            VersionText = versionText;
            Packages = packages;
        }

        /// <summary>尝试解析清单里的版本号，失败返回 false。</summary>
        public bool TryGetVersion(out HotfixVersion version) => HotfixVersion.TryParse(VersionText, out version);
    }
}
