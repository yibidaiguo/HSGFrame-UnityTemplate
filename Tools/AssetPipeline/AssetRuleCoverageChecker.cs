using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>
    /// 导入规则覆盖校验：放了资产的目录必须能解析到一份「导入规则.json」——
    /// 自己有，或者从最近的祖先继承。解析不到就等于那里的命名、扩展名与大小没人管。
    /// </summary>
    public static class AssetRuleCoverageChecker
    {
        /// <summary>检查扫描根下每个放了资产的目录是否被导入规则覆盖，返回全部违规。</summary>
        /// <param name="assetsRootDirectory">Assets 根目录。</param>
        /// <param name="settings">覆盖范围配置。</param>
        public static IReadOnlyList<AssetBundleGroupViolation> Check(
            string assetsRootDirectory,
            AssetRuleCoverageSettings settings)
        {
            if (settings == null
                || settings.ScanRoots == null
                || settings.ScanRoots.Count == 0
                || string.IsNullOrWhiteSpace(assetsRootDirectory)
                || !Directory.Exists(assetsRootDirectory))
            {
                return Array.Empty<AssetBundleGroupViolation>();
            }

            var assetsRoot = Path.GetFullPath(assetsRootDirectory);
            var violations = new List<AssetBundleGroupViolation>();

            foreach (var scanRoot in settings.ScanRoots)
            {
                // 配置里写了将来才有的目录不算错：目录不存在直接跳过。
                if (string.IsNullOrWhiteSpace(scanRoot))
                {
                    continue;
                }

                var scanRootDirectory = Path.Combine(assetsRoot, scanRoot);
                if (!Directory.Exists(scanRootDirectory))
                {
                    continue;
                }

                CheckDirectoryTree(scanRootDirectory, assetsRoot, settings, violations);
            }

            violations.Sort((left, right) => string.CompareOrdinal(left.AssetPath, right.AssetPath));
            return violations;
        }

        private static void CheckDirectoryTree(
            string directory,
            string assetsRoot,
            AssetRuleCoverageSettings settings,
            List<AssetBundleGroupViolation> violations)
        {
            var relativePath = Path.GetRelativePath(assetsRoot, directory).Replace('\\', '/');
            if (!IsExempt(relativePath, settings.ExemptDirectories))
            {
                var assetCount = CountDirectAssets(directory);
                if (assetCount > 0 && AssetImportRuleSet.LoadForDirectory(directory, assetsRoot) == null)
                {
                    violations.Add(new AssetBundleGroupViolation(
                        relativePath,
                        $"目录里有 {assetCount} 个资产，却解析不到「导入规则.json」（自己没有，向上也继承不到）",
                        "在这个目录或它的某级父目录放一份「导入规则.json」，或把它加进规则覆盖范围的豁免目录",
                        "Tools/AssetPipeline/Config/rule-coverage.json"));
                }
            }

            foreach (var subdirectory in Directory.EnumerateDirectories(directory))
            {
                CheckDirectoryTree(subdirectory, assetsRoot, settings, violations);
            }
        }

        // 直接资产 = 不是 .meta、也不是管线自己的配置文件（导入规则/归档路由）。
        // 这两种文件不参与资产计数：管线配置不算资产，.meta 是资产的附属不是资产。
        private static int CountDirectAssets(string directory)
        {
            var count = 0;
            foreach (var filePath in Directory.EnumerateFiles(directory))
            {
                var fileName = Path.GetFileName(filePath);
                if (!fileName.EndsWith(".meta", StringComparison.Ordinal)
                    && !AssetNameNormalizer.IsPipelineConfigurationFile(fileName))
                {
                    count++;
                }
            }

            return count;
        }

        // 豁免按路径段对齐比：「Art/Prefab」放行「Art/Prefab/子夹」，但绝不放行「Art/PrefabBackup」——
        // 裸前缀比会把同形的兄弟目录一起放行，豁免就失效了。
        private static bool IsExempt(string relativePath, IReadOnlyList<string> exemptDirectories)
        {
            if (exemptDirectories == null || exemptDirectories.Count == 0)
            {
                return false;
            }

            var pathSegments = SplitPathSegments(relativePath);
            foreach (var exemptDirectory in exemptDirectories)
            {
                if (string.IsNullOrWhiteSpace(exemptDirectory))
                {
                    continue;
                }

                var exemptSegments = SplitPathSegments(exemptDirectory);
                if (MatchesSegmentPrefix(pathSegments, exemptSegments))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] SplitPathSegments(string path)
        {
            return path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool MatchesSegmentPrefix(string[] pathSegments, string[] exemptSegments)
        {
            if (exemptSegments.Length > pathSegments.Length)
            {
                return false;
            }

            for (var index = 0; index < exemptSegments.Length; index++)
            {
                if (!string.Equals(pathSegments[index], exemptSegments[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
