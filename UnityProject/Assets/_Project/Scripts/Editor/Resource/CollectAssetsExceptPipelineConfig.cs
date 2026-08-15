using System;
using System.IO;
using YooAsset.Editor;

namespace Template.Toolkit.Editor
{
    /// <summary>收集资源但跳过资产管线自己的配置文件。它们是管线的输入，不是要打进资源包的内容。</summary>
    [DisplayName("收集资源（跳过管线配置）")]
    public sealed class CollectAssetsExceptPipelineConfig : IAssetFilterRule
    {
        // 每个资产目录下都躺着一份同名的「导入规则.json」。开了可寻址之后它们的定位地址都是
        // 「导入规则」，三份撞在一起，构建直接报 Address already exists。
        private static readonly string[] PipelineConfigFileNames = { "导入规则.json", "归档路由.json" };

        /// <summary>这条规则面向全部资源类型。</summary>
        public string FindAssetType => EAssetFilterType.All.ToString();

        /// <summary>判断一个资源要不要收进包裹。</summary>
        /// <param name="data">被判定的资源信息。</param>
        public bool IsCollectAsset(AssetFilterRuleData data)
        {
            var fileName = Path.GetFileName(data.AssetPath);
            foreach (var configFileName in PipelineConfigFileNames)
            {
                if (string.Equals(fileName, configFileName, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
