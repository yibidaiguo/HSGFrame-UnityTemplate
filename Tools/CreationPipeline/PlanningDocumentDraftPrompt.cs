using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 组「照代码与已有需求产一份模块策划案草案」的提示词，并把模型的回答解析回来。
    ///
    /// 这一步只产**人写区**：目标用途、玩法、边界与不做。现状那一段是投影，
    /// 由 <see cref="PlanningDocumentRenderer"/> 从正本算出来，不问模型。
    ///
    /// **「往后要做成什么样」一律留空。** 那是人的判断，代码里没有依据——
    /// 让模型编一段「未来规划」，它会被往后所有东西当成事实继承
    /// （与总设计层同一条规矩，子文档 10 §四）。编出来的方向没人同意过，
    /// 却会让整个模块照着它一直走。
    /// </summary>
    public static class PlanningDocumentDraftPrompt
    {
        /// <summary>给模型的角色交代。</summary>
        public const string SystemContextText =
            "你在给一个已经写完的游戏模块补一份策划正本。"
            + "**照着已有的实现写，不要设计新东西**；看不出来的地方如实说「看不出来」，不要编。"
            + "只回一份 JSON，不要解释、不要代码块。";

        /// <summary>「往后要做成什么样」那一节的占位符：草案里一律留它。</summary>
        public const string FuturePlaceholder = "（待补）";

        /// <summary>
        /// 组提示词。
        /// </summary>
        /// <param name="moduleName">模块名。</param>
        /// <param name="readmeText">模块自述 README.md 全文；没有给空串。</param>
        /// <param name="codeSurface">代码公开面（类型与成员），一行一条。</param>
        /// <param name="requirementSummaries">挂在这个模块名下的需求，一行一条。</param>
        public static string Build(
            string moduleName,
            string readmeText,
            IReadOnlyList<string> codeSurface,
            IReadOnlyList<string> requirementSummaries)
        {
            var builder = new StringBuilder();
            builder.Append("照下面这些材料，给模块 **").Append(moduleName)
                .Append("** 产一份策划正本的草案：它统共负责什么、玩法是什么、什么明确不做。\n\n");

            if (!string.IsNullOrWhiteSpace(readmeText))
            {
                builder.Append("## 模块自述（程序视角）\n").Append(readmeText.Trim()).Append("\n\n");
            }

            builder.Append("## 代码公开面\n");
            if (codeSurface.Count == 0)
            {
                builder.Append("（抽不出来）\n");
            }
            else
            {
                foreach (var line in codeSurface)
                {
                    builder.Append("- ").Append(line).Append('\n');
                }
            }

            builder.Append("\n## 这个模块名下已有的需求\n");
            if (requirementSummaries.Count == 0)
            {
                builder.Append("（一条都没有）\n");
            }
            else
            {
                foreach (var line in requirementSummaries)
                {
                    builder.Append("- ").Append(line).Append('\n');
                }
            }

            builder.Append("\n## 回什么\n只回这一份 JSON：\n");
            builder.Append("{\"标题\": \"人话名字，如 背包\", ");
            builder.Append("\"目标用途\": \"这个模块为什么存在、给谁用、解决什么，两三句\", ");
            builder.Append("\"玩法\": \"规则怎么走，照实现写\", ");
            builder.Append("\"边界与不做\": \"明确不归它管的事；看不出来就写「还没定」\"}\n\n");

            builder.Append("## 硬规矩\n");
            builder.Append("1. **照实现写，不要设计新东西。** 这个模块已经做完了，"
                + "你的活是把它说清楚，不是把它做得更好。想到的改进意见一个字都别写进来。\n");
            builder.Append("2. **看不出来就说看不出来。** 代码里没有依据的事——比如动效、"
                + "失败提示的文案——写「还没定」，别照常理补一个。"
                + "编出来的会被往后所有东西当成事实继承，那比空着糟得多。\n");
            builder.Append("3. **「边界与不做」写的是范围不是缺陷。** "
                + "「装备穿戴不归它管」是边界；「满包时没有提示」是缺陷，不写在这一节。\n");
            builder.Append("4. **不许回「往后要做成什么样」。** 那是人的判断，这一节由人来写。\n");

            return builder.ToString();
        }

        /// <summary>
        /// 把模型的回答解析成人写区各节的正文。
        /// </summary>
        /// <param name="modelText">模型原文。</param>
        /// <param name="sections">解析出的小节正文，键是小节标题。</param>
        /// <param name="reason">解析失败原因；成功时为空串。</param>
        public static bool TryParse(
            string modelText, out IReadOnlyDictionary<string, string> sections, out string reason)
        {
            sections = null;
            reason = "";

            if (string.IsNullOrWhiteSpace(modelText))
            {
                reason = "执行后端回了空文本";
                return false;
            }

            var json = ExtractFirstJsonObject(modelText);
            if (json.Length == 0)
            {
                reason = "回答里找不到一个完整的 JSON 对象";
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException exception)
            {
                reason = "回答里那段 JSON 语法不合法：" + exception.Message;
                return false;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "回答的顶层不是 JSON 对象";
                    return false;
                }

                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var key in new[] { "标题", "目标用途", "玩法", "边界与不做" })
                {
                    if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        var text = (value.GetString() ?? "").Trim();
                        if (text.Length > 0)
                        {
                            result[key] = text;
                        }
                    }
                }

                if (!result.ContainsKey("目标用途"))
                {
                    reason = "回答里没有「目标用途」，或者它是空的";
                    return false;
                }

                // **模型给的「往后要做成什么样」一律丢掉**，哪怕它回了。
                // 规矩四写在提示词里，但提示词不是保证——这里再挡一道。
                result["往后要做成什么样"] = FuturePlaceholder;

                sections = result;
                return true;
            }
        }

        /// <summary>模块自述 README.md 的路径。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">模块名。</param>
        public static string ReadmeFile(string repositoryRoot, string moduleName)
        {
            return Path.Combine(
                ModuleInterfaceDigest.ModulesRoot(repositoryRoot), moduleName ?? "", "README.md");
        }

        // 从一段文本里抠出第一个大括号平衡的 JSON 对象；模型爱在前后加话，抠出来就好。
        private static string ExtractFirstJsonObject(string text)
        {
            var start = text.IndexOf('{');
            if (start < 0)
            {
                return "";
            }

            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var index = start; index < text.Length; index++)
            {
                var character = text[index];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (character == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (character == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (character == '{')
                {
                    depth++;
                }
                else if (character == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(start, index - start + 1);
                    }
                }
            }

            return "";
        }
    }
}
