using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Template.Toolkit.Gates
{
    /// <summary>
    /// 通用性检查器：查宿主项目专属名字有没有焊进标识符、菜单路径与路径字面量。
    /// 判据是黑名单词出现在标识符、菜单路径、路径字面量里就报，出现在注释与面向用户
    /// 的字符串里放行——注释里说明「这段来自某某框架」是有价值的信息，面向用户的消息里
    /// 出现项目名也正常，而标识符、菜单路径、路径字面量里出现宿主名就是把宿主的身份焊进
    /// 了通用件。注意 HSGFrame 不进黑名单：它是框架自己的名字，与 Unity.Mathematics 地位
    /// 相同，框架有自己的名字恰恰是通用的表现。
    /// </summary>
    public static class GenericNameChecker
    {
        private static readonly Regex IdentifierPattern = new Regex(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled);

        private static readonly Regex SegmentPattern = new Regex(
            @"[A-Z]+(?![a-z])|[A-Z][a-z0-9]*|[a-z0-9]+",
            RegexOptions.Compiled);

        /// <summary>
        /// 菜单 / 编辑器窗口标题的上下文标记：行内出现其一，该行的字符串就是「产品面貌」，
        /// 要查而不是当成给人读的消息放行。
        /// </summary>
        private static readonly string[] MenuContextMarkers =
        {
            "MenuItem(", "AddComponentMenu(", "CreateAssetMenu(", "GetWindow<", "titleContent"
        };

        /// <summary>
        /// 对一组源文件跑通用性检查，返回全部发现。
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

                if (IsExemptPath(filePath, configuration))
                {
                    continue;
                }

                findings.AddRange(CheckFile(filePath, configuration));
            }

            return findings;
        }

        private static IReadOnlyList<GateFinding> CheckFile(string filePath, GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            var blacklist = configuration.GenericNameBlacklist ?? Array.Empty<string>();
            if (blacklist.Count == 0)
            {
                return findings;
            }

            var lines = File.ReadAllLines(filePath);

            // 两套独立的状态机：一套剥注释与字符串得到代码，一套提取字符串字面量。
            // 它们遍历的是同一行序列，状态演化一致，各自维护才能互不干扰。
            var codeInBlockComment = false;
            var codeInVerbatimString = false;
            var literalInBlockComment = false;
            var literalInVerbatimString = false;

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var lineNumber = lineIndex + 1;
                var line = lines[lineIndex];

                var code = StripNonCode(line, ref codeInBlockComment, ref codeInVerbatimString);
                findings.AddRange(CheckIdentifiers(filePath, lineNumber, code, configuration));

                // 菜单路径与路径字面量恰恰在字符串里，所以要在剥之前先把字符串字面量
                // 提出来分成三类：菜单上下文的、像路径的、其余的。前两类查，第三类放行。
                var isMenuContext = IsMenuContextLine(code);
                var literals = ExtractStringLiterals(line, ref literalInBlockComment, ref literalInVerbatimString);
                findings.AddRange(CheckStringLiterals(filePath, lineNumber, isMenuContext, literals, blacklist));
            }

            return findings;
        }

        private static IReadOnlyList<GateFinding> CheckIdentifiers(
            string filePath,
            int lineNumber,
            string code,
            GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            var blacklist = configuration.GenericNameBlacklist ?? Array.Empty<string>();
            var exemptions = configuration.AbbreviationExemptIdentifiers ?? Array.Empty<string>();
            var reportedTokens = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in IdentifierPattern.Matches(code))
            {
                var token = match.Value;
                if (reportedTokens.Contains(token))
                {
                    continue;
                }

                // 第三方 API 的成员名由对方定，调用点绕不开写出它们，逐字豁免。
                if (exemptions.Contains(token, StringComparer.Ordinal))
                {
                    continue;
                }

                var matched = MatchBlacklistSegment(token, blacklist);
                if (matched == null)
                {
                    continue;
                }

                findings.Add(new GateFinding(
                    $"{filePath}:{lineNumber}",
                    $"标识符「{token}」含宿主项目专属名字「{matched}」",
                    "换成与宿主项目无关的通用名字",
                    "Tools/Gates/Config/gate-config.json"));
                reportedTokens.Add(token);
            }

            return findings;
        }

        private static IReadOnlyList<GateFinding> CheckStringLiterals(
            string filePath,
            int lineNumber,
            bool isMenuContext,
            IReadOnlyList<string> literals,
            IReadOnlyList<string> blacklist)
        {
            var findings = new List<GateFinding>();

            foreach (var literal in literals)
            {
                string category;
                if (isMenuContext)
                {
                    category = "菜单路径";
                }
                else if (LooksLikePath(literal))
                {
                    category = "路径字面量";
                }
                else
                {
                    // 面向用户的消息，放行。
                    continue;
                }

                var matched = MatchBlacklistSubstring(literal, blacklist);
                if (matched == null)
                {
                    continue;
                }

                findings.Add(new GateFinding(
                    $"{filePath}:{lineNumber}",
                    $"{category}「{literal}」含宿主项目专属名字「{matched}」",
                    "换成与宿主项目无关的通用名字",
                    "Tools/Gates/Config/gate-config.json"));
            }

            return findings;
        }

        private static bool IsMenuContextLine(string code)
        {
            foreach (var marker in MenuContextMarkers)
            {
                if (code.Contains(marker, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikePath(string literal)
        {
            return !literal.Contains(' ')
                && (literal.Contains('/') || literal.Contains('\\'));
        }

        /// <summary>
        /// 按词段匹配黑名单词：把标识符与黑名单词都拆成词段，检查黑名单词的完整词段
        /// 序列是否作为连续窗口出现在标识符词段序列里。这样多段词（如
        /// GameTemplateForAgent）也能命中，而不会误伤只含其中某一段的标识符。
        /// </summary>
        private static string MatchBlacklistSegment(string token, IReadOnlyList<string> blacklist)
        {
            var tokenSegments = SplitIdentifierSegments(token).ToArray();

            foreach (var entry in blacklist)
            {
                var entrySegments = SplitIdentifierSegments(entry).ToArray();
                if (entrySegments.Length == 0 || entrySegments.Length > tokenSegments.Length)
                {
                    continue;
                }

                for (var start = 0; start <= tokenSegments.Length - entrySegments.Length; start++)
                {
                    var matches = true;
                    for (var offset = 0; offset < entrySegments.Length; offset++)
                    {
                        if (!string.Equals(tokenSegments[start + offset], entrySegments[offset], StringComparison.OrdinalIgnoreCase))
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                    {
                        return entry;
                    }
                }
            }

            return null;
        }

        private static string MatchBlacklistSubstring(string literal, IReadOnlyList<string> blacklist)
        {
            foreach (var entry in blacklist)
            {
                if (literal.IndexOf(entry, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return entry;
                }
            }

            return null;
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

        private static bool IsExemptPath(string filePath, GateConfiguration configuration)
        {
            var exemptions = configuration.GenericNameExemptPaths ?? Array.Empty<string>();
            if (exemptions.Count == 0)
            {
                return false;
            }

            var normalized = filePath.Replace('\\', '/');
            foreach (var rawPrefix in exemptions)
            {
                if (string.IsNullOrWhiteSpace(rawPrefix))
                {
                    continue;
                }

                // 前缀按路径段对齐匹配：匹配点前后都必须是路径边界，免得「RebuiltRPG」
                // 误伤「RebuiltRPGX」这类只共享前几段的路径。
                var prefix = rawPrefix.Replace('\\', '/').TrimEnd('/');
                if (prefix.Length == 0)
                {
                    continue;
                }

                var searchIndex = 0;
                while (true)
                {
                    var index = normalized.IndexOf(prefix, searchIndex, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        break;
                    }

                    var startsAtBoundary = index == 0 || normalized[index - 1] == '/';
                    var endsAtBoundary = index + prefix.Length >= normalized.Length || normalized[index + prefix.Length] == '/';
                    if (startsAtBoundary && endsAtBoundary)
                    {
                        return true;
                    }

                    searchIndex = index + prefix.Length;
                }
            }

            return false;
        }

        private static List<string> ExtractStringLiterals(string line, ref bool inBlockComment, ref bool inVerbatimString)
        {
            var literals = new List<string>();
            var builder = new StringBuilder();
            var index = 0;

            while (index < line.Length)
            {
                // 逐字字符串（@"..."）能跨行，内容要原样保留——路径分隔符 \ 在这里是
                // 普通字符，不能像普通字符串那样按转义吞掉。
                if (inVerbatimString)
                {
                    if (line[index] == '"')
                    {
                        if (index + 1 < line.Length && line[index + 1] == '"')
                        {
                            builder.Append('"');
                            index += 2;
                            continue;
                        }

                        literals.Add(builder.ToString());
                        builder.Clear();
                        index++;
                        inVerbatimString = false;
                        continue;
                    }

                    builder.Append(line[index]);
                    index++;
                    continue;
                }

                if (index + 1 < line.Length && line[index] == '@' && line[index + 1] == '"')
                {
                    builder.Clear();
                    index += 2;
                    inVerbatimString = true;
                    continue;
                }

                if (inBlockComment)
                {
                    if (index + 1 < line.Length && line[index] == '*' && line[index + 1] == '/')
                    {
                        index += 2;
                        inBlockComment = false;
                    }
                    else
                    {
                        index++;
                    }

                    continue;
                }

                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/')
                {
                    break;
                }

                if (index + 1 < line.Length && line[index] == '/' && line[index + 1] == '*')
                {
                    index += 2;
                    inBlockComment = true;
                    continue;
                }

                if (line[index] == '"')
                {
                    builder.Clear();
                    index++;
                    while (index < line.Length)
                    {
                        if (line[index] == '\\' && index + 1 < line.Length)
                        {
                            // \\ 还原成单个反斜杠，保证 "Assets\\RPG\\Config" 这类
                            // 路径字面量不会因为转义被吃成 AssetsRPGConfig 而漏判。
                            if (line[index + 1] == '\\')
                            {
                                builder.Append('\\');
                            }
                            else
                            {
                                builder.Append(' ');
                            }

                            index += 2;
                            continue;
                        }

                        if (line[index] == '"')
                        {
                            literals.Add(builder.ToString());
                            builder.Clear();
                            index++;
                            break;
                        }

                        builder.Append(line[index]);
                        index++;
                    }

                    continue;
                }

                index++;
            }

            return literals;
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
    }
}
