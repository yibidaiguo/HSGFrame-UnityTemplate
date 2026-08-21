using System;
using System.Collections.Generic;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一次语义冲突比对提示词组装的结果：提示词全文 + 提示词版本。
    /// 提示词版本由调用方传入（改提示词必须同步改它）——报告里靠它说明「用的是哪一版提示词」（决策 89）。
    /// </summary>
    public sealed class SemanticConflictPromptResult
    {
        /// <summary>
        /// 构造一份提示词结果。
        /// </summary>
        /// <param name="promptText">提示词全文。</param>
        /// <param name="promptVersion">提示词版本号。</param>
        public SemanticConflictPromptResult(string promptText, string promptVersion)
        {
            PromptText = promptText ?? "";
            PromptVersion = promptVersion ?? "";
        }

        /// <summary>提示词全文。</summary>
        public string PromptText { get; }

        /// <summary>提示词版本号。</summary>
        public string PromptVersion { get; }
    }

    /// <summary>
    /// 语义冲突比对的提示词组装器（子文档 02 §第 4 条「冲突扫描」的语义那一半）：
    /// 设计池汇总文本 + 存量需求列表，要求执行后端产出冲突候选 + 置信度。
    /// 本类不碰网络；组装是确定性的——同一份输入两次组装必须逐字符相同（决策 58 同源）。
    /// 照决策 67：同一对需求命中多条判据时，每条判据各产一条候选，不合并、不取最大——提示词里明确要求。
    /// 照决策 66：本类只产候选，不写盘到冲突账本；是否发卡由人在报告上决定（只有高置信度才建议发卡）。
    /// </summary>
    public static class SemanticConflictPrompt
    {
        // 指令块（提示词里不随输入变的那部分）。版本对它取哈希——AssistantServePrompt 立的规矩，
        // 此前是写死的 semantic-conflict-v1，改了模板版本号照旧说谎。
        private static readonly string InstructionText = BuildInstructionText();

        /// <summary>缺省提示词版本：由指令文本哈希算出，指令一变版本就变。</summary>
        public static string PromptVersion { get; } = "semantic-conflict-" + AssistantServePrompt.ShortHash(InstructionText);

        /// <summary>
        /// 组装语义冲突比对提示词。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录（当前用于占位与文档语义；提示词里不含路径）。</param>
        /// <param name="designPoolSummaryText">设计池汇总文本。</param>
        /// <param name="existingRequirements">存量需求列表，每个元素是一份需求的文本。</param>
        /// <param name="promptVersion">提示词版本号，报告里要写清用的哪一版（决策 89）。</param>
        public static SemanticConflictPromptResult Build(
            string repositoryRoot,
            string designPoolSummaryText,
            IReadOnlyList<string> existingRequirements,
            string promptVersion)
        {
            var builder = new StringBuilder();
            builder.Append(InstructionText);
            builder.AppendLine(PromptEnvelope.DataSection("设计池汇总"));
            builder.AppendLine(designPoolSummaryText ?? "");
            builder.AppendLine();
            builder.AppendLine(PromptEnvelope.DataSection("存量需求"));
            if (existingRequirements != null && existingRequirements.Count > 0)
            {
                foreach (var requirementText in existingRequirements)
                {
                    builder.AppendLine(requirementText ?? "");
                    builder.AppendLine();
                }
            }
            else
            {
                builder.AppendLine("（没有存量需求）");
            }

            builder.AppendLine(PromptEnvelope.ClosingLine("比对"));

            return new SemanticConflictPromptResult(builder.ToString(), promptVersion ?? PromptVersion);
        }

        // 指令块单独组装成一段文本：Build 里直接拼它，版本号对它取哈希——两处用的是同一份，改不脱节。
        // 置信度三档各带可操作判据与下游后果（此前三档全是口语，「基本可以断定」跨次运行方差极大；
        // 「高才建议发卡」的规矩只写在代码里，模型根本不知道自己的分档会引发什么）。
        private static string BuildInstructionText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("你是创作管线的「语义冲突比对员」。");
            builder.AppendLine("你的任务：把设计池汇总与存量需求列表做语义比对，找出可能互相冲突的需求对。");
            builder.AppendLine("比对的是语义：标题、验收标准、设计记录指向、专项归属是否撞车——不是逐字重复。");
            builder.AppendLine("置信度分三档，按判据定档，不凭语感：");
            builder.AppendLine("- 高：两条需求的验收标准或目标实质相同、或直接矛盾，同时落地必然打架。这一档会建议给人发卡裁决。");
            builder.AppendLine("- 中：范围有实质重叠但入手点不同，可能各做各的。这一档只进报告，值得人看一眼。");
            builder.AppendLine("- 低：仅主题沾边，不需要人处理，只在需求上留标注。");
            builder.AppendLine("输出要求：");
            builder.AppendLine(PromptEnvelope.JsonOnlyRule);
            builder.AppendLine("- JSON 形状：{\"冲突候选\":[{\"需求A\":\"…\",\"需求B\":\"…\",\"置信度\":\"高|中|低\",\"判据\":\"…\",\"说明\":\"…\"}]}");
            builder.AppendLine("- 「需求A」「需求B」写需求 id，如 REQ-0001；顺序无关，但同一对不要两条都列。");
            builder.AppendLine("- **同一对需求命中多条判据时，每条判据各产一条候选，不许合并、不许只取最大那条。**");
            builder.AppendLine("  合并会把「为什么判它冲突」压成一个数字，人就无法判断这次比对靠不靠谱。");
            builder.AppendLine("- 「判据」写清命中的是什么：如「验收标准重合」「设计记录共用」「标题语义相近」「专项内目标重叠」。");
            builder.AppendLine("- 「说明」写一句人话，说清重叠在哪。");
            builder.AppendLine("- 没有冲突也要输出 {\"冲突候选\":[]}。");
            builder.AppendLine();
            return builder.ToString();
        }
    }
}
