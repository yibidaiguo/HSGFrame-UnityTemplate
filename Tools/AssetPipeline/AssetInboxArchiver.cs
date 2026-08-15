using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>收件箱归档器：按路由表把收件箱里的资产分派到正式目录，并按目标目录的规则改名。</summary>
    public static class AssetInboxArchiver
    {
        /// <summary>产出归档计划，不落盘。</summary>
        /// <param name="inboxDirectory">收件箱目录。</param>
        /// <param name="assetsRootDirectory">Assets 根目录，路由表里的目标目录相对它解析。</param>
        /// <param name="routingTable">归档路由表。</param>
        public static IReadOnlyList<AssetArchivePlan> Plan(
            string inboxDirectory, string assetsRootDirectory, AssetRoutingTable routingTable)
        {
            if (!Directory.Exists(inboxDirectory) || routingTable == null)
            {
                return Array.Empty<AssetArchivePlan>();
            }

            var fileNames = new List<string>();
            foreach (var filePath in Directory.EnumerateFiles(inboxDirectory))
            {
                var fileName = Path.GetFileName(filePath);
                if (ShouldSkip(fileName))
                {
                    continue;
                }

                fileNames.Add(fileName);
            }

            // 按源文件名升序，让同一批输入的归档结果稳定可复现。
            fileNames.Sort(string.CompareOrdinal);

            var assetsRoot = Path.GetFullPath(assetsRootDirectory);

            // 每个目标目录维护一份已占用名集合：既有文件与本批已分配的名字都算占用。
            var occupiedNamesByDirectory = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var plans = new List<AssetArchivePlan>();

            foreach (var fileName in fileNames)
            {
                var extension = Path.GetExtension(fileName);
                var targetRelativeDirectory = routingTable.FindTargetDirectory(extension);
                if (targetRelativeDirectory == null)
                {
                    // 路由表里没有这个扩展名：交给 asset.validate 去报，这里安静跳过。
                    continue;
                }

                var targetDirectory = Path.GetFullPath(Path.Combine(assetsRoot, targetRelativeDirectory));

                var rule = AssetImportRuleSet.LoadForDirectory(targetDirectory);
                if (rule == null)
                {
                    throw new AssetRoutingException(
                        $"位置：{targetDirectory}；原因：目标目录及其上级都没有「导入规则.json」；修复：为目标目录补上导入规则.json；参考：归档时按目标目录自己的规则重命名");
                }

                if (!occupiedNamesByDirectory.TryGetValue(targetDirectory, out var occupiedNames))
                {
                    occupiedNames = LoadOccupiedNames(targetDirectory);
                    occupiedNamesByDirectory.Add(targetDirectory, occupiedNames);
                }

                var normalized = AssetNameNormalizer.Normalize(fileName, rule);
                var targetFileName = ResolveCollision(normalized, occupiedNames);
                occupiedNames.Add(targetFileName);

                plans.Add(new AssetArchivePlan(
                    Path.Combine(inboxDirectory, fileName),
                    targetDirectory,
                    targetFileName));
            }

            return plans;
        }

        /// <summary>执行一批归档计划：移动资产并让 .meta 跟着走，返回实际移动的条数。</summary>
        /// <param name="plans">要执行的归档计划。</param>
        public static int Apply(IReadOnlyList<AssetArchivePlan> plans)
        {
            var movedCount = 0;
            foreach (var plan in plans)
            {
                if (!Directory.Exists(plan.TargetDirectory))
                {
                    Directory.CreateDirectory(plan.TargetDirectory);
                }

                if (File.Exists(plan.TargetPath))
                {
                    throw new IOException(
                        $"位置：{plan.TargetPath}；原因：目标路径已被占用；修复：先处理同名文件或改用别的名字；参考：归档不得覆盖既有资产");
                }

                File.Move(plan.SourcePath, plan.TargetPath);

                // .meta 跟着资产一起搬，否则 Unity 下次打开会当成新资产重新分配 guid，所有引用全部断掉。
                var metaPath = plan.SourcePath + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Move(metaPath, plan.TargetPath + ".meta");
                }

                movedCount++;
            }

            return movedCount;
        }

        private static bool ShouldSkip(string fileName)
        {
            return fileName.EndsWith(".meta", StringComparison.Ordinal)
                || string.Equals(fileName, "导入规则.json", StringComparison.Ordinal)
                || string.Equals(fileName, "归档路由.json", StringComparison.Ordinal);
        }

        private static HashSet<string> LoadOccupiedNames(string targetDirectory)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (!Directory.Exists(targetDirectory))
            {
                return names;
            }

            foreach (var filePath in Directory.EnumerateFiles(targetDirectory))
            {
                names.Add(Path.GetFileName(filePath));
            }

            return names;
        }

        // 撞名去重：与 AssetNameNormalizer.PlanDirectory 同款手法——归一后撞上已占用名时，
        // 在主干后缀追加 _2、_3，直到不撞。两处行为保持一致。
        private static string ResolveCollision(string normalized, HashSet<string> occupiedNames)
        {
            if (!occupiedNames.Contains(normalized))
            {
                return normalized;
            }

            var extension = Path.GetExtension(normalized);
            var stem = extension.Length == 0
                ? normalized
                : normalized.Substring(0, normalized.Length - extension.Length);

            var counter = 2;
            while (true)
            {
                var candidate = stem + "_" + counter + extension;
                if (!occupiedNames.Contains(candidate))
                {
                    return candidate;
                }

                counter++;
            }
        }
    }
}
