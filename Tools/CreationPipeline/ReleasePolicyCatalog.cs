using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 放行策略目录：基线 ⊕ 项目 ⊕ 业务三层合并后的「风险级 × 范围 → 自动放行 | 人审」表，
    /// 连同可覆盖清单、建议数阈值、高危范围与合并过程中发现的违规。
    /// </summary>
    public sealed class ReleasePolicyCatalog
    {
        private const string AutomaticRelease = "自动放行";
        private const string ManualReview = "人审";

        /// <summary>
        /// 构造一份放行策略目录。
        /// </summary>
        /// <param name="policies">合并后的策略表，键为「&lt;风险级&gt;.&lt;范围&gt;」。</param>
        /// <param name="overridableKeys">基线「可覆盖」清单里的键。</param>
        /// <param name="suggestionThreshold">建议级发现数阈值。</param>
        /// <param name="highRiskScopes">高危范围清单。</param>
        /// <param name="findings">合并过程中发现的违规。</param>
        /// <param name="sourceLayers">每个策略键最后动过它的层名：基线 / 项目 / 业务。</param>
        public ReleasePolicyCatalog(
            IReadOnlyDictionary<string, string> policies,
            IReadOnlyList<string> overridableKeys,
            int suggestionThreshold,
            IReadOnlyList<string> highRiskScopes,
            IReadOnlyList<PoolFinding> findings,
            IReadOnlyDictionary<string, string> sourceLayers)
        {
            Policies = policies ?? new Dictionary<string, string>(StringComparer.Ordinal);
            OverridableKeys = overridableKeys ?? Array.Empty<string>();
            SuggestionThreshold = suggestionThreshold;
            HighRiskScopes = highRiskScopes ?? Array.Empty<string>();
            Findings = findings ?? Array.Empty<PoolFinding>();
            SourceLayers = sourceLayers ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>合并后的策略表，键为「&lt;风险级&gt;.&lt;范围&gt;」。</summary>
        public IReadOnlyDictionary<string, string> Policies { get; }

        /// <summary>基线「可覆盖」清单里的键，序数序。</summary>
        public IReadOnlyList<string> OverridableKeys { get; }

        /// <summary>建议级发现数阈值，来自基线「建议数阈值」，缺省 3。</summary>
        public int SuggestionThreshold { get; }

        /// <summary>高危范围清单，来自基线「高危范围」。</summary>
        public IReadOnlyList<string> HighRiskScopes { get; }

        /// <summary>合并过程中发现的违规。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }

        /// <summary>每个策略键最后动过它的层名：基线 / 项目 / 业务。</summary>
        public IReadOnlyDictionary<string, string> SourceLayers { get; }

        /// <summary>
        /// 查「&lt;风险级&gt;.&lt;范围&gt;」对应的放行结论；查不到返回「人审」。
        /// 策略读不出来时最安全的行为是永不自动，这与引擎配置缺失时返回值守是同一道理。
        /// </summary>
        /// <param name="riskGrade">风险级：低 / 常规 / 高。</param>
        /// <param name="scope">范围名。</param>
        public string Decide(string riskGrade, string scope)
        {
            var key = (riskGrade ?? "") + "." + (scope ?? "");
            return Policies.TryGetValue(key, out var value) && string.Equals(value, AutomaticRelease, StringComparison.Ordinal)
                ? AutomaticRelease
                : ManualReview;
        }

        /// <summary>
        /// 读基线并逐层叠项目、业务，返回合并后的放行策略目录。
        /// 放行策略用两值偏序：自动放行→人审 算收紧任意层可改，人审→自动放行 算放宽只有基线
        /// 「可覆盖」列出的键才允许；出现第三种值报非法并沿用上层值。放宽被拒时不采纳新值但
        /// 合并继续走完，一次报出全部问题。「可覆盖」「建议数阈值」「高危范围」只从基线读。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">业务模块名；空白时跳过业务层。</param>
        public static ReleasePolicyCatalog Load(string repositoryRoot, string moduleName)
        {
            var policies = new Dictionary<string, string>(StringComparer.Ordinal);
            var sourceLayers = new Dictionary<string, string>(StringComparer.Ordinal);
            var findings = new List<PoolFinding>();
            var baselineFile = SpecificationPaths.BaselineReleasePolicyFile(repositoryRoot);

            if (!File.Exists(baselineFile))
            {
                findings.Add(new PoolFinding(
                    RepositoryRelative(repositoryRoot, baselineFile),
                    "放行策略基线文件不存在",
                    "从模板同步一份 规范/基线/放行策略.基线.json",
                    "规范/基线/放行策略.基线.json"));
                return new ReleasePolicyCatalog(
                    policies, Array.Empty<string>(), 3, Array.Empty<string>(), findings, sourceLayers);
            }

            var overridableKeys = new List<string>();
            var suggestionThreshold = 3;
            var highRiskScopes = new List<string>();

            foreach (var layer in CollectLayers(repositoryRoot, moduleName))
            {
                var layerData = TryParseLayer(repositoryRoot, layer.FilePath, layer.LayerName, findings);
                if (layerData == null)
                {
                    continue;
                }

                if (string.Equals(layer.LayerName, "基线", StringComparison.Ordinal))
                {
                    overridableKeys = layerData.OverridableKeys
                        .OrderBy(key => key, StringComparer.Ordinal)
                        .ToList();
                    suggestionThreshold = layerData.SuggestionThreshold ?? 3;
                    // 高危范围只从基线读，顺序照基线文件原文，不重排。
                    highRiskScopes = layerData.HighRiskScopes.ToList();
                }
                else
                {
                    ReportLowerLayerReservedKeys(repositoryRoot, layer, layerData, findings);
                }

                foreach (var pair in layerData.Policies)
                {
                    if (string.Equals(layer.LayerName, "基线", StringComparison.Ordinal))
                    {
                        if (!IsValidValue(pair.Value))
                        {
                            findings.Add(new PoolFinding(
                                RepositoryRelative(repositoryRoot, layer.FilePath),
                                $"基线把「{pair.Key}」写成了非法值「{pair.Value}」，只许「自动放行」或「人审」",
                                "改成「自动放行」或「人审」",
                                "规范/基线/放行策略.基线.json"));
                            continue;
                        }

                        policies[pair.Key] = pair.Value;
                        sourceLayers[pair.Key] = "基线";
                        continue;
                    }

                    MergeKey(repositoryRoot, layer, pair, policies, sourceLayers, overridableKeys, findings);
                }
            }

            return new ReleasePolicyCatalog(
                policies, overridableKeys, suggestionThreshold, highRiskScopes, findings, sourceLayers);
        }

        private static List<(string LayerName, string FilePath)> CollectLayers(string repositoryRoot, string moduleName)
        {
            var layers = new List<(string, string)>
            {
                ("基线", SpecificationPaths.BaselineReleasePolicyFile(repositoryRoot))
            };

            var projectFile = SpecificationPaths.ProjectReleasePolicyFile(repositoryRoot);
            if (File.Exists(projectFile))
            {
                layers.Add(("项目", projectFile));
            }

            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                var businessFile = SpecificationPaths.BusinessReleasePolicyFile(repositoryRoot, moduleName);
                if (File.Exists(businessFile))
                {
                    layers.Add(("业务", businessFile));
                }
            }

            return layers;
        }

        /// <summary>解析一个放行策略层文件；JSON 语法错时报 finding 并返回 null。</summary>
        private static ReleasePolicyLayerData TryParseLayer(
            string repositoryRoot,
            string filePath,
            string layerName,
            List<PoolFinding> findings)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(filePath));
                var root = document.RootElement;

                var data = new ReleasePolicyLayerData();

                if (root.TryGetProperty("策略", out var policiesElement) && policiesElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var policyProperty in policiesElement.EnumerateObject())
                    {
                        if (policyProperty.Value.ValueKind == JsonValueKind.String)
                        {
                            data.Policies[policyProperty.Name] = policyProperty.Value.GetString() ?? "";
                        }
                    }
                }

                if (root.TryGetProperty("可覆盖", out var overridableElement) && overridableElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in overridableElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            data.OverridableKeys.Add(item.GetString() ?? "");
                        }
                    }
                }

                if (root.TryGetProperty("建议数阈值", out var thresholdElement) && thresholdElement.ValueKind == JsonValueKind.Number
                    && thresholdElement.TryGetInt32(out var threshold))
                {
                    data.SuggestionThreshold = threshold;
                }

                if (root.TryGetProperty("高危范围", out var highRiskElement) && highRiskElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in highRiskElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            data.HighRiskScopes.Add(item.GetString() ?? "");
                        }
                    }
                }

                return data;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                findings.Add(new PoolFinding(
                    RepositoryRelative(repositoryRoot, filePath),
                    $"{layerName}层文件不是合法 JSON：{exception.Message}",
                    "修正该文件的 JSON 语法",
                    "规范/基线/放行策略.基线.json"));
                return null;
            }
        }

        /// <summary>下层写了基线独有的键：各出一条 finding，数据不生效。</summary>
        private static void ReportLowerLayerReservedKeys(
            string repositoryRoot,
            (string LayerName, string FilePath) layer,
            ReleasePolicyLayerData layerData,
            List<PoolFinding> findings)
        {
            if (layerData.OverridableKeys.Count > 0)
            {
                findings.Add(new PoolFinding(
                    RepositoryRelative(repositoryRoot, layer.FilePath),
                    $"{layer.LayerName}层写了「可覆盖」，但它是基线独有的数据，下层写了不生效",
                    "删掉该键；需要放宽就到基线的「可覆盖」里登记",
                    "规范/基线/放行策略.基线.json"));
            }

            if (layerData.SuggestionThreshold.HasValue)
            {
                findings.Add(new PoolFinding(
                    RepositoryRelative(repositoryRoot, layer.FilePath),
                    $"{layer.LayerName}层写了「建议数阈值」，但它是基线独有的数据，下层写了不生效",
                    "删掉该键；阈值只由基线定",
                    "规范/基线/放行策略.基线.json"));
            }

            if (layerData.HighRiskScopes.Count > 0)
            {
                findings.Add(new PoolFinding(
                    RepositoryRelative(repositoryRoot, layer.FilePath),
                    $"{layer.LayerName}层写了「高危范围」，但它是基线独有的数据，下层写了不生效",
                    "删掉该键；高危范围只由基线定",
                    "规范/基线/放行策略.基线.json"));
            }
        }

        /// <summary>把下层的一个策略键叠到当前合并结果上，按两值偏序判收紧 / 放宽 / 非法值。</summary>
        private static void MergeKey(
            string repositoryRoot,
            (string LayerName, string FilePath) layer,
            KeyValuePair<string, string> pair,
            Dictionary<string, string> policies,
            Dictionary<string, string> sourceLayers,
            List<string> overridableKeys,
            List<PoolFinding> findings)
        {
            if (!policies.TryGetValue(pair.Key, out var oldValue))
            {
                // 基线没有这个键：新键等于多一条要求，算收紧，直接采纳。
                policies[pair.Key] = pair.Value;
                sourceLayers[pair.Key] = layer.LayerName;
                return;
            }

            if (string.Equals(oldValue, pair.Value, StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(oldValue, AutomaticRelease, StringComparison.Ordinal)
                && string.Equals(pair.Value, ManualReview, StringComparison.Ordinal))
            {
                // 自动放行 → 人审：收紧，任意层随便改。
                policies[pair.Key] = pair.Value;
                sourceLayers[pair.Key] = layer.LayerName;
                return;
            }

            if (string.Equals(oldValue, ManualReview, StringComparison.Ordinal)
                && string.Equals(pair.Value, AutomaticRelease, StringComparison.Ordinal))
            {
                // 人审 → 自动放行：放宽，只有基线「可覆盖」列出的键才允许。
                if (overridableKeys.Contains(pair.Key, StringComparer.Ordinal))
                {
                    policies[pair.Key] = pair.Value;
                    sourceLayers[pair.Key] = layer.LayerName;
                    return;
                }

                findings.Add(new PoolFinding(
                    RepositoryRelative(repositoryRoot, layer.FilePath),
                    $"{layer.LayerName}层把「{pair.Key}」从「{oldValue}」放宽成「{pair.Value}」，而基线没把它列进「可覆盖」",
                    "改成收紧，或在基线的「可覆盖」里加上这个键",
                    "规范/基线/放行策略.基线.json"));
                return;
            }

            // 第三种值：非法，报一条发现，该键沿用上层值（不采纳）。
            findings.Add(new PoolFinding(
                RepositoryRelative(repositoryRoot, layer.FilePath),
                $"{layer.LayerName}层把「{pair.Key}」写成了非法值「{pair.Value}」，只许「自动放行」或「人审」",
                "改成「自动放行」或「人审」",
                "规范/基线/放行策略.基线.json"));
        }

        private static bool IsValidValue(string value)
        {
            return string.Equals(value, AutomaticRelease, StringComparison.Ordinal)
                || string.Equals(value, ManualReview, StringComparison.Ordinal);
        }

        /// <summary>把绝对路径转成仓库相对路径，正斜杠。</summary>
        private static string RepositoryRelative(string repositoryRoot, string fullPath)
        {
            return Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(fullPath)).Replace('\\', '/');
        }

        /// <summary>一个放行策略层文件的解析结果：策略表与三个基线独有键（下层没写时为缺省态）。</summary>
        private sealed class ReleasePolicyLayerData
        {
            public ReleasePolicyLayerData()
            {
                Policies = new Dictionary<string, string>(StringComparer.Ordinal);
                OverridableKeys = new List<string>();
                SuggestionThreshold = null;
                HighRiskScopes = new List<string>();
            }

            public Dictionary<string, string> Policies { get; }

            public List<string> OverridableKeys { get; }

            public int? SuggestionThreshold { get; set; }

            public List<string> HighRiskScopes { get; }
        }
    }
}
