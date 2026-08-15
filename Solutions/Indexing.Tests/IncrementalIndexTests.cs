using System;
using System.IO;
using System.Linq;
using Template.Toolkit.Indexing;
using Xunit;

namespace Template.Toolkit.IndexingTests
{
    /// <summary>索引增量重建测试：验证 BuildIncremental 与全量 Build 结果一致，并按文件长度与修改时间复用旧哈希。</summary>
    public class IncrementalIndexTests
    {
        [Fact]
        public void FirstIncrementalBuildWithNullPreviousMatchesFullBuild()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                CreateSourceFile(root, "samples", "alpha.txt", "alpha content");
                CreateSourceFile(root, "samples", "beta.txt", "beta content");

                var definition = NewDefinition();
                var full = IndexBuilder.Build(root, definition);
                var incremental = IndexBuilder.BuildIncremental(root, definition, null);

                Assert.Equal(full.SourceHash, incremental.SourceHash);
                Assert.Equal(full.Entries.Count, incremental.Entries.Count);
                Assert.Equal(0, incremental.ReusedEntryCount);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void IncrementalReusesAllEntriesWhenDiskUnchanged()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                CreateSourceFile(root, "samples", "alpha.txt", "alpha content");
                CreateSourceFile(root, "samples", "beta.txt", "beta content");

                var definition = NewDefinition();
                var first = IndexBuilder.Build(root, definition);

                var second = IndexBuilder.BuildIncremental(root, definition, first);

                Assert.Equal(first.Entries.Count, second.ReusedEntryCount);
                Assert.Equal(IndexBuilder.Build(root, definition).SourceHash, second.SourceHash);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void IncrementalRecomputesOnlyChangedEntry()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                CreateSourceFile(root, "samples", "alpha.txt", "alpha content");
                CreateSourceFile(root, "samples", "beta.txt", "beta content");

                var definition = NewDefinition();
                var first = IndexBuilder.Build(root, definition);

                // 只改 beta，长度与修改时间都变，只有它被重算。
                CreateSourceFile(root, "samples", "beta.txt", "beta content changed");

                var second = IndexBuilder.BuildIncremental(root, definition, first);

                Assert.Equal(first.Entries.Count - 1, second.ReusedEntryCount);
                Assert.Equal(IndexBuilder.Build(root, definition).SourceHash, second.SourceHash);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void IncrementalAddsNewFile()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                CreateSourceFile(root, "samples", "alpha.txt", "alpha content");

                var definition = NewDefinition();
                var first = IndexBuilder.Build(root, definition);

                CreateSourceFile(root, "samples", "delta.txt", "delta content");

                var second = IndexBuilder.BuildIncremental(root, definition, first);

                Assert.Equal(first.Entries.Count + 1, second.Entries.Count);
                var newEntry = second.Entries.Single(entry => entry.FileName == "delta.txt");
                Assert.False(string.IsNullOrEmpty(newEntry.FileHash));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void IncrementalRemovesDeletedFile()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                CreateSourceFile(root, "samples", "alpha.txt", "alpha content");
                CreateSourceFile(root, "samples", "beta.txt", "beta content");

                var definition = NewDefinition();
                var first = IndexBuilder.Build(root, definition);

                File.Delete(Path.Combine(root, "samples", "alpha.txt"));

                var second = IndexBuilder.BuildIncremental(root, definition, first);

                Assert.Equal(first.Entries.Count - 1, second.Entries.Count);
                Assert.DoesNotContain(second.Entries, entry => entry.FileName == "alpha.txt");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// 文件长度不变、内容变了、修改时间也被改回原值时，增量会复用旧哈希。
        /// 这是增量的已知代价：拿时间戳与长度换速度，内容伪装成未变时哈希会陈旧；拿不准时跑一次全量即可兜底。
        /// </summary>
        [Fact]
        public void IncrementalReusesOldHashWhenLengthAndTimestampUnchanged()
        {
            var root = CreateTemporaryDirectory();
            try
            {
                var filePath = Path.Combine(root, "samples", "alpha.txt");
                CreateSourceFile(root, "samples", "alpha.txt", "abc");

                var definition = NewDefinition();
                var first = IndexBuilder.Build(root, definition);
                var originalEntry = first.Entries.Single(entry => entry.FileName == "alpha.txt");

                // 内容改成同长度的 "def"，再把修改时间改回 Build 时记录的值。
                File.WriteAllText(filePath, "def");
                File.SetLastWriteTimeUtc(filePath, new DateTime(originalEntry.LastWriteTimeUtcTicks, DateTimeKind.Utc));

                var second = IndexBuilder.BuildIncremental(root, definition, first);
                var entry = second.Entries.Single(item => item.FileName == "alpha.txt");

                Assert.Equal(originalEntry.FileHash, entry.FileHash);
                Assert.Equal(first.Entries.Count, second.ReusedEntryCount);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static IndexDefinition NewDefinition()
        {
            return new IndexDefinition
            {
                IndexName = "测试索引",
                SourceRoot = "samples",
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
            var path = Path.Combine(Path.GetTempPath(), "IncrementalIndexTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
