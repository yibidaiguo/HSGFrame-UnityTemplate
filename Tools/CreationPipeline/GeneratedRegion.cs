using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 「生成区」这件事本身：一对标记之间那段由机器写、人不许手改的正文。
    ///
    /// 需求案与模块策划案都有这么一段，机制一模一样——标记怎么找、旧的怎么剜掉、
    /// 哈希怎么算。**只留一处实现**：两处各写一遍的话，
    /// 哪天改了哈希的归一规则而只改了一边，「手改了生成区」这道判据就会在另一边失准，
    /// 而失准的方向是**误报**——文档明明没被动过却天天报红，人很快就学会无视它。
    /// </summary>
    public static class GeneratedRegion
    {
        /// <summary>
        /// 算生成区正文的哈希，形如 `sha256:1f4b…`。
        /// 行尾空白与末尾空行不计入——它们改了不算「手改了生成区」。
        /// </summary>
        /// <param name="lines">生成区正文行，不含标记行。</param>
        public static string Hash(IReadOnlyList<string> lines)
        {
            var normalized = new List<string>();
            foreach (var line in lines ?? Array.Empty<string>())
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

        /// <summary>
        /// 把整段生成区（含两行标记）从行表里剜掉；没有就什么都不做。
        ///
        /// 找不到结尾标记时**整段都不动**：那说明文档被截断或者标记被人删了一半，
        /// 这时按「从开头标记一路删到文末」处理会连人写的正文一起吃掉。
        /// </summary>
        /// <param name="lines">文档行表，就地修改。</param>
        /// <param name="beginMarker">开始标记行（比较时两端去空白）。</param>
        /// <param name="endMarker">结束标记行。</param>
        public static void Strip(List<string> lines, string beginMarker, string endMarker)
        {
            if (lines == null)
            {
                return;
            }

            var start = -1;
            var end = -1;
            for (var index = 0; index < lines.Count; index++)
            {
                var trimmed = lines[index].Trim();
                if (start < 0 && trimmed == beginMarker)
                {
                    start = index;
                }
                else if (start >= 0 && trimmed == endMarker)
                {
                    end = index;
                    break;
                }
            }

            if (start >= 0 && end > start)
            {
                lines.RemoveRange(start, end - start + 1);
            }
        }

        /// <summary>
        /// 读出生成区正文行（不含标记）。没有生成区时给空表，并把 present 置 false——
        /// **「没有生成区」与「生成区是空的」是两支**（决策 42）：前者是文档还没渲过，
        /// 后者是渲过但这次没内容，两种情形该做的事不一样。
        /// </summary>
        /// <param name="lines">文档行表。</param>
        /// <param name="beginMarker">开始标记行。</param>
        /// <param name="endMarker">结束标记行。</param>
        /// <param name="present">文档里到底有没有这一段。</param>
        public static IReadOnlyList<string> Read(
            IReadOnlyList<string> lines, string beginMarker, string endMarker, out bool present)
        {
            present = false;
            var body = new List<string>();
            if (lines == null)
            {
                return body;
            }

            var start = -1;
            for (var index = 0; index < lines.Count; index++)
            {
                var trimmed = lines[index].Trim();
                if (start < 0)
                {
                    if (trimmed == beginMarker)
                    {
                        start = index;
                    }

                    continue;
                }

                if (trimmed == endMarker)
                {
                    present = true;
                    return body;
                }

                body.Add(lines[index]);
            }

            // 只有开头没有结尾：当成「没有生成区」，正文一行都不认。
            body.Clear();
            return body;
        }
    }
}
