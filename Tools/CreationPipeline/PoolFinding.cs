using System;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一条池子校验发现：位置、原因、修复动作与参考示例，四要素齐全。</summary>
    public sealed class PoolFinding
    {
        /// <summary>
        /// 构造一条池子校验发现。
        /// </summary>
        /// <param name="location">报错位置，形如「文件路径:行号」，目录级问题可为纯文件路径。</param>
        /// <param name="reason">违规原因。</param>
        /// <param name="fixAction">建议的修复动作。</param>
        /// <param name="referenceExamplePath">参考示例文件的路径。</param>
        public PoolFinding(string location, string reason, string fixAction, string referenceExamplePath)
        {
            Location = location;
            Reason = reason;
            FixAction = fixAction;
            ReferenceExamplePath = referenceExamplePath;
        }

        /// <summary>报错位置，形如「文件路径:行号」。</summary>
        public string Location { get; }

        /// <summary>违规原因。</summary>
        public string Reason { get; }

        /// <summary>建议的修复动作。</summary>
        public string FixAction { get; }

        /// <summary>参考示例文件的路径。</summary>
        public string ReferenceExamplePath { get; }

        /// <summary>把四要素拼成一行给人读的中文文本。</summary>
        public string ToDisplayText()
        {
            return $"位置：{Location}；原因：{Reason}；修复：{FixAction}；参考：{ReferenceExamplePath}";
        }
    }
}
