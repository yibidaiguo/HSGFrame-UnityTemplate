using System;
using System.IO;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>重复资产检查器测试：正式区同内容两份被报、内容不同放行、.meta 与导入规则与区外文件豁免。</summary>
    public class AssetDuplicateCheckerTests
    {
        [Fact]
        public void SameContentInFormalAreaIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Game/Art/Texture/T_A.png", "同一张贴图的内容");
                WriteFile(assetsRoot, "Game/Art/Texture/T_B.png", "同一张贴图的内容");

                var violations = AssetDuplicateChecker.Check(assetsRoot);

                // 同一哈希一组只报一条，报的是排序后第一个路径。
                var violation = Assert.Single(violations);
                Assert.Equal("Game/Art/Texture/T_A.png", violation.AssetPath);
                Assert.Equal("与另外 1 个资产内容完全相同：Game/Art/Texture/T_B.png", violation.Reason);
                Assert.Equal("复用只走引用：删掉多余的那几份改成引用同一个，预制体要定制差异用 Prefab Variant", violation.Fix);
                Assert.Equal("Specifications/structure-assets.md 第四节", violation.Reference);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void DifferentContentIsAccepted()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Game/Art/Texture/T_A.png", "贴图 A 的内容");
                WriteFile(assetsRoot, "Game/Art/Texture/T_B.png", "贴图 B 的内容");

                var violations = AssetDuplicateChecker.Check(assetsRoot);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void MetaFilesAreExcluded()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Game/Art/Texture/T_A.png.meta", "fileFormatVersion: 2\nguid: 00000000000000000000000000000001\n");
                WriteFile(assetsRoot, "Game/Art/Texture/T_B.png.meta", "fileFormatVersion: 2\nguid: 00000000000000000000000000000001\n");

                var violations = AssetDuplicateChecker.Check(assetsRoot);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void ImportRuleFilesAreExcluded()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Game/Art/Texture/import-rules.json", "{\"目录用途\":\"贴图\"}");
                WriteFile(assetsRoot, "Game/ResourceArt/Level/import-rules.json", "{\"目录用途\":\"贴图\"}");

                var violations = AssetDuplicateChecker.Check(assetsRoot);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void OutsideFormalAreaIsAccepted()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Plugins/某包/A.txt", "插件内容");
                WriteFile(assetsRoot, "Plugins/某包/B.txt", "插件内容");

                var violations = AssetDuplicateChecker.Check(assetsRoot);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
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
            var directory = Path.Combine(Path.GetTempPath(), "AssetDuplicateCheckerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
