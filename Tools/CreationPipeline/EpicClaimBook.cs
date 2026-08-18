using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 专项认领表：读 Pools/专项/*.json，建「专项 id → (职责 → open_id 列表)」的表，
    /// 供卡片路由第②步按专项认领查人。坏文件跳过，原因累加进 LoadFailureReason。
    /// </summary>
    public sealed class EpicClaimBook
    {
        /// <summary>专项 id → 职责 → open_id 列表。</summary>
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> _entries;

        /// <summary>
        /// 构造一本专项认领表。
        /// </summary>
        /// <param name="entries">专项 id → (职责 → open_id 列表) 的映射，传 null 视为空表。</param>
        /// <param name="loadFailureReason">加载失败原因，正常加载为空串。</param>
        public EpicClaimBook(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> entries,
            string loadFailureReason)
        {
            var copy = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
            if (entries != null)
            {
                foreach (var pair in entries)
                {
                    copy[pair.Key] = pair.Value;
                }
            }

            _entries = copy;
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>
        /// 从池根扫描全部专项文件：读 &lt;池根&gt;/专项/*.json（顶层，不递归）。
        /// 文件解析失败、根不是对象或缺 id 的跳过并累加原因；缺「认领」的专项视为无认领人，不算坏文件。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static EpicClaimBook Load(string poolRoot)
        {
            var epicDirectory = PoolPaths.EpicsDirectory(poolRoot);
            var failures = new List<string>();
            var table = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);

            if (!Directory.Exists(epicDirectory))
            {
                return new EpicClaimBook(table, "");
            }

            foreach (var filePath in Directory.EnumerateFiles(epicDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(File.ReadAllText(filePath));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    failures.Add($"专项文件解析失败：{filePath}：{exception.Message}");
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        failures.Add($"专项文件根必须是 JSON 对象：{filePath}");
                        continue;
                    }

                    if (!root.TryGetProperty("id", out var identifierElement)
                        || identifierElement.ValueKind != JsonValueKind.String
                        || string.IsNullOrEmpty(identifierElement.GetString()))
                    {
                        failures.Add($"专项文件缺 id：{filePath}");
                        continue;
                    }

                    var epicIdentifier = identifierElement.GetString();
                    var dutyMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                    if (root.TryGetProperty("认领", out var claimsElement) && claimsElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in claimsElement.EnumerateObject())
                        {
                            if (property.Value.ValueKind != JsonValueKind.Array)
                            {
                                continue;
                            }

                            var identifiers = new List<string>();
                            foreach (var item in property.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    identifiers.Add(item.GetString() ?? "");
                                }
                            }

                            dutyMap[property.Name] = identifiers;
                        }
                    }

                    table[epicIdentifier] = dutyMap;
                }
            }

            return new EpicClaimBook(table, string.Join("\n", failures));
        }

        /// <summary>加载失败原因，正常加载为空串；多条原因用换行分隔。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 取某专项某职责的认领人 open_id 列表；查不到返回空列表，返回前按序数序排序。
        /// </summary>
        /// <param name="epicIdentifier">专项 id，如「EP-0003」。</param>
        /// <param name="duty">职责名。</param>
        public IReadOnlyList<string> ClaimersOf(string epicIdentifier, string duty)
        {
            var result = new List<string>();
            if (_entries.TryGetValue(epicIdentifier, out var dutyMap)
                && dutyMap.TryGetValue(duty, out var identifiers))
            {
                result.AddRange(identifiers);
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
