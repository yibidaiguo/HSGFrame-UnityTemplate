using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>需求文档里的一个二级小节：标题、起始行号与正文行。</summary>
    public sealed class RequirementDocumentSection
    {
        /// <summary>
        /// 构造一个小节。
        /// </summary>
        /// <param name="title">小节标题，`## ` 后面那串，两端已去空白。</param>
        /// <param name="lineNumber">标题所在行号，从 1 起。</param>
        /// <param name="lines">正文行，不含标题行本身。</param>
        /// <param name="isInGeneratedRegion">这一节在不在生成区里。</param>
        public RequirementDocumentSection(string title, int lineNumber, IReadOnlyList<string> lines, bool isInGeneratedRegion)
        {
            Title = title;
            LineNumber = lineNumber;
            Lines = lines;
            IsInGeneratedRegion = isInGeneratedRegion;
        }

        /// <summary>小节标题。</summary>
        public string Title { get; }

        /// <summary>标题所在行号，从 1 起。</summary>
        public int LineNumber { get; }

        /// <summary>正文行，不含标题行本身。</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>这一节在不在生成区里。</summary>
        public bool IsInGeneratedRegion { get; }
    }

    /// <summary>
    /// 需求文档的 frontmatter。
    ///
    /// **这是个只认三种形状的窄解析器，不是 YAML 实现**：标量、一层嵌套的映射、对象列表。
    /// 规范（基线第三节）把话说死在这三种上，就是为了不必往工具链里拖一个 YAML 依赖，
    /// 也为了写文档的人不会掉进 YAML 那些反直觉的角落里（`是`、`否`、`12:30` 各有各的惊喜）。
    /// 认不出来的写法不报语法错，而是**读不出那个键**——于是「必备键缺失」替它报红，
    /// 报出来的话人看得懂，比「第 7 行缩进不对」有用。
    /// </summary>
    public sealed class RequirementDocumentFrontMatter
    {
        private readonly IReadOnlyDictionary<string, string> _scalars;
        private readonly IReadOnlyDictionary<string, int> _lineNumbers;
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _maps;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> _lists;

        internal RequirementDocumentFrontMatter(
            bool isPresent,
            IReadOnlyDictionary<string, string> scalars,
            IReadOnlyDictionary<string, int> lineNumbers,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> maps,
            IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> lists)
        {
            IsPresent = isPresent;
            _scalars = scalars;
            _lineNumbers = lineNumbers;
            _maps = maps;
            _lists = lists;
        }

        /// <summary>文档开头到底有没有一段 `---` 包起来的 frontmatter。</summary>
        public bool IsPresent { get; }

        /// <summary>某个键在不在（三种形状里的任何一种都算在）。</summary>
        /// <param name="key">键名。</param>
        public bool Has(string key)
        {
            return _scalars.ContainsKey(key) || _maps.ContainsKey(key) || _lists.ContainsKey(key);
        }

        /// <summary>取标量值；不是标量或不存在时返回空串。</summary>
        /// <param name="key">键名。</param>
        public string Scalar(string key)
        {
            return _scalars.TryGetValue(key, out var value) ? value : "";
        }

        /// <summary>取一层嵌套映射；不存在时返回空表。</summary>
        /// <param name="key">键名。</param>
        public IReadOnlyDictionary<string, string> Map(string key)
        {
            return _maps.TryGetValue(key, out var value)
                ? value
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>取对象列表；不存在时返回空列表。</summary>
        /// <param name="key">键名。</param>
        public IReadOnlyList<IReadOnlyDictionary<string, string>> List(string key)
        {
            return _lists.TryGetValue(key, out var value)
                ? value
                : Array.Empty<IReadOnlyDictionary<string, string>>();
        }

        /// <summary>某个键写在第几行，从 1 起；不存在时返回 0。</summary>
        /// <param name="key">键名。</param>
        public int LineOf(string key)
        {
            return _lineNumbers.TryGetValue(key, out var value) ? value : 0;
        }
    }

    /// <summary>
    /// 解析出来的需求文档：frontmatter、二级小节列表、生成区。
    /// 解析只做「读得出什么」，一条合规判定都不做——判定全在 <see cref="RequirementDocumentChecker"/> 里。
    /// </summary>
    public sealed class RequirementDocument
    {
        private RequirementDocument(
            RequirementDocumentFrontMatter frontMatter,
            IReadOnlyList<RequirementDocumentSection> sections,
            IReadOnlyList<string> bodyLines,
            IReadOnlyList<string> generatedRegionLines,
            bool hasGeneratedRegion,
            int generatedRegionLineNumber)
        {
            FrontMatter = frontMatter;
            Sections = sections;
            BodyLines = bodyLines;
            GeneratedRegionLines = generatedRegionLines;
            HasGeneratedRegion = hasGeneratedRegion;
            GeneratedRegionLineNumber = generatedRegionLineNumber;
        }

        /// <summary>文档的 frontmatter。</summary>
        public RequirementDocumentFrontMatter FrontMatter { get; }

        /// <summary>全部二级小节，按出现顺序。</summary>
        public IReadOnlyList<RequirementDocumentSection> Sections { get; }

        /// <summary>frontmatter 之后的全部正文行（含生成区），供正文里找媒体引用用。</summary>
        public IReadOnlyList<string> BodyLines { get; }

        /// <summary>生成区里的正文行，不含两条标记行本身。</summary>
        public IReadOnlyList<string> GeneratedRegionLines { get; }

        /// <summary>文档里有没有生成区（两条标记都在且成对）。</summary>
        public bool HasGeneratedRegion { get; }

        /// <summary>生成区开始标记所在行号，从 1 起；没有生成区时为 0。</summary>
        public int GeneratedRegionLineNumber { get; }

        /// <summary>
        /// 解析一份需求文档。
        ///
        /// 只有两种情况算解析失败：frontmatter 开了头没收尾，生成区开了头没收尾。
        /// 其余一律解析成功——**读不出来的东西交给检查器去报**，
        /// 解析器一旦开始拒收，人拿到的就只有一句「格式不对」，指不到具体哪里。
        /// </summary>
        /// <param name="text">文档全文。</param>
        /// <param name="specification">需求文档规范，用于识别生成区标记。</param>
        /// <param name="document">解析结果。</param>
        /// <param name="failureReason">解析失败原因；成功时为空串。</param>
        public static bool TryParse(
            string text,
            RequirementDocumentSpec specification,
            out RequirementDocument document,
            out string failureReason)
        {
            document = null;
            failureReason = "";

            var lines = (text ?? "").Replace("\r\n", "\n").TrimStart('﻿').Split('\n');
            var index = 0;

            var frontMatterPresent = false;
            var frontMatterLines = new List<string>();
            var frontMatterStartLine = 0;
            if (lines.Length > 0 && lines[0].Trim() == "---")
            {
                frontMatterPresent = true;
                frontMatterStartLine = 2;
                var closed = false;
                for (index = 1; index < lines.Length; index++)
                {
                    if (lines[index].Trim() == "---")
                    {
                        index++;
                        closed = true;
                        break;
                    }

                    frontMatterLines.Add(lines[index]);
                }

                if (!closed)
                {
                    failureReason = "frontmatter 的开头有 --- 却没有结尾的 ---";
                    return false;
                }
            }

            var frontMatter = ParseFrontMatter(frontMatterPresent, frontMatterLines, frontMatterStartLine);

            var bodyLines = new List<string>();
            var sections = new List<RequirementDocumentSection>();
            var generatedLines = new List<string>();
            var hasGeneratedRegion = false;
            var generatedRegionLineNumber = 0;
            var insideGeneratedRegion = false;
            var insideFence = false;
            RequirementDocumentSection currentSection = null;
            var currentLines = new List<string>();

            for (; index < lines.Length; index++)
            {
                var line = lines[index];
                var lineNumber = index + 1;
                bodyLines.Add(line);

                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    insideFence = !insideFence;
                }

                if (!insideFence && line.Trim() == specification.GeneratedRegionBegin)
                {
                    insideGeneratedRegion = true;
                    hasGeneratedRegion = true;
                    generatedRegionLineNumber = lineNumber;
                    continue;
                }

                if (!insideFence && line.Trim() == specification.GeneratedRegionEnd)
                {
                    if (!insideGeneratedRegion)
                    {
                        failureReason = $"第 {lineNumber} 行有生成区结束标记，却没有配对的开始标记";
                        return false;
                    }

                    insideGeneratedRegion = false;
                    FlushSection(sections, ref currentSection, currentLines);
                    continue;
                }

                if (insideGeneratedRegion)
                {
                    generatedLines.Add(line);
                }

                if (!insideFence && line.StartsWith("## ", StringComparison.Ordinal))
                {
                    FlushSection(sections, ref currentSection, currentLines);
                    currentSection = new RequirementDocumentSection(
                        line.Substring(3).Trim(),
                        lineNumber,
                        Array.Empty<string>(),
                        insideGeneratedRegion);
                    currentLines = new List<string>();
                    continue;
                }

                if (currentSection != null)
                {
                    currentLines.Add(line);
                }
            }

            if (insideGeneratedRegion)
            {
                failureReason = "生成区开了头却没有结束标记";
                return false;
            }

            FlushSection(sections, ref currentSection, currentLines);

            document = new RequirementDocument(
                frontMatter,
                sections,
                bodyLines,
                generatedLines,
                hasGeneratedRegion,
                generatedRegionLineNumber);
            return true;
        }

        /// <summary>
        /// 算生成区正文的哈希，形如 `sha256:1f4b…`。
        /// 行尾空白与末尾空行不计入——它们改了不算「手改了生成区」。
        /// </summary>
        /// <param name="generatedRegionLines">生成区正文行，不含标记行。</param>
        public static string HashGeneratedRegion(IReadOnlyList<string> generatedRegionLines)
        {
            var normalized = new List<string>();
            foreach (var line in generatedRegionLines ?? Array.Empty<string>())
            {
                normalized.Add(line.TrimEnd());
            }

            while (normalized.Count > 0 && normalized[normalized.Count - 1].Length == 0)
            {
                normalized.RemoveAt(normalized.Count - 1);
            }

            var bytes = Encoding.UTF8.GetBytes(string.Join("\n", normalized));
            var builder = new StringBuilder("sha256:");
            foreach (var value in SHA256.HashData(bytes))
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static void FlushSection(
            List<RequirementDocumentSection> sections,
            ref RequirementDocumentSection currentSection,
            List<string> currentLines)
        {
            if (currentSection == null)
            {
                return;
            }

            sections.Add(new RequirementDocumentSection(
                currentSection.Title,
                currentSection.LineNumber,
                currentLines.ToArray(),
                currentSection.IsInGeneratedRegion));
            currentSection = null;
        }

        // 三种形状的窄解析：标量、一层嵌套映射、对象列表。缩进按空格数分层，制表符不认。
        private static RequirementDocumentFrontMatter ParseFrontMatter(
            bool isPresent,
            IReadOnlyList<string> lines,
            int startLineNumber)
        {
            var scalars = new Dictionary<string, string>(StringComparer.Ordinal);
            var lineNumbers = new Dictionary<string, int>(StringComparer.Ordinal);
            var maps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            var lists = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(StringComparer.Ordinal);

            string pendingKey = null;
            Dictionary<string, string> pendingMap = null;
            List<IReadOnlyDictionary<string, string>> pendingList = null;
            Dictionary<string, string> pendingListItem = null;

            for (var index = 0; index < lines.Count; index++)
            {
                var raw = lines[index];
                var lineNumber = startLineNumber + index;
                if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var indent = CountIndent(raw);
                var content = raw.Trim();

                if (indent == 0)
                {
                    CommitPending(maps, lists, ref pendingKey, ref pendingMap, ref pendingList, ref pendingListItem);

                    if (!TrySplitKeyValue(content, out var key, out var value))
                    {
                        continue;
                    }

                    lineNumbers[key] = lineNumber;
                    if (value.Length == 0)
                    {
                        // 值空着：底下缩进的东西决定它是映射还是对象列表。
                        pendingKey = key;
                        continue;
                    }

                    scalars[key] = value;
                    continue;
                }

                if (pendingKey == null)
                {
                    continue;
                }

                if (content.StartsWith("- ", StringComparison.Ordinal))
                {
                    pendingList ??= new List<IReadOnlyDictionary<string, string>>();
                    pendingListItem = new Dictionary<string, string>(StringComparer.Ordinal);
                    pendingList.Add(pendingListItem);
                    if (TrySplitKeyValue(content.Substring(2).Trim(), out var itemKey, out var itemValue))
                    {
                        pendingListItem[itemKey] = itemValue;
                    }

                    continue;
                }

                if (!TrySplitKeyValue(content, out var childKey, out var childValue))
                {
                    continue;
                }

                if (pendingListItem != null)
                {
                    pendingListItem[childKey] = childValue;
                    continue;
                }

                pendingMap ??= new Dictionary<string, string>(StringComparer.Ordinal);
                pendingMap[childKey] = childValue;
            }

            CommitPending(maps, lists, ref pendingKey, ref pendingMap, ref pendingList, ref pendingListItem);

            return new RequirementDocumentFrontMatter(isPresent, scalars, lineNumbers, maps, lists);
        }

        private static void CommitPending(
            Dictionary<string, IReadOnlyDictionary<string, string>> maps,
            Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> lists,
            ref string pendingKey,
            ref Dictionary<string, string> pendingMap,
            ref List<IReadOnlyDictionary<string, string>> pendingList,
            ref Dictionary<string, string> pendingListItem)
        {
            if (pendingKey != null)
            {
                if (pendingList != null)
                {
                    lists[pendingKey] = pendingList;
                }
                else if (pendingMap != null)
                {
                    maps[pendingKey] = pendingMap;
                }
            }

            pendingKey = null;
            pendingMap = null;
            pendingList = null;
            pendingListItem = null;
        }

        private static bool TrySplitKeyValue(string content, out string key, out string value)
        {
            key = "";
            value = "";
            var separator = content.IndexOf(':');
            if (separator <= 0)
            {
                return false;
            }

            key = content.Substring(0, separator).Trim();
            value = StripComment(content.Substring(separator + 1).Trim());
            return key.Length > 0;
        }

        // 值里的 # 之后算注释；整个值被引号裹住时原样保留（连同里面的 #）。
        private static string StripComment(string value)
        {
            if (value.Length >= 2
                && ((value[0] == '"' && value[value.Length - 1] == '"')
                    || (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                return value.Substring(1, value.Length - 2);
            }

            var hash = value.IndexOf('#');
            return hash < 0 ? value : value.Substring(0, hash).TrimEnd();
        }

        private static int CountIndent(string line)
        {
            var count = 0;
            while (count < line.Length && line[count] == ' ')
            {
                count++;
            }

            return count;
        }
    }
}
