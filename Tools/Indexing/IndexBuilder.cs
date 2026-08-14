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

                    entries.Add(new IndexEntry
                    {
                        RelativePath = relativePath,
                        FileName = Path.GetFileName(filePath),
                        AssetGuid = ReadAssetGuid(filePath),
                        FileHash = ComputeSha256(filePath)
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
