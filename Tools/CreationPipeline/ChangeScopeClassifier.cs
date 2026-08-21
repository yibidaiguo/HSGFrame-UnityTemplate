using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 仓库相对路径 → 范围名。判定表从上到下第一个命中的赢，路径分隔符先统一成「/」，
    /// 比较大小写不敏感。范围名与放行策略数据里的「范围」键一致：框架 / 引擎 / 检查器 /
    /// 构建 / 规范 / 业务 / 其他。
    /// </summary>
    public static class ChangeScopeClassifier
    {
        /// <summary>
        /// 把一条仓库相对路径分类到范围名。
        /// </summary>
        /// <param name="repositoryRelativePath">仓库相对路径，如「Packages/com.hsgframe.core/Runtime/A.cs」。</param>
        public static string Classify(string repositoryRelativePath)
        {
            var normalized = (repositoryRelativePath ?? "").Replace('\\', '/');

            if (StartsWith(normalized, "Packages/"))
            {
                return "框架";
            }

            if (StartsWith(normalized, "Tools/Gates/"))
            {
                return "检查器";
            }

            if (StartsWith(normalized, "Tools/CreationPipeline/")
                || StartsWith(normalized, "Tools/Cli/")
                || StartsWith(normalized, "Tools/Dashboard/"))
            {
                return "引擎";
            }

            if (EndsWith(normalized, ".sln")
                || EndsWith(normalized, ".csproj")
                || StartsWith(normalized, "Tools/Gates/gate")
                || StartsWith(normalized, ".github/"))
            {
                return "构建";
            }

            if (StartsWith(normalized, "Specifications/") || StartsWith(normalized, "Doc/"))
            {
                return "Specifications";
            }

            if (StartsWith(normalized, "UnityProject/Assets/Game/Scripts/Modules/"))
            {
                return "业务";
            }

            return "其他";
        }

        /// <summary>
        /// 把一组仓库相对路径逐条分类并去重，按序数序排序返回。
        /// </summary>
        /// <param name="repositoryRelativePaths">仓库相对路径列表。</param>
        public static IReadOnlyList<string> ClassifyAll(IReadOnlyList<string> repositoryRelativePaths)
        {
            if (repositoryRelativePaths == null || repositoryRelativePaths.Count == 0)
            {
                return Array.Empty<string>();
            }

            return repositoryRelativePaths
                .Select(Classify)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(scope => scope, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool StartsWith(string value, string prefix)
        {
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EndsWith(string value, string suffix)
        {
            return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
