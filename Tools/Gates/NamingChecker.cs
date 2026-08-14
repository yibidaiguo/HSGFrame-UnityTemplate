using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Template.Toolkit.Gates
{
    /// <summary>命名与注释规范检查器：缩写黑名单、公开类型中文摘要、目录命名。</summary>
    public static class NamingChecker
    {
        private static readonly Regex IdentifierPattern = new Regex(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled);

        private static readonly Regex PublicTypePattern = new Regex(
            @"public\s+(?:(?:sealed|static|abstract|partial)\s+)*(?:class|struct|interface|enum|record)\s+[A-Za-z_][A-Za-z0-9_]*",
            RegexOptions.Compiled);

        private static readonly Regex ChineseIdentifierPattern = new Regex(
            @"[A-Za-z0-9_一-鿿]*[一-鿿][A-Za-z0-9_一-鿿]*",
            RegexOptions.Compiled);

        private static readonly Regex SegmentPattern = new Regex(
            @"[A-Z]+(?![a-z])|[A-Z][a-z0-9]*|[a-z0-9]+",
            RegexOptions.Compiled);

        private static readonly string[] DirectorySkipSegments = { "bin", "obj", ".git", "Library", "Temp" };

        private static readonly string[] EnumerateSkipSegments = { "bin", "obj", ".git", "Library", "Temp", "_Phase0Verify" };

        /// <summary>
        /// 对一组源文件跑命名与注释规范检查，返回全部发现。
        /// </summary>
        /// <param name="sourceFilePaths">源文件路径列表。</param>
        /// <param name="configuration">门禁配置。</param>
        public static IReadOnlyList<GateFinding> Check(IEnumerable<string> sourceFilePaths, GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            foreach (var filePath in sourceFilePaths)
            {
                if (!File.Exists(filePath))
                {
                    continue;
                }

                findings.AddRange(CheckFile(filePath, configuration));
            }

            return findings;
        }

        /// <summary>
        /// 递归枚举根目录下的全部 *.cs，跳过 bin/obj/.git/Library/Temp/_Phase0Verify 目录。
        /// </summary>
        /// <param name="rootDirectory">扫描根目录。</param>
        public static IEnumerable<string> EnumerateSourceFiles(string rootDirectory)
        {
            if (!Directory.Exists(rootDirectory))
            {
                return Enumerable.Empty<string>();
            }

            return Directory.EnumerateFiles(rootDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !ContainsAnySegment(path, EnumerateSkipSegments));
        }

        private static IReadOnlyList<GateFinding> CheckFile(string filePath, GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            var lines = File.ReadAllLines(filePath);
            var inBlockComment = false;
            var inVerbatimString = false;

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var lineNumber = lineIndex + 1;
                var line = lines[lineIndex];

                var code = StripNonCode(line, ref inBlockComment, ref inVerbatimString);
                findings.AddRange(CheckAbbreviations(filePath, lineNumber, code, configuration));
                findings.AddRange(CheckChineseIdentifiers(filePath, lineNumber, code));

                if (PublicTypePattern.IsMatch(code) && !HasChineseSummary(lines, lineIndex))
                {
                    findings.Add(new GateFinding(
                        $"{filePath}:{lineNumber}",
                        "公开类型缺少中文 <summary> 注释",
                        "给公开类型补一行中文摘要",
                        "Template/Tools/Cli/CommandFramework/CommandRegistry.cs"));
                }
            }

            findings.AddRange(CheckDirectoryNames(filePath, configuration));
            return findings;
        }

        private static IReadOnlyList<GateFinding> CheckAbbreviations(
            string filePath,
            int lineNumber,
            string code,
            GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            var blacklist = configuration.AbbreviationBlacklist ?? Array.Empty<string>();
            var reportedTokens = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in IdentifierPattern.Matches(code))
            {
                var token = match.Value;
                if (reportedTokens.Contains(token))
                {
                    continue;
                }

                foreach (var entry in blacklist)
                {
                    if (!ContainsAbbreviation(token, entry))
                    {
                        continue;
                    }

                    findings.Add(new GateFinding(
                        $"{filePath}:{lineNumber}",
                        $"标识符「{token}」含缩写「{entry}」",
                        "换成完整单词",
                        "Template/Tools/Cli/CommandFramework/CommandRegistry.cs"));
                    reportedTokens.Add(token);
                    break;
                }
            }

            return findings;
        }

        private static IReadOnlyList<GateFinding> CheckChineseIdentifiers(string filePath, int lineNumber, string code)
        {
            var findings = new List<GateFinding>();
            var reportedTokens = new HashSet<string>(StringComparer.Ordinal);

            // 注释、字符串字面量、逐字字符串在 StripNonCode 里已经抹掉，
            // 所以这里剩下的中文只可能出现在标识符上。
            foreach (Match match in ChineseIdentifierPattern.Matches(code))
            {
                var token = match.Value;
                if (reportedTokens.Add(token))
                {
                    findings.Add(new GateFinding(
                        $"{filePath}:{lineNumber}",
                        $"标识符「{token}」含中文",
                        "标识符换成英文完整单词，中文写进注释或 [JsonPropertyName] 一类的数据键",
                        "Template/UnityProject/Assets/_Project/Scripts/Logic/Data/Level/LogicEntityPlacement.cs"));
                }
            }

            return findings;
        }

        private static bool ContainsAbbreviation(string token, string entry)
        {
            // 必须整段相等才算命中：靠正则做前后边界约束会在忽略大小写时失效
            // （`Conf` 会把 `Configuration` 咬住），所以先把标识符拆成词段再逐段比。
            return SplitIdentifierSegments(token)
                .Any(segment => string.Equals(segment, entry, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> SplitIdentifierSegments(string token)
        {
            foreach (var part in token.Split('_'))
            {
                // 连续大写视为缩略词整体（HTTPServer → HTTP / Server）。
                foreach (Match segment in SegmentPattern.Matches(part))
                {
                    yield return segment.Value;
                }
            }
        }

        private static bool HasChineseSummary(string[] lines, int declarationIndex)
        {
            var index = declarationIndex - 1;

            // 声明行之上可能隔着空行与特性行（[AttributeUsage] 之类），摘要在它们再上面。
            while (index >= 0
                && (string.IsNullOrWhiteSpace(lines[index]) || lines[index].TrimStart().StartsWith("[", StringComparison.Ordinal)))
            {
                index--;
            }

            if (index < 0 || !lines[index].TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                return false;
            }

            // 收集连续 /// 注释块，检查其中是否出现过 CJK 字符。
            while (index >= 0 && lines[index].TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                if (Regex.IsMatch(lines[index], @"[\u4e00-\u9fff]"))
                {
                    return true;
                }

                index--;
            }

            return false;
        }

        private static IReadOnlyList<GateFinding> CheckDirectoryNames(string filePath, GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            var directoryPath = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directoryPath))
            {
                return findings;
            }

            var blacklist = configuration.DirectoryNameBlacklist ?? Array.Empty<string>();
            var pattern = configuration.DirectoryNamePattern;

            foreach (var segment in directoryPath.Replace('\\', '/').Split('/'))
            {
                // "." / ".." 是相对路径的构件，不是目录名（扫描根传 "." 时会走到这里）。
                if (segment.Length == 0 || segment.Contains(':') || segment == "." || segment == "..")
                {
                    continue;
                }

                if (DirectorySkipSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (blacklist.Any(entry => string.Equals(entry, segment, StringComparison.OrdinalIgnoreCase)))
                {
                    findings.Add(new GateFinding(
                        filePath,
                        $"目录名「{segment}」命中目录黑名单",
                        "换一个有意义的完整单词目录名",
                        "Template/Tools/Cli/CommandFramework"));
                    continue;
                }

                if (!string.IsNullOrEmpty(pattern) && !Regex.IsMatch(segment, pattern))
                {
                    findings.Add(new GateFinding(
                        filePath,
                        $"目录名「{segment}」不符合命名规范",
                        "改用字母、数字、下划线、点，且以字母或下划线开头",
                        "Template/Tools/Cli/CommandFramework"));
                }
            }

            return findings;
        }

        private static string StripNonCode(string line, ref bool inBlockComment, ref bool inVerbatimString)
        {
            var builder = new StringBuilder(line.Length);
            var index = 0;

            while (index < line.Length)
            {
                // 逐字字符串（@"..."）能跨行，测试里的样例源码就装在里面，
                // 不摘掉它会把样例里故意写坏的名字当成本文件的违规报出来。
                if (inVerbatimString)
                {
                    if (line[index] == '"')
                    {
                        if (index + 1 < line.Length && line[index + 1] == '"')
                        {
                            builder.Append("  ");
                            index += 2;
                            continue;
                        }

                        builder.Append(' ');
                        index++;
                        inVerbatimString = false;
                        continue;
                    }

                    builder.Append(' ');
                    index++;
                    continue;
                }

                if (index + 1 < line.Length && line[index] == '@' && line[index + 1] == '"')
                {
                    builder.Append("  ");
                    index += 2;
                    inVerbatimString = true;
                    continue;
                }

                if (inBlockComment)
                {
                    if (index + 1 < line.Length && line[index] == '*' && line[index + 1] == '/')
                    {
                        builder.Append("  ");
                        index += 2;
                        inBlockComment = false;
                    }
                    else
                    {
                        builder.Append(' ');
                        index++;
                    }

                    continue;
                }

                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/')
                {
                    while (index < line.Length)
                    {
                        builder.Append(' ');
                        index++;
                    }

                    break;
                }

                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '*')
                {
                    builder.Append("  ");
                    index += 2;
                    inBlockComment = true;
                    continue;
                }

                if (line[index] == '"')
                {
                    builder.Append(' ');
                    index++;
                    while (index < line.Length)
                    {
                        if (line[index] == '\\' && index + 1 < line.Length)
                        {
                            builder.Append("  ");
                            index += 2;
                            continue;
                        }

                        if (line[index] == '"')
                        {
                            builder.Append(' ');
                            index++;
                            break;
                        }

                        builder.Append(' ');
                        index++;
                    }

                    continue;
                }

                builder.Append(line[index]);
                index++;
            }

            return builder.ToString();
        }

        private static bool ContainsAnySegment(string path, string[] segments)
        {
            var normalized = path.Replace('\\', '/').Split('/');
            return normalized.Any(segment => segments.Contains(segment, StringComparer.OrdinalIgnoreCase));
        }
    }
}
