using System;
using System.IO;
using System.Linq;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>引用扫描的范围测试：内置 guid、包里的 guid、非内容资产三类都不该进结果。</summary>
    public class AssetReferenceScannerScopeTests
    {
        // 这三条都是拿真工程跑扫描时暴露的：第一版在这个工程上报了 20 条悬空引用与 74 个无人引用，
        // 逐条看下来没有一条是真问题。
        [Fact]
        public void BuiltinGuidsAreNotReportedAsDangling()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                // Unity 内置资源的 guid：16 个 0、一位十六进制、再 15 个 0。
                WriteAsset(assetsRoot, "场景.unity", "  m_Script: {fileID: 12, guid: 0000000000000000e000000000000000, type: 0}");
                WriteMeta(assetsRoot, "场景.unity", "a0000000000000000000000000000001");

                var report = AssetReferenceScanner.Scan(assetsRoot);

                Assert.Empty(report.DanglingReferences);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void GuidsOwnedByAnAdditionalSourceAreNotReportedAsDangling()
        {
            var assetsRoot = CreateTempDirectory();
            var packagesRoot = CreateTempDirectory();
            try
            {
                const string packageGuid = "b0000000000000000000000000000002";
                WriteAsset(assetsRoot, "预制.prefab", $"  m_Script: {{fileID: 11, guid: {packageGuid}, type: 3}}");
                WriteMeta(assetsRoot, "预制.prefab", "a0000000000000000000000000000001");
                File.WriteAllText(Path.Combine(packagesRoot, "包脚本.cs.meta"), $"fileFormatVersion: 2\nguid: {packageGuid}\n");

                var withoutSource = AssetReferenceScanner.Scan(assetsRoot);
                var withSource = AssetReferenceScanner.Scan(assetsRoot, null, new[] { packagesRoot });

                Assert.NotEmpty(withoutSource.DanglingReferences);
                Assert.Empty(withSource.DanglingReferences);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
                Directory.Delete(packagesRoot, true);
            }
        }

        [Fact]
        public void ScriptsAndAssemblyDefinitionsAreNotReportedAsUnreferenced()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteAsset(assetsRoot, "某个脚本.cs", "public class 某个脚本 { }");
                WriteMeta(assetsRoot, "某个脚本.cs", "a0000000000000000000000000000011");
                WriteAsset(assetsRoot, "某个程序集.asmdef", "{}");
                WriteMeta(assetsRoot, "某个程序集.asmdef", "a0000000000000000000000000000012");

                var report = AssetReferenceScanner.Scan(assetsRoot);

                Assert.Empty(report.UnreferencedAssetPaths);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void ContentAssetsAreStillReportedAsUnreferenced()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteAsset(assetsRoot, "T_孤儿贴图.png", "内容无所谓");
                WriteMeta(assetsRoot, "T_孤儿贴图.png", "a0000000000000000000000000000021");

                var report = AssetReferenceScanner.Scan(assetsRoot);

                Assert.Equal(new[] { "T_孤儿贴图.png" }, report.UnreferencedAssetPaths);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        [Fact]
        public void AMissingAdditionalSourceDirectoryIsIgnored()
        {
            var assetsRoot = CreateTempDirectory();
            try
            {
                WriteAsset(assetsRoot, "T_贴图.png", "内容无所谓");
                WriteMeta(assetsRoot, "T_贴图.png", "a0000000000000000000000000000031");

                var missing = Path.Combine(Path.GetTempPath(), "没有这个目录-" + Guid.NewGuid().ToString("N"));
                var report = AssetReferenceScanner.Scan(assetsRoot, null, new[] { missing });

                Assert.Single(report.UnreferencedAssetPaths);
            }
            finally
            {
                Directory.Delete(assetsRoot, true);
            }
        }

        private static void WriteAsset(string root, string fileName, string content)
        {
            File.WriteAllText(Path.Combine(root, fileName), content);
        }

        private static void WriteMeta(string root, string fileName, string guid)
        {
            File.WriteAllText(Path.Combine(root, fileName + ".meta"), $"fileFormatVersion: 2\nguid: {guid}\n");
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AssetReferenceScannerScopeTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
