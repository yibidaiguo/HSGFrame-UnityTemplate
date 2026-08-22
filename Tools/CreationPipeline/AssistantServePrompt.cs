using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 助手 B 形态（常驻会话）一轮的提示词：系统提示 + 知识文件 + 输出契约 + 用户这句话。
    ///
    /// **知识与 A 形态（配置包）共用同一批文件**（子文档 02 §五）：
    /// 系统提示与知识都读 <c>_Generated/Bridges/&lt;driver&gt;/assistant-package/</c> 下那几份，
    /// 不另抄一份——抄一份就等于开了第二个事实源，两边迟早说不一样的话。
    ///
    /// **提示词版本是算出来的，不是写死的常量。** P8 批次 5 留过一个洞：
    /// `prereview-v1` 是硬编码的，改了提示词而不改它，旧缓存会带着新提示词的名义命中
    /// （决策 90 就白写了）。这里改成对「系统提示 + 知识 + 输出契约」整体取哈希，
    /// 内容一变版本就变，人不可能忘。
    /// </summary>
    public sealed class AssistantServePrompt
    {
        /// <summary>
        /// 构造一份提示词。
        /// </summary>
        /// <param name="promptText">发给执行后端的完整提示文本。</param>
        /// <param name="systemContext">系统上下文（角色与硬约束）。</param>
        /// <param name="promptVersion">提示词版本，由内容算出。</param>
        /// <param name="knowledgeFileCount">读进来的知识文件数。</param>
        /// <param name="degradedReason">知识缺失时的降级说明；正常为空串。</param>
        public AssistantServePrompt(
            string promptText,
            string systemContext,
            string promptVersion,
            int knowledgeFileCount,
            string degradedReason)
        {
            PromptText = promptText ?? "";
            SystemContext = systemContext ?? "";
            PromptVersion = promptVersion ?? "";
            KnowledgeFileCount = knowledgeFileCount;
            DegradedReason = degradedReason ?? "";
        }

        /// <summary>发给执行后端的完整提示文本。</summary>
        public string PromptText { get; }

        /// <summary>系统上下文（角色与硬约束）。</summary>
        public string SystemContext { get; }

        /// <summary>提示词版本，由内容算出——内容变，版本就变。</summary>
        public string PromptVersion { get; }

        /// <summary>读进来的知识文件数。</summary>
        public int KnowledgeFileCount { get; }

        /// <summary>知识缺失时的降级说明；正常为空串。知识缺了照样能跑，但要**说出来**。</summary>
        public string DegradedReason { get; }

        /// <summary>输出契约：模型必须回一份这个形状的 JSON。写在这里是为了让它进版本哈希。</summary>
        public const string OutputContract =
            "你的回答必须是一份 JSON 对象，且只有 JSON，不许有解释、不许包在代码块里。形状：\n"
            + "{\n"
            + "  \"回话\": \"给人看的中文回复：先说你听懂了什么，再往前推一步\",\n"
            + "  \"我理解你想干的\": \"一句话说清这个人想要什么（做什么系统 / 改什么 / 要什么图 / 修什么问题）\",\n"
            + "  \"要问的问题\": [\"这一轮最想确认的点，最多两条，问人话；没有要问的给空数组\"],\n"
            + "  \"要什么\": \"功能\" 或 \"图\",\n"
            + "  \"要不要建需求\": true 或 false,\n"
            + "  \"出图请求\": { \"资产类型\": \"图标 / 界面底图 / 立绘…\", \"命名\": \"英文小写下划线，如 icon_bag\", \"描述\": \"画什么，一段话\", \"变体数\": 6 },\n"
            + "  \"需求草稿\": { 需求对象，字段名照 schema 摘要里的中文字段名；\"要不要建需求\"为 false 时给 null }\n"
            + "}\n"
            + "硬规矩：\n"
            + "1. **一轮最多问两条**，别问第三条。人是来把事说清楚的，不是来填表的。\n"
            + "   把字段名罗列成清单甩回去（「还缺：类型、标题、验收标准」这种）是错的写法，一律不许。\n"
            + "2. **能从上下文推出来的，先替人填进草稿**，并在回话里说明「我先按 X 填了，不对你就说」。\n"
            + "   能推的也拿去问，人只会觉得你在刁难他。\n"
            + "3. 推断有边界：只许从这个人已经说过的话、知识里的既有设计往下推，\n"
            + "   **不许凭空发明他没提过的玩法、数值与范围**。真无从推断、不问就会做错的，才问。\n"
            + "4. 「要不要建需求」= 草稿已经立得住（该有的都有值、验收标准能一条条勾）。\n"
            + "   为 true 必须给「需求草稿」；确实立不住就 false，回话里说清还差哪一步。\n"
            + "5. 「需求草稿」里不许出现 schema 摘要之外的字段。\n"
            + "6. id、状态、来源、锁定、schema版本 这几个工程侧字段不用你填，引擎会补。\n"
            + "7. 「验收标准」是字符串数组，一条一句，能一条条勾。\n"
            + "8. 建不建、什么时候建，**由人点按钮决定**——你只负责把草稿整理到能看懂的程度。\n"
            + "   所以回话里不许写「已经建好了」，要写「你看看对不对」。\n"
            + "9. **「要什么」决定这一轮往哪走，别搞混**：\n"
            + "   · 人要的是「做出某个东西」（系统、改动、修 BUG）→ 填「功能」，走「需求草稿」。\n"
            + "   · 人要的是**一张图**（图标、界面图、立绘、效果图、参考图）→ 填「图」，走「出图请求」，\n"
            + "     「需求草稿」给 null。**这一支是真去生图的**，不是把要图这件事写成一条需求。\n"
            + "   · 分不清就看他要的东西交付出来是什么：是能运行的功能，还是一张图片文件。\n"
            + "10. 填「出图请求」时：「资产类型」照资产规格里的名字写（图标 / 界面底图 / 立绘 …）；\n"
            + "    「命名」是英文小写加下划线，看得出画的是什么；「描述」写**画面本身**——画什么、\n"
            + "    什么风格、什么构图。别写「用于确认视觉方向」这类目的性的话，那对出图没有用。";

        /// <summary>
        /// 组一轮的提示词。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">助手 port 路由到的 driver 名，一律走参数（决策 17）。</param>
        /// <param name="userText">用户这一句话。</param>
        /// <param name="historyText">这条会话之前聊过什么（已按轮数与字数裁过）；空串表示没有历史。</param>
        public static AssistantServePrompt Build(
            string repositoryRoot,
            string driverName,
            string userText,
            string historyText = "")
        {
            var degraded = new List<string>();

            var systemPromptFile = Path.Combine(
                ProvisionPaths.AssistantPackageDirectory(repositoryRoot, driverName),
                "system-prompt.md");
            var systemPrompt = ReadTextOrEmpty(systemPromptFile);
            if (systemPrompt.Length == 0)
            {
                degraded.Add("读不到助手系统提示（" + systemPromptFile + "）——先跑一次 bridge.provision");
                // 降级时 schema 摘要也一起没了，输出契约却要求「字段名照 schema 摘要」——
                // 那张表正好在读不到的文件里。所以降级轮一律不建需求，只回话说明缺供给。
                systemPrompt = "你是策划、美术、程序都能找的需求助手。（注意：本轮没能读到供给产出的系统提示与 schema 摘要，"
                    + "知识是降级的——「要不要建需求」一律回 false，"
                    + "把「要问的问题」写成「助手知识未供给，请先跑 bridge.provision」。）";
            }

            var knowledgeTexts = new List<string>();
            var knowledgeDirectory = ProvisionPaths.AssistantKnowledgeDirectory(repositoryRoot, driverName);
            if (Directory.Exists(knowledgeDirectory))
            {
                foreach (var file in Directory
                    .GetFiles(knowledgeDirectory, "*.md", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
                {
                    var text = ReadTextOrEmpty(file);
                    if (text.Length > 0)
                    {
                        knowledgeTexts.Add("### 知识文件：" + Path.GetFileName(file) + "\n\n" + text);
                    }
                }
            }

            if (knowledgeTexts.Count == 0)
            {
                degraded.Add("助手知识目录里一个文件都没读到（" + knowledgeDirectory + "）");
            }

            var builder = new StringBuilder();
            builder.AppendLine("## 你的角色与规矩");
            builder.AppendLine();
            builder.AppendLine(systemPrompt);
            builder.AppendLine();
            if (knowledgeTexts.Count > 0)
            {
                builder.AppendLine("## 知识");
                builder.AppendLine();
                foreach (var text in knowledgeTexts)
                {
                    builder.AppendLine(text);
                    builder.AppendLine();
                }
            }

            builder.AppendLine("## 输出契约");
            builder.AppendLine();
            builder.AppendLine(OutputContract);
            builder.AppendLine();

            // 历史在用户这句话**之前**出现：模型读到最后一句时，前因已经在手上了。
            // 顺序反过来的话，长历史会把「他刚说的那句」挤到注意力的边上。
            if (!string.IsNullOrWhiteSpace(historyText))
            {
                builder.AppendLine("## 之前聊过什么（同一条会话，从上到下按时间）");
                builder.AppendLine();
                builder.AppendLine(historyText);
                builder.AppendLine();
                builder.AppendLine("接着上面的聊，别把已经问过的再问一遍。");
                builder.AppendLine();
            }

            builder.AppendLine("## 这个人刚说");
            builder.AppendLine();
            builder.AppendLine(userText ?? "");

            var promptText = builder.ToString();

            // 版本只对「稳定的那几段」取哈希：系统提示 + 知识 + 输出契约。
            // 用户那句话每次都不一样，把它算进去等于每轮一个新版本，版本就没意义了。
            var stablePart = systemPrompt + "\n" + string.Join("\n", knowledgeTexts) + "\n" + OutputContract;
            var version = "assist-serve-" + ShortHash(stablePart);

            return new AssistantServePrompt(
                promptText,
                SystemContextText,
                version,
                knowledgeTexts.Count,
                string.Join("；", degraded));
        }

        /// <summary>系统上下文：给执行后端的角色设定，与提示词一起进版本哈希之外，单独固定。</summary>
        public const string SystemContextText =
            "你是游戏项目里策划、美术、程序都能找的需求助手，只回 JSON，不回别的。"
            + "先把人想干的事聊明白，再谈落表；能从上下文推断的先替人填上并说明，"
            + "但不许凭空发明他没提过的内容。";

        /// <summary>算一段文本的短哈希（sha256 前 12 位十六进制），当版本号用。</summary>
        /// <param name="text">要取哈希的文本。</param>
        public static string ShortHash(string text)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
            var builder = new StringBuilder();
            for (var index = 0; index < 6; index++)
            {
                builder.Append(bytes[index].ToString("x2"));
            }

            return builder.ToString();
        }

        /// <summary>读文本文件；读不动给空串（调用方据此走降级并说明原因）。</summary>
        private static string ReadTextOrEmpty(string filePath)
        {
            try
            {
                return File.Exists(filePath) ? File.ReadAllText(filePath) : "";
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return "";
            }
        }
    }
}
