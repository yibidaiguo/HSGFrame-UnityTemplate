using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一条需求文档与下游那份文档的同步账：节点 token、链接、上次同步的正文哈希与时间。
    /// 四样东西住在 frontmatter 的「同步」块里（需求文档规范基线第一节已经把形状定死了），
    /// 这里只负责读它、算它、写回它。
    /// </summary>
    public sealed class RequirementDocumentSyncState
    {
        /// <summary>frontmatter 里同步块的键名。</summary>
        public const string SectionKey = "同步";

        /// <summary>同步块里「节点token」的键名。</summary>
        public const string NodeTokenKey = "节点token";

        /// <summary>同步块里「链接」的键名。</summary>
        public const string LinkKey = "链接";

        /// <summary>同步块里「最后同步hash」的键名。</summary>
        public const string LastHashKey = "最后同步hash";

        /// <summary>同步块里「最后同步时间」的键名。</summary>
        public const string LastTimeKey = "最后同步时间";

        /// <summary>
        /// 构造一份同步账。
        /// </summary>
        /// <param name="nodeToken">下游那份文档的节点 token；没推过时空串。</param>
        /// <param name="link">下游那份文档的链接；没推过时空串。</param>
        /// <param name="lastHash">上次推上去的正文哈希，形如 <c>sha256:1f4b…</c>；没推过时空串。</param>
        /// <param name="lastTime">上次推上去的时间，ISO-8601 UTC；没推过时空串。</param>
        public RequirementDocumentSyncState(string nodeToken, string link, string lastHash, string lastTime)
        {
            NodeToken = nodeToken ?? "";
            Link = link ?? "";
            LastHash = lastHash ?? "";
            LastTime = lastTime ?? "";
        }

        /// <summary>下游那份文档的节点 token；没推过时空串。</summary>
        public string NodeToken { get; }

        /// <summary>下游那份文档的链接；没推过时空串。</summary>
        public string Link { get; }

        /// <summary>上次推上去的正文哈希；没推过时空串。</summary>
        public string LastHash { get; }

        /// <summary>上次推上去的时间；没推过时空串。</summary>
        public string LastTime { get; }

        /// <summary>推过没有：认「节点token 非空」这一条。</summary>
        public bool HasBeenPushed => NodeToken.Length > 0;

        /// <summary>
        /// 这份正文要不要再推一次：没推过要推，正文哈希与上次对不上要推，其余不推。
        ///
        /// **判据是正文哈希，不是文件时间戳**——同一份内容重排一次格式、
        /// 或者 doc.render 跑一遍原样重写，时间戳都会变而内容没变。
        /// 按时间戳推的话每跑一次流水线就在下游刷一遍全文，改动历史里全是空版本。
        /// </summary>
        /// <param name="currentHash">当前正文的哈希，用 <see cref="HashBody"/> 算。</param>
        public bool NeedsPush(string currentHash)
        {
            return !HasBeenPushed || !string.Equals(LastHash, currentHash ?? "", StringComparison.Ordinal);
        }

        /// <summary>从一份解析好的文档里读同步账；没有同步块时四项全空。</summary>
        /// <param name="document">解析好的需求文档。</param>
        public static RequirementDocumentSyncState Read(RequirementDocument document)
        {
            if (document == null)
            {
                return new RequirementDocumentSyncState("", "", "", "");
            }

            var map = document.FrontMatter.Map(SectionKey);
            return new RequirementDocumentSyncState(
                ReadValue(map, NodeTokenKey),
                ReadValue(map, LinkKey),
                ReadValue(map, LastHashKey),
                ReadValue(map, LastTimeKey));
        }

        /// <summary>
        /// 算正文哈希：只算 frontmatter 之后的正文，形如 <c>sha256:1f4b…</c>（前 16 位十六进制）。
        ///
        /// **frontmatter 不进哈希**，理由就在这个类自己身上：同步账本身就写在 frontmatter 里，
        /// 把它算进去的话每推一次都会改哈希，于是下一次又判定「变了、要再推」，
        /// 一条需求会自己把自己推到天荒地老。
        /// </summary>
        /// <param name="documentText">index.md 全文。</param>
        public static string HashBody(string documentText)
        {
            var body = StripFrontMatter(documentText ?? "");
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(body));
                var builder = new StringBuilder("sha256:");
                for (var index = 0; index < 8; index++)
                {
                    builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        /// <summary>
        /// 把同步账写回全文的 frontmatter：已有「同步」块整块换掉，没有就补在 frontmatter 末尾。
        /// 正文一个字都不碰——这一条与 doc.render「只加不改」是同一条规矩。
        /// </summary>
        /// <param name="documentText">index.md 全文。</param>
        /// <param name="state">要写进去的同步账。</param>
        /// <exception cref="InvalidOperationException">文档没有 frontmatter 时抛出：没有地方可写。</exception>
        public static string Write(string documentText, RequirementDocumentSyncState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var text = documentText ?? "";
            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = new List<string>(text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
            if (lines.Count == 0 || lines[0].Trim() != "---")
            {
                throw new InvalidOperationException("这份文档没有 frontmatter，同步账没有地方可写；先跑一次 doc.render");
            }

            var closing = -1;
            for (var index = 1; index < lines.Count; index++)
            {
                if (lines[index].Trim() == "---")
                {
                    closing = index;
                    break;
                }
            }

            if (closing < 0)
            {
                throw new InvalidOperationException("这份文档的 frontmatter 没有收尾的 `---`，不敢往里写");
            }

            var start = -1;
            var end = closing;
            for (var index = 1; index < closing; index++)
            {
                if (lines[index].Trim() == SectionKey + ":")
                {
                    start = index;
                    end = index + 1;
                    while (end < closing && lines[end].StartsWith(" ", StringComparison.Ordinal))
                    {
                        end++;
                    }

                    break;
                }
            }

            var block = BuildBlock(state);
            if (start < 0)
            {
                lines.InsertRange(closing, block);
            }
            else
            {
                lines.RemoveRange(start, end - start);
                lines.InsertRange(start, block);
            }

            return string.Join(newline, lines);
        }

        /// <summary>拼同步块的四行；缩进两格，与规范里那份样例一模一样。</summary>
        private static List<string> BuildBlock(RequirementDocumentSyncState state)
        {
            return new List<string>
            {
                SectionKey + ":",
                "  " + NodeTokenKey + ": " + state.NodeToken,
                "  " + LinkKey + ": " + state.Link,
                "  " + LastHashKey + ": " + state.LastHash,
                "  " + LastTimeKey + ": " + state.LastTime
            };
        }

        /// <summary>去掉开头那段 frontmatter，返回正文；没有 frontmatter 时原样返回。</summary>
        private static string StripFrontMatter(string documentText)
        {
            var lines = documentText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != "---")
            {
                return documentText.Replace("\r\n", "\n").Replace('\r', '\n');
            }

            for (var index = 1; index < lines.Length; index++)
            {
                if (lines[index].Trim() == "---")
                {
                    return string.Join("\n", lines, index + 1, lines.Length - index - 1);
                }
            }

            return string.Join("\n", lines);
        }

        private static string ReadValue(IReadOnlyDictionary<string, string> map, string key)
        {
            return map != null && map.TryGetValue(key, out var value) ? value ?? "" : "";
        }
    }
}
