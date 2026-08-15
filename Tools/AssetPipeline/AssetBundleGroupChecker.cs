using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>
    /// 打包分组校验：判「谁该和谁打进同一个包」。
    /// 依赖方向检查器管的是「谁不许引用谁」，这一条管的是分组本身合不合理。
    /// </summary>
    public static class AssetBundleGroupChecker
    {
        /// <summary>按分组规则检查 Assets 根下的资产分组，返回全部违规。</summary>
        /// <param name="assetsRootDirectory">Assets 根目录。</param>
        /// <param name="ruleSet">分组规则。</param>
        public static IReadOnlyList<AssetBundleGroupViolation> Check(
            string assetsRootDirectory,
            AssetBundleGroupRuleSet ruleSet)
        {
            if (ruleSet == null || ruleSet.Groups == null || ruleSet.Groups.Count == 0)
            {
                return Array.Empty<AssetBundleGroupViolation>();
            }

            if (!Directory.Exists(assetsRootDirectory))
            {
                return Array.Empty<AssetBundleGroupViolation>();
            }

            var edges = AssetReferenceScanner.ScanReferenceEdges(assetsRootDirectory);

            // 反向表：被引用资产路径 → 引用它的资产所属的非共享分组名集合。
            // 引用方自己没分组时算不出跨组，跳过记账；引用方在共享组里时，它引用别人
            // 不构成「被多个业务包各复制一份」，也跳过。
            var groupNamesByReferenced = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var edge in edges)
            {
                var referencerGroup = ruleSet.FindGroup(edge.Key);
                if (referencerGroup == null || referencerGroup.IsShared)
                {
                    continue;
                }

                foreach (var referencedPath in edge.Value)
                {
                    if (!groupNamesByReferenced.TryGetValue(referencedPath, out var groupNames))
                    {
                        groupNames = new HashSet<string>(StringComparer.Ordinal);
                        groupNamesByReferenced.Add(referencedPath, groupNames);
                    }

                    groupNames.Add(referencerGroup.GroupName);
                }
            }

            // 共享资产未落共享组：被两个以上分组引用的资产，如果自己不在共享组里，
            // 会在每个包里各复制一份——包体白白膨胀，同一份资源还成了两个不同实例。
            var violationsByAsset = new Dictionary<string, AssetBundleGroupViolation>(StringComparer.Ordinal);
            foreach (var pair in groupNamesByReferenced)
            {
                if (pair.Value.Count < 2)
                {
                    continue;
                }

                var assetGroup = ruleSet.FindGroup(pair.Key);
                if (assetGroup != null && assetGroup.IsShared)
                {
                    continue;
                }

                var sortedGroupNames = pair.Value.ToList();
                sortedGroupNames.Sort(StringComparer.Ordinal);
                violationsByAsset.Add(pair.Key, new AssetBundleGroupViolation(
                    pair.Key,
                    $"被 {pair.Value.Count} 个打包分组引用（{string.Join("、", sortedGroupNames)}），自己却不在共享组里",
                    "把它移进共享组的目录，或在打包分组规则里给它所在目录加一个「是共享组」为 true 的分组",
                    "Tools/AssetPipeline/Config/打包分组规则.json"));
            }

            // 未分组资产：开关打开时，引用图里出现过的每个资产（引用方与被引用方都算）
            // 不落在任何分组前缀下就报一条。共享资产那条已经入账的优先，未分组不覆盖它，
            // 一个资产最多一条违规，避免两条互相矛盾的修复建议同时出现。
            if (ruleSet.ReportUngroupedAssets)
            {
                foreach (var edge in edges)
                {
                    TryAddUngroupedViolation(violationsByAsset, ruleSet, edge.Key);
                    foreach (var referencedPath in edge.Value)
                    {
                        TryAddUngroupedViolation(violationsByAsset, ruleSet, referencedPath);
                    }
                }
            }

            var violations = violationsByAsset.Values.ToList();
            violations.Sort(CompareViolations);
            return violations;
        }

        private static void TryAddUngroupedViolation(
            Dictionary<string, AssetBundleGroupViolation> violationsByAsset,
            AssetBundleGroupRuleSet ruleSet,
            string assetPath)
        {
            if (ruleSet.FindGroup(assetPath) != null || violationsByAsset.ContainsKey(assetPath))
            {
                return;
            }

            violationsByAsset.Add(assetPath, new AssetBundleGroupViolation(
                assetPath,
                "不落在任何打包分组里",
                "在打包分组规则里给它所在目录补一个分组，或把它移进已有分组的目录",
                "Tools/AssetPipeline/Config/打包分组规则.json"));
        }

        private static int CompareViolations(AssetBundleGroupViolation left, AssetBundleGroupViolation right)
        {
            return string.CompareOrdinal(left.AssetPath, right.AssetPath);
        }
    }
}
