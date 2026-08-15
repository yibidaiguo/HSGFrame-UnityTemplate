using System;
using System.IO;
using System.Linq;
using Template.Toolkit.Indexing;
using Xunit;

namespace Template.Toolkit.IndexingTests
{
    /// <summary>索引生成的边界测试：空源、缺 meta、生成目录排除与排序稳定性。</summary>
    public class IndexBoundaryTests
    {
        [Fact]
        public void BuildWithMissingSourceRootYieldsZeroEntries()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                var definition = NewDefinition("不存在的目录");
                var document = IndexBuilder.Build(root, definition);

                Assert.Empty(document.Entries);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void BuildWithEmptySourceRootYieldsEmptySourceHash()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "samples"));

                var document = IndexBuilder.Build(root, NewDefinition("samples"));

                Assert.Empty(document.Entries);
                // 空集合的源哈希就是空字符串的 SHA-256。
                Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", document.SourceHash);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void BuildWithNoMatchingFilesYieldsZeroEntries()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                var sourceDirectory = Path.Combine(root, "samples");
                Directory.CreateDirectory(sourceDirectory);
                File.WriteAllText(Path.Combine(sourceDirectory, "note.md"), "content");

                var definition = NewDefinition("samples");
                var document = IndexBuilder.Build(root, definition);

                Assert.Empty(document.Entries);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void BuildSkipsGeneratedDirectories()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                var sourceDirectory = Path.Combine(root, "samples");
                Directory.CreateDirectory(Path.Combine(sourceDirectory, "bin"));
                Directory.CreateDirectory(Path.Combine(sourceDirectory, "obj"));
                Directory.CreateDirectory(Path.Combine(sourceDirectory, "Library"));
                Directory.CreateDirectory(Path.Combine(sourceDirectory, "Temp"));
                Directory.CreateDirectory(Path.Combine(sourceDirectory, "real"));
                File.WriteAllText(Path.Combine(sourceDirectory, "bin", "a.txt"), "skip");
                File.WriteAllText(Path.Combine(sourceDirectory, "obj", "b.txt"), "skip");
                File.WriteAllText(Path.Combine(sourceDirectory, "Library", "c.txt"), "skip");
                File.WriteAllText(Path.Combine(sourceDirectory, "Temp", "d.txt"), "skip");
                File.WriteAllText(Path.Combine(sourceDirectory, "real", "keep.txt"), "keep");

                var document = IndexBuilder.Build(root, NewDefinition("samples"));

                var entry = Assert.Single(document.Entries);
                Assert.Equal("keep.txt", entry.FileName);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void BuildWithoutMetaLeavesEmptyGuid()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                var sourceDirectory = Path.Combine(root, "samples");
                Directory.CreateDirectory(sourceDirectory);
                File.WriteAllText(Path.Combine(sourceDirectory, "no-meta.fbx"), "fake fbx");

                var definition = new IndexDefinition
                {
                    IndexName = "模型索引",
                    SourceRoot = "samples",
                    FilePattern = "*.fbx",
                    OutputPath = "index.json"
                };

                var document = IndexBuilder.Build(root, definition);

                var entry = Assert.Single(document.Entries);
                Assert.Equal(string.Empty, entry.AssetGuid);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void BuildReadsGuidFromMetaFile()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                var sourceDirectory = Path.Combine(root, "samples");
                Directory.CreateDirectory(sourceDirectory);
                var filePath = Path.Combine(sourceDirectory, "model.fbx");
                File.WriteAllText(filePath, "fake fbx");
                File.WriteAllText(filePath + ".meta", "fileFormatVersion: 2\nguid: deadbeef0123\n");

                var definition = new IndexDefinition
                {
                    IndexName = "模型索引",
                    SourceRoot = "samples",
                    FilePattern = "*.fbx",
                    OutputPath = "index.json"
                };

                var document = IndexBuilder.Build(root, definition);

                var entry = Assert.Single(document.Entries);
                Assert.Equal("deadbeef0123", entry.AssetGuid);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void BuildOrdersEntriesStablyRegardlessOfCreationOrder()
        {
            var rootA = CreateTemporaryDirectory();
            var rootB = CreateTemporaryDirectory();
            try
            {
                CreateSourceFile(rootA, "samples", "z.txt", "z");
                CreateSourceFile(rootA, "samples", "a.txt", "a");
                CreateSourceFile(rootB, "samples", "a.txt", "a");
                CreateSourceFile(rootB, "samples", "z.txt", "z");

                var documentA = IndexBuilder.Build(rootA, NewDefinition("samples"));
                var documentB = IndexBuilder.Build(rootB, NewDefinition("samples"));

                Assert.Equal(
                    documentA.Entries.Select(entry => entry.RelativePath),
                    documentB.Entries.Select(entry => entry.RelativePath));
                Assert.Equal(documentA.SourceHash, documentB.SourceHash);
            }
            finally
            {
                Directory.Delete(rootA, true);
                Directory.Delete(rootB, true);
            }
        }

        [Fact]
        public void SaveToFileOmitsReusedEntryCount()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                CreateSourceFile(root, "samples", "a.txt", "content");
                var document = IndexBuilder.Build(root, NewDefinition("samples"));
                document.ReusedEntryCount = 42;
                var indexPath = Path.Combine(root, "index.json");
                document.SaveToFile(indexPath);

                var json = File.ReadAllText(indexPath);
                Assert.DoesNotContain("reusedEntryCount", json);

                var loaded = IndexDocument.LoadFromFile(indexPath);
                Assert.Equal(0, loaded.ReusedEntryCount);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static IndexDefinition NewDefinition(string sourceRoot)
        {
            return new IndexDefinition
            {
                IndexName = "测试索引",
                SourceRoot = sourceRoot,
                FilePattern = "*.txt",
                OutputPath = "index.json"
            };
        }

        private static void CreateSourceFile(string root, string sourceRoot, string fileName, string content)
        {
            var directory = Path.Combine(root, sourceRoot);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), content);
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "IndexBoundaryTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
