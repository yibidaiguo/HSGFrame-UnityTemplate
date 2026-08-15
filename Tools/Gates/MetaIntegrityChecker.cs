using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Template.Toolkit.Gates
{
    /// <summary>.meta 完整性检查器：每个资产都要有同名 .meta，每条 .meta 也要有对应资产。</summary>
    public static class MetaIntegrityChecker
    {
        private const string ReferenceExamplePath = "Tools/Gates/gate-unity.ps1";

        /// <summary>
        /// 遍历资产根目录，报出缺失的 .meta 与找不到宿主的孤儿 .meta。
        /// </summary>
        /// <param name="assetsRootDirectory">UnityProject/Assets 的路径。</param>
        /// <param name="configuration">门禁配置。</param>
        public static IReadOnlyList<GateFinding> Check(string assetsRootDirectory, GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();

            // 门禁在新生成、还没被 Unity 打开过的工程上也要能跑完，根目录不存在只报一条而不是抛异常。
            if (!Directory.Exists(assetsRootDirectory))
            {
                findings.Add(new GateFinding(
                    assetsRootDirectory,
                    "资产根目录不存在",
                    "确认传入的是 UnityProject/Assets 的路径",
                    ReferenceExamplePath));
                return findings;
            }

            var skipSegments = (configuration.SourceScanSkipSegments ?? Array.Empty<string>()).ToArray();

            foreach (var itemPath in Directory.EnumerateFileSystemEntries(assetsRootDirectory, "*", SearchOption.AllDirectories))
            {
                if (ShouldSkip(itemPath, assetsRootDirectory, skipSegments))
                {
                    continue;
                }

                var relative = ToRelative(assetsRootDirectory, itemPath);

                if (itemPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    var ownerPath = itemPath.Substring(0, itemPath.Length - ".meta".Length);
                    if (!File.Exists(ownerPath) && !Directory.Exists(ownerPath))
                    {
                        findings.Add(new GateFinding(
                            relative,
                            ".meta 对应的资产已不存在",
                            "删掉这个 .meta，或把资产放回原位",
                            ReferenceExamplePath));
                    }
                }
                else if (!File.Exists(itemPath + ".meta"))
                {
                    findings.Add(new GateFinding(
                        relative,
                        "缺少 .meta 文件",
                        "用 Unity 打开一次工程让它生成，或补上同名 .meta",
                        ReferenceExamplePath));
                }
            }

            return findings;
        }

        private static bool ShouldSkip(string itemPath, string assetsRootDirectory, string[] skipSegments)
        {
            if (string.Equals(Path.GetFileName(itemPath), ".DS_Store", StringComparison.Ordinal))
            {
                return true;
            }

            return ContainsAnySegment(ToRelative(assetsRootDirectory, itemPath), skipSegments);
        }

        private static string ToRelative(string assetsRootDirectory, string itemPath)
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(assetsRootDirectory), Path.GetFullPath(itemPath));
            return relative.Replace('\\', '/');
        }

        private static bool ContainsAnySegment(string relative, string[] segments)
        {
            var normalized = relative.Replace('\\', '/').Split('/');
            return normalized.Any(segment => segments.Contains(segment, StringComparer.OrdinalIgnoreCase));
        }
    }
}
