using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>
    /// 加载分组校验：动态收集根下的分组必须写「加载分组」且取值合法，预制体必须住 ResourceArt 树，
    /// 并且这些分组条目要与 YooAsset 的收集器 group 一一对上。
    /// 打包分组检查器管「谁和谁打进同一个包」，这一条管「这个包什么时候加载、什么时候释放」——
    /// 同一个包里一半常驻一半按需，正是分包要避免的事；第三条子检查再管「规则里写的和真收进包的是不是同一批」。
    /// </summary>
    public static class AssetLoadGroupChecker
    {
        // 动态收集根：只有这两处底下的分组进 YooAsset 收集面，也只有它们要写加载分组。
        private static readonly string[] DynamicCollectRoots = { "Game/ResourceArt/", "Game/Scenes/World/" };

        // 加载分组的三个合法取值，见《结构规范-资源》第三节。
        private static readonly string[] AllowedLoadGroups = { "常驻", "按需", "随场景" };

        private const string FormalAreaPrefix = "Game/";
        private const string ResourceArtPrefix = "Game/ResourceArt/";

        /// <summary>检查分组的加载分组字段、预制体落点，以及收集器 group 与分组条目的对账，返回全部违规。</summary>
        /// <param name="assetsRootDirectory">Assets 根目录。</param>
        /// <param name="ruleSet">打包分组规则，加载分组字段写在它的分组条目上。</param>
        /// <param name="collectorSettingPath">YooAsset 收集器配置路径；为空或文件不在时跳过对账那一条。</param>
        public static IReadOnlyList<AssetBundleGroupViolation> Check(
            string assetsRootDirectory,
            AssetBundleGroupRuleSet ruleSet,
            string collectorSettingPath = null)
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
            CheckCollectorReconciliation(ruleSet, collectorSettingPath, violations);

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

        // 子检查三：写了加载分组的分组条目，与 YooAsset 收集器里的 group 必须一一对上。
        // 两边分开维护就会漂：规则里说「这个目录按需加载」，收集器里却根本没收它，
        // 出包时那批资产悄悄不进包，症状要到运行期寻址失败才现形。
        private static void CheckCollectorReconciliation(
            AssetBundleGroupRuleSet ruleSet,
            string collectorSettingPath,
            List<AssetBundleGroupViolation> violations)
        {
            if (string.IsNullOrWhiteSpace(collectorSettingPath) || !File.Exists(collectorSettingPath))
            {
                return;
            }

            var expectedPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            var expectedLoadGroups = new Dictionary<string, string>(StringComparer.Ordinal);
            var expectedPrefixes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in ruleSet.Groups)
            {
                if (group == null
                    || string.IsNullOrWhiteSpace(group.GroupName)
                    || string.IsNullOrWhiteSpace(group.PathPrefix)
                    || string.IsNullOrWhiteSpace(group.LoadGroup))
                {
                    continue;
                }

                expectedPaths[group.GroupName] = "Assets/" + group.PathPrefix.Replace('\\', '/').TrimEnd('/');
                expectedLoadGroups[group.GroupName] = group.LoadGroup;
                expectedPrefixes[group.GroupName] = group.PathPrefix;
            }

            var collectorGroups = BundleCollectorSettingReader.Read(collectorSettingPath);
            var actualNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var collectorGroup in collectorGroups)
            {
                actualNames.Add(collectorGroup.GroupName);
                if (!expectedPaths.TryGetValue(collectorGroup.GroupName, out var expectedPath))
                {
                    violations.Add(new AssetBundleGroupViolation(
                        collectorGroup.CollectPaths.Count > 0 ? collectorGroup.CollectPaths[0] : collectorGroup.GroupName,
                        $"收集器 group「{collectorGroup.GroupName}」在打包分组规则里找不到同名的分组条目",
                        "给它在打包分组规则里补一条带「加载分组」的条目，或从收集器里删掉这个 group",
                        "Tools/AssetPipeline/Config/打包分组规则.json"));
                    continue;
                }

                if (collectorGroup.CollectPaths.Count != 1)
                {
                    violations.Add(new AssetBundleGroupViolation(
                        expectedPath,
                        $"收集器 group「{collectorGroup.GroupName}」下有 {collectorGroup.CollectPaths.Count} 条收集路径，无法与分组条目一一对账",
                        "一个 group 只配一条收集路径，多个目录就拆成多个 group",
                        "UnityProject/Assets/Game/Settings/Resource/BundleCollectorSetting.asset"));
                    continue;
                }

                var actualPath = collectorGroup.CollectPaths[0].Replace('\\', '/').TrimEnd('/');
                if (!string.Equals(actualPath, expectedPath, StringComparison.Ordinal))
                {
                    violations.Add(new AssetBundleGroupViolation(
                        expectedPath,
                        $"收集器 group「{collectorGroup.GroupName}」的收集路径是「{actualPath}」，与分组条目算出来的「{expectedPath}」对不上",
                        "两边改成一致：收集路径 = Assets/ + 分组条目的路径前缀",
                        "Tools/AssetPipeline/Config/打包分组规则.json"));
                }
            }

            foreach (var pair in expectedPaths)
            {
                if (actualNames.Contains(pair.Key))
                {
                    continue;
                }

                violations.Add(new AssetBundleGroupViolation(
                    expectedPrefixes[pair.Key],
                    $"分组「{pair.Key}」写了加载分组「{expectedLoadGroups[pair.Key]}」，YooAsset 收集器里却没有同名 group",
                    "跑一次「工具链/资源/配置资源采集规则」重建收集器配置，或把这个分组条目删掉",
                    "UnityProject/Assets/Game/Settings/Resource/BundleCollectorSetting.asset"));
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
