using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>
    /// 重复资产检查：正式区里同内容的资产出现两份即违规。复用只走引用，
    /// 复制出第二份改是结构规范第四节明令禁止的——预制体要定制差异用 Prefab Variant。
    /// </summary>
    public static class AssetDuplicateChecker
    {
        /// <summary>扫描 Assets 根下的正式区资产，按内容哈希找出重复资产，返回全部违规。</summary>
        /// <param name="assetsRootDirectory">Assets 根目录。</param>
        public static IReadOnlyList<AssetBundleGroupViolation> Check(string assetsRootDirectory)
        {
            if (string.IsNullOrWhiteSpace(assetsRootDirectory)
                || !Directory.Exists(assetsRootDirectory))
            {
                return Array.Empty<AssetBundleGroupViolation>();
            }

            var assetsRoot = Path.GetFullPath(assetsRootDirectory);
            var pathsByHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var filePath in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
            {
                // 只扫正式区（Game/ 打头的文件）；Plugins、Editor 之类区外文件不在此列。
                var relativePath = Path.GetRelativePath(assetsRoot, filePath).Replace('\\', '/');
                if (!relativePath.StartsWith("Game/", StringComparison.Ordinal))
                {
                    continue;
                }

                var fileName = Path.GetFileName(filePath);
                if (fileName.EndsWith(".meta", StringComparison.Ordinal)
                    || string.Equals(fileName, "导入规则.json", StringComparison.Ordinal))
                {
                    continue;
                }

                var bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 0)
                {
                    continue;
                }

                // 每份资产只进哈希桶一次，同一个哈希的记录全部落在同一个组里。
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!pathsByHash.TryGetValue(hash, out var paths))
                {
                    paths = new List<string>();
                    pathsByHash.Add(hash, paths);
                }

                paths.Add(relativePath);
            }

            var violations = new List<AssetBundleGroupViolation>();
            foreach (var group in pathsByHash.Values)
            {
                if (group.Count < 2)
                {
                    continue;
                }

                // 一组只报一条：报排序后第一个路径，其余路径进原因串。
                group.Sort(StringComparer.Ordinal);
                var remainingPaths = group.GetRange(1, group.Count - 1);
                violations.Add(new AssetBundleGroupViolation(
                    group[0],
                    $"与另外 {remainingPaths.Count} 个资产内容完全相同：{string.Join("、", remainingPaths)}",
                    "复用只走引用：删掉多余的那几份改成引用同一个，预制体要定制差异用 Prefab Variant",
                    "Specifications/structure-assets.md 第四节"));
            }

            violations.Sort((left, right) => string.CompareOrdinal(left.AssetPath, right.AssetPath));
            return violations;
        }
    }
}
