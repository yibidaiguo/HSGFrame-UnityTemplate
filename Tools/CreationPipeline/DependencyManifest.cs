using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>依赖清单里的一条：名称、类别、版本、来源与可选安装命令。</summary>
    public sealed class DependencyEntry
    {
        /// <summary>
        /// 构造一条依赖。
        /// </summary>
        /// <param name="name">依赖名，如「Impact-Pack」。</param>
        /// <param name="category">类别：节点 / 模型 / lora。</param>
        /// <param name="version">依赖版本。</param>
        /// <param name="source">来源 URL。</param>
        /// <param name="installCommand">安装命令；可空。</param>
        /// <param name="description">说明。</param>
        public DependencyEntry(string name, string category, string version, string source, string installCommand, string description)
        {
            Name = name ?? "";
            Category = category ?? "";
            Version = version ?? "";
            Source = source ?? "";
            InstallCommand = installCommand ?? "";
            Description = description ?? "";
        }

        /// <summary>依赖名，如「Impact-Pack」。</summary>
        public string Name { get; }

        /// <summary>类别：节点 / 模型 / lora。</summary>
        public string Category { get; }

        /// <summary>依赖版本。</summary>
        public string Version { get; }

        /// <summary>来源 URL。</summary>
        public string Source { get; }

        /// <summary>安装命令；空串表示清单没给。</summary>
        public string InstallCommand { get; }

        /// <summary>说明。</summary>
        public string Description { get; }
    }

    /// <summary>
    /// 本地形态 driver 的依赖清单：从 &lt;仓库根&gt;/Bridges/&lt;driver&gt;/dependencies.json 读出，
    /// 条目按名称序数序排序。文件缺失或类别不合法时抛 InvalidOperationException。
    /// </summary>
    public sealed class DependencyManifest
    {
        /// <summary>类别只许这三个值。</summary>
        public static readonly string[] AllowedCategories = { "节点", "模型", "lora" };

        /// <summary>
        /// 构造一份依赖清单。
        /// </summary>
        /// <param name="contractVersion">清单的契约版本。</param>
        /// <param name="entries">依赖条目列表，按名称序数序。</param>
        public DependencyManifest(string contractVersion, IReadOnlyList<DependencyEntry> entries)
        {
            ContractVersion = contractVersion ?? "";
            Entries = entries ?? Array.Empty<DependencyEntry>();
        }

        /// <summary>清单的契约版本。</summary>
        public string ContractVersion { get; }

        /// <summary>依赖条目列表，按名称序数序。</summary>
        public IReadOnlyList<DependencyEntry> Entries { get; }

        /// <summary>
        /// 读取并校验一份依赖清单：文件必须在，条目类别只许 节点 / 模型 / lora。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <exception cref="InvalidOperationException">清单文件缺失或某条类别不合法时抛出。</exception>
        public static DependencyManifest Load(string repositoryRoot, string driverName)
        {
            var filePath = DependencyManifestFile(repositoryRoot, driverName);
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException($"找不到依赖清单文件：{filePath}");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new InvalidOperationException($"依赖清单文件不是合法 JSON：{filePath}：{exception.Message}", exception);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException($"依赖清单文件不是合法 JSON：{filePath}");
                }

                var entries = new List<DependencyEntry>();
                if (root.TryGetProperty("依赖", out var dependencyElement) && dependencyElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dependencyElement.EnumerateArray())
                    {
                        var name = ReadStringOrEmpty(item, "名称");
                        var category = ReadStringOrEmpty(item, "类别");
                        if (!Array.Exists(AllowedCategories, allowed => string.Equals(allowed, category, StringComparison.Ordinal)))
                        {
                            throw new InvalidOperationException(
                                $"依赖清单第 {entries.Count + 1} 条「{name}」的类别「{category}」不合法，类别只许：{string.Join("、", AllowedCategories)}：{filePath}");
                        }

                        entries.Add(new DependencyEntry(
                            name,
                            category,
                            ReadStringOrEmpty(item, "版本"),
                            ReadStringOrEmpty(item, "来源"),
                            ReadStringOrEmpty(item, "安装命令"),
                            ReadStringOrEmpty(item, "说明")));
                    }
                }

                entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
                return new DependencyManifest(ReadStringOrEmpty(root, "契约版本"), entries);
            }
        }

        /// <summary>某 driver 的依赖清单文件是否存在。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static bool Exists(string repositoryRoot, string driverName)
        {
            return File.Exists(DependencyManifestFile(repositoryRoot, driverName));
        }

        /// <summary>
        /// 按名称序数比较查一条依赖；查不到返回 false。
        /// </summary>
        /// <param name="name">依赖名。</param>
        /// <param name="entry">命中的依赖条目；没查到给 null。</param>
        public bool TryFind(string name, out DependencyEntry entry)
        {
            foreach (var candidate in Entries)
            {
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        /// <summary>依赖清单文件的路径：Bridges/&lt;driver&gt;/dependencies.json。</summary>
        private static string DependencyManifestFile(string repositoryRoot, string driverName)
        {
            return RecipePaths.DependencyManifestFile(repositoryRoot, driverName);
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }
    }
}
