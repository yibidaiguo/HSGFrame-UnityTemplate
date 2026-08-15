using System;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一条资产依赖方向规则：某目录前缀下的资产禁止引用另一目录前缀下的资产。</summary>
    public sealed class AssetDependencyRule
    {
        /// <summary>
        /// 构造一条依赖方向规则，两个目录前缀按固定规则规范化：空白存空串、反斜杠换正斜杠、
        /// 去掉开头的 ./、结尾补上斜杠。
        /// </summary>
        /// <param name="fromPathPrefix">引用方目录前缀，相对 Assets 根。</param>
        /// <param name="forbiddenPathPrefix">被引用方目录前缀，相对 Assets 根；该前缀下的资产禁止被引用。</param>
        /// <param name="reason">这条规则的理由说明，为 null 时存空串。</param>
        public AssetDependencyRule(string fromPathPrefix, string forbiddenPathPrefix, string reason)
        {
            FromPathPrefix = NormalizePrefix(fromPathPrefix);
            ForbiddenPathPrefix = NormalizePrefix(forbiddenPathPrefix);
            Reason = reason ?? string.Empty;
        }

        /// <summary>引用方目录前缀，相对 Assets 根，结尾带斜杠；空串表示匹配所有路径。</summary>
        public string FromPathPrefix { get; }

        /// <summary>被引用方目录前缀，相对 Assets 根，结尾带斜杠；空串表示匹配所有路径。</summary>
        public string ForbiddenPathPrefix { get; }

        /// <summary>这条规则的理由说明。</summary>
        public string Reason { get; }

        private static string NormalizePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return string.Empty;
            }

            var normalized = prefix.Replace('\\', '/');
            if (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            if (!normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized + "/";
            }

            return normalized;
        }
    }
}
