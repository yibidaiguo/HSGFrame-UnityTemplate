using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>
    /// 加载分组校验：动态收集根下的分组必须写「加载分组」且取值合法，预制体必须住 ResourceArt 树。
    /// 打包分组检查器管「谁和谁打进同一个包」，这一条管「这个包什么时候加载、什么时候释放」——
    /// 同一个包里一半常驻一半按需，正是分包要避免的事。
    /// </summary>
    public static class AssetLoadGroupChecker
    {
        // 动态收集根：只有这两处底下的分组进 YooAsset 收集面，也只有它们要写加载分组。
        private static readonly string[] DynamicCollectRoots = { "Game/ResourceArt/", "Game/Scenes/World/" };

        // 加载分组的三个合法取值，见《结构规范-资源》第三节。
        private static readonly string[] AllowedLoadGroups = { "常驻", "按需", "随场景" };

        private const string FormalAreaPrefix = "Game/";
        private const string ResourceArtPrefix = "Game/ResourceArt/";

        /// <summary>检查分组的加载分组字段与预制体落点，返回全部违规。</summary>
        /// <param name="assetsRootDirectory">Assets 根目录。</param>
        /// <param name="ruleSet">打包分组规则，加载分组字段写在它的分组条目上。</param>
        public static IReadOnlyList<AssetBundleGroupViolation> Check(
            string assetsRootDirectory,
            AssetBundleGroupRuleSet ruleSet)
        {
            if (ruleSet == null
                || ruleSet.Groups == null
                || ruleSet.Groups.Count == 0
                || string.IsNullOrWhiteSpace(assetsRootDirectory)
                || !Directory.Exists(assetsRootDirectory))
            {
                return Array.Empty<AssetBundleGroupViolation>();
            }

            var violations = new List<AssetBundleGroupViolation>();
            CheckLoadGroupFields(ruleSet, violations);
            CheckPrefabLocations(assetsRootDirectory, violations);

            violations.Sort((left, right) => string.CompareOrdinal(left.AssetPath, right.AssetPath));
            return violations;
        }

        // 子检查一：落在动态收集根下的分组，必须写一个合法的加载分组。
        private static void CheckLoadGroupFields(
            AssetBundleGroupRuleSet ruleSet,
            List<AssetBundleGroupViolation> violations)
        {
            foreach (var group in ruleSet.Groups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.PathPrefix))
                {
                    continue;
                }

                var pathPrefix = group.PathPrefix.Replace('\\', '/');
                if (!IsUnderDynamicCollectRoot(pathPrefix))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(group.LoadGroup))
                {
                    violations.Add(new AssetBundleGroupViolation(
                        group.PathPrefix,
                        $"分组「{group.GroupName}」落在动态收集根下，却没写「加载分组」",
                        "在这个分组条目上补「加载分组」，取值 常驻 / 按需 / 随场景 三选一",
                        "Tools/AssetPipeline/Config/打包分组规则.json"));
                    continue;
                }

                if (!IsAllowedLoadGroup(group.LoadGroup))
                {
                    violations.Add(new AssetBundleGroupViolation(
                        group.PathPrefix,
                        $"分组「{group.GroupName}」的「加载分组」写成了「{group.LoadGroup}」，不是三个合法值之一",
                        "改成 常驻 / 按需 / 随场景 三选一",
                        "Tools/AssetPipeline/Config/打包分组规则.json"));
                }
            }
        }

        // 子检查二：正式区里的预制体只准住 ResourceArt 树。
        // 第三方自留地（Plugins/ 等）里的预制体各自工具管理，不归这条规矩管。
        private static void CheckPrefabLocations(
            string assetsRootDirectory,
            List<AssetBundleGroupViolation> violations)
        {
            var assetsRoot = Path.GetFullPath(assetsRootDirectory);
            foreach (var prefabPath in Directory.EnumerateFiles(assetsRoot, "*.prefab", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(assetsRoot, prefabPath).Replace('\\', '/');
                if (!relativePath.StartsWith(FormalAreaPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (relativePath.StartsWith(ResourceArtPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                violations.Add(new AssetBundleGroupViolation(
                    relativePath,
                    "预制体不在 ResourceArt 树里",
                    "把它连同 .meta 一起挪进 Game/ResourceArt/<功能>/，Art/ 只放被引用的源生资产",
                    "规范/结构规范-资源.md 第二节"));
            }
        }

        private static bool IsUnderDynamicCollectRoot(string pathPrefix)
        {
            foreach (var dynamicRoot in DynamicCollectRoots)
            {
                if (pathPrefix.StartsWith(dynamicRoot, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAllowedLoadGroup(string loadGroup)
        {
            foreach (var allowed in AllowedLoadGroups)
            {
                if (string.Equals(loadGroup, allowed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
