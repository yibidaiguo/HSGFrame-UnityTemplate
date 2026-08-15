using System;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一条资产依赖方向违规：引用方、被引用方与命中它的规则。</summary>
    public sealed class AssetDependencyViolation
    {
        /// <summary>构造一条依赖方向违规。</summary>
        /// <param name="referencingAssetPath">引用方资产路径，相对 Assets 根。</param>
        /// <param name="referencedAssetPath">被引用方资产路径，相对 Assets 根。</param>
        /// <param name="rule">命中的依赖方向规则。</param>
        public AssetDependencyViolation(string referencingAssetPath, string referencedAssetPath, AssetDependencyRule rule)
        {
            ReferencingAssetPath = referencingAssetPath;
            ReferencedAssetPath = referencedAssetPath;
            Rule = rule;
        }

        /// <summary>引用方资产路径，相对 Assets 根。</summary>
        public string ReferencingAssetPath { get; }

        /// <summary>被引用方资产路径，相对 Assets 根。</summary>
        public string ReferencedAssetPath { get; }

        /// <summary>命中的依赖方向规则。</summary>
        public AssetDependencyRule Rule { get; }

        /// <summary>把四要素（位置、原因、修复、参考）拼成一行给人读的中文文本。</summary>
        public string ToDisplayText()
        {
            return $"位置：{ReferencingAssetPath}；原因：它引用了 {ReferencedAssetPath}，{Rule.Reason}；修复：改走 {Rule.FromPathPrefix} 内部的资产，或者把被引用的资产挪出 {Rule.ForbiddenPathPrefix}；参考：Tools/AssetPipeline/AssetDependencyDirectionChecker.cs";
        }
    }
}
