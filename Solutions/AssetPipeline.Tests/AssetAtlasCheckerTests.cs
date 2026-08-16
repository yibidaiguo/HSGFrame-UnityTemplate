using System;
using System.IO;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>图集对齐检查器测试：图集缺失被报、图集未收录目录被报、真收录放行、前缀错误被报、无图集字段放行。</summary>
    public class AssetAtlasCheckerTests
    {
        [Fact]
        public void MissingAtlasAssetIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteImportRule(assetsRoot, "Game/Art/Texture/Ui/导入规则.json", "SA_Inventory");
                Directory.CreateDirectory(Path.Combine(assetsRoot, "Game/Settings/Atlas"));

                var violations = AssetAtlasChecker.Check(assetsRoot);

                var violation = Assert.Single(violations);
                Assert.Equal("Game/Settings/Atlas/SA_Inventory.spriteatlas", violation.AssetPath);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void AtlasNotCoveringDirectoryIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteImportRule(assetsRoot, "Game/Art/Texture/Ui/导入规则.json", "SA_Inventory");
                WriteFile(assetsRoot, "Game/Art/Texture/Ui.meta", "fileFormatVersion: 2\nguid: abc123def456\n");
                WriteFile(assetsRoot, "Game/Settings/Atlas/SA_Inventory.spriteatlas", "%YAML 1.1\n--- !u!687078895\n");

                var violations = AssetAtlasChecker.Check(assetsRoot);

                var violation = Assert.Single(violations);
                Assert.Equal("Game/Art/Texture/Ui", violation.AssetPath);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void AtlasCoveringDirectoryIsAccepted()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteImportRule(assetsRoot, "Game/Art/Texture/Ui/导入规则.json", "SA_Inventory");
                WriteFile(assetsRoot, "Game/Art/Texture/Ui.meta", "fileFormatVersion: 2\nguid: abc123def456\n");
                WriteFile(assetsRoot, "Game/Settings/Atlas/SA_Inventory.spriteatlas", "{fileID: 102900000, guid: abc123def456, type: 3}");

                var violations = AssetAtlasChecker.Check(assetsRoot);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void WrongAtlasPrefixIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteImportRule(assetsRoot, "Game/Art/Texture/Ui/导入规则.json", "Inventory");

                var violations = AssetAtlasChecker.Check(assetsRoot);

                var violation = Assert.Single(violations);
                Assert.Equal("Game/Art/Texture/Ui/导入规则.json", violation.AssetPath);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void RuleWithoutAtlasIsAccepted()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(
                    assetsRoot,
                    "Game/Art/Texture/Ui/导入规则.json",
                    "{\"目录用途\":\"贴图-UI\",\"文件名前缀\":\"T_\",\"允许扩展名\":[\".png\"],\"命名风格\":\"PascalCase\",\"最大文件字节\":8388608}");

                var violations = AssetAtlasChecker.Check(assetsRoot);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        private static void WriteImportRule(string root, string relativePath, string atlas)
        {
            WriteFile(
                root,
                relativePath,
                "{\"目录用途\":\"贴图-UI\",\"文件名前缀\":\"T_\",\"允许扩展名\":[\".png\"],\"命名风格\":\"PascalCase\",\"最大文件字节\":8388608,\"图集\":\"" + atlas + "\"}");
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
            var directory = Path.Combine(Path.GetTempPath(), "AssetAtlasCheckerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
