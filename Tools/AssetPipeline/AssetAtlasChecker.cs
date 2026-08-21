using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>
    /// 图集对齐校验：声明了「图集」的贴图目录，图集资产必须存在且真收录了它——
    /// 图集名要 SA_ 打头、图集资产要在 Game/Settings/Atlas/ 下、图集文件里要能查到目录的 guid。
    /// </summary>
    public static class AssetAtlasChecker
    {
        /// <summary>检查扫描根下声明了图集的目录是否与图集资产对齐，返回全部违规。</summary>
        /// <param name="assetsRootDirectory">Assets 根目录。</param>
        public static IReadOnlyList<AssetBundleGroupViolation> Check(string assetsRootDirectory)
        {
            if (string.IsNullOrWhiteSpace(assetsRootDirectory) || !Directory.Exists(assetsRootDirectory))
            {
                return Array.Empty<AssetBundleGroupViolation>();
            }

            var assetsRoot = Path.GetFullPath(assetsRootDirectory);
            var violations = new List<AssetBundleGroupViolation>();

            foreach (var ruleFilePath in Directory.EnumerateFiles(assetsRoot, "导入规则.json", SearchOption.AllDirectories))
            {
                var ruleRelativePath = Path.GetRelativePath(assetsRoot, ruleFilePath).Replace('\\', '/');
                if (!ruleRelativePath.StartsWith("Game/", StringComparison.Ordinal))
                {
                    continue;
                }

                var rule = AssetImportRule.LoadFromFile(ruleFilePath);
                if (string.IsNullOrWhiteSpace(rule.Atlas))
                {
                    continue;
                }

                var directory = Path.GetDirectoryName(ruleRelativePath).Replace('\\', '/');
                var atlasName = rule.Atlas.Trim();

                // 子检查一 · 前缀：图集名必须 SA_ 打头，否则整条规则不再往下查。
                if (!atlasName.StartsWith("SA_", StringComparison.Ordinal))
                {
                    violations.Add(new AssetBundleGroupViolation(
                        directory + "/导入规则.json",
                        $"图集名「{atlasName}」不是 SA_ 前缀",
                        "图集资产按前缀表用 SA_ 打头",
                        "Specifications/structure-assets.md 第五节"));
                    continue;
                }

                // 子检查二 · 图集资产存在：spriteatlas 与 spriteatlasv2 两个候选，命中任一即可。
                var existingAtlasPath = FindExistingAtlas(assetsRoot, atlasName);
                if (existingAtlasPath == null)
                {
                    violations.Add(new AssetBundleGroupViolation(
                        $"Game/Settings/Atlas/{atlasName}.spriteatlas",
                        $"目录「{directory}」声明了图集「{atlasName}」，但这张图集不存在",
                        "在 Game/Settings/Atlas/ 下建这张图集，或把导入规则里的「图集」字段清掉",
                        "Specifications/structure-assets.md 第八节"));
                    continue;
                }

                // 子检查三 · 图集真收录了这个目录：读目录 .meta 的 guid，再在图集文件文本里找。
                // .meta 缺失或读不出 guid 就跳过——缺 .meta 归 gate.meta 管，这里不重复报。
                var directoryGuid = ReadDirectoryGuid(assetsRoot, directory);
                if (directoryGuid == null)
                {
                    continue;
                }

                var atlasText = File.ReadAllText(existingAtlasPath);
                if (!atlasText.Contains(directoryGuid, StringComparison.Ordinal))
                {
                    violations.Add(new AssetBundleGroupViolation(
                        directory,
                        $"图集「{atlasName}」没有收录目录「{directory}」",
                        "在图集的 Objects for Packing 里把这个目录加进去",
                        "Specifications/structure-assets.md 第三节"));
                }
            }

            violations.Sort((left, right) => string.CompareOrdinal(left.AssetPath, right.AssetPath));
            return violations;
        }

        // 两个候选图集文件名，命中任一即认为图集资产存在。
        private static string FindExistingAtlas(string assetsRoot, string atlasName)
        {
            var atlasDirectory = Path.Combine(assetsRoot, "Game", "Settings", "Atlas");
            var versionOnePath = Path.Combine(atlasDirectory, atlasName + ".spriteatlas");
            if (File.Exists(versionOnePath))
            {
                return versionOnePath;
            }

            var versionTwoPath = Path.Combine(atlasDirectory, atlasName + ".spriteatlasv2");
            if (File.Exists(versionTwoPath))
            {
                return versionTwoPath;
            }

            return null;
        }

        // 从目录的 .meta 里读 guid: 那一行的值；读不到返回 null。
        private static string ReadDirectoryGuid(string assetsRoot, string directory)
        {
            var metaPath = Path.Combine(assetsRoot, directory + ".meta");
            if (!File.Exists(metaPath))
            {
                return null;
            }

            foreach (var line in File.ReadLines(metaPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("guid:", StringComparison.Ordinal))
                {
                    var value = trimmed.Substring("guid:".Length).Trim();
                    return value.Length == 0 ? null : value;
                }
            }

            return null;
        }
    }
}
