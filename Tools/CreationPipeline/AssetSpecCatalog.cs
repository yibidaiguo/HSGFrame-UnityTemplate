using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一个资产类型的合并后规格：扁平化键值、可覆盖清单与最后动过它的层。</summary>
    public sealed class AssetTypeSpecification
    {
        /// <summary>
        /// 构造一个资产类型的合并后规格。
        /// </summary>
        /// <param name="typeName">资产类型名，如「图标」。</param>
        /// <param name="values">扁平化规格键值，键形如「规格.宽」「落点」「命名模式」。</param>
        /// <param name="overridableKeys">基线「可覆盖」清单里的键。</param>
        /// <param name="sourceLayer">最后动过这个类型的层：基线 / 项目 / 业务。</param>
        public AssetTypeSpecification(
            string typeName,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyList<string> overridableKeys,
            string sourceLayer)
        {
            TypeName = typeName;
            Values = values ?? new Dictionary<string, string>();
            OverridableKeys = overridableKeys ?? Array.Empty<string>();
            SourceLayer = sourceLayer ?? "";
            Domain = Values.TryGetValue("域", out var domain) ? domain : "";
        }

        /// <summary>资产类型名，如「图标」。</summary>
        public string TypeName { get; }

        /// <summary>域，取自该类型规格数据的「域」键。</summary>
        public string Domain { get; }

        /// <summary>扁平化规格键值，键形如「规格.宽」「落点」「命名模式」。</summary>
        public IReadOnlyDictionary<string, string> Values { get; }

        /// <summary>基线「可覆盖」清单里的键。</summary>
        public IReadOnlyList<string> OverridableKeys { get; }

        /// <summary>最后动过这个类型的层：基线 / 项目 / 业务。</summary>
        public string SourceLayer { get; }

        /// <summary>落点目录，取「落点」键；取不到给空串。</summary>
        public string Destination
        {
            get { return Values.TryGetValue("落点", out var destination) ? destination : ""; }
        }

        /// <summary>命名模式，取「命名模式」键；取不到给空串。</summary>
        public string NamingPattern
        {
            get { return Values.TryGetValue("命名模式", out var pattern) ? pattern : ""; }
        }
    }

    /// <summary>资产规格目录：三层合并后的全部资产类型与合并过程中发现的违规。</summary>
    public sealed class AssetSpecCatalog
    {
        /// <summary>
        /// 构造一份资产规格目录。
        /// </summary>
        /// <param name="types">合并后的资产类型表。</param>
        /// <param name="findings">合并过程中发现的违规。</param>
        public AssetSpecCatalog(
            IReadOnlyDictionary<string, AssetTypeSpecification> types,
            IReadOnlyList<PoolFinding> findings)
        {
            Types = types ?? new Dictionary<string, AssetTypeSpecification>();
            Findings = findings ?? Array.Empty<PoolFinding>();
        }

        /// <summary>合并后的资产类型表，键为资产类型名。</summary>
        public IReadOnlyDictionary<string, AssetTypeSpecification> Types { get; }

        /// <summary>合并过程中发现的违规。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }

        /// <summary>
        /// 按资产类型名查合并后的规格；找不到返回 null。
        /// </summary>
        /// <param name="typeName">资产类型名，如「图标」。</param>
        public AssetTypeSpecification Find(string typeName)
        {
            return Types.TryGetValue(typeName ?? "", out var spec) ? spec : null;
        }

        /// <summary>
        /// 读基线并逐层叠项目、业务，返回合并后的资产规格目录。
        /// 可覆盖清单之外的键只许收紧不许放宽：数字新值不大于旧值算收紧、布尔 false 改 true 算收紧，
        /// 其余一律算放宽；放宽的值不采纳但合并继续走完，报一条 finding 带位置、原因与修复。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">业务模块名；空白时跳过业务层。</param>
        public static AssetSpecCatalog Load(string repositoryRoot, string moduleName)
        {
            var types = new Dictionary<string, AssetTypeSpecification>(StringComparer.Ordinal);
            var findings = new List<PoolFinding>();
            var baselineFile = SpecificationPaths.BaselineAssetSpecFile(repositoryRoot);

            if (!File.Exists(baselineFile))
            {
                findings.Add(new PoolFinding(
                    RepositoryRelative(repositoryRoot, baselineFile),
                    "资产规格基线文件不存在",
                    "从模板同步一份 规范/基线/资产规格.基线.json",
                    "规范/基线/资产规格.基线.json"));
                return new AssetSpecCatalog(types, findings);
            }

            var baselineTypes = new Dictionary<string, AssetTypeSpecification>(StringComparer.Ordinal);

            foreach (var layer in CollectLayers(repositoryRoot, moduleName))
            {
                var incoming = TryParseLayer(repositoryRoot, layer.FilePath, layer.LayerName, findings);
                if (incoming == null)
                {
                    continue;
                }

                foreach (var pair in incoming)
                {
                    if (string.Equals(layer.LayerName, "基线", StringComparison.Ordinal))
                    {
                        baselineTypes[pair.Key] = pair.Value;
                        types[pair.Key] = WithSourceLayer(pair.Value, "基线");
                        continue;
                    }

                    if (!baselineTypes.TryGetValue(pair.Key, out var baselineSpec))
                    {
                        // 基线里没有这个类型：新类型允许，直接新增，SourceLayer 记成当前层。
                        types[pair.Key] = WithSourceLayer(pair.Value, layer.LayerName);
                        continue;
                    }

                    types[pair.Key] = MergeLayerOntoBaselineType(
                        repositoryRoot,
                        layer,
                        pair.Value,
                        types[pair.Key],
                        baselineSpec,
                        findings);
                }
            }

            return new AssetSpecCatalog(types, findings);
        }

        /// <summary>新值相对旧值是否算放宽：数字变大、布尔 true 改 false，其余一律放宽；值相同不算。</summary>
        /// <param name="oldRaw">旧值，JSON 原始文本。</param>
        /// <param name="newRaw">新值，JSON 原始文本。</param>
        internal static bool IsLoosening(string oldRaw, string newRaw)
        {
            if (string.Equals(oldRaw, newRaw, StringComparison.Ordinal))
            {
                return false;
            }

            if (double.TryParse(oldRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var oldNumber)
                && double.TryParse(newRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var newNumber))
            {
                return newNumber > oldNumber;
            }

            if (bool.TryParse(oldRaw, out var oldBoolean) && bool.TryParse(newRaw, out var newBoolean))
            {
                return oldBoolean && !newBoolean;
            }

            return true;
        }

        private static List<(string LayerName, string FilePath)> CollectLayers(string repositoryRoot, string moduleName)
        {
            var layers = new List<(string, string)>
            {
                ("基线", SpecificationPaths.BaselineAssetSpecFile(repositoryRoot))
            };

            var projectFile = SpecificationPaths.ProjectAssetSpecFile(repositoryRoot);
            if (File.Exists(projectFile))
            {
                layers.Add(("项目", projectFile));
            }

            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                var businessFile = SpecificationPaths.BusinessAssetSpecFile(repositoryRoot, moduleName);
                if (File.Exists(businessFile))
                {
                    layers.Add(("业务", businessFile));
                }
            }

            return layers;
        }

        /// <summary>解析一个资产规格层文件的「资产类型」表；JSON 语法错时报 finding 并返回 null。</summary>
        private static Dictionary<string, AssetTypeSpecification> TryParseLayer(
            string repositoryRoot,
            string filePath,
            string layerName,
            List<PoolFinding> findings)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(filePath));
                var root = document.RootElement;
                if (!root.TryGetProperty("资产类型", out var typesElement) || typesElement.ValueKind != JsonValueKind.Object)
                {
                    return new Dictionary<string, AssetTypeSpecification>(StringComparer.Ordinal);
                }

                var result = new Dictionary<string, AssetTypeSpecification>(StringComparer.Ordinal);
                foreach (var typeProperty in typesElement.EnumerateObject())
                {
                    result[typeProperty.Name] = ParseType(typeProperty.Name, typeProperty.Value);
                }

                return result;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                findings.Add(new PoolFinding(
                    RepositoryRelative(repositoryRoot, filePath),
                    $"{layerName}层文件不是合法 JSON：{exception.Message}",
                    "修正该文件的 JSON 语法",
                    "规范/基线/资产规格.基线.json"));
                return null;
            }
        }

        /// <summary>把一个资产类型扁平化成键值表：域、规格.&lt;子键&gt;、落点、命名模式；可覆盖单独存。</summary>
        private static AssetTypeSpecification ParseType(string typeName, JsonElement element)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var overridableKeys = new List<string>();

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "域", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.String)
                {
                    values["域"] = property.Value.GetString() ?? "";
                }
                else if (string.Equals(property.Name, "规格", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var specProperty in property.Value.EnumerateObject())
                    {
                        values["规格." + specProperty.Name] = specProperty.Value.GetRawText();
                    }
                }
                else if (string.Equals(property.Name, "落点", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.String)
                {
                    values["落点"] = property.Value.GetString() ?? "";
                }
                else if (string.Equals(property.Name, "命名模式", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.String)
                {
                    values["命名模式"] = property.Value.GetString() ?? "";
                }
                else if (string.Equals(property.Name, "可覆盖", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            overridableKeys.Add(item.GetString() ?? "");
                        }
                    }
                }
            }

            return new AssetTypeSpecification(typeName, values, overridableKeys, "");
        }

        /// <summary>把一个上层的类型逐键叠到当前合并结果上；可覆盖清单之外只许收紧，放宽不采纳并报 finding。</summary>
        private static AssetTypeSpecification MergeLayerOntoBaselineType(
            string repositoryRoot,
            (string LayerName, string FilePath) layer,
            AssetTypeSpecification incoming,
            AssetTypeSpecification current,
            AssetTypeSpecification baselineSpec,
            List<PoolFinding> findings)
        {
            var overridable = new HashSet<string>(baselineSpec.OverridableKeys, StringComparer.Ordinal);
            var mergedValues = new Dictionary<string, string>(current.Values, StringComparer.Ordinal);

            foreach (var pair in incoming.Values)
            {
                if (overridable.Contains(pair.Key))
                {
                    mergedValues[pair.Key] = pair.Value;
                    continue;
                }

                if (!current.Values.TryGetValue(pair.Key, out var oldValue))
                {
                    // 基线没有这个键：新键等于多一条要求，算收紧，直接采纳。
                    mergedValues[pair.Key] = pair.Value;
                    continue;
                }

                if (IsLoosening(oldValue, pair.Value))
                {
                    findings.Add(new PoolFinding(
                        RepositoryRelative(repositoryRoot, layer.FilePath),
                        $"{layer.LayerName}层把「{incoming.TypeName}.{pair.Key}」从「{oldValue}」放宽成「{pair.Value}」，而基线没把它列进「可覆盖」",
                        "改成收紧，或在基线的「可覆盖」里加上这个键",
                        "规范/基线/资产规格.基线.json"));
                    continue;
                }

                mergedValues[pair.Key] = pair.Value;
            }

            return new AssetTypeSpecification(
                incoming.TypeName,
                mergedValues,
                baselineSpec.OverridableKeys,
                layer.LayerName);
        }

        /// <summary>换一份 SourceLayer 的新规格对象，其余字段原样。</summary>
        private static AssetTypeSpecification WithSourceLayer(AssetTypeSpecification spec, string layerName)
        {
            return new AssetTypeSpecification(spec.TypeName, spec.Values, spec.OverridableKeys, layerName);
        }

        /// <summary>把绝对路径转成仓库相对路径，正斜杠。</summary>
        private static string RepositoryRelative(string repositoryRoot, string fullPath)
        {
            return Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(fullPath)).Replace('\\', '/');
        }
    }
}
