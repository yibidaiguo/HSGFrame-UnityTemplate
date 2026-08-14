using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Template.Toolkit.Gates
{
    /// <summary>单文档行数上限检查：超过阈值的文档报一条发现，豁免名单直接跳过。</summary>
    public static class DocumentLengthChecker
    {
        /// <summary>
        /// 检查一组文档的行数是否超过上限。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录，用于把文档路径转成仓库相对路径。</param>
        /// <param name="documentPaths">文档文件路径列表。</param>
        /// <param name="configuration">门禁配置。</param>
        public static IReadOnlyList<GateFinding> Check(
            string repositoryRoot,
            IEnumerable<string> documentPaths,
            GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            var exemptions = configuration.DocumentExemptions ?? Array.Empty<string>();

            foreach (var documentPath in documentPaths)
            {
                if (!File.Exists(documentPath))
                {
                    continue;
                }

                var relative = ToRepositoryRelative(repositoryRoot, documentPath);

                // 豁免项以 / 结尾时按目录前缀豁免整棵子树（旧工程遗留文档整目录豁免用得上）。
                var exempted = exemptions.Any(entry => entry.EndsWith("/", StringComparison.Ordinal)
                    ? relative.StartsWith(entry, StringComparison.Ordinal)
                    : string.Equals(relative, entry, StringComparison.Ordinal));
                if (exempted)
                {
                    continue;
                }

                var lineCount = File.ReadLines(documentPath).Count();
                if (lineCount > configuration.DocumentLineLimit)
                {
                    findings.Add(new GateFinding(
                        relative,
                        $"文档共 {lineCount} 行，超过上限 {configuration.DocumentLineLimit} 行",
                        "拆分文档或精简内容",
                        "Doc/改造方案.md"));
                }
            }

            return findings;
        }

        private static string ToRepositoryRelative(string repositoryRoot, string filePath)
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(filePath));
            return relative.Replace('\\', '/');
        }
    }
}
