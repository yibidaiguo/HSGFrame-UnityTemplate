using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>
    /// 常驻预算检查：加载分组为「常驻」的目录合计不得超过 gate-config.json 的 residentBudgetBytes。
    /// 预算由调用方传入；单组超预算与全部常驻组合计超预算各报一条。为第一个常驻分组进来那天准备的检查。
    /// </summary>
    public static class AssetResidentBudgetChecker
    {
        private const string ResidentLoadGroup = "常驻";
        private const string MetaExtension = ".meta";
        private const string ImportRuleFileName = "import-rules.json";
        private const string TotalAssetPath = "Game/";

        /// <summary>检查常驻分组的字节总和是否超过预算，返回全部违规。</summary>
        /// <param name="assetsRootDirectory">Assets 根目录。</param>
        /// <param name="ruleSet">打包分组规则，加载分组写在它的分组条目上。</param>
        /// <param name="residentBudgetBytes">全部常驻分组的字节预算。</param>
        public static IReadOnlyList<AssetBundleGroupViolation> Check(
            string assetsRootDirectory,
            AssetBundleGroupRuleSet ruleSet,
            long residentBudgetBytes)
        {
            if (ruleSet == null
                || ruleSet.Groups == null
                || ruleSet.Groups.Count == 0
                || string.IsNullOrWhiteSpace(assetsRootDirectory)
                || !Directory.Exists(assetsRootDirectory)
                || residentBudgetBytes <= 0)
            {
                return Array.Empty<AssetBundleGroupViolation>();
            }

            var residentGroups = new List<AssetBundleGroupDefinition>();
            foreach (var group in ruleSet.Groups)
            {
                if (group == null || group.PathPrefix == null)
                {
                    continue;
                }

                if (string.Equals(group.LoadGroup, ResidentLoadGroup, StringComparison.Ordinal))
                {
                    residentGroups.Add(group);
                }
            }

            if (residentGroups.Count == 0)
            {
                return Array.Empty<AssetBundleGroupViolation>();
            }

            var assetsRoot = Path.GetFullPath(assetsRootDirectory);
            var violations = new List<AssetBundleGroupViolation>();
            long totalBytes = 0;
            foreach (var group in residentGroups)
            {
                var bytes = CountGroupBytes(assetsRoot, group.PathPrefix);
                totalBytes += bytes;
                if (bytes <= residentBudgetBytes)
                {
                    continue;
                }

                violations.Add(BuildOverBudgetViolation(
                    group.PathPrefix,
                    $"常驻分组「{group.GroupName}」合计 {bytes} 字节，超过预算 {residentBudgetBytes} 字节"));
            }

            if (totalBytes > residentBudgetBytes)
            {
                violations.Add(BuildOverBudgetViolation(
                    TotalAssetPath,
                    $"全部常驻分组合计 {totalBytes} 字节，超过预算 {residentBudgetBytes} 字节"));
            }

            violations.Sort((left, right) => StringComparer.Ordinal.Compare(left.AssetPath, right.AssetPath));
            return violations;
        }

        // 算一个分组前缀下全部文件的字节总和；.meta 与「import-rules.json」是管线配置、不进包，不计入。
        private static long CountGroupBytes(string assetsRoot, string pathPrefix)
        {
            var normalizedPrefix = pathPrefix.Replace('\\', '/');
            long totalBytes = 0;
            foreach (var filePath in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(assetsRoot, filePath).Replace('\\', '/');
                if (!relativePath.StartsWith(normalizedPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(Path.GetExtension(relativePath), MetaExtension, StringComparison.Ordinal)
                    || string.Equals(Path.GetFileName(relativePath), ImportRuleFileName, StringComparison.Ordinal))
                {
                    continue;
                }

                totalBytes += new FileInfo(filePath).Length;
            }

            return totalBytes;
        }

        private static AssetBundleGroupViolation BuildOverBudgetViolation(string assetPath, string reason)
        {
            return new AssetBundleGroupViolation(
                assetPath,
                reason,
                "把不必全程常驻的内容改成按需分组，或调高 gate-config.json 的 residentBudgetBytes",
                "Specifications/structure-assets.md 第三节");
        }
    }
}
