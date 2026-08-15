using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Template.Toolkit.Indexing
{
    /// <summary>扫描源文件生成索引。</summary>
    public static class IndexBuilder
    {
        /// <summary>扫描一类索引的全部命中文件，拼成一份索引文档。</summary>
        /// <param name="repositoryRoot">仓库根目录，相对路径都以此为基准。</param>
        /// <param name="definition">索引定义。</param>
        public static IndexDocument Build(string repositoryRoot, IndexDefinition definition)
        {
            var sourceDirectory = Path.Combine(repositoryRoot, definition.SourceRoot);
            var entries = new List<IndexEntry>();

            if (Directory.Exists(sourceDirectory))
            {
                foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, definition.FilePattern, SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(repositoryRoot, filePath).Replace('\\', '/');
                    if (ShouldSkip(relativePath))
                    {
                        continue;
                    }

                    var fileInfo = new FileInfo(filePath);
                    entries.Add(new IndexEntry
                    {
                        RelativePath = relativePath,
                        FileName = Path.GetFileName(filePath),
                        AssetGuid = ReadAssetGuid(filePath),
                        FileHash = ComputeSha256(filePath),
                        FileLength = fileInfo.Length,
                        LastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks
                    });
                }
            }

            entries.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));

            return new IndexDocument
            {
                IndexName = definition.IndexName,
                SourceRoot = definition.SourceRoot,
                SourceHash = ComputeSourceHash(entries),
                GeneratedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Entries = entries
            };
        }

        /// <summary>扫描并直接把索引写到定义里的输出路径。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="definition">索引定义。</param>
        public static void BuildAndSave(string repositoryRoot, IndexDefinition definition)
        {
            var document = Build(repositoryRoot, definition);
            document.SaveToFile(Path.Combine(repositoryRoot, definition.OutputPath));
        }

        /// <summary>
        /// 增量重建索引：文件长度与最后写入时间都没变的条目复用上次的哈希，其余照全量重算。
        /// previousDocument 为 null 时退回全量。同一份磁盘状态下，结果与 Build 完全一致。
        /// </summary>
        /// <param name="templateRoot">仓库根目录，相对路径都以此为基准。</param>
        /// <param name="definition">索引定义。</param>
        /// <param name="previousDocument">上一份索引文档，可为 null。</param>
        public static IndexDocument BuildIncremental(
            string templateRoot,
            IndexDefinition definition,
            IndexDocument previousDocument)
        {
            if (previousDocument == null)
            {
                return Build(templateRoot, definition);
            }

            var previousByPath = previousDocument.Entries.ToDictionary(entry => entry.RelativePath);
            var sourceDirectory = Path.Combine(templateRoot, definition.SourceRoot);
            var entries = new List<IndexEntry>();
            var reusedEntryCount = 0;

            if (Directory.Exists(sourceDirectory))
            {
                foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, definition.FilePattern, SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(templateRoot, filePath).Replace('\\', '/');
                    if (ShouldSkip(relativePath))
                    {
                        continue;
                    }

                    var fileInfo = new FileInfo(filePath);
                    if (previousByPath.TryGetValue(relativePath, out var previous)
                        && previous.FileLength == fileInfo.Length
                        && previous.LastWriteTimeUtcTicks == fileInfo.LastWriteTimeUtc.Ticks)
                    {
                        entries.Add(new IndexEntry
                        {
                            RelativePath = relativePath,
                            FileName = Path.GetFileName(filePath),
                            AssetGuid = previous.AssetGuid,
                            FileHash = previous.FileHash,
                            FileLength = previous.FileLength,
                            LastWriteTimeUtcTicks = previous.LastWriteTimeUtcTicks
                        });
                        reusedEntryCount++;
                    }
                    else
                    {
                        entries.Add(new IndexEntry
                        {
                            RelativePath = relativePath,
                            FileName = Path.GetFileName(filePath),
                            AssetGuid = ReadAssetGuid(filePath),
                            FileHash = ComputeSha256(filePath),
                            FileLength = fileInfo.Length,
                            LastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks
                        });
                    }
                }
            }

            entries.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));

            return new IndexDocument
            {
                IndexName = definition.IndexName,
                SourceRoot = definition.SourceRoot,
                SourceHash = ComputeSourceHash(entries),
                GeneratedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Entries = entries,
                ReusedEntryCount = reusedEntryCount
            };
        }

        // 源哈希算法必须稳定：按相对路径升序，逐个拼 "<相对路径>:<文件哈希>\n"，
        // 再对整体 UTF-8 字节算 SHA256。空集合就是空字符串的 SHA256。
        private static string ComputeSourceHash(IReadOnlyList<IndexEntry> entries)
        {
            var builder = new StringBuilder();
            foreach (var entry in entries)
            {
                builder.Append(entry.RelativePath).Append(':').Append(entry.FileHash).Append('\n');
            }

            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        private static string ComputeSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static string ReadAssetGuid(string filePath)
        {
            var metaPath = filePath + ".meta";
            if (!File.Exists(metaPath))
            {
                return string.Empty;
            }

            foreach (var line in File.ReadLines(metaPath))
            {
                if (line.StartsWith("guid:", StringComparison.Ordinal))
                {
                    return line.Substring("guid:".Length).Trim();
                }
            }

            return string.Empty;
        }

        // 按路径目录段精确排除生成目录，而不是做子串匹配，避免误伤名为 binaries 之类的合法目录。
        private static bool ShouldSkip(string relativePath)
        {
            return relativePath
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segment, "Library", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segment, "Temp", StringComparison.OrdinalIgnoreCase));
        }
    }
}
