using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>单条入站记录的六种决策。</summary>
    public enum IntakeDecision
    {
        /// <summary>入池：新记录通过校验，写入需求目录。</summary>
        Accepted,

        /// <summary>更新：既有未锁定需求按更高修订覆盖内容字段后写回。</summary>
        Updated,

        /// <summary>跳过：修订不新或已锁定但字段无变化，不做任何写盘。</summary>
        Skipped,

        /// <summary>拒收：校验未通过，写拒收单，记录留在收件箱。</summary>
        Rejected,

        /// <summary>转为变更请求：已锁定需求的下游改动落变更目录与累积文件，不入池。</summary>
        Diverted,

        /// <summary>信封无法解析或处理中发生文件异常，未做任何写盘。</summary>
        Unreadable
    }

    /// <summary>单条入站记录的处理结果：决策、来源、关联需求、人话与校验发现。</summary>
    public sealed class IntakeOutcome
    {
        /// <summary>
        /// 构造一条处理结果（无校验发现）。
        /// </summary>
        /// <param name="decision">入站决策。</param>
        /// <param name="sourceFilePath">来源信封的文件路径。</param>
        /// <param name="requirementIdentifier">关联的需求 id，无则空串。</param>
        /// <param name="message">一句中文人话结果说明。</param>
        public IntakeOutcome(IntakeDecision decision, string sourceFilePath, string requirementIdentifier, string message)
            : this(decision, sourceFilePath, requirementIdentifier, message, null)
        {
        }

        /// <summary>
        /// 构造一条处理结果（带校验发现）；非拒收时发现列表一律存空。
        /// </summary>
        /// <param name="decision">入站决策。</param>
        /// <param name="sourceFilePath">来源信封的文件路径。</param>
        /// <param name="requirementIdentifier">关联的需求 id，无则空串。</param>
        /// <param name="message">一句中文人话结果说明。</param>
        /// <param name="findings">校验发现，仅拒收时保留。</param>
        public IntakeOutcome(
            IntakeDecision decision,
            string sourceFilePath,
            string requirementIdentifier,
            string message,
            IReadOnlyList<PoolFinding> findings)
        {
            Decision = decision;
            SourceFilePath = sourceFilePath;
            RequirementIdentifier = requirementIdentifier ?? "";
            Message = message;
            Findings = decision == IntakeDecision.Rejected && findings != null
                ? findings
                : Array.Empty<PoolFinding>();
        }

        /// <summary>入站决策。</summary>
        public IntakeDecision Decision { get; }

        /// <summary>来源信封的文件路径。</summary>
        public string SourceFilePath { get; }

        /// <summary>关联的需求 id，无则空串。</summary>
        public string RequirementIdentifier { get; }

        /// <summary>一句中文人话结果说明。</summary>
        public string Message { get; }

        /// <summary>校验发现列表，非拒收时为空列表。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }

        /// <summary>把结果拼成一行中文给人看。</summary>
        public string ToDisplayText()
        {
            return $"{Decision}：{Message}（{SourceFilePath}）";
        }
    }
}
