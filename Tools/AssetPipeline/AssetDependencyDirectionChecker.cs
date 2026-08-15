using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>按目录前缀规则检查资产引用方向：命中规则的引用对被逐条报成违规。</summary>
    public static class AssetDependencyDirectionChecker
    {
        /// <summary>检查 Assets 根下全部引用边，返回命中规则的违规列表。</summary>
        /// <param name="assetsRootDirectory">Assets 根目录。</param>
        /// <param name="rules">依赖方向规则；为 null 或空集合时跳过扫描直接返回空列表。</param>
        /// <param name="scannedExtensions">要当作引用方读取的文本资产扩展名，为空时用默认集合。</param>
        public static IReadOnlyList<AssetDependencyViolation> Check(
            string assetsRootDirectory,
            IReadOnlyList<AssetDependencyRule> rules,
            IReadOnlyList<string> scannedExtensions = null)
        {
            var violations = new List<AssetDependencyViolation>();
            if (rules == null || rules.Count == 0)
            {
                return violations;
            }

            if (!Directory.Exists(assetsRootDirectory))
            {
                return violations;
            }

            var edges = AssetReferenceScanner.ScanReferenceEdges(assetsRootDirectory, scannedExtensions);
            foreach (var edge in edges)
            {
                foreach (var referencedPath in edge.Value)
                {
                    foreach (var rule in rules)
                    {
                        if (MatchesPrefix(edge.Key, rule.FromPathPrefix)
                            && MatchesPrefix(referencedPath, rule.ForbiddenPathPrefix))
                        {
                            violations.Add(new AssetDependencyViolation(edge.Key, referencedPath, rule));
                        }
                    }
                }
            }

            violations.Sort(CompareViolations);
            return violations;
        }

        // 空前缀视为「匹配所有路径」；非空前缀按忽略大小写比较开头。
        private static bool MatchesPrefix(string path, string prefix)
        {
            return prefix.Length == 0
                || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareViolations(AssetDependencyViolation left, AssetDependencyViolation right)
        {
            var byReferencer = string.CompareOrdinal(left.ReferencingAssetPath, right.ReferencingAssetPath);
            return byReferencer != 0
                ? byReferencer
                : string.CompareOrdinal(left.ReferencedAssetPath, right.ReferencedAssetPath);
        }
    }
}
