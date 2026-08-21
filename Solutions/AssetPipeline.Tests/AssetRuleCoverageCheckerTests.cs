using System;
using System.IO;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>导入规则覆盖校验测试：无规则目录被报、继承/自有规则放行、空目录放行、豁免目录按路径段对齐。</summary>
    public class AssetRuleCoverageCheckerTests
    {
        [Fact]
        public void DirectoryWithAssetsButNoRuleIsReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Art/Prefab/P_样例.prefab", "prefab 内容无所谓");

                var settings = new AssetRuleCoverageSettings
                {
                    ScanRoots = new[] { "Art" },
                    ExemptDirectories = Array.Empty<string>(),
                };

                var violations = AssetRuleCoverageChecker.Check(assetsRoot, settings);

                var violation = Assert.Single(violations);
                Assert.Equal("Art/Prefab", violation.AssetPath);
                Assert.Contains("解析不到", violation.ToDisplayText());
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void DirectoryInheritsRuleFromAncestor()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteImportRule(assetsRoot, "Art");
                WriteFile(assetsRoot, "Art/Texture/T_英雄_贴图.png", "贴图内容无所谓");

                var settings = new AssetRuleCoverageSettings
                {
                    ScanRoots = new[] { "Art" },
                    ExemptDirectories = Array.Empty<string>(),
                };

                var violations = AssetRuleCoverageChecker.Check(assetsRoot, settings);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void DirectoryWithOwnRuleIsNotReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteImportRule(assetsRoot, "Art/Texture");
                WriteFile(assetsRoot, "Art/Texture/T_英雄_贴图.png", "贴图内容无所谓");

                var settings = new AssetRuleCoverageSettings
                {
                    ScanRoots = new[] { "Art" },
                    ExemptDirectories = Array.Empty<string>(),
                };

                var violations = AssetRuleCoverageChecker.Check(assetsRoot, settings);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void EmptyDirectoryIsNotReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Art/空夹/某资产.png.meta", "fileFormatVersion: 2\nguid: 00000000000000000000000000000001\n");

                var settings = new AssetRuleCoverageSettings
                {
                    ScanRoots = new[] { "Art" },
                    ExemptDirectories = Array.Empty<string>(),
                };

                var violations = AssetRuleCoverageChecker.Check(assetsRoot, settings);

                Assert.Empty(violations);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void ExemptDirectoryIsNotReported()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteFile(assetsRoot, "Art/Prefab/P_样例.prefab", "prefab 内容无所谓");
                WriteFile(assetsRoot, "Art/PrefabBackup/P_另一个.prefab", "prefab 内容无所谓");

                var settings = new AssetRuleCoverageSettings
                {
                    ScanRoots = new[] { "Art" },
                    ExemptDirectories = new[] { "Art/Prefab" },
                };

                var violations = AssetRuleCoverageChecker.Check(assetsRoot, settings);

                // Art/Prefab 被豁免放行，但 Art/PrefabBackup 只是裸前缀同形，路径段不同，必须仍被报出。
                var violation = Assert.Single(violations);
                Assert.Equal("Art/PrefabBackup", violation.AssetPath);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        private static void WriteImportRule(string root, string relativeDirectory)
        {
            WriteFile(
                root,
                Path.Combine(relativeDirectory, "import-rules.json"),
                "{\"目录用途\":\"贴图\",\"文件名前缀\":\"T_\",\"允许扩展名\":[\".png\"],\"命名风格\":\"PascalCase\",\"最大文件字节\":8388608}");
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
            var directory = Path.Combine(Path.GetTempPath(), "AssetRuleCoverageCheckerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
