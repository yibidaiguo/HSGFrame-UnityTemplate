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

            return EnumerateSourceFiles(rootDirectory, Array.Empty<string>());
        }

        /// <summary>
        /// 同上，另外跳过调用方指定的目录名（第三方源码与生成物走这条）。
        /// </summary>
        /// <param name="rootDirectory">扫描根目录。</param>
        /// <param name="extraSkipSegments">额外要跳过的目录名。</param>
        public static IEnumerable<string> EnumerateSourceFiles(string rootDirectory, IReadOnlyList<string> extraSkipSegments)
        {
            if (!Directory.Exists(rootDirectory))
            {
                return Enumerable.Empty<string>();
            }

            var skipSegments = EnumerateSkipSegments.Concat(extraSkipSegments ?? Array.Empty<string>()).ToArray();
            return Directory.EnumerateFiles(rootDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !ContainsAnySegment(path, skipSegments));
        }

        private static IReadOnlyList<GateFinding> CheckFile(string filePath, GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            var lines = File.ReadAllLines(filePath);
            var codeLines = StripNonCode(lines);

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var lineNumber = lineIndex + 1;
                var code = codeLines[lineIndex];

                findings.AddRange(CheckAbbreviations(filePath, lineNumber, code, configuration));
                findings.AddRange(CheckChineseIdentifiers(filePath, lineNumber, code));

                if (PublicTypePattern.IsMatch(code) && !HasChineseSummary(lines, codeLines, lineIndex))
                {
                    findings.Add(new GateFinding(
                        $"{filePath}:{lineNumber}",
                        "公开类型缺少中文 <summary> 注释",
                        "给公开类型补一行中文摘要",
                        "Tools/Cli/CommandFramework/CommandRegistry.cs"));
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
            var exemptions = configuration.AbbreviationExemptIdentifiers ?? Array.Empty<string>();
            var reportedTokens = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in IdentifierPattern.Matches(code))
            {
                var token = match.Value;
                if (reportedTokens.Contains(token))
                {
                    continue;
                }

                // 第三方 API 的成员名由对方定，调用点绕不开写出它们。豁免逐字匹配而不是按词段，
                // 免得一条豁免顺带把我们自己代码里同词段的标识符也放过去。
                if (exemptions.Contains(token, StringComparer.Ordinal))
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
                        "Tools/Cli/CommandFramework/CommandRegistry.cs"));
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
                        "UnityProject/Assets/Game/Scripts/Modules/Level/Data/LogicEntityPlacement.cs"));
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

        private static bool HasChineseSummary(string[] lines, string[] codeLines, int declarationIndex)
        {
            var index = declarationIndex - 1;

            // 声明行之上可能隔着空行与特性块（[AttributeUsage] 之类），摘要在它们再上面。
            // 特性能写成多行，末行长这样：`        false)]`——它不以 [ 开头，所以不能逐行看开头，
            // 得按方括号配对整块往回跳，否则回扫停在末行上，摘要明明在也会被报成缺失。
            while (index >= 0)
            {
                if (string.IsNullOrWhiteSpace(lines[index]))
                {
                    index--;
                    continue;
                }

                var attributeStartIndex = FindAttributeBlockStart(codeLines, index);
                if (attributeStartIndex < 0)
                {
                    break;
                }

                index = attributeStartIndex - 1;
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

        /// <summary>
        /// 若 <paramref name="endIndex"/> 行是一个独立成行的特性块的末行，返回该块起始行下标，否则返回 -1。
        /// </summary>
        /// <remarks>
        /// 输入是 StripNonCode 之后的代码行，所以字符串字面量里的方括号已经被抹成空格，
        /// 不会把配对计数带偏——`[Obsolete("参见 Foo[0]")]` 这种写法照样算得准。
        /// </remarks>
        /// <param name="codeLines">StripNonCode 之后的整份源码行。</param>
        /// <param name="endIndex">要判定的行下标。</param>
        private static int FindAttributeBlockStart(string[] codeLines, int endIndex)
        {
            // 特性块必须以 ']' 收尾。不先卡这一下的话，注释行（整行被抹成空格）会让
            // 下面的扫描一路穿到更上面去，把某个无关的 ']' 认成本块的末尾。
            if (!codeLines[endIndex].TrimEnd().EndsWith("]", StringComparison.Ordinal))
            {
                return -1;
            }

            var depth = 0;
            for (var index = endIndex; index >= 0; index--)
            {
                var line = codeLines[index];
                for (var column = line.Length - 1; column >= 0; column--)
                {
                    var current = line[column];
                    if (current == ']')
                    {
                        depth++;
                        continue;
                    }

                    if (current != '[')
                    {
                        continue;
                    }

                    depth--;
                    if (depth > 0)
                    {
                        continue;
                    }

                    // 配平的那个 '[' 之前只许有空白，否则这是索引器或数组下标，不是特性。
                    return line.Substring(0, column).Trim().Length == 0 ? index : -1;
                }
            }

            return -1;
        }

        private static string[] StripNonCode(string[] lines)
        {
            var codeLines = new string[lines.Length];
            var inBlockComment = false;
            var inVerbatimString = false;
            var rawStringFenceLength = 0;

            for (var index = 0; index < lines.Length; index++)
            {
                codeLines[index] = StripNonCode(lines[index], ref inBlockComment, ref inVerbatimString, ref rawStringFenceLength);
            }

            return codeLines;
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
                        "Tools/Cli/CommandFramework"));
                    continue;
                }

                // 以下划线开头的名字先过豁免名单：放行的是机器管理区（_Inbox / _Generated /
                // _Scratch）与迁移期还没改完的过渡名。名单之外的名字交给下面的正则判，
                // 而正则已经收紧成「必须字母开头」。
                if (segment.StartsWith("_", StringComparison.Ordinal))
                {
                    if (IsUnderscoreExempt(segment, configuration))
                    {
                        continue;
                    }

                    findings.Add(new GateFinding(
                        filePath,
                        $"目录名「{segment}」以下划线开头，而它不在下划线豁免名单里",
                        "改成字母开头的名字；确实是机器管理区就加进 gate-config.json 的 underscoreExemptNames",
                        "Tools/Gates/Config/gate-config.json"));
                    continue;
                }

                if (!string.IsNullOrEmpty(pattern) && !Regex.IsMatch(segment, pattern))
                {
                    findings.Add(new GateFinding(
                        filePath,
                        $"目录名「{segment}」不符合命名规范",
                        "改用字母、数字、下划线、点，且以字母或下划线开头",
                        "Tools/Cli/CommandFramework"));
                }
            }

            return findings;
        }

        /// <summary>判断一个以下划线开头的名字是否在豁免名单里（逐字匹配，忽略大小写）。</summary>
        /// <param name="name">目录名或文件名。</param>
        /// <param name="configuration">门禁配置。</param>
        public static bool IsUnderscoreExempt(string name, GateConfiguration configuration)
        {
            var exemptions = configuration?.UnderscoreExemptNames ?? Array.Empty<string>();
            return exemptions.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 把一行源码里的注释、字符串字面量与逐字字符串抹成空格，只留下真正是代码的部分。
        /// 逐行调用，跨行状态由两个 ref 参数带着走。模块边界检查器也用它，别再写第二份。
        /// </summary>
        /// <param name="line">要处理的一行源码。</param>
        /// <param name="inBlockComment">进入本行时是否处在块注释里，返回时更新为出本行时的状态。</param>
        /// <param name="inVerbatimString">进入本行时是否处在逐字字符串里，返回时更新为出本行时的状态。</param>
        public static string StripNonCode(string line, ref bool inBlockComment, ref bool inVerbatimString)
        {
            var rawStringFenceLength = 0;
            return StripNonCode(line, ref inBlockComment, ref inVerbatimString, ref rawStringFenceLength);
        }

        /// <summary>
        /// 同上，外加原始字符串字面量（C# 11 的 <c>"""…"""</c>）的跨行状态。
        ///
        /// 没有这一路时，扫描器把 <c>"""</c> 看成「一个空串加一个开引号」，
        /// 于是原始串里**没被引号裹住**的中文（markdown 正文、样例文档）会被当成标识符报出来——
        /// 一份合法的测试样例换来几十条假红。装样例的原始串正是测试最该用的东西，
        /// 所以这一路必须认。
        /// </summary>
        /// <param name="line">要处理的一行源码。</param>
        /// <param name="inBlockComment">进入本行时是否处在块注释里，返回时更新为出本行时的状态。</param>
        /// <param name="inVerbatimString">进入本行时是否处在逐字字符串里，返回时更新为出本行时的状态。</param>
        /// <param name="rawStringFenceLength">
        /// 进入本行时所处原始字符串的引号栅栏长度（0 表示不在原始字符串里），返回时更新为出本行时的状态。
        /// </param>
        public static string StripNonCode(
            string line,
            ref bool inBlockComment,
            ref bool inVerbatimString,
            ref int rawStringFenceLength)
        {
            var builder = new StringBuilder(line.Length);
            var index = 0;

            while (index < line.Length)
            {
                // 原始字符串里：整行抹平，直到遇上一段不短于开栅栏的引号。
                if (rawStringFenceLength > 0)
                {
                    var closing = CountQuoteRun(line, index);
                    if (closing >= rawStringFenceLength)
                    {
                        builder.Append(' ', closing);
                        index += closing;
                        rawStringFenceLength = 0;
                        continue;
                    }

                    builder.Append(' ');
                    index++;
                    continue;
                }

                // 开栅栏：三个及以上连续引号，前面可以带 $ 或 $$（插值原始串）。
                var prefixLength = CountRawStringPrefix(line, index);
                if (prefixLength >= 0)
                {
                    var fence = CountQuoteRun(line, index + prefixLength);
                    if (fence >= 3)
                    {
                        builder.Append(' ', prefixLength + fence);
                        index += prefixLength + fence;
                        rawStringFenceLength = fence;
                        continue;
                    }
                }

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

                // 逐字插值字符串（$@"..." / @$"..."）走既有的逐字分支：它能跨行，
                // 而下面那段单行插值的括号配对逻辑跨不了行。
                if (index + 2 < line.Length
                    && ((line[index] == '$' && line[index + 1] == '@') || (line[index] == '@' && line[index + 1] == '$'))
                    && line[index + 2] == '"')
                {
                    builder.Append("   ");
                    index += 3;
                    inVerbatimString = true;
                    continue;
                }

                // 单行插值字符串（$"..."）：整段抹掉，包括 {} 洞里的内容。
                // 不这么做的话，扫描器会把开头那个引号当成普通字符串的开始、在洞里第一个引号处
                // 就以为字符串结束了，于是 $"...{flag ? "甲" : "乙"}..." 里的「甲」「乙」被当成标识符报出来。
                // 洞里的东西一律不查是有意的：洞里出现的是标识符的**使用**，而命名是**声明**的属性，
                // 每一处使用都有一个声明在扫描范围内，查使用只会重复报同一件事。
                if (index + 1 < line.Length && line[index] == '$' && line[index + 1] == '"')
                {
                    builder.Append("  ");
                    index += 2;
                    var braceDepth = 0;

                    while (index < line.Length)
                    {
                        var current = line[index];

                        // 转义序列整对跳过：$"…参考：\"动作\"…" 里那对 \" 不是字符串的结束，
                        // 不跳的话扫描器会提前出串，把后面的中文当成标识符报出来。
                        if (current == '\\' && index + 1 < line.Length)
                        {
                            builder.Append("  ");
                            index += 2;
                            continue;
                        }

                        if (current == '{' || current == '}')
                        {
                            // {{ 与 }} 是转义的花括号，不进出洞。
                            if (index + 1 < line.Length && line[index + 1] == current)
                            {
                                builder.Append("  ");
                                index += 2;
                                continue;
                            }

                            braceDepth += current == '{' ? 1 : -1;
                        }
                        else if (current == '"' && braceDepth <= 0)
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

        /// <summary>数从 start 起有几个连续的引号。</summary>
        private static int CountQuoteRun(string line, int start)
        {
            var count = 0;
            while (start + count < line.Length && line[start + count] == '"')
            {
                count++;
            }

            return count;
        }

        /// <summary>
        /// 原始字符串开头那几个 $ 的个数：0 表示没有前缀（普通原始串），-1 表示这里根本不是原始串开头。
        /// </summary>
        private static int CountRawStringPrefix(string line, int index)
        {
            var dollars = 0;
            while (index + dollars < line.Length && line[index + dollars] == '$')
            {
                dollars++;
            }

            return index + dollars < line.Length && line[index + dollars] == '"' ? dollars : -1;
        }

        private static bool ContainsAnySegment(string path, string[] segments)
        {
            var normalized = path.Replace('\\', '/').Split('/');
            return normalized.Any(segment => segments.Contains(segment, StringComparer.OrdinalIgnoreCase));
        }
    }
}
