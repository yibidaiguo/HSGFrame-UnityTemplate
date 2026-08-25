using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>一类资产的分层规则：这一类的门类词表与允许的扩展名。</summary>
    public sealed class AssetLayoutTypeRule
    {
        /// <summary>构造一条类型规则。</summary>
        /// <param name="typeName">类型目录名，如 Model。</param>
        /// <param name="categories">这一类允许的门类（已把主题门类合并进来）。</param>
        /// <param name="allowedExtensions">这一类允许的扩展名，小写带点。</param>
        public AssetLayoutTypeRule(
            string typeName,
            IReadOnlyCollection<string> categories,
            IReadOnlyCollection<string> allowedExtensions)
        {
            TypeName = typeName ?? "";
            Categories = categories ?? Array.Empty<string>();
            AllowedExtensions = allowedExtensions ?? Array.Empty<string>();
        }

        /// <summary>类型目录名。</summary>
        public string TypeName { get; }

        /// <summary>这一类允许的门类。</summary>
        public IReadOnlyCollection<string> Categories { get; }

        /// <summary>这一类允许的扩展名，小写带点。</summary>
        public IReadOnlyCollection<string> AllowedExtensions { get; }
    }

    /// <summary>
    /// 资产分层词表：从 <c>asset-layout.baseline.json</c> 读出来的那一份。
    ///
    /// **这是规则数据不是代码**——加一个门类是加一条数据，不改这个文件（总纲 §九）。
    /// 项目层可以就近覆盖：<c>Specifications/Project/asset-layout.json</c> 里出现的键盖掉基线的同名键。
    /// </summary>
    public sealed class AssetLayoutRuleSet
    {
        /// <summary>基线文件名。</summary>
        public const string BaselineFileName = "asset-layout.baseline.json";

        /// <summary>项目层文件名。</summary>
        public const string ProjectFileName = "asset-layout.json";

        private readonly Dictionary<string, AssetLayoutTypeRule> _types;

        /// <summary>构造一份分层词表。</summary>
        /// <param name="assetRoot">资产根，相对仓库根。</param>
        /// <param name="minimumDepth">最小层数：从资产根往下数几层目录，文件才允许出现。</param>
        /// <param name="types">按类型目录名索引的规则。</param>
        /// <param name="bannedModuleNames">模块层不许用的名字。</param>
        /// <param name="loadFailureReason">读取失败原因；正常为空串。</param>
        public AssetLayoutRuleSet(
            string assetRoot,
            int minimumDepth,
            Dictionary<string, AssetLayoutTypeRule> types,
            IReadOnlyCollection<string> bannedModuleNames,
            string loadFailureReason)
        {
            AssetRoot = assetRoot ?? "";
            MinimumDepth = minimumDepth;
            _types = types ?? new Dictionary<string, AssetLayoutTypeRule>(StringComparer.OrdinalIgnoreCase);
            BannedModuleNames = bannedModuleNames ?? Array.Empty<string>();
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>资产根，相对仓库根。</summary>
        public string AssetRoot { get; }

        /// <summary>最小层数。</summary>
        public int MinimumDepth { get; }

        /// <summary>模块层不许用的名字。</summary>
        public IReadOnlyCollection<string> BannedModuleNames { get; }

        /// <summary>读取失败原因；正常为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>全部类型目录名。</summary>
        public IReadOnlyCollection<string> TypeNames
        {
            get { return _types.Keys; }
        }

        /// <summary>按类型目录名取规则；没有这一类返回 null。</summary>
        /// <param name="typeName">类型目录名。</param>
        public AssetLayoutTypeRule Find(string typeName)
        {
            return _types.TryGetValue(typeName ?? "", out var rule) ? rule : null;
        }

        /// <summary>
        /// 读基线并叠项目层。两份都不存在时返回一份空词表并把原因写在
        /// <see cref="LoadFailureReason"/> 里——**不抛**，调用方是门禁，
        /// 它要报的是「词表没读到」而不是崩掉。
        /// </summary>
        /// <param name="baselineFilePath">基线文件路径。</param>
        /// <param name="projectFilePath">项目层文件路径；不存在就跳过。</param>
        public static AssetLayoutRuleSet Load(string baselineFilePath, string projectFilePath)
        {
            if (string.IsNullOrWhiteSpace(baselineFilePath) || !File.Exists(baselineFilePath))
            {
                return Empty($"资产分层词表不存在：{baselineFilePath}");
            }

            JsonElement baseline;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(baselineFilePath));
                baseline = document.RootElement.Clone();
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException)
            {
                return Empty($"资产分层词表读不动：{exception.Message}");
            }

            JsonElement? project = null;
            if (!string.IsNullOrWhiteSpace(projectFilePath) && File.Exists(projectFilePath))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(projectFilePath));
                    project = document.RootElement.Clone();
                }
                catch (Exception exception) when (exception is IOException || exception is JsonException)
                {
                    return Empty($"项目层资产分层词表读不动：{exception.Message}");
                }
            }

            var assetRoot = ReadString(baseline, project, "资产根", "Assets/Game/Art");
            var minimumDepth = ReadInt(baseline, project, "最小层数", 3);
            var themeCategories = ReadStringArray(baseline, project, "主题门类");
            var bannedModuleNames = ReadStringArray(baseline, project, "模块层禁用名");

            var types = new Dictionary<string, AssetLayoutTypeRule>(StringComparer.OrdinalIgnoreCase);
            var typeElement = PickObject(baseline, project, "类型");
            if (typeElement.HasValue)
            {
                foreach (var property in typeElement.Value.EnumerateObject())
                {
                    if (property.Name.StartsWith("_", StringComparison.Ordinal) || property.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var categories = new List<string>(ReadArray(property.Value, "门类"));
                    if (ReadBool(property.Value, "用主题门类", defaultValue: false))
                    {
                        categories.AddRange(themeCategories);
                    }

                    var extensions = ReadArray(property.Value, "允许扩展名")
                        .Select(item => item.ToLowerInvariant())
                        .ToList();

                    types[property.Name] = new AssetLayoutTypeRule(
                        property.Name,
                        categories.Distinct(StringComparer.Ordinal).ToList(),
                        extensions);
                }
            }

            return new AssetLayoutRuleSet(assetRoot, minimumDepth, types, bannedModuleNames, "");
        }

        private static AssetLayoutRuleSet Empty(string reason)
        {
            return new AssetLayoutRuleSet(
                "Assets/Game/Art",
                3,
                new Dictionary<string, AssetLayoutTypeRule>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<string>(),
                reason);
        }

        private static JsonElement? PickObject(JsonElement baseline, JsonElement? project, string key)
        {
            if (project.HasValue
                && project.Value.TryGetProperty(key, out var overridden)
                && overridden.ValueKind == JsonValueKind.Object)
            {
                return overridden;
            }

            return baseline.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object
                ? value
                : (JsonElement?)null;
        }

        private static string ReadString(JsonElement baseline, JsonElement? project, string key, string fallback)
        {
            if (project.HasValue && project.Value.TryGetProperty(key, out var overridden) && overridden.ValueKind == JsonValueKind.String)
            {
                return overridden.GetString() ?? fallback;
            }

            return baseline.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static int ReadInt(JsonElement baseline, JsonElement? project, string key, int fallback)
        {
            if (project.HasValue && project.Value.TryGetProperty(key, out var overridden) && overridden.ValueKind == JsonValueKind.Number)
            {
                return overridden.GetInt32();
            }

            return baseline.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : fallback;
        }

        private static IReadOnlyList<string> ReadStringArray(JsonElement baseline, JsonElement? project, string key)
        {
            if (project.HasValue && project.Value.TryGetProperty(key, out var overridden) && overridden.ValueKind == JsonValueKind.Array)
            {
                return ReadArray(project.Value, key);
            }

            return ReadArray(baseline, key);
        }

        private static IReadOnlyList<string> ReadArray(JsonElement owner, string key)
        {
            if (!owner.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var items = new List<string>();
            foreach (var element in value.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        items.Add(text.Trim());
                    }
                }
            }

            return items;
        }

        private static bool ReadBool(JsonElement owner, string key, bool defaultValue)
        {
            if (!owner.TryGetProperty(key, out var value))
            {
                return defaultValue;
            }

            return value.ValueKind == JsonValueKind.True || (value.ValueKind != JsonValueKind.False && defaultValue);
        }
    }
}
