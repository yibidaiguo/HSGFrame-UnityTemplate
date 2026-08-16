using System;
using System.IO;
using System.Linq;
using System.Text;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>加载分组校验测试：动态分组缺字段与取值非法各报一条、合法与 Art 分组放行、预制体落点只认 ResourceArt 树。</summary>
    public class AssetLoadGroupCheckerTests
    {
        /// <summary>动态收集根下的分组没写加载分组时必须报出来——不报就等于这个包的生命周期没人定。</summary>
        [Fact]
        public void DynamicGroupWithoutLoadGroupIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                var ruleSet = BuildRuleSet(("资源-关卡实体", "Game/ResourceArt/Level/", null));

                var violations = AssetLoadGroupChecker.Check(assetsRoot, ruleSet);

                var violation = Assert.Single(violations);
                Assert.Equal("Game/ResourceArt/Level/", violation.AssetPath);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>加载分组写了三个合法值之外的词时必须报出来，且把那个词回显进原因里。</summary>
        [Fact]
        public void DynamicGroupWithInvalidLoadGroupIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                var ruleSet = BuildRuleSet(("资源-关卡实体", "Game/ResourceArt/Level/", "永驻"));

                var violations = AssetLoadGroupChecker.Check(assetsRoot, ruleSet);

                var violation = Assert.Single(violations);
                Assert.Contains("永驻", violation.Reason);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>加载分组取值合法、树里也没有跑偏的预制体时，一条都不该报。</summary>
        [Fact]
        public void DynamicGroupWithValidLoadGroupIsAccepted()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                var ruleSet = BuildRuleSet(("资源-关卡实体", "Game/ResourceArt/Level/", "按需"));

                var violations = AssetLoadGroupChecker.Check(assetsRoot, ruleSet);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>Art 下的分组是被引用的源生资产、不是收集入口，不写加载分组是对的，不能报。</summary>
        [Fact]
        public void ArtGroupWithoutLoadGroupIsAccepted()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                var ruleSet = BuildRuleSet(("美术-贴图", "Game/Art/Texture/", null));

                var violations = AssetLoadGroupChecker.Check(assetsRoot, ruleSet);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>正式区里跑到 ResourceArt 树外面的预制体必须报出来。</summary>
        [Fact]
        public void PrefabOutsideResourceArtIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Game/Art/Prefab/P_样例.prefab", "prefab 内容无所谓");
                var ruleSet = BuildRuleSet(("资源-关卡实体", "Game/ResourceArt/Level/", "按需"));

                var violations = AssetLoadGroupChecker.Check(assetsRoot, ruleSet);

                var violation = Assert.Single(violations);
                Assert.Equal("Game/Art/Prefab/P_样例.prefab", violation.AssetPath);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>第三方自留地里的预制体由各自工具管理，这条规矩管不着，不能报。</summary>
        [Fact]
        public void PrefabInThirdPartyDirectoryIsAccepted()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Plugins/某包/P_样例.prefab", "prefab 内容无所谓");
                var ruleSet = BuildRuleSet(("资源-关卡实体", "Game/ResourceArt/Level/", "按需"));

                var violations = AssetLoadGroupChecker.Check(assetsRoot, ruleSet);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>规则里写了加载分组、收集器里却没有同名 group 时，两侧各报一条——漏收的那批资产出包时会悄悄不进包。</summary>
        [Fact]
        public void CollectorGroupMissingIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                var ruleSet = BuildRuleSet(("资源-关卡实体", "Game/ResourceArt/Level/", "按需"));
                var settingPath = WriteCollectorSetting(
                    assetsRoot,
                    ("场景-世界", new[] { "Assets/Game/Scenes/World" }));

                var violations = AssetLoadGroupChecker.Check(assetsRoot, ruleSet, settingPath);

                Assert.Equal(2, violations.Count);
                Assert.Single(violations, violation => violation.AssetPath == "Game/ResourceArt/Level/");
                Assert.Single(violations, violation => violation.AssetPath == "Assets/Game/Scenes/World");
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>组名对得上但收集路径指错地方时必须报——这正是「换了收集根忘了改规则」的形状。</summary>
        [Fact]
        public void CollectorPathMismatchIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                var ruleSet = BuildRuleSet(("资源-关卡实体", "Game/ResourceArt/Level/", "按需"));
                var settingPath = WriteCollectorSetting(
                    assetsRoot,
                    ("资源-关卡实体", new[] { "Assets/Game/Art" }));

                var violations = AssetLoadGroupChecker.Check(assetsRoot, ruleSet, settingPath);

                var violation = Assert.Single(violations);
                Assert.Equal("Assets/Game/ResourceArt/Level", violation.AssetPath);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>一个 group 下配了两条收集路径就没法一一对账，必须报，不能挑一条凑合比。</summary>
        [Fact]
        public void CollectorGroupWithTwoPathsIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                var ruleSet = BuildRuleSet(("资源-关卡实体", "Game/ResourceArt/Level/", "按需"));
                var settingPath = WriteCollectorSetting(
                    assetsRoot,
                    ("资源-关卡实体", new[] { "Assets/Game/ResourceArt/Level", "Assets/Game/Art" }));

                var violations = AssetLoadGroupChecker.Check(assetsRoot, ruleSet, settingPath);

                var violation = Assert.Single(violations);
                Assert.Equal("Assets/Game/ResourceArt/Level", violation.AssetPath);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        /// <summary>组名与收集路径都对得上时一条都不报，且组名写成 Unity 的 \uXXXX 转义形式也要认得出来。</summary>
        [Fact]
        public void MatchingCollectorReportsNothing()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                var ruleSet = BuildRuleSet(("资源-关卡实体", "Game/ResourceArt/Level/", "按需"));
                var settingPath = WriteCollectorSetting(
                    assetsRoot,
                    ("\\u8D44\\u6E90-\\u5173\\u5361\\u5B9E\\u4F53", new[] { "Assets/Game/ResourceArt/Level" }),
                    quoteGroupNames: true);

                var violations = AssetLoadGroupChecker.Check(assetsRoot, ruleSet, settingPath);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        // 按 Unity 写出来的形状造一份最小收集器配置：只有本检查认的 GroupName 与 CollectPath 两个键。
        private static string WriteCollectorSetting(
            string root,
            params (string GroupName, string[] CollectPaths)[] groups)
        {
            return WriteCollectorSetting(root, false, groups);
        }

        private static string WriteCollectorSetting(
            string root,
            (string GroupName, string[] CollectPaths) group,
            bool quoteGroupNames)
        {
            return WriteCollectorSetting(root, quoteGroupNames, new[] { group });
        }

        private static string WriteCollectorSetting(
            string root,
            bool quoteGroupNames,
            (string GroupName, string[] CollectPaths)[] groups)
        {
            var builder = new StringBuilder();
            builder.AppendLine("MonoBehaviour:");
            builder.AppendLine("  Packages:");
            builder.AppendLine("  - PackageName: DefaultPackage");
            builder.AppendLine("    Groups:");
            foreach (var group in groups)
            {
                var groupName = quoteGroupNames ? "\"" + group.GroupName + "\"" : group.GroupName;
                builder.AppendLine("    - GroupName: " + groupName);
                builder.AppendLine("      ActiveRuleName: EnableGroup");
                builder.AppendLine("      Collectors:");
                foreach (var collectPath in group.CollectPaths)
                {
                    builder.AppendLine("      - CollectPath: " + collectPath);
                    builder.AppendLine("        PackRuleName: PackDirectory");
                }
            }

            var settingPath = Path.Combine(root, "BundleCollectorSetting.asset");
            File.WriteAllText(settingPath, builder.ToString());
            return settingPath;
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
            var directory = Path.Combine(Path.GetTempPath(), "AssetLoadGroupCheckerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
