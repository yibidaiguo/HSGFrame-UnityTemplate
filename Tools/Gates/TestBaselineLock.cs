using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Template.Toolkit.Gates
{
    /// <summary>测试基线锁：登记测试源文件哈希，并在校验时比对变化。</summary>
    public static class TestBaselineLock
    {
        private const string FixActionText = "测试断言的改动走单独一次提交并带 [测试变更] 标记，然后跑 gate.baseline 的更新模式重建基线";

        private const string ReferenceExamplePath = "Template/Tools/Gates/Config/test-baseline.json";

        /// <summary>
        /// 按测试文件 glob 收集全部测试源文件，计算 SHA256 并写入基线 JSON。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="configuration">门禁配置。</param>
        /// <param name="baselinePath">基线文件输出路径。</param>
        public static void WriteBaseline(string repositoryRoot, GateConfiguration configuration, string baselinePath)
        {
            var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (var file in FindTestFiles(repositoryRoot, configuration))
            {
                entries[ToRepositoryRelative(repositoryRoot, file)] = ComputeSha256(file);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(new { files = entries }, options);
            File.WriteAllText(baselinePath, json);
        }

        /// <summary>
        /// 重算测试文件哈希并与基线比对，返回全部不一致发现。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="configuration">门禁配置。</param>
        /// <param name="baselinePath">基线文件路径。</param>
        public static IReadOnlyList<GateFinding> Check(
            string repositoryRoot,
            GateConfiguration configuration,
            string baselinePath)
        {
            var findings = new List<GateFinding>();
            var baseline = ReadBaseline(baselinePath);
            var current = ComputeCurrent(repositoryRoot, configuration);

            foreach (var pair in current)
            {
                if (baseline.TryGetValue(pair.Key, out var baselineHash))
                {
                    if (!string.Equals(baselineHash, pair.Value, StringComparison.Ordinal))
                    {
                        findings.Add(new GateFinding(pair.Key, "测试文件内容与基线不一致", FixActionText, ReferenceExamplePath));
                    }
                }
                else
                {
                    findings.Add(new GateFinding(pair.Key, "新增测试文件尚未登记进基线", FixActionText, ReferenceExamplePath));
                }
            }

            foreach (var pair in baseline)
            {
                if (!current.ContainsKey(pair.Key))
                {
                    findings.Add(new GateFinding(pair.Key, "基线登记的测试文件已消失", FixActionText, ReferenceExamplePath));
                }
            }

            return findings;
        }

        private static Dictionary<string, string> ComputeCurrent(string repositoryRoot, GateConfiguration configuration)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in FindTestFiles(repositoryRoot, configuration))
            {
                result[ToRepositoryRelative(repositoryRoot, file)] = ComputeSha256(file);
            }

            return result;
        }

        private static Dictionary<string, string> ReadBaseline(string baselinePath)
        {
            if (!File.Exists(baselinePath))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var document = JsonDocument.Parse(File.ReadAllText(baselinePath)))
            {
                if (document.RootElement.TryGetProperty("files", out var filesElement))
                {
                    foreach (var property in filesElement.EnumerateObject())
                    {
                        result[property.Name] = property.Value.GetString();
                    }
                }
            }

            return result;
        }

        private static IEnumerable<string> FindTestFiles(string repositoryRoot, GateConfiguration configuration)
        {
            if (!Directory.Exists(repositoryRoot))
            {
                return Enumerable.Empty<string>();
            }

            var patterns = (configuration.TestFileGlobs ?? Array.Empty<string>())
                .Select(GlobToRegex)
                .ToList();

            return Directory.EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file => !ContainsBinOrObj(file))
                .Where(file => patterns.Any(pattern => pattern.IsMatch(ToRepositoryRelative(repositoryRoot, file))));
        }

        private static bool ContainsBinOrObj(string path)
        {
            var segments = path.Replace('\\', '/').Split('/');
            return segments.Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
        }

        private static Regex GlobToRegex(string glob)
        {
            var builder = new System.Text.StringBuilder("^");
            foreach (var character in glob)
            {
                switch (character)
                {
                    case '*':
                        builder.Append("[^/]*");
                        break;
                    case '?':
                        builder.Append("[^/]");
                        break;
                    default:
                        builder.Append(Regex.Escape(character.ToString()));
                        break;
                }
            }

            builder.Append("$");
            return new Regex(builder.ToString(), RegexOptions.IgnoreCase);
        }

        private static string ToRepositoryRelative(string repositoryRoot, string filePath)
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(filePath));
            return relative.Replace('\\', '/');
        }

        private static string ComputeSha256(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha256 = SHA256.Create())
            {
                return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
            }
        }
    }
}
