using System;
using System.IO;
using System.Linq;
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
