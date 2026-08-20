using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>资产规格三层数据在仓库根之下的路径拼装，全部以仓库根为起点。</summary>
    public static class SpecificationPaths
    {
        /// <summary>基线层目录：规范/基线。</summary>
        public static string BaselineDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "规范", "基线");
        }

        /// <summary>项目层目录：规范/项目。</summary>
        public static string ProjectDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "规范", "项目");
        }

        /// <summary>业务层目录：规范/业务/&lt;模块名&gt;。</summary>
        public static string BusinessDirectory(string repositoryRoot, string moduleName)
        {
            return Path.Combine(repositoryRoot, "规范", "业务", moduleName);
        }

        /// <summary>基线资产规格文件：规范/基线/资产规格.基线.json。</summary>
        public static string BaselineAssetSpecFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "规范", "基线", "资产规格.基线.json");
        }

        /// <summary>项目资产规格文件：规范/项目/资产规格.json。</summary>
        public static string ProjectAssetSpecFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "规范", "项目", "资产规格.json");
        }

        /// <summary>业务资产规格文件：规范/业务/&lt;模块名&gt;/资产规格.json。</summary>
        public static string BusinessAssetSpecFile(string repositoryRoot, string moduleName)
        {
            return Path.Combine(repositoryRoot, "规范", "业务", moduleName, "资产规格.json");
        }

        /// <summary>基线放行策略文件：规范/基线/放行策略.基线.json。</summary>
        public static string BaselineReleasePolicyFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "规范", "基线", "放行策略.基线.json");
        }

        /// <summary>项目放行策略文件：规范/项目/放行策略.json。</summary>
        public static string ProjectReleasePolicyFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "规范", "项目", "放行策略.json");
        }

        /// <summary>业务放行策略文件：规范/业务/&lt;模块名&gt;/放行策略.json。</summary>
        public static string BusinessReleasePolicyFile(string repositoryRoot, string moduleName)
        {
            return Path.Combine(repositoryRoot, "规范", "业务", moduleName, "放行策略.json");
        }

        /// <summary>检查器草案目录：提案/检查器。</summary>
        public static string CheckerDraftDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "提案", "检查器");
        }

        /// <summary>项目层预审规则文件：规范/项目/预审规则.json。</summary>
        public static string ProjectPreReviewRuleFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "规范", "项目", "预审规则.json");
        }
    }
}
