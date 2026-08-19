using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.Gates
{
    /// <summary>
    /// 下游边界检查：引擎与管线层代码里不许出现下游 driver 名——driver 名只能是运行时参数。
    /// 本文件不引用任何管线类型，driver 名自己从 Bridges 下的 driver.json 里抠。
    /// </summary>
    public static class BridgeBoundaryChecker
    {
        /// <summary>
        /// 从 <c>Bridges/&lt;名&gt;/driver.json</c> 收集全部 driver 名：读顶层「名称」字段的字符串值，
        /// 读不到或不是字符串时退化成目录名。结果按序数序排序、去重；Bridges/ 不存在返回空列表。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static IReadOnlyList<string> ReadDriverNames(string repositoryRoot)
        {
            var bridgesDirectory = Path.Combine(repositoryRoot, "Bridges");
            if (!Directory.Exists(bridgesDirectory))
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            foreach (var driverDirectory in Directory.EnumerateDirectories(bridgesDirectory))
            {
                var driverFile = Path.Combine(driverDirectory, "driver.json");
                if (!File.Exists(driverFile))
                {
                    continue;
                }

                var directoryName = Path.GetFileName(driverDirectory);
                names.Add(ReadNameOrDefault(driverFile, directoryName));
            }

            names.Sort(StringComparer.Ordinal);
            return names.Distinct(StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// 检查一组扫描根下的 <c>*.cs</c> 是否出现 driver 名；同一文件同一 driver 名命中多行逐行各报一条。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录，用于把文件路径转成仓库相对路径。</param>
        /// <param name="scanRootDirectories">相对仓库根的扫描根目录列表。</param>
        /// <param name="configuration">门禁配置。</param>
        public static IReadOnlyList<GateFinding> Check(
            string repositoryRoot,
            IReadOnlyList<string> scanRootDirectories,
            GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            var driverNames = ReadDriverNames(repositoryRoot);
            if (driverNames.Count == 0)
            {
                return findings;
            }

            var exemptions = configuration.BridgeBoundaryExemptions ?? Array.Empty<string>();

            foreach (var scanRoot in scanRootDirectories ?? Array.Empty<string>())
            {
                var scanRootFullPath = Path.Combine(repositoryRoot, scanRoot);
                if (!Directory.Exists(scanRootFullPath))
                {
                    continue;
                }

                foreach (var filePath in Directory.EnumerateFiles(scanRootFullPath, "*.cs", SearchOption.AllDirectories))
                {
                    var relativePath = ToRepositoryRelative(repositoryRoot, filePath);
                    if (ShouldSkip(relativePath) || IsExempted(relativePath, exemptions))
                    {
                        continue;
                    }

                    var lineNumber = 0;
                    foreach (var line in File.ReadLines(filePath))
                    {
                        lineNumber++;
                        foreach (var driverName in driverNames)
                        {
                            if (line.IndexOf(driverName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                findings.Add(new GateFinding(
                                    $"{relativePath}:{lineNumber}",
                                    $"引擎代码里出现了下游 driver 名「{driverName}」——driver 名只能是运行时参数",
                                    "把 driver 名改成参数或配置项；确实要在这里出现就加进 gate-config 的 bridgeBoundaryExemptions",
                                    "Doc/创作管线子文档/05-下游Driver框架.md"));
                            }
                        }
                    }
                }
            }

            return findings;
        }

        /// <summary>读 driver.json 顶层「名称」字段的字符串值；读不到或不是字符串时返回目录名。</summary>
        private static string ReadNameOrDefault(string driverFile, string directoryName)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(driverFile));
                if (document.RootElement.TryGetProperty("名称", out var nameElement)
                    && nameElement.ValueKind == JsonValueKind.String)
                {
                    var name = nameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 自述文件坏掉时退化成目录名，照样能扫。
            }

            return directoryName;
        }

        /// <summary>bin/obj 与测试树下的文件不在引擎扫描范围内（不分大小写）。</summary>
        private static bool ShouldSkip(string relativePath)
        {
            return relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains(".Tests", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>豁免项以 / 结尾按目录前缀匹配，否则按整串相等，都是仓库相对路径。</summary>
        private static bool IsExempted(string relativePath, IEnumerable<string> exemptions)
        {
            return exemptions.Any(entry => entry.EndsWith("/", StringComparison.Ordinal)
                ? relativePath.StartsWith(entry, StringComparison.Ordinal)
                : string.Equals(relativePath, entry, StringComparison.Ordinal));
        }

        /// <summary>把绝对路径转成仓库相对路径，正斜杠。</summary>
        private static string ToRepositoryRelative(string repositoryRoot, string filePath)
        {
            return Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(filePath)).Replace('\\', '/');
        }
    }
}
