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

        private const string ReferenceExamplePath = "Tools/Gates/Config/test-baseline.json";

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

            foreach (var problem in CheckSolutionMembership(repositoryRoot))
            {
                findings.Add(new GateFinding(problem, "测试工程不在解决方案里（dotnet test 不会跑它，基线照绿是假绿）", FixActionText, ReferenceExamplePath));
            }

            return findings;
        }

        /// <summary>
        /// 对账 Solutions/ 下所有 *.Tests 工程是否都登记进解决方案。
        /// Solutions/ 不存在、或里面没有 .sln 时返回空清单直接跳过——刚生成的项目和测试的合成目录
        /// 都没有，跳过而不是报红（与 gate.whitelist 在没有 git 时的降级是同一个哲学）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static List<string> CheckSolutionMembership(string repositoryRoot)
        {
            var problems = new List<string>();

            var solutionsDir = Path.Combine(repositoryRoot, "Solutions");
            if (!Directory.Exists(solutionsDir))
            {
                return problems;
            }

            var slnFile = Directory.EnumerateFiles(solutionsDir, "*.sln", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault();
            if (slnFile == null)
            {
                return problems;
            }

            var slnText = File.ReadAllText(slnFile);

            foreach (var testsDir in Directory.EnumerateDirectories(solutionsDir))
            {
                var directoryName = Path.GetFileName(testsDir);
                if (!directoryName.EndsWith(".Tests", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var csproj in Directory.EnumerateFiles(testsDir, "*.csproj", SearchOption.TopDirectoryOnly))
                {
                    var projectFileName = Path.GetFileName(csproj);
                    if (slnText.IndexOf(projectFileName, StringComparison.Ordinal) < 0)
                    {
                        problems.Add($"测试工程不在解决方案里：{ToRepositoryRelative(repositoryRoot, csproj)}（dotnet test 不会跑它，基线照绿是假绿）");
                    }
                }
            }

            return problems;
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
            var segments = glob.Split(new[] { "**" }, StringSplitOptions.None);
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                if (segmentIndex > 0)
                {
                    builder.Append(".*");
                }

                foreach (var character in segments[segmentIndex])
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
            }

            builder.Append("$");
            return new Regex(builder.ToString(), RegexOptions.IgnoreCase);
        }

        private static string ToRepositoryRelative(string repositoryRoot, string filePath)
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(filePath));
            return relative.Replace('\\', '/');
        }

        /// <summary>
        /// 计算测试文件的内容哈希。
        /// </summary>
        /// <remarks>
        /// 哈希前先把行尾统一成 LF 并去掉 BOM：`.gitattributes` 里 `.cs` 走 `text=auto`，
        /// 索引里存 LF、Windows 检出成 CRLF，直接哈希原始字节会让基线跟着检出方式变——
        /// 换台机器克隆、或者跑一次 `git add --renormalize`，基线就恒红，
        /// 而恒红的门禁等于没有门禁。行尾不是测试内容，不该进哈希。
        /// </remarks>
        /// <param name="filePath">测试源文件路径。</param>
        private static string ComputeSha256(string filePath)
        {
            var normalized = NormalizeContent(File.ReadAllText(filePath));
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized));
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }

        /// <summary>
        /// 行尾统一成 LF，并去掉开头的 BOM。
        /// </summary>
        /// <param name="text">文件文本。</param>
        private static string NormalizeContent(string text)
        {
            var body = text.TrimStart('\uFEFF');
            return body.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
