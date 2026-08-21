using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>美术资产在仓库根之下的路径拼装：资产请求、变体、边车、弃置与预览，全部以仓库根为起点。</summary>
    public static class AssetPaths
    {
        /// <summary>某需求下资产请求的存放目录：_Tasks/&lt;需求id&gt;/资产请求。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static string AssetRequestDirectory(string repositoryRoot, string requirementIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "asset-requests");
        }

        /// <summary>某资产的请求文件：资产请求目录下的 &lt;资产id&gt;.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        public static string AssetRequestFile(string repositoryRoot, string requirementIdentifier, string assetIdentifier)
        {
            return Path.Combine(AssetRequestDirectory(repositoryRoot, requirementIdentifier), $"{assetIdentifier}.json");
        }

        /// <summary>某资产的变体目录：_Tasks/&lt;需求id&gt;/30-outputs/&lt;资产id&gt;/变体。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        public static string VariantDirectory(string repositoryRoot, string requirementIdentifier, string assetIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "30-outputs", assetIdentifier, "variants");
        }

        /// <summary>人工产出变体的目录：变体目录下的「人工」。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        public static string ManualVariantDirectory(string repositoryRoot, string requirementIdentifier, string assetIdentifier)
        {
            return Path.Combine(VariantDirectory(repositoryRoot, requirementIdentifier, assetIdentifier), "manual");
        }

        /// <summary>某变体文件的溯源边车文件：变体目录下的 &lt;变体文件名&gt;.provenance.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        /// <param name="variantFileName">变体文件名，如「v1.png」。</param>
        public static string SidecarFile(string repositoryRoot, string requirementIdentifier, string assetIdentifier, string variantFileName)
        {
            return Path.Combine(VariantDirectory(repositoryRoot, requirementIdentifier, assetIdentifier), $"{variantFileName}.provenance.json");
        }

        /// <summary>不合格变体的弃置目录：30-outputs/&lt;资产id&gt;/弃。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        public static string RejectedDirectory(string repositoryRoot, string requirementIdentifier, string assetIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "30-outputs", assetIdentifier, "discarded");
        }

        /// <summary>某资产的实机预览截图：_Tasks/&lt;需求id&gt;/预览/&lt;资产id&gt;.png。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        public static string PreviewFile(string repositoryRoot, string requirementIdentifier, string assetIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "preview", $"{assetIdentifier}.png");
        }
    }
}
