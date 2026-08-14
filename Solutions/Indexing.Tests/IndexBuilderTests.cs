using System;
using System.IO;
using Template.Toolkit.Indexing;
using Xunit;

namespace Template.Toolkit.IndexingTests
{
    /// <summary>索引生成与新鲜度校验测试。</summary>
    public class IndexBuilderTests
    {
        [Fact]
        public void IndexBuilderBuildsEntriesWithFileHashes()
        {
            var repositoryRoot = CreateTemporaryDirectory();
            try
            {
                var sourceDirectory = Path.Combine(repositoryRoot, "samples");
                Directory.CreateDirectory(sourceDirectory);
                File.WriteAllText(Path.Combine(sourceDirectory, "alpha.txt"), "alpha content");
                File.WriteAllText(Path.Combine(sourceDirectory, "beta.txt"), "beta content");
                File.WriteAllText(Path.Combine(sourceDirectory, "gamma.txt"), "gamma content");

                var definition = NewDefinition("测试索引", "samples", "*.txt", "index.json");
                var document = IndexBuilder.Build(repositoryRoot, definition);

                Assert.Equal(3, document.Entries.Count);
                Assert.All(document.Entries, entry => Assert.False(string.IsNullOrEmpty(entry.FileHash)));
            }
            finally
            {
                Directory.Delete(repositoryRoot, true);
            }
        }

        [Fact]
        public void IndexBuilderReadsAssetGuidFromMetaFile()
        {
            var repositoryRoot = CreateTemporaryDirectory();
            try
            {
                var sourceDirectory = Path.Combine(repositoryRoot, "samples");
                Directory.CreateDirectory(sourceDirectory);
                var filePath = Path.Combine(sourceDirectory, "model.fbx");
                File.WriteAllText(filePath, "fake fbx");
                File.WriteAllText(filePath + ".meta", "fileFormatVersion: 2\nguid: abc123def456\n");

                var definition = NewDefinition("模型索引", "samples", "*.fbx", "index.json");
                var document = IndexBuilder.Build(repositoryRoot, definition);

                var entry = Assert.Single(document.Entries);
                Assert.Equal("abc123def456", entry.AssetGuid);
            }
            finally
            {
                Directory.Delete(repositoryRoot, true);
            }
        }

        [Fact]
        public void FreshnessCheckerReturnsEmptyWhenFresh()
        {
            var repositoryRoot = CreateTemporaryDirectory();
            try
            {
                var sourceDirectory = Path.Combine(repositoryRoot, "samples");
                Directory.CreateDirectory(sourceDirectory);
                File.WriteAllText(Path.Combine(sourceDirectory, "a.txt"), "content");

                var definition = NewDefinition("测试索引", "samples", "*.txt", "index.json");
                IndexBuilder.BuildAndSave(repositoryRoot, definition);
                var configuration = new IndexConfiguration { Definitions = new[] { definition } };

                var problems = IndexFreshnessChecker.Check(repositoryRoot, configuration);

                Assert.Empty(problems);
            }
            finally
            {
                Directory.Delete(repositoryRoot, true);
            }
        }

        [Fact]
        public void FreshnessCheckerReportsStaleAfterSourceChanges()
        {
            var repositoryRoot = CreateTemporaryDirectory();
            try
            {
                var sourceDirectory = Path.Combine(repositoryRoot, "samples");
                Directory.CreateDirectory(sourceDirectory);
                var filePath = Path.Combine(sourceDirectory, "a.txt");
                File.WriteAllText(filePath, "original content");

                var definition = NewDefinition("测试索引", "samples", "*.txt", "index.json");
                IndexBuilder.BuildAndSave(repositoryRoot, definition);
                var configuration = new IndexConfiguration { Definitions = new[] { definition } };

                File.WriteAllText(filePath, "changed content");

                var problems = IndexFreshnessChecker.Check(repositoryRoot, configuration);

                Assert.Contains(problems, problem => problem.Contains("索引已过期"));
            }
            finally
            {
                Directory.Delete(repositoryRoot, true);
            }
        }

        [Fact]
        public void FreshnessCheckerReportsMissingWhenOutputDeleted()
        {
            var repositoryRoot = CreateTemporaryDirectory();
            try
            {
                var sourceDirectory = Path.Combine(repositoryRoot, "samples");
                Directory.CreateDirectory(sourceDirectory);
                File.WriteAllText(Path.Combine(sourceDirectory, "a.txt"), "content");

                var definition = NewDefinition("测试索引", "samples", "*.txt", "index.json");
                IndexBuilder.BuildAndSave(repositoryRoot, definition);
                var configuration = new IndexConfiguration { Definitions = new[] { definition } };

                File.Delete(Path.Combine(repositoryRoot, "index.json"));

                var problems = IndexFreshnessChecker.Check(repositoryRoot, configuration);

                Assert.Contains(problems, problem => problem.Contains("索引尚未生成"));
            }
            finally
            {
                Directory.Delete(repositoryRoot, true);
            }
        }

        private static IndexDefinition NewDefinition(string indexName, string sourceRoot, string filePattern, string outputPath)
        {
            return new IndexDefinition
            {
                IndexName = indexName,
                SourceRoot = sourceRoot,
                FilePattern = filePattern,
                OutputPath = outputPath
            };
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "IndexingTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
