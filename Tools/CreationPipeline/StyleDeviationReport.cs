using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一份定稿色板：从 Pools/Designs/定稿/&lt;定稿名&gt;/定稿.json 读出来的色板数据。</summary>
    public sealed class FinalPalette
    {
        /// <summary>构造一份定稿色板。</summary>
        /// <param name="name">定稿名。</param>
        /// <param name="version">版本号。</param>
        /// <param name="colors">色板颜色。</param>
        /// <param name="loadFailureReason">加载失败原因；正常为空串。</param>
        /// <param name="loaded">读成了没有。</param>
        public FinalPalette(string name, int version, IReadOnlyList<SrgbColor> colors, string loadFailureReason, bool loaded)
        {
            Name = name ?? "";
            Version = version;
            Colors = colors ?? Array.Empty<SrgbColor>();
            LoadFailureReason = loadFailureReason ?? "";
            Loaded = loaded;
        }

        /// <summary>定稿名。</summary>
        public string Name { get; }

        /// <summary>版本号。</summary>
        public int Version { get; }

        /// <summary>色板颜色。</summary>
        public IReadOnlyList<SrgbColor> Colors { get; }

        /// <summary>加载失败原因；正常为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>读成了没有。</summary>
        public bool Loaded { get; }

        /// <summary>
        /// 从 Pools/Designs/定稿/&lt;名&gt;/定稿.json 读一份定稿色板。
        /// 文件读不动、JSON 解析失败、根不是对象、缺「色板」或色板不是数组都算没读成，
        /// 原因写进 LoadFailureReason。色板里个别颜色不合法只跳过那一个并记原因，其余照收——
        /// 不静默吞掉，也不让一颗坏螺丝废掉整份色板。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="finalName">定稿名。</param>
        public static FinalPalette Load(string poolRoot, string finalName)
        {
            if (string.IsNullOrWhiteSpace(poolRoot) || string.IsNullOrWhiteSpace(finalName))
            {
                return new FinalPalette("", 0, Array.Empty<SrgbColor>(), "定稿名或池子根目录为空，无从读起", false);
            }

            var filePath = Path.Combine(poolRoot, "Designs", "定稿", finalName, "定稿.json");
            string text;
            try
            {
                text = File.ReadAllText(filePath);
            }
            catch (Exception exception) when (exception is FileNotFoundException
                || exception is DirectoryNotFoundException
                || exception is IOException
                || exception is UnauthorizedAccessException)
            {
                // 裸抛 .NET 的英文异常文案会漏进面板与命令输出，中文界面里那是内部黑话；
                // 与下面 JSON 解析那支一样，包一层中文再往外给。
                return new FinalPalette("", 0, Array.Empty<SrgbColor>(), $"定稿 {finalName} 读不了：{exception.Message}", false);
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new FinalPalette("", 0, Array.Empty<SrgbColor>(), "定稿文件的根不是 JSON 对象", false);
                }

                if (!root.TryGetProperty("色板", out var paletteElement) || paletteElement.ValueKind != JsonValueKind.Array)
                {
                    return new FinalPalette("", 0, Array.Empty<SrgbColor>(), "定稿文件缺「色板」数组，无从比较", false);
                }

                var name = "";
                if (root.TryGetProperty("名称", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                {
                    name = nameElement.GetString() ?? "";
                }

                var version = 0;
                if (root.TryGetProperty("版本", out var versionElement) && versionElement.TryGetInt32(out var parsedVersion))
                {
                    version = parsedVersion;
                }

                var colors = new List<SrgbColor>();
                var problems = new List<string>();
                foreach (var item in paletteElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        problems.Add("色板里有一项不是字符串");
                        continue;
                    }

                    var hex = item.GetString();
                    if (SrgbColor.TryParseHex(hex, out var color))
                    {
                        colors.Add(color);
                    }
                    else
                    {
                        problems.Add($"色板里有不合法的颜色「{hex}」");
                    }
                }

                var failureReason = problems.Count == 0 ? "" : string.Join("；", problems);
                return new FinalPalette(name, version, colors, failureReason, true);
            }
            catch (JsonException exception)
            {
                return new FinalPalette("", 0, Array.Empty<SrgbColor>(), $"定稿 JSON 解析失败：{exception.Message}", false);
            }
        }
    }

    /// <summary>一条资产的离风格结果：算成了给距离，没算成给原因。</summary>
    public sealed class StyleDeviationEntry
    {
        /// <summary>构造一条离风格结果。</summary>
        /// <param name="assetPath">资产文件路径。</param>
        /// <param name="deviation">加权最小距离和。</param>
        /// <param name="swatches">这张图的主色板。</param>
        /// <param name="measured">算成了没有。</param>
        /// <param name="skipReason">没算成的原因；算成了是空串。</param>
        public StyleDeviationEntry(string assetPath, double deviation, IReadOnlyList<PaletteSwatch> swatches, bool measured, string skipReason)
        {
            AssetPath = assetPath ?? "";
            Deviation = deviation;
            Swatches = swatches ?? Array.Empty<PaletteSwatch>();
            Measured = measured;
            SkipReason = skipReason ?? "";
        }

        /// <summary>资产文件路径。</summary>
        public string AssetPath { get; }

        /// <summary>加权最小距离和：Σ(每个主色到定稿色板的最小 ΔE × 该主色权重)，四位小数。</summary>
        public double Deviation { get; }

        /// <summary>这张图的主色板。</summary>
        public IReadOnlyList<PaletteSwatch> Swatches { get; }

        /// <summary>算成了没有。</summary>
        public bool Measured { get; }

        /// <summary>没算成的原因（解码失败 / 全透明 / 读不动）；算成了是空串。</summary>
        public string SkipReason { get; }
    }

    /// <summary>离风格报告：只报告，不自动行动。</summary>
    public sealed class StyleDeviationResult
    {
        /// <summary>构造一份离风格报告。</summary>
        /// <param name="ranked">算成了的条目。</param>
        /// <param name="skipped">没算成的条目。</param>
        /// <param name="paletteLoaded">定稿色板读成了没有。</param>
        /// <param name="paletteFailureReason">定稿色板没读成的原因。</param>
        public StyleDeviationResult(
            IReadOnlyList<StyleDeviationEntry> ranked,
            IReadOnlyList<StyleDeviationEntry> skipped,
            bool paletteLoaded,
            string paletteFailureReason)
        {
            Ranked = ranked ?? Array.Empty<StyleDeviationEntry>();
            Skipped = skipped ?? Array.Empty<StyleDeviationEntry>();
            PaletteLoaded = paletteLoaded;
            PaletteFailureReason = paletteFailureReason ?? "";
        }

        /// <summary>算成了的条目，按 Deviation 降序（离风格的排前面）；相同按路径序数序。</summary>
        public IReadOnlyList<StyleDeviationEntry> Ranked { get; }

        /// <summary>没算成的条目，按路径序数序。跳过的项必须报出来（决策 46）。</summary>
        public IReadOnlyList<StyleDeviationEntry> Skipped { get; }

        /// <summary>定稿色板读成了没有。</summary>
        public bool PaletteLoaded { get; }

        /// <summary>定稿色板没读成的原因。</summary>
        public string PaletteFailureReason { get; }
    }

    /// <summary>
    /// 离风格分析：对一组 PNG 路径算「资产主色聚类 vs 定稿色板最小距离和」，排序出 top-N。
    /// 只报告不自动行动（硬约束）——这个类不写盘、不移动文件、不改任何资产。
    /// 「没色板 / 全透明 / 解码失败」与「算出来距离 0」必须是两个分支（决策 42）：
    /// 前者进 Skipped 并写明原因，绝不渲染成「符合风格」。
    /// </summary>
    public static class StyleDeviationAnalyzer
    {
        /// <summary>定稿色板没读成时给全部输入的统一跳过原因。</summary>
        private const string PaletteMissingSkipReason = "定稿色板没读成，无从比较";

        /// <summary>定稿色板是空板时的跳过原因。</summary>
        private const string PaletteEmptySkipReason = "定稿色板为空，无从比较";

        /// <summary>
        /// 对一组 PNG 路径算离风格并排序；topCount &lt;= 0 表示全要。
        /// 定稿色板没读成或色板为空时，全部输入进 Skipped，绝不给「距离 0」的排名（决策 42）。
        /// 单张图解码失败或全透明只进 Skipped，其余照常算——一张坏图不许让整份报告失败。
        /// topCount 截断在算完全量之后做，截断本身由命令层的输出文案说明。
        /// </summary>
        /// <param name="imagePaths">PNG 文件路径列表。</param>
        /// <param name="palette">定稿色板。</param>
        /// <param name="clusterCount">聚类色数，默认 8。</param>
        /// <param name="topCount">列出条数上限；小于等于 0 表示全要。</param>
        public static StyleDeviationResult Measure(
            IReadOnlyList<string> imagePaths,
            FinalPalette palette,
            int clusterCount,
            int topCount)
        {
            var paths = imagePaths ?? Array.Empty<string>();
            var paletteUsable = palette != null && palette.Loaded && palette.Colors.Count > 0;
            if (!paletteUsable)
            {
                var skipReason = palette == null || !palette.Loaded ? PaletteMissingSkipReason : PaletteEmptySkipReason;
                var failureReason = palette == null
                    ? PaletteMissingSkipReason
                    : palette.Loaded ? PaletteEmptySkipReason : palette.LoadFailureReason;
                var skipped = paths
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => new StyleDeviationEntry(path, 0.0, Array.Empty<PaletteSwatch>(), false, skipReason))
                    .ToList();
                return new StyleDeviationResult(Array.Empty<StyleDeviationEntry>(), skipped, false, failureReason);
            }

            var allMeasured = new List<StyleDeviationEntry>();
            var skippedList = new List<StyleDeviationEntry>();
            foreach (var path in paths)
            {
                var decode = PngDecoder.DecodeFile(path);
                if (!decode.Succeeded)
                {
                    skippedList.Add(new StyleDeviationEntry(path, 0.0, Array.Empty<PaletteSwatch>(), false, decode.FailureReason));
                    continue;
                }

                var clustering = ColorPalette.Cluster(decode.Image, clusterCount);
                if (!clustering.Clustered)
                {
                    skippedList.Add(new StyleDeviationEntry(path, 0.0, Array.Empty<PaletteSwatch>(), false, clustering.FailureReason));
                    continue;
                }

                var deviation = ComputeDeviation(clustering.Swatches, palette.Colors);
                allMeasured.Add(new StyleDeviationEntry(path, deviation, clustering.Swatches, true, ""));
            }

            allMeasured.Sort((left, right) =>
            {
                var byDeviation = right.Deviation.CompareTo(left.Deviation);
                if (byDeviation != 0)
                {
                    return byDeviation;
                }

                return string.CompareOrdinal(left.AssetPath, right.AssetPath);
            });

            skippedList.Sort((left, right) => string.CompareOrdinal(left.AssetPath, right.AssetPath));

            IReadOnlyList<StyleDeviationEntry> ranked = allMeasured;
            if (topCount > 0 && allMeasured.Count > topCount)
            {
                ranked = allMeasured.Take(topCount).ToList();
            }

            return new StyleDeviationResult(ranked, skippedList, true, "");
        }

        /// <summary>加权最小距离和：Σ(每个主色到定稿色板的最小 ΔE × 该主色权重)，结果四舍五入到四位小数。</summary>
        private static double ComputeDeviation(IReadOnlyList<PaletteSwatch> swatches, IReadOnlyList<SrgbColor> paletteColors)
        {
            var sum = 0.0;
            foreach (var swatch in swatches)
            {
                var minDistance = double.MaxValue;
                foreach (var paletteColor in paletteColors)
                {
                    var distance = ColorPalette.Distance(swatch.Color, paletteColor);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                    }
                }

                sum += minDistance * swatch.Weight;
            }

            return Math.Round(sum, 4, MidpointRounding.AwayFromZero);
        }
    }
}
