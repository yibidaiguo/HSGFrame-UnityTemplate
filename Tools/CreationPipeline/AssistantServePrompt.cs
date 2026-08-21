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
            + "  \"回话\": \"给提需求的人看的中文回复，说明你理解成了什么、还缺什么\",\n"
            + "  \"要不要建需求\": true 或 false,\n"
            + "  \"还缺什么\": [\"缺的字段或信息，一条一句；不缺给空数组\"],\n"
            + "  \"需求草稿\": { 需求对象，字段名照 schema 摘要里的中文字段名；\"要不要建需求\"为 false 时给 null }\n"
            + "}\n"
            + "硬规矩：\n"
            + "1. 信息不足以填出必填字段时，「要不要建需求」必须是 false，把缺的写进「还缺什么」——**不许编**。\n"
            + "2. 「需求草稿」里不许出现 schema 摘要之外的字段。\n"
            + "3. id、状态、来源、锁定、schema版本 这几个工程侧字段不用你填，引擎会补。\n"
            + "4. 「验收标准」是字符串数组，一条一句，能一条条勾。";

        /// <summary>
        /// 组一轮的提示词。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">助手 port 路由到的 driver 名，一律走参数（决策 17）。</param>
        /// <param name="userText">用户这一句话。</param>
        public static AssistantServePrompt Build(string repositoryRoot, string driverName, string userText)
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
                systemPrompt = "你是策划提需求时的助手。（注意：本轮没能读到供给产出的系统提示与 schema 摘要，"
                    + "知识是降级的——「要不要建需求」一律回 false，"
                    + "把「还缺什么」写成「助手知识未供给，请先跑 bridge.provision」。）";
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
            builder.AppendLine("## 提需求的人说");
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
            "你是游戏项目的需求助手，只回 JSON，不回别的。宁可说「信息不够」，也不许编造需求内容。";

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
