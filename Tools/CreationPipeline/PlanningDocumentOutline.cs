using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 中性文档块：一条策划文档拆出来的一段东西，**不带任何下游的形状**。
    /// 「标题 / 段落 / 有序项 / 无序项 / 引用 / 代码 / 媒体」这七种就是全部——
    /// 正好覆盖策划文档规范（基线第一节）里那份 index.md 用到的 markdown 词汇。
    /// </summary>
    public sealed class PlanningDocumentOutlineBlock
    {
        /// <summary>
        /// 构造一个中性块。
        /// </summary>
        /// <param name="kind">块类型，取 <see cref="PlanningDocumentOutline"/> 上那七个常量之一。</param>
        /// <param name="text">正文文本；媒体块放说明。</param>
        /// <param name="level">标题层级 1/2/3；不是标题时为 0。</param>
        /// <param name="target">媒体块的相对路径；其余块为空串。</param>
        public PlanningDocumentOutlineBlock(string kind, string text, int level = 0, string target = "")
        {
            Kind = kind ?? "";
            Text = text ?? "";
            Level = level;
            Target = target ?? "";
        }

        /// <summary>块类型。</summary>
        public string Kind { get; }

        /// <summary>正文文本；媒体块放说明。</summary>
        public string Text { get; }

        /// <summary>标题层级 1/2/3；不是标题时为 0。</summary>
        public int Level { get; }

        /// <summary>媒体块的相对路径；其余块为空串。</summary>
        public string Target { get; }

        /// <summary>序列化成协议 JSON 节点：键是中文，与信封其余部分同一套写法。</summary>
        public JsonObject ToJsonNode()
        {
            var node = new JsonObject
            {
                ["类型"] = Kind,
                ["文本"] = Text
            };
            if (Level > 0)
            {
                node["层级"] = Level;
            }

            if (Target.Length > 0)
            {
                node["目标"] = Target;
            }

            return node;
        }

        /// <summary>从协议 JSON 读回一个块；「类型」不是字符串时读成空类型，由调用方当不认识处理。</summary>
        /// <param name="element">一个块的 JSON 对象。</param>
        public static PlanningDocumentOutlineBlock FromJson(JsonElement element)
        {
            return new PlanningDocumentOutlineBlock(
                ReadString(element, "类型"),
                ReadString(element, "文本"),
                element.TryGetProperty("层级", out var level) && level.ValueKind == JsonValueKind.Number ? level.GetInt32() : 0,
                ReadString(element, "目标"));
        }

        private static string ReadString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }
    }

    /// <summary>
    /// 把 `index.md` 拆成一串中性块，供任意下游各自翻译成自己的形状。
    ///
    /// **这一层刻意不认识任何一个具体下游**，与建表那条链完全对称：工具链出中性的「建表描述」，
    /// 桥那边用自己的字段类型 codec 把它翻成那个下游的字段类型。文档这条链同样——
    /// 这里出中性块，桥那边用自己的块 codec 翻成那个下游的文档块。
    /// 换一个下游（换个知识库、换个 wiki、换个静态站）时要重写的只有那半页 codec，
    /// 「一份 md 怎么拆」这件事一个字都不用动，也不用再被测一遍。
    ///
    /// 这段注释本身就被下游边界门禁盯着：在这个文件里写下任何一个 driver 的名字都会判红，
    /// 哪怕只是在注释里举个例子。那道门禁是对的——例子写着写着就会变成代码。
    ///
    /// 两处刻意的取舍：
    /// **一、frontmatter 不进块流。** 它是给机器判定用的字段，不是文档正文；
    /// 需要哪个字段就由调用方单独取（标题就是这么来的），整段塞进正文只会让下游那份文档多出一坨噪音。
    /// **二、生成区的两行 HTML 注释标记丢掉，中间的正文照留。** docx 里没有「注释」这种东西，
    /// 而标记的用处（判定生成区有没有被手改）只在仓库这一侧成立——下游那份是投影，不是正本。
    /// </summary>
    public static class PlanningDocumentOutline
    {
        /// <summary>块类型：标题，层级看 Level。</summary>
        public const string KindHeading = "标题";

        /// <summary>块类型：普通段落。</summary>
        public const string KindParagraph = "段落";

        /// <summary>块类型：有序列表的一项。</summary>
        public const string KindOrderedItem = "有序项";

        /// <summary>块类型：无序列表的一项。</summary>
        public const string KindBulletItem = "无序项";

        /// <summary>块类型：引用行（`&gt; ` 开头）。</summary>
        public const string KindQuote = "引用";

        /// <summary>块类型：代码块整段，Text 是块内全文。</summary>
        public const string KindCode = "代码";

        /// <summary>块类型：媒体引用，Text 是说明、Target 是相对路径。</summary>
        public const string KindMedia = "媒体";

        /// <summary>
        /// 把一份策划文档全文拆成中性块。frontmatter 与生成区标记行不进结果；
        /// 空行只用来断段，不产块。认不出来的行一律当段落——
        /// **降级成段落而不是报错**：文档是人写的，冒出一个没见过的写法时
        /// 该把它原样送到下游去，而不是让整条同步链停在「第 7 行我不认识」上。
        /// </summary>
        /// <param name="documentText">index.md 全文。</param>
        public static IReadOnlyList<PlanningDocumentOutlineBlock> Build(string documentText)
        {
            var blocks = new List<PlanningDocumentOutlineBlock>();
            var lines = SplitLines(documentText ?? "");
            var index = SkipFrontMatter(lines);
            var paragraph = new List<string>();

            void FlushParagraph()
            {
                if (paragraph.Count == 0)
                {
                    return;
                }

                blocks.Add(new PlanningDocumentOutlineBlock(KindParagraph, string.Join(" ", paragraph)));
                paragraph.Clear();
            }

            while (index < lines.Count)
            {
                var line = lines[index];
                var trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    FlushParagraph();
                    index++;
                    continue;
                }

                // 生成区的两条标记是 HTML 注释，只在仓库侧有意义，丢掉不往下游送。
                if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    index++;
                    continue;
                }

                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    index = ReadCodeFence(lines, index, blocks);
                    continue;
                }

                var headingLevel = HeadingLevelOf(trimmed);
                if (headingLevel > 0)
                {
                    FlushParagraph();
                    blocks.Add(new PlanningDocumentOutlineBlock(
                        KindHeading, trimmed.Substring(headingLevel).Trim(), headingLevel));
                    index++;
                    continue;
                }

                if (TryReadMedia(trimmed, out var mediaBlock))
                {
                    FlushParagraph();
                    blocks.Add(mediaBlock);
                    index++;
                    continue;
                }

                if (TryReadOrderedItem(trimmed, out var orderedText))
                {
                    FlushParagraph();
                    blocks.Add(new PlanningDocumentOutlineBlock(KindOrderedItem, orderedText));
                    index++;
                    continue;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    blocks.Add(new PlanningDocumentOutlineBlock(KindBulletItem, trimmed.Substring(2).Trim()));
                    index++;
                    continue;
                }

                if (trimmed.StartsWith("> ", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    blocks.Add(new PlanningDocumentOutlineBlock(KindQuote, trimmed.Substring(2).Trim()));
                    index++;
                    continue;
                }

                // 连着的几行普通文字合成一段：md 里换行不断段，下游那边一行一块的话
                // 一段话会被拆成七八块，读起来全是碎的。
                paragraph.Add(trimmed);
                index++;
            }

            FlushParagraph();
            return blocks;
        }

        /// <summary>把一串块序列化成协议数组，供请求信封的载荷直接挂上去。</summary>
        /// <param name="blocks">中性块。</param>
        public static JsonArray ToJsonArray(IReadOnlyList<PlanningDocumentOutlineBlock> blocks)
        {
            var array = new JsonArray();
            if (blocks == null)
            {
                return array;
            }

            foreach (var block in blocks)
            {
                array.Add(block.ToJsonNode());
            }

            return array;
        }

        /// <summary>从协议数组读回一串块；不是数组时给空清单。</summary>
        /// <param name="element">块数组的 JSON。</param>
        public static IReadOnlyList<PlanningDocumentOutlineBlock> FromJsonArray(JsonElement element)
        {
            var blocks = new List<PlanningDocumentOutlineBlock>();
            if (element.ValueKind != JsonValueKind.Array)
            {
                return blocks;
            }

            foreach (var item in element.EnumerateArray())
            {
                blocks.Add(PlanningDocumentOutlineBlock.FromJson(item));
            }

            return blocks;
        }

        /// <summary>读代码围栏：返回围栏结束之后的行号，并把整段作为一个代码块加进去。</summary>
        private static int ReadCodeFence(IReadOnlyList<string> lines, int fenceIndex, List<PlanningDocumentOutlineBlock> blocks)
        {
            var body = new List<string>();
            var index = fenceIndex + 1;
            while (index < lines.Count && !lines[index].Trim().StartsWith("```", StringComparison.Ordinal))
            {
                body.Add(lines[index]);
                index++;
            }

            blocks.Add(new PlanningDocumentOutlineBlock(KindCode, string.Join("\n", body)));

            // 收尾那行围栏也吃掉；文档结尾少一条围栏时 index 已经到头，不会越界。
            return index < lines.Count ? index + 1 : index;
        }

        /// <summary>`#` 的个数就是层级，最多认到三级；不是标题给 0。</summary>
        private static int HeadingLevelOf(string trimmed)
        {
            var level = 0;
            while (level < trimmed.Length && trimmed[level] == '#')
            {
                level++;
            }

            if (level == 0 || level > 3 || level >= trimmed.Length || trimmed[level] != ' ')
            {
                return 0;
            }

            return level;
        }

        /// <summary>认 `1. 正文` 这种有序项；序号本身不留——下游自己会编号。</summary>
        private static bool TryReadOrderedItem(string trimmed, out string text)
        {
            text = "";
            var digits = 0;
            while (digits < trimmed.Length && char.IsDigit(trimmed[digits]))
            {
                digits++;
            }

            if (digits == 0 || digits + 1 >= trimmed.Length || trimmed[digits] != '.' || trimmed[digits + 1] != ' ')
            {
                return false;
            }

            text = trimmed.Substring(digits + 2).Trim();
            return true;
        }

        /// <summary>认 `![说明](media/x.png)` 与 `[说明](media/x.mp4)` 两种媒体引用。</summary>
        private static bool TryReadMedia(string trimmed, out PlanningDocumentOutlineBlock block)
        {
            block = null;
            var body = trimmed.StartsWith("!", StringComparison.Ordinal) ? trimmed.Substring(1) : trimmed;
            if (!body.StartsWith("[", StringComparison.Ordinal) || !body.EndsWith(")", StringComparison.Ordinal))
            {
                return false;
            }

            var close = body.IndexOf("](", StringComparison.Ordinal);
            if (close <= 0)
            {
                return false;
            }

            var caption = body.Substring(1, close - 1);
            var target = body.Substring(close + 2, body.Length - close - 3);
            if (!target.StartsWith(PoolPaths.RequirementMediaDirectoryName + "/", StringComparison.Ordinal))
            {
                return false;
            }

            block = new PlanningDocumentOutlineBlock(KindMedia, caption, 0, target);
            return true;
        }

        /// <summary>跳过开头那段 `---` 包起来的 frontmatter，返回正文第一行的行号。</summary>
        private static int SkipFrontMatter(IReadOnlyList<string> lines)
        {
            if (lines.Count == 0 || lines[0].Trim() != "---")
            {
                return 0;
            }

            for (var index = 1; index < lines.Count; index++)
            {
                if (lines[index].Trim() == "---")
                {
                    return index + 1;
                }
            }

            // 只有开头那条横线、没有收尾的：当作没有 frontmatter，整篇都是正文。
            return 0;
        }

        /// <summary>按行切开，三种换行都认；结尾的空行不产生多余的一行。</summary>
        private static List<string> SplitLines(string text)
        {
            return new List<string>(text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        }
    }
}
