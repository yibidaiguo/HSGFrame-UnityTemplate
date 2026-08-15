using System;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一条打包分组违规：资产路径、原因、修复与参考。</summary>
    public sealed class AssetBundleGroupViolation
    {
        /// <summary>构造一条打包分组违规。</summary>
        /// <param name="assetPath">资产路径，相对 Assets 根。</param>
        /// <param name="reason">违规原因。</param>
        /// <param name="fix">修复建议。</param>
        /// <param name="reference">参考示例。</param>
        public AssetBundleGroupViolation(string assetPath, string reason, string fix, string reference)
        {
            AssetPath = assetPath;
            Reason = reason;
            Fix = fix;
            Reference = reference;
        }

        /// <summary>资产路径，相对 Assets 根。</summary>
        public string AssetPath { get; }

        /// <summary>违规原因。</summary>
        public string Reason { get; }

        /// <summary>修复建议。</summary>
        public string Fix { get; }

        /// <summary>参考示例。</summary>
        public string Reference { get; }

        /// <summary>把四要素（位置、原因、修复、参考）拼成一行给人读的中文文本。</summary>
        public string ToDisplayText()
        {
            return $"位置：{AssetPath}；原因：{Reason}；修复：{Fix}；参考：{Reference}";
        }
    }
}
