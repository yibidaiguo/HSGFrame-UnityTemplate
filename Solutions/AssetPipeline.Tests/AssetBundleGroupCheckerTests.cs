using System;
using System.IO;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>打包分组校验测试：跨组共享、共享组豁免、单组引用、未分组开关与最长前缀匹配。</summary>
    public class AssetBundleGroupCheckerTests
    {
        [Fact]
        public void SharedAssetReferencedByTwoGroupsIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string sharedImageGuid = "c00000000000000000000000000000a1";
                WriteAsset(assetsRoot, "_Project/Art/A/PA.prefab", $"  m_Script: {{fileID: 11, guid: {sharedImageGuid}, type: 3}}");
                WriteMeta(assetsRoot, "_Project/Art/A/PA.prefab", "c0000000000000000000000000000001");
                WriteAsset(assetsRoot, "_Project/Art/B/PB.prefab", $"  m_Script: {{fileID: 11, guid: {sharedImageGuid}, type: 3}}");
                WriteMeta(assetsRoot, "_Project/Art/B/PB.prefab", "c0000000000000000000000000000002");
                WriteAsset(assetsRoot, "_Project/Art/Texture/共用图.png", "内容无所谓");
                WriteMeta(assetsRoot, "_Project/Art/Texture/共用图.png", sharedImageGuid);

                var ruleSet = BuildRuleSet(
                    ("美术-A", "_Project/Art/A/", false),
                    ("美术-B", "_Project/Art/B/", false),
                    ("美术-贴图", "_Project/Art/Texture/", false));
                var violations = AssetBundleGroupChecker.Check(assetsRoot, ruleSet);

                var violation = Assert.Single(violations);
                Assert.Equal("_Project/Art/Texture/共用图.png", violation.AssetPath);
                Assert.Contains("被 2 个打包分组引用", violation.ToDisplayText());
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void SharedAssetInsideSharedGroupIsNotReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string sharedImageGuid = "c00000000000000000000000000000b1";
                WriteAsset(assetsRoot, "_Project/Art/A/PA.prefab", $"  m_Script: {{fileID: 11, guid: {sharedImageGuid}, type: 3}}");
                WriteMeta(assetsRoot, "_Project/Art/A/PA.prefab", "c0000000000000000000000000000001");
                WriteAsset(assetsRoot, "_Project/Art/B/PB.prefab", $"  m_Script: {{fileID: 11, guid: {sharedImageGuid}, type: 3}}");
                WriteMeta(assetsRoot, "_Project/Art/B/PB.prefab", "c0000000000000000000000000000002");
                WriteAsset(assetsRoot, "_Project/Art/Shared/共用图.png", "内容无所谓");
                WriteMeta(assetsRoot, "_Project/Art/Shared/共用图.png", sharedImageGuid);

                var ruleSet = BuildRuleSet(
                    ("美术-A", "_Project/Art/A/", false),
                    ("美术-B", "_Project/Art/B/", false),
                    ("美术-共享", "_Project/Art/Shared/", true));
                var violations = AssetBundleGroupChecker.Check(assetsRoot, ruleSet);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void AssetReferencedByOnlyOneGroupIsNotReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string sharedImageGuid = "c00000000000000000000000000000c1";
                WriteAsset(assetsRoot, "_Project/Art/A/PA.prefab", $"  m_Script: {{fileID: 11, guid: {sharedImageGuid}, type: 3}}");
                WriteMeta(assetsRoot, "_Project/Art/A/PA.prefab", "c0000000000000000000000000000001");
                WriteAsset(assetsRoot, "_Project/Art/Texture/共用图.png", "内容无所谓");
                WriteMeta(assetsRoot, "_Project/Art/Texture/共用图.png", sharedImageGuid);

                var ruleSet = BuildRuleSet(
                    ("美术-A", "_Project/Art/A/", false),
                    ("美术-贴图", "_Project/Art/Texture/", false));
                var violations = AssetBundleGroupChecker.Check(assetsRoot, ruleSet);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void UngroupedAssetIsReportedWhenSwitchIsOn()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string orphanImageGuid = "c00000000000000000000000000000d1";
                WriteAsset(assetsRoot, "_Project/Art/A/PA.prefab", $"  m_Script: {{fileID: 11, guid: {orphanImageGuid}, type: 3}}");
                WriteMeta(assetsRoot, "_Project/Art/A/PA.prefab", "c0000000000000000000000000000001");
                WriteAsset(assetsRoot, "Orphan/孤儿.png", "内容无所谓");
                WriteMeta(assetsRoot, "Orphan/孤儿.png", orphanImageGuid);

                var ruleSet = BuildRuleSet(("美术-A", "_Project/Art/A/", false));
                ruleSet.ReportUngroupedAssets = true;
                var violations = AssetBundleGroupChecker.Check(assetsRoot, ruleSet);

                var violation = Assert.Single(violations);
                Assert.Equal("Orphan/孤儿.png", violation.AssetPath);
                Assert.Contains("不落在任何打包分组里", violation.ToDisplayText());
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void UngroupedAssetIsNotReportedWhenSwitchIsOff()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string orphanImageGuid = "c00000000000000000000000000000e1";
                WriteAsset(assetsRoot, "_Project/Art/A/PA.prefab", $"  m_Script: {{fileID: 11, guid: {orphanImageGuid}, type: 3}}");
                WriteMeta(assetsRoot, "_Project/Art/A/PA.prefab", "c0000000000000000000000000000001");
                WriteAsset(assetsRoot, "Orphan/孤儿.png", "内容无所谓");
                WriteMeta(assetsRoot, "Orphan/孤儿.png", orphanImageGuid);

                var ruleSet = BuildRuleSet(("美术-A", "_Project/Art/A/", false));
                var violations = AssetBundleGroupChecker.Check(assetsRoot, ruleSet);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void LongestPathPrefixWinsWhenGroupsNest()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                const string sharedImageGuid = "c00000000000000000000000000000f1";
                WriteAsset(assetsRoot, "_Project/Art/A/PA.prefab", $"  m_Script: {{fileID: 11, guid: {sharedImageGuid}, type: 3}}");
                WriteMeta(assetsRoot, "_Project/Art/A/PA.prefab", "c0000000000000000000000000000001");
                WriteAsset(assetsRoot, "_Project/Art/B/PB.prefab", $"  m_Script: {{fileID: 11, guid: {sharedImageGuid}, type: 3}}");
                WriteMeta(assetsRoot, "_Project/Art/B/PB.prefab", "c0000000000000000000000000000002");
                WriteAsset(assetsRoot, "_Project/Art/Shared/共用图.png", "内容无所谓");
                WriteMeta(assetsRoot, "_Project/Art/Shared/共用图.png", sharedImageGuid);

                // 外层组 _Project/Art/ 先声明：按声明顺序匹配会让共享目录被外层业务组吃掉，
                // 必须取最长前缀 _Project/Art/Shared/（共享组）才能不报。
                var ruleSet = BuildRuleSet(
                    ("美术-A", "_Project/Art/A/", false),
                    ("美术-B", "_Project/Art/B/", false),
                    ("美术-全部", "_Project/Art/", false),
                    ("美术-共享", "_Project/Art/Shared/", true));
                var violations = AssetBundleGroupChecker.Check(assetsRoot, ruleSet);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        private static AssetBundleGroupRuleSet BuildRuleSet(params (string GroupName, string PathPrefix, bool IsShared)[] groups)
        {
            var definitions = new AssetBundleGroupDefinition[groups.Length];
            for (var index = 0; index < groups.Length; index++)
            {
                definitions[index] = new AssetBundleGroupDefinition
                {
                    GroupName = groups[index].GroupName,
                    PathPrefix = groups[index].PathPrefix,
                    IsShared = groups[index].IsShared,
                };
            }

            return new AssetBundleGroupRuleSet
            {
                Groups = definitions,
                ReportUngroupedAssets = false,
            };
        }

        private static void WriteAsset(string root, string fileName, string content)
        {
            var fullPath = Path.Combine(root, fileName);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
        }

        private static void WriteMeta(string root, string fileName, string guid)
        {
            File.WriteAllText(Path.Combine(root, fileName + ".meta"), $"fileFormatVersion: 2\nguid: {guid}\n");
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AssetBundleGroupCheckerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
