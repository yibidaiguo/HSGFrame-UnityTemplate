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
        /// <param name="probedEndpoint">这份探测是对着哪个地址探的；探测产出没盖这个章时给空串。</param>
        /// <param name="probedAtText">探测时间原文（ISO-8601 往返格式）；没盖章时给空串。</param>
        public CapabilityProbeResult(
            IReadOnlyList<CapabilityItem> nodes,
            IReadOnlyList<CapabilityItem> models,
            IReadOnlyList<CapabilityItem> loras,
            string probedEndpoint = "",
            string probedAtText = "")
        {
            Nodes = nodes ?? Array.Empty<CapabilityItem>();
            Models = models ?? Array.Empty<CapabilityItem>();
            Loras = loras ?? Array.Empty<CapabilityItem>();
            ProbedEndpoint = probedEndpoint ?? "";
            ProbedAtText = probedAtText ?? "";
        }

        /// <summary>节点类能力列表。</summary>
        public IReadOnlyList<CapabilityItem> Nodes { get; }

        /// <summary>模型类能力列表。</summary>
        public IReadOnlyList<CapabilityItem> Models { get; }

        /// <summary>lora 类能力列表。</summary>
        public IReadOnlyList<CapabilityItem> Loras { get; }

        /// <summary>
        /// 这份探测是对着哪个地址探的。**空串 = 这份产出没盖章**（老产出，或探测时本机没配地址），
        /// 不是「地址为空」——两者差得远：前者说明我们判断不了清单是不是当前地址的，后者是配置缺失。
        /// </summary>
        public string ProbedEndpoint { get; }

        /// <summary>
        /// 探测时间的原文（ISO-8601 往返格式，UTC）。空串同样是「没盖章」，不是「时间为零」。
        /// </summary>
        public string ProbedAtText { get; }

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

                // 「探于」「探测时间」是 bridge.probe 事后盖的章，**可选**：
                // 老的产出文件里没有这两个键，缺了照样读得动，只是判断不了它是哪个地址探的。
                return new CapabilityProbeResult(
                    ReadItems(root, "节点"),
                    ReadItems(root, "模型"),
                    ReadItems(root, "lora"),
                    ReadStringOrEmpty(root, "探于"),
                    ReadStringOrEmpty(root, "探测时间"));
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
