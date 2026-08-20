using System;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一条冲突候选：存量需求与新需求的一次命中。只读数据类，由 ConflictDetector 产出，
    /// 探测器只算不写盘，发不发卡由人/命令层按 ShouldRaiseCard 决定。
    /// </summary>
    public sealed class ConflictCandidate
    {
        /// <summary>
        /// 构造一条冲突候选。置信度由分数分档得出：&gt;= 0.75 高，0.5 到 0.75 中，&lt; 0.5 低；
        /// 只有「高」的候选 ShouldRaiseCard 为 true（低置信度只在需求上标注、不发卡）。
        /// </summary>
        /// <param name="oldIdentifier">存量需求 id。</param>
        /// <param name="newIdentifier">新需求 id。</param>
        /// <param name="reason">命中的判据名：标题相似 / 共用设计记录 / 验收标准重合。</param>
        /// <param name="score">置信度，0 到 1，保留三位小数。</param>
        /// <param name="detail">一句人话，说清命中的是什么。</param>
        internal ConflictCandidate(string oldIdentifier, string newIdentifier, string reason, double score, string detail)
        {
            OldIdentifier = oldIdentifier;
            NewIdentifier = newIdentifier;
            Reason = reason;
            Score = Math.Round(score, 3);
            Detail = detail;
            Confidence = Score >= 0.75 ? "高" : (Score >= 0.5 ? "中" : "低");
            ShouldRaiseCard = string.Equals(Confidence, "高", StringComparison.Ordinal);
        }

        /// <summary>存量需求 id。</summary>
        public string OldIdentifier { get; }

        /// <summary>新需求 id。</summary>
        public string NewIdentifier { get; }

        /// <summary>命中的判据名，取值只有三个：标题相似 / 共用设计记录 / 验收标准重合。</summary>
        public string Reason { get; }

        /// <summary>置信度，0 到 1，保留三位小数。</summary>
        public double Score { get; }

        /// <summary>置信度分档：高 / 中 / 低，由 Score 分档得出。</summary>
        public string Confidence { get; }

        /// <summary>一句人话，说清命中的是什么。</summary>
        public string Detail { get; }

        /// <summary>是否该发卡挂账；只有「高」才 true。</summary>
        public bool ShouldRaiseCard { get; }
    }
}
