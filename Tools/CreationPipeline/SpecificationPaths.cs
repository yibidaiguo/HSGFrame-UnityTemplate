using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>资产规格三层数据在仓库根之下的路径拼装，全部以仓库根为起点。</summary>
    public static class SpecificationPaths
    {
        /// <summary>基线层目录：Specifications/Baseline。</summary>
        public static string BaselineDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Baseline");
        }

        /// <summary>项目层目录：Specifications/Project。</summary>
        public static string ProjectDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Project");
        }

        /// <summary>
        /// 业务层根目录：Specifications/Business。
        /// 有它才枚举得出「有哪些模块写了规范」——按模块名取的那个方法回答不了这个问题，
        /// 于是面板那边只好自己 Path.Combine 一遍，路径就有了第二个来源。
        /// 路径常量只许有一个出处，这个方法就是把那处绕过补回来的。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string BusinessRoot(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Business");
        }

        /// <summary>业务层目录：Specifications/Business/&lt;模块名&gt;。</summary>
        public static string BusinessDirectory(string repositoryRoot, string moduleName)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Business", moduleName);
        }

        /// <summary>基线资产规格文件：Specifications/Baseline/asset-spec.baseline.json。</summary>
        public static string BaselineAssetSpecFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Baseline", "asset-spec.baseline.json");
        }

        /// <summary>项目资产规格文件：Specifications/Project/asset-spec.json。</summary>
        public static string ProjectAssetSpecFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Project", "asset-spec.json");
        }

        /// <summary>业务资产规格文件：Specifications/Business/&lt;模块名&gt;/asset-spec.json。</summary>
        public static string BusinessAssetSpecFile(string repositoryRoot, string moduleName)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Business", moduleName, "asset-spec.json");
        }

        /// <summary>基线放行策略文件：Specifications/Baseline/release-policy.baseline.json。</summary>
        public static string BaselineReleasePolicyFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Baseline", "release-policy.baseline.json");
        }

        /// <summary>项目放行策略文件：Specifications/Project/release-policy.json。</summary>
        public static string ProjectReleasePolicyFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Project", "release-policy.json");
        }

        /// <summary>业务放行策略文件：Specifications/Business/&lt;模块名&gt;/release-policy.json。</summary>
        public static string BusinessReleasePolicyFile(string repositoryRoot, string moduleName)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Business", moduleName, "release-policy.json");
        }

        /// <summary>基线需求文档规范：Specifications/Baseline/requirement-doc.baseline.md。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string BaselineRequirementDocumentFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Baseline", "requirement-doc.baseline.md");
        }

        /// <summary>项目层需求文档规范追加项：Specifications/Project/requirement-doc.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ProjectRequirementDocumentFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Project", "requirement-doc.json");
        }

        /// <summary>基线模块策划案规范：Specifications/Baseline/planning-doc.baseline.md。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string BaselinePlanningDocumentFile(string repositoryRoot)
        {
            return Path.Combine(BaselineDirectory(repositoryRoot), "planning-doc.baseline.md");
        }

        /// <summary>项目层模块策划案规范追加项：Specifications/Project/planning-doc.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ProjectPlanningDocumentFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Project", "planning-doc.json");
        }

        /// <summary>检查器草案目录：Proposals/Checkers。</summary>
        public static string CheckerDraftDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "提案", "检查器");
        }

        /// <summary>项目层预审规则文件：Specifications/Project/prereview-rules.json（路径 ASCII 门禁是 block 模式，文件名必须 ASCII）。</summary>
        public static string ProjectPreReviewRuleFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Specifications", "Project", "prereview-rules.json");
        }
    }
}
