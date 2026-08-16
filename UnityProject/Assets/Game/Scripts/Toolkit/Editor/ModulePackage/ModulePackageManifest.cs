using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Template.Toolkit.Editor
{
    /// <summary>
    /// 读写 <c>UnityProject/Packages/manifest.json</c> 的 dependencies 块。
    /// 整份反序列化再写回会把人手维护的键序与缩进洗掉，所以跟 FeatureRemover 一样按行改：
    /// 只动目标那一行，行尾逗号统一在最后重排一次。
    /// </summary>
    public static class ModulePackageManifest
    {
        private const string DependenciesKey = "\"dependencies\"";

        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        /// <summary>
        /// 读出 dependencies 块里的全部键值；文件不存在或结构不认识时返回空表。
        /// </summary>
        /// <param name="manifestPath">manifest.json 的完整路径。</param>
        public static IReadOnlyDictionary<string, string> ReadDependencies(string manifestPath)
        {
            var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(manifestPath))
            {
                return dependencies;
            }

            var lines = ReadLines(manifestPath, out _);
            if (!TryLocateBlock(lines, out var firstEntryLine, out var closeLine))
            {
                return dependencies;
            }

            for (var index = firstEntryLine; index < closeLine; index++)
            {
                if (TryReadEntry(lines[index], out var key, out var value))
                {
                    dependencies[key] = value;
                }
            }

            return dependencies;
        }

        /// <summary>
        /// 往 dependencies 里加一条。已经在里面时不改文件、按成功返回。
        /// </summary>
        /// <param name="manifestPath">manifest.json 的完整路径。</param>
        /// <param name="packageName">要写入的包名。</param>
        /// <param name="versionExpression">写入的值，本地包是 file: 路径，第三方包是版本号或 git 地址。</param>
        /// <param name="message">给人看的一行结果说明。</param>
        public static bool TryAddDependency(
            string manifestPath, string packageName, string versionExpression, out string message)
        {
            if (!TryReadForEdit(manifestPath, packageName, out var lines, out var hasBom, out var block, out message))
            {
                return false;
            }

            if (block.EntryLines.ContainsKey(packageName))
            {
                message = $"{packageName} 已经在清单里，没有改动";
                return true;
            }

            // 落点：最后一条本地包条目之后。清单里的键序是人手排的、并非字典序，
            // 硬按字典序插会把这份秩序搅乱；跟着同类条目走，diff 才看得懂。
            var anchorLine = block.LastLocalEntryLine >= 0 ? block.LastLocalEntryLine : block.LastEntryLine;
            var indent = block.EntryIndent;
            var entryText = $"{indent}\"{packageName}\": \"{versionExpression}\"";

            if (anchorLine >= 0)
            {
                lines.Insert(anchorLine + 1, entryText);
            }
            else
            {
                // 一条依赖都没有：dependencies 那行的下一行就是落点。
                lines.Insert(block.OpenLine + 1, entryText);
            }

            if (!TryRewriteCommas(lines, out message))
            {
                return false;
            }

            WriteLines(manifestPath, lines, hasBom);
            message = $"已写入 {packageName}: {versionExpression}";
            return true;
        }

        /// <summary>
        /// 从 dependencies 里摘掉一条。本来就不在时不改文件、按成功返回。
        /// </summary>
        /// <param name="manifestPath">manifest.json 的完整路径。</param>
        /// <param name="packageName">要摘掉的包名。</param>
        /// <param name="message">给人看的一行结果说明。</param>
        public static bool TryRemoveDependency(string manifestPath, string packageName, out string message)
        {
            if (!TryReadForEdit(manifestPath, packageName, out var lines, out var hasBom, out var block, out message))
            {
                return false;
            }

            if (!block.EntryLines.TryGetValue(packageName, out var entryLine))
            {
                message = $"{packageName} 本来就不在清单里，没有改动";
                return true;
            }

            lines.RemoveAt(entryLine);

            if (!TryRewriteCommas(lines, out message))
            {
                return false;
            }

            WriteLines(manifestPath, lines, hasBom);
            message = $"已摘掉 {packageName}";
            return true;
        }

        private static bool TryReadForEdit(
            string manifestPath,
            string packageName,
            out List<string> lines,
            out bool hasBom,
            out DependencyBlock block,
            out string message)
        {
            lines = null;
            hasBom = false;
            block = null;

            if (string.IsNullOrWhiteSpace(packageName))
            {
                message = ComposeError("（空包名）", "没有指定要改哪个包", "传入包名", "com.hsgframe.timer");
                return false;
            }

            if (!File.Exists(manifestPath))
            {
                message = ComposeError(
                    manifestPath ?? "（空路径）", "找不到清单文件", "确认工程里有这份清单",
                    "UnityProject/Packages/manifest.json");
                return false;
            }

            lines = ReadLines(manifestPath, out hasBom);
            if (!TryDescribeBlock(lines, out block))
            {
                message = ComposeError(
                    manifestPath, "清单里找不到成对的 dependencies 块", "确认清单是标准 UPM 格式、没有被改坏",
                    "UnityProject/Packages/manifest.json");
                return false;
            }

            message = null;
            return true;
        }

        /// <summary>把 dependencies 块里除末条以外的条目行都补上行尾逗号，末条去掉——增删之后统一重排一次。</summary>
        private static bool TryRewriteCommas(List<string> lines, out string message)
        {
            if (!TryDescribeBlock(lines, out var block))
            {
                message = ComposeError(
                    "manifest.json", "改动之后 dependencies 块对不上了", "撤销这次改动，检查清单结构",
                    "UnityProject/Packages/manifest.json");
                return false;
            }

            var entryLineNumbers = new List<int>();
            for (var index = block.OpenLine + 1; index < block.CloseLine; index++)
            {
                if (TryReadEntry(lines[index], out _, out _))
                {
                    entryLineNumbers.Add(index);
                }
            }

            for (var position = 0; position < entryLineNumbers.Count; position++)
            {
                var lineNumber = entryLineNumbers[position];
                var bare = lines[lineNumber].TrimEnd().TrimEnd(',');
                lines[lineNumber] = position == entryLineNumbers.Count - 1 ? bare : bare + ",";
            }

            message = null;
            return true;
        }

        private static bool TryLocateBlock(List<string> lines, out int firstEntryLine, out int closeLine)
        {
            firstEntryLine = -1;
            closeLine = -1;
            if (!TryDescribeBlock(lines, out var block))
            {
                return false;
            }

            firstEntryLine = block.OpenLine + 1;
            closeLine = block.CloseLine;
            return true;
        }

        /// <summary>按大括号配对切出 dependencies 块，顺带记下条目行、缩进与最后一条本地包条目的位置。</summary>
        private static bool TryDescribeBlock(List<string> lines, out DependencyBlock block)
        {
            block = null;

            var openLine = lines.FindIndex(line => line.Contains(DependenciesKey, StringComparison.Ordinal));
            if (openLine < 0 || !lines[openLine].Contains("{", StringComparison.Ordinal))
            {
                return false;
            }

            var depth = CountBraceDepth(lines[openLine]);
            var closeLine = -1;
            for (var index = openLine + 1; index < lines.Count; index++)
            {
                depth += CountBraceDepth(lines[index]);
                if (depth <= 0)
                {
                    closeLine = index;
                    break;
                }
            }

            if (closeLine < 0)
            {
                return false;
            }

            var described = new DependencyBlock
            {
                OpenLine = openLine,
                CloseLine = closeLine,
                EntryIndent = "    ",
                LastEntryLine = -1,
                LastLocalEntryLine = -1,
                EntryLines = new Dictionary<string, int>(StringComparer.Ordinal),
            };

            var sawFirstEntry = false;
            for (var index = openLine + 1; index < closeLine; index++)
            {
                if (!TryReadEntry(lines[index], out var key, out var value))
                {
                    continue;
                }

                if (!sawFirstEntry)
                {
                    described.EntryIndent = ReadIndent(lines[index]);
                    sawFirstEntry = true;
                }

                described.EntryLines[key] = index;
                described.LastEntryLine = index;
                if (value.StartsWith("file:", StringComparison.Ordinal))
                {
                    described.LastLocalEntryLine = index;
                }
            }

            block = described;
            return true;
        }

        private static bool TryReadEntry(string line, out string key, out string value)
        {
            key = null;
            value = null;

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("\"", StringComparison.Ordinal))
            {
                return false;
            }

            var keyEnd = trimmed.IndexOf('"', 1);
            if (keyEnd < 0)
            {
                return false;
            }

            var separator = trimmed.IndexOf(':', keyEnd);
            if (separator < 0)
            {
                return false;
            }

            var rest = trimmed.Substring(separator + 1).Trim().TrimEnd(',').Trim();
            if (rest.Length < 2 || !rest.StartsWith("\"", StringComparison.Ordinal)
                                || !rest.EndsWith("\"", StringComparison.Ordinal))
            {
                return false;
            }

            key = trimmed.Substring(1, keyEnd - 1);
            value = rest.Substring(1, rest.Length - 2);
            return true;
        }

        /// <summary>数一行里的大括号净增减，字符串字面量里的括号不算。</summary>
        private static int CountBraceDepth(string line)
        {
            var depth = 0;
            var insideString = false;
            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (insideString)
                {
                    if (character == '\\')
                    {
                        index++;
                        continue;
                    }

                    if (character == '"')
                    {
                        insideString = false;
                    }

                    continue;
                }

                switch (character)
                {
                    case '"':
                        insideString = true;
                        break;
                    case '{':
                        depth++;
                        break;
                    case '}':
                        depth--;
                        break;
                }
            }

            return depth;
        }

        private static string ReadIndent(string line)
        {
            var length = 0;
            while (length < line.Length && (line[length] == ' ' || line[length] == '\t'))
            {
                length++;
            }

            return line.Substring(0, length);
        }

        private static List<string> ReadLines(string filePath, out bool hasBom)
        {
            var bytes = File.ReadAllBytes(filePath);
            hasBom = bytes.Length >= Utf8Bom.Length
                     && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2];
            var text = hasBom
                ? Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length)
                : Encoding.UTF8.GetString(bytes);
            return new List<string>(text.Replace("\r\n", "\n").Split('\n'));
        }

        private static void WriteLines(string filePath, IReadOnlyList<string> lines, bool hasBom)
        {
            File.WriteAllText(filePath, string.Join("\n", lines), new UTF8Encoding(hasBom));
        }

        private static string ComposeError(string location, string reason, string fix, string reference)
        {
            return $"位置：{location}；原因：{reason}；修复：{fix}；参考：{reference}";
        }

        /// <summary>dependencies 块的位置账：起止行、条目行、缩进、最后一条本地包条目在哪。</summary>
        private sealed class DependencyBlock
        {
            public int OpenLine { get; set; }

            public int CloseLine { get; set; }

            public string EntryIndent { get; set; }

            public int LastEntryLine { get; set; }

            public int LastLocalEntryLine { get; set; }

            public Dictionary<string, int> EntryLines { get; set; }
        }
    }
}
