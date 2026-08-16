using System;
using System.IO;
using System.Linq;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>常驻预算检查测试：单组超预算与总量超预算各报一条、预算内与按需分组放行、meta 与导入规则不计入。</summary>
    public class AssetResidentBudgetCheckerTests
    {
        /// <summary>常驻分组合计超过预算时，单组一条、总量一条，共两条，且单组那条落在分组前缀上。</summary>
        [Fact]
        public void ResidentGroupOverBudgetIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Game/ResourceArt/Ui/T_通用.png", new string('a', 200));
                var ruleSet = BuildRuleSet(("资源-常驻通用件", "Game/ResourceArt/Ui/", "常驻"));

                var violations = AssetResidentBudgetChecker.Check(assetsRoot, ruleSet, 100);

                Assert.Equal(2, violations.Count);
                Assert.Single(violations, violation => violation.AssetPath == "Game/ResourceArt/Ui/");
                Assert.Single(violations, violation => violation.AssetPath == "Game/");
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>常驻分组合计在预算内时一条都不该报。</summary>
        [Fact]
        public void ResidentGroupWithinBudgetIsAccepted()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Game/ResourceArt/Ui/T_通用.png", new string('a', 200));
                var ruleSet = BuildRuleSet(("资源-常驻通用件", "Game/ResourceArt/Ui/", "常驻"));

                var violations = AssetResidentBudgetChecker.Check(assetsRoot, ruleSet, 1000);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>加载分组是「按需」的目录不占常驻预算，再多也不该报。</summary>
        [Fact]
        public void OnDemandGroupIsIgnored()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Game/ResourceArt/Ui/T_通用.png", new string('a', 200));
                var ruleSet = BuildRuleSet(("资源-常驻通用件", "Game/ResourceArt/Ui/", "按需"));

                var violations = AssetResidentBudgetChecker.Check(assetsRoot, ruleSet, 100);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>.meta 与各目录的「导入规则.json」是管线配置、不进包，不能算进常驻字节。</summary>
        [Fact]
        public void MetaAndImportRuleFilesAreNotCounted()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Game/ResourceArt/Ui/T_通用.png.meta", new string('a', 200));
                WriteFile(assetsRoot, "Game/ResourceArt/Ui/导入规则.json", new string('a', 200));
                var ruleSet = BuildRuleSet(("资源-常驻通用件", "Game/ResourceArt/Ui/", "常驻"));

                var violations = AssetResidentBudgetChecker.Check(assetsRoot, ruleSet, 100);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>每个常驻分组单独都没超，但合在一起超过预算时，也要报一条总量违规。</summary>
        [Fact]
        public void TotalOverBudgetIsReportedEvenWhenEachGroupFits()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Game/ResourceArt/Ui/T_通用.png", new string('a', 80));
                WriteFile(assetsRoot, "Game/ResourceArt/Audio/BGM_主城.ogg", new string('a', 80));
                var ruleSet = BuildRuleSet(
                    ("资源-常驻通用件", "Game/ResourceArt/Ui/", "常驻"),
                    ("资源-常驻音效", "Game/ResourceArt/Audio/", "常驻"));

                var violations = AssetResidentBudgetChecker.Check(assetsRoot, ruleSet, 100);

                var violation = Assert.Single(violations);
                Assert.Equal("Game/", violation.AssetPath);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        private static AssetBundleGroupRuleSet BuildRuleSet(params (string GroupName, string PathPrefix, string LoadGroup)[] groups)
        {
            return new AssetBundleGroupRuleSet
            {
                Groups = groups.Select(group => new AssetBundleGroupDefinition
                {
                    GroupName = group.GroupName,
                    PathPrefix = group.PathPrefix,
                    IsShared = false,
                    LoadGroup = group.LoadGroup,
                }).ToArray(),
            };
        }

        private static void WriteFile(string root, string relativePath, string content)
        {
            var fullPath = Path.Combine(root, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AssetResidentBudgetCheckerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
