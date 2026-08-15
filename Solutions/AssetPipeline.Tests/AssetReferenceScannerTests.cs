using System;
using System.IO;
using System.Linq;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipelineTests
{
    /// <summary>GUID 级引用扫描测试。</summary>
    public class AssetReferenceScannerTests
    {
        private const string UsedGuid = "11111111111111111111111111111111";
        private const string OrphanGuid = "22222222222222222222222222222222";
        private const string FolderGuid = "33333333333333333333333333333333";
        private const string PrefabGuid = "44444444444444444444444444444444";
        private const string SceneGuid = "55555555555555555555555555555555";
        private const string DanglingGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        [Fact]
        public void ReferencedAssetIsNotReportedAsUnreferenced()
        {
            using var root = new TempRoot();
            WriteAsset(root.RootPath, "Texture/used.png", "x");
            WriteMeta(root.RootPath, "Texture/used.png", UsedGuid);
            WriteAsset(root.RootPath, "Prefab/ref.prefab", $"m_Script: {{fileID: 1, guid: {UsedGuid}, type: 3}}");
            WriteMeta(root.RootPath, "Prefab/ref.prefab", PrefabGuid);

            var report = AssetReferenceScanner.Scan(root.RootPath);

            Assert.DoesNotContain("Texture/used.png", report.UnreferencedAssetPaths);
        }

        [Fact]
        public void UnreferencedAssetIsReported()
        {
            using var root = new TempRoot();
            WriteAsset(root.RootPath, "Texture/orphan.png", "x");
            WriteMeta(root.RootPath, "Texture/orphan.png", OrphanGuid);

            var report = AssetReferenceScanner.Scan(root.RootPath);

            Assert.Contains("Texture/orphan.png", report.UnreferencedAssetPaths);
        }

        [Fact]
        public void DanglingReferenceIsKeyedByReferencerRelativePath()
        {
            using var root = new TempRoot();
            WriteAsset(root.RootPath, "Prefab/ref.prefab", $"guid: {DanglingGuid}");
            WriteMeta(root.RootPath, "Prefab/ref.prefab", PrefabGuid);

            var report = AssetReferenceScanner.Scan(root.RootPath);

            Assert.True(report.DanglingReferences.ContainsKey("Prefab/ref.prefab"));
        }

        [Fact]
        public void DanglingReferenceRecordsTheExactGuidText()
        {
            using var root = new TempRoot();
            WriteAsset(root.RootPath, "Prefab/ref.prefab", $"guid: {DanglingGuid}");
            WriteMeta(root.RootPath, "Prefab/ref.prefab", PrefabGuid);

            var report = AssetReferenceScanner.Scan(root.RootPath);

            Assert.Contains(DanglingGuid, report.DanglingReferences["Prefab/ref.prefab"]);
        }

        [Fact]
        public void DirectoryMetaIsNotReportedAsUnreferenced()
        {
            using var root = new TempRoot();
            Directory.CreateDirectory(Path.Combine(root.RootPath, "Texture", "folder"));
            WriteMeta(root.RootPath, "Texture/folder", FolderGuid);

            var report = AssetReferenceScanner.Scan(root.RootPath);

            Assert.DoesNotContain("Texture/folder", report.UnreferencedAssetPaths);
        }

        [Fact]
        public void UnreferencedReferencerItselfIsStillReported()
        {
            using var root = new TempRoot();
            WriteAsset(root.RootPath, "Prefab/ref.prefab", "guid: 00000000000000000000000000000000");
            WriteMeta(root.RootPath, "Prefab/ref.prefab", PrefabGuid);

            var report = AssetReferenceScanner.Scan(root.RootPath);

            Assert.Contains("Prefab/ref.prefab", report.UnreferencedAssetPaths);
        }

        [Fact]
        public void DuplicateGuidReferencesAreCountedOnce()
        {
            using var root = new TempRoot();
            WriteAsset(root.RootPath, "Prefab/ref.prefab",
                $"guid: {DanglingGuid}\nguid: {DanglingGuid}\nguid: {DanglingGuid}");
            WriteMeta(root.RootPath, "Prefab/ref.prefab", PrefabGuid);

            var report = AssetReferenceScanner.Scan(root.RootPath);

            var danglingList = report.DanglingReferences["Prefab/ref.prefab"];
            Assert.Single(danglingList);
            Assert.Equal(DanglingGuid, danglingList[0]);
        }

        [Fact]
        public void CustomScannedExtensionsNarrowWhichFilesAreReferencers()
        {
            using var root = new TempRoot();
            WriteAsset(root.RootPath, "Scenes/scene.unity", $"guid: {DanglingGuid}");
            WriteMeta(root.RootPath, "Scenes/scene.unity", SceneGuid);

            var report = AssetReferenceScanner.Scan(root.RootPath, new[] { ".prefab" });

            Assert.False(report.DanglingReferences.ContainsKey("Scenes/scene.unity"));
            Assert.Contains("Scenes/scene.unity", report.UnreferencedAssetPaths);
        }

        [Fact]
        public void EmptyRootYieldsEmptyReport()
        {
            using var root = new TempRoot();

            var report = AssetReferenceScanner.Scan(root.RootPath);

            Assert.Empty(report.UnreferencedAssetPaths);
            Assert.Empty(report.DanglingReferences);
        }

        [Fact]
        public void ResultsAreSortedAndStableAcrossRuns()
        {
            using var root = new TempRoot();
            WriteAsset(root.RootPath, "Texture/zebra.png", "x");
            WriteMeta(root.RootPath, "Texture/zebra.png", "99999999999999999999999999999999");
            WriteAsset(root.RootPath, "Texture/alpha.png", "x");
            WriteMeta(root.RootPath, "Texture/alpha.png", "88888888888888888888888888888888");
            WriteAsset(root.RootPath, "Audio/middle.wav", "x");
            WriteMeta(root.RootPath, "Audio/middle.wav", "77777777777777777777777777777777");

            var first = AssetReferenceScanner.Scan(root.RootPath);
            var second = AssetReferenceScanner.Scan(root.RootPath);

            Assert.Equal(first.UnreferencedAssetPaths, second.UnreferencedAssetPaths);
            Assert.Equal(
                first.UnreferencedAssetPaths.OrderBy(path => path, StringComparer.Ordinal).ToList(),
                first.UnreferencedAssetPaths);
        }

        [Fact]
        public void MetaLinesWithWrongGuidFormatAreIgnored()
        {
            using var root = new TempRoot();
            WriteAsset(root.RootPath, "Texture/noisy.png", "x");
            File.WriteAllText(
                Path.Combine(root.RootPath, "Texture", "noisy.png.meta"),
                "fileFormatVersion: 2\n"
                + "GUID: " + DanglingGuid + "\n"
                + "guid: " + DanglingGuid + " \n"
                + "guid: zzzz\n");

            var report = AssetReferenceScanner.Scan(root.RootPath);

            Assert.DoesNotContain("Texture/noisy.png", report.UnreferencedAssetPaths);
        }

        private static void WriteAsset(string root, string relativePath, string content)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
        }

        private static void WriteMeta(string root, string relativeAssetPath, string guid)
        {
            var metaPath = Path.Combine(root, relativeAssetPath.Replace('/', Path.DirectorySeparatorChar) + ".meta");
            Directory.CreateDirectory(Path.GetDirectoryName(metaPath));
            File.WriteAllText(metaPath, "fileFormatVersion: 2\nguid: " + guid + "\n");
        }

        private sealed class TempRoot : IDisposable
        {
            public TempRoot()
            {
                RootPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "AssetReferenceScannerTests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(RootPath);
            }

            public string RootPath { get; }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(RootPath, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
