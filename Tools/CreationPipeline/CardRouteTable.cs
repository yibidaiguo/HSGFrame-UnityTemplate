using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 卡片类型 → 职责的路由表：内建默认表随基线发，项目可用 Pools/组织/卡片路由.json 逐键覆盖。
    /// 「提出人」是伪职责，由路由在查人前特判，不是成员表里的真职责。
    /// </summary>
    public sealed class CardRouteTable
    {
        /// <summary>
        /// 构造一张路由表。
        /// </summary>
        /// <param name="entries">卡片类型到职责的映射，传 null 视为空表。</param>
        /// <param name="loadFailureReason">加载失败原因，正常加载为空串。</param>
        public CardRouteTable(IReadOnlyDictionary<string, string> entries, string loadFailureReason)
        {
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            if (entries != null)
            {
                foreach (var pair in entries)
                {
                    copy[pair.Key] = pair.Value;
                }
            }

            Entries = copy;
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>
        /// 内建默认路由表：随基线发布的落点。项目覆盖文件只写要改的那几条。
        /// </summary>
        public static CardRouteTable Default()
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["选片"] = "美术",
                ["关卡"] = "程序",
                ["方案审"] = "程序",
                ["终审"] = "程序",
                ["冲突"] = "策划",
                ["待验收"] = "提出人",
                ["完成"] = "提出人",
                ["喊人"] = "管理员",
                ["熔断"] = "管理员",
                ["失败停机"] = "管理员",
                ["预算"] = "管理员"
            };

            return new CardRouteTable(entries, "");
        }

        /// <summary>
        /// 从池根加载路由表：先取默认表，若 &lt;池根&gt;/组织/卡片路由.json 存在且是
        /// 「卡片类型 → 职责」的 JSON 对象，就逐键覆盖默认值；文件坏掉时退回纯默认表，
        /// 原因记进 LoadFailureReason。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static CardRouteTable Load(string poolRoot)
        {
            var defaultTable = Default();
            var filePath = Path.Combine(PoolPaths.OrganizationDirectory(poolRoot), "卡片路由.json");
            if (!File.Exists(filePath))
            {
                return defaultTable;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new CardRouteTable(defaultTable.Entries, $"卡片路由文件解析失败：{filePath}：{exception.Message}");
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new CardRouteTable(defaultTable.Entries, $"卡片路由文件根必须是 JSON 对象：{filePath}");
                }

                var merged = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in defaultTable.Entries)
                {
                    merged[pair.Key] = pair.Value;
                }

                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        merged[property.Name] = property.Value.GetString() ?? "";
                    }
                }

                return new CardRouteTable(merged, "");
            }
        }

        /// <summary>卡片类型到职责的映射。</summary>
        private IReadOnlyDictionary<string, string> Entries { get; }

        /// <summary>加载失败原因，正常加载为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 取某卡片类型对应的职责；表里没有该卡片类型时返回空串。
        /// </summary>
        /// <param name="cardType">卡片类型。</param>
        public string DutyOf(string cardType)
        {
            return Entries.TryGetValue(cardType, out var duty) ? duty : "";
        }

        /// <summary>全部卡片类型，按序数序排序。</summary>
        public IReadOnlyList<string> CardTypes
        {
            get
            {
                var types = new List<string>(Entries.Keys);
                types.Sort(StringComparer.Ordinal);
                return types;
            }
        }
    }
}
