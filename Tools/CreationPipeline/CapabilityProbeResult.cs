using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>探测输出里的一项能力：名称、版本与可选哈希。</summary>
    public sealed class CapabilityItem
    {
        /// <summary>
        /// 构造一项探测到的能力。
        /// </summary>
        /// <param name="name">能力名，如「Impact-Pack」。</param>
        /// <param name="version">版本。</param>
        /// <param name="hash">哈希；探测没给时为空串。</param>
        public CapabilityItem(string name, string version, string hash)
        {
            Name = name ?? "";
            Version = version ?? "";
            Hash = hash ?? "";
        }

        /// <summary>能力名，如「Impact-Pack」。</summary>
        public string Name { get; }

        /// <summary>版本。</summary>
        public string Version { get; }

        /// <summary>哈希；探测没给时为空串。</summary>
        public string Hash { get; }
    }

    /// <summary>
    /// 本地形态 driver 的能力探测输出：节点 / 模型 / lora 三类能力列表。
    /// 从探测生成的文件读，文件缺失或 JSON 坏掉抛 InvalidOperationException；
    /// 三个顶层键缺任意一个都当空列表处理——探测器可能只报它认识的那几类。
    /// </summary>
    public sealed class CapabilityProbeResult
    {
        /// <summary>
        /// 构造一次能力探测结果。
        /// </summary>
        /// <param name="nodes">节点类能力列表。</param>
        /// <param name="models">模型类能力列表。</param>
        /// <param name="loras">lora 类能力列表。</param>
        public CapabilityProbeResult(
            IReadOnlyList<CapabilityItem> nodes,
            IReadOnlyList<CapabilityItem> models,
            IReadOnlyList<CapabilityItem> loras)
        {
            Nodes = nodes ?? Array.Empty<CapabilityItem>();
            Models = models ?? Array.Empty<CapabilityItem>();
            Loras = loras ?? Array.Empty<CapabilityItem>();
        }

        /// <summary>节点类能力列表。</summary>
        public IReadOnlyList<CapabilityItem> Nodes { get; }

        /// <summary>模型类能力列表。</summary>
        public IReadOnlyList<CapabilityItem> Models { get; }

        /// <summary>lora 类能力列表。</summary>
        public IReadOnlyList<CapabilityItem> Loras { get; }

        /// <summary>
        /// 从文件读一份能力探测输出；文件不存在或 JSON 坏掉抛 InvalidOperationException。
        /// </summary>
        /// <param name="filePath">探测输出文件的绝对或相对路径。</param>
        /// <exception cref="InvalidOperationException">文件缺失或 JSON 非法时抛出。</exception>
        public static CapabilityProbeResult LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException($"找不到能力探测输出文件：{filePath}");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new InvalidOperationException($"能力探测输出文件不是合法 JSON：{filePath}：{exception.Message}", exception);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException($"能力探测输出文件不是合法 JSON：{filePath}");
                }

                return new CapabilityProbeResult(
                    ReadItems(root, "节点"),
                    ReadItems(root, "模型"),
                    ReadItems(root, "lora"));
            }
        }

        /// <summary>
        /// 探测结果里有没有指定类别、指定名称的能力；类别不认识时返回 false。
        /// </summary>
        /// <param name="category">类别：节点 / 模型 / lora。</param>
        /// <param name="name">能力名。</param>
        public bool Contains(string category, string name)
        {
            var items = category switch
            {
                "节点" => Nodes,
                "模型" => Models,
                "lora" => Loras,
                _ => null
            };

            if (items == null)
            {
                return false;
            }

            foreach (var item in items)
            {
                if (string.Equals(item.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>读一个类别数组；键缺失或不是数组时当空列表。</summary>
        private static IReadOnlyList<CapabilityItem> ReadItems(JsonElement root, string propertyName)
        {
            var items = new List<CapabilityItem>();
            if (!root.TryGetProperty(propertyName, out var listElement) || listElement.ValueKind != JsonValueKind.Array)
            {
                return items;
            }

            foreach (var item in listElement.EnumerateArray())
            {
                items.Add(new CapabilityItem(
                    ReadStringOrEmpty(item, "名"),
                    ReadStringOrEmpty(item, "版本"),
                    ReadStringOrEmpty(item, "hash")));
            }

            return items;
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
