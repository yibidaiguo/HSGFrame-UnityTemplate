using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一个 sRGB 颜色，8 位分量。</summary>
    public readonly struct SrgbColor : IEquatable<SrgbColor>
    {
        /// <summary>用三个 8 位分量构造一个颜色。</summary>
        /// <param name="red">红，0..255。</param>
        /// <param name="green">绿，0..255。</param>
        /// <param name="blue">蓝，0..255。</param>
        public SrgbColor(byte red, byte green, byte blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        /// <summary>红分量。</summary>
        public byte Red { get; }

        /// <summary>绿分量。</summary>
        public byte Green { get; }

        /// <summary>蓝分量。</summary>
        public byte Blue { get; }

        /// <summary>渲染成 #RRGGBB（大写十六进制）。</summary>
        public string ToHex()
        {
            return "#" + Red.ToString("X2", CultureInfo.InvariantCulture)
                + Green.ToString("X2", CultureInfo.InvariantCulture)
                + Blue.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 解析 #RRGGBB 或 RRGGBB；不合法返回 false。
        /// </summary>
        /// <param name="text">要解析的十六进制串。</param>
        /// <param name="color">解析成功时的颜色。</param>
        public static bool TryParseHex(string text, out SrgbColor color)
        {
            color = default(SrgbColor);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var hex = text;
            if (hex.Length == 7 && hex[0] == '#')
            {
                hex = hex.Substring(1);
            }
            else if (hex.Length == 6 && hex[0] != '#')
            {
                // 无 # 前缀的 6 位十六进制，直接用。
            }
            else
            {
                return false;
            }

            var values = new int[3];
            for (var i = 0; i < 6; i++)
            {
                var digit = HexDigitValue(hex[i]);
                if (digit < 0)
                {
                    return false;
                }

                values[i / 2] = values[i / 2] * 16 + digit;
            }

            color = new SrgbColor((byte)values[0], (byte)values[1], (byte)values[2]);
            return true;
        }

        /// <summary>按值比较两个颜色。</summary>
        /// <param name="other">另一个颜色。</param>
        public bool Equals(SrgbColor other)
        {
            return Red == other.Red && Green == other.Green && Blue == other.Blue;
        }

        /// <summary>按值比较。</summary>
        /// <param name="obj">另一个对象。</param>
        public override bool Equals(object obj)
        {
            return obj is SrgbColor other && Equals(other);
        }

        /// <summary>哈希码：三个分量打包成一个 int。</summary>
        public override int GetHashCode()
        {
            return (Red << 16) | (Green << 8) | Blue;
        }

        /// <summary>两个颜色相等。</summary>
        public static bool operator ==(SrgbColor left, SrgbColor right)
        {
            return left.Equals(right);
        }

        /// <summary>两个颜色不相等。</summary>
        public static bool operator !=(SrgbColor left, SrgbColor right)
        {
            return !left.Equals(right);
        }

        /// <summary>单个十六进制字符的值；不是十六进制字符返回 -1。</summary>
        private static int HexDigitValue(char character)
        {
            if (character >= '0' && character <= '9')
            {
                return character - '0';
            }

            if (character >= 'a' && character <= 'f')
            {
                return character - 'a' + 10;
            }

            if (character >= 'A' && character <= 'F')
            {
                return character - 'A' + 10;
            }

            return -1;
        }
    }

    /// <summary>色板里的一色：颜色与它占的权重。</summary>
    public sealed class PaletteSwatch
    {
        /// <summary>构造一个色板条目。</summary>
        /// <param name="color">颜色。</param>
        /// <param name="weight">占比，0..1。</param>
        /// <param name="sampleCount">归到这一簇的采样像素数。</param>
        public PaletteSwatch(SrgbColor color, double weight, int sampleCount)
        {
            Color = color;
            Weight = weight;
            SampleCount = sampleCount;
        }

        /// <summary>颜色。</summary>
        public SrgbColor Color { get; }

        /// <summary>占比，0..1，四舍五入到四位小数。</summary>
        public double Weight { get; }

        /// <summary>归到这一簇的采样像素数。</summary>
        public int SampleCount { get; }
    }

    /// <summary>主色聚类的结果。</summary>
    public sealed class PaletteResult
    {
        /// <summary>构造一个聚类结果。</summary>
        /// <param name="swatches">色板。</param>
        /// <param name="sampledPixelCount">真正参与聚类的采样像素数。</param>
        /// <param name="skippedTransparentCount">因为透明被跳过的像素数。</param>
        /// <param name="clustered">聚类跑成了没有。</param>
        /// <param name="failureReason">没跑成的原因；跑成了是空串。</param>
        public PaletteResult(
            IReadOnlyList<PaletteSwatch> swatches,
            int sampledPixelCount,
            int skippedTransparentCount,
            bool clustered,
            string failureReason)
        {
            Swatches = swatches ?? Array.Empty<PaletteSwatch>();
            SampledPixelCount = sampledPixelCount;
            SkippedTransparentCount = skippedTransparentCount;
            Clustered = clustered;
            FailureReason = failureReason ?? "";
        }

        /// <summary>色板，按权重降序；权重相同按十六进制串序数序。</summary>
        public IReadOnlyList<PaletteSwatch> Swatches { get; }

        /// <summary>真正参与聚类的采样像素数。</summary>
        public int SampledPixelCount { get; }

        /// <summary>因为透明被跳过的像素数。</summary>
        public int SkippedTransparentCount { get; }

        /// <summary>聚类跑成了没有。全透明或零像素时为 false。</summary>
        public bool Clustered { get; }

        /// <summary>没跑成的原因；跑成了是空串。</summary>
        public string FailureReason { get; }
    }

    /// <summary>
    /// 确定性主色聚类与 CIELAB 距离。
    /// 全程零随机数（决策 58）：k-means 的播种、抽样、tie-break 全部确定性，
    /// 同一张图两次跑给出完全一样的色板——随机的东西在门禁里没法验。
    /// </summary>
    public static class ColorPalette
    {
        /// <summary>默认聚类色数，子文档 06 §五写死的 8。</summary>
        public const int DefaultClusterCount = 8;

        /// <summary>采样上限：像素多于这个数时按固定步长抽样，保证大图也能跑完且结果确定。</summary>
        public const int MaximumSampleCount = 20000;

        /// <summary>alpha 低于这个值的像素算透明，不参与聚类。</summary>
        public const byte OpaqueAlphaThreshold = 128;

        /// <summary>初始质心互相之间必须隔开的 ΔE 下限：小于 12 的颜色视为「太近」，不配当第二个质心。</summary>
        private const double InitialCentroidMinimumDistance = 12.0;

        /// <summary>Lloyd 迭代轮数上限，保证终止。</summary>
        private const int MaximumIterationCount = 30;

        /// <summary>D65 白点，用于 XYZ 归一化。</summary>
        private const double WhiteX = 0.95047;
        private const double WhiteY = 1.00000;
        private const double WhiteZ = 1.08883;

        /// <summary>
        /// 对一张图做 k-means 主色聚类。算法与参数全部写死，不许自由发挥：
        /// 固定步长抽样（不随机）→ 跳过透明 → 按颜色频次确定性播种 → Lab 空间 Lloyd 迭代
        /// （最多 30 轮，距离相等归到下标小的簇，空簇丢弃不重播种，质心不变提前停）→
        /// 按权重降序输出，权重 = 簇点数 / 采样点数，四舍五入到四位小数。
        /// </summary>
        /// <param name="image">要聚类的图。</param>
        /// <param name="clusterCount">聚类色数；小于 1 时按默认 8 算，超过不同颜色数时自然收缩。</param>
        public static PaletteResult Cluster(PngImage image, int clusterCount)
        {
            if (image == null)
            {
                return new PaletteResult(Array.Empty<PaletteSwatch>(), 0, 0, false, "图为空，没有可聚类的颜色");
            }

            var totalPixelCount = (long)image.Width * image.Height;
            if (image.Pixels == null || image.Pixels.Count == 0 || totalPixelCount <= 0)
            {
                return new PaletteResult(Array.Empty<PaletteSwatch>(), 0, 0, false, "图没有像素，无法聚类");
            }

            // 1. 采样：从下标 0 起每隔 stride 取一个像素，固定步长，不许随机抽样。
            var stride = Math.Max(1, (int)(totalPixelCount / MaximumSampleCount));
            var sampled = new List<SrgbColor>();
            var skippedTransparent = 0;
            for (var i = 0L; i < totalPixelCount; i += stride)
            {
                var offset = (int)(i * 4L);
                if (image.Pixels[offset + 3] < OpaqueAlphaThreshold)
                {
                    skippedTransparent++;
                    continue;
                }

                sampled.Add(new SrgbColor(image.Pixels[offset], image.Pixels[offset + 1], image.Pixels[offset + 2]));
            }

            if (sampled.Count == 0)
            {
                return new PaletteResult(Array.Empty<PaletteSwatch>(), 0, skippedTransparent, false, "全部像素都是透明的，没有可聚类的颜色");
            }

            var sampledPixelCount = sampled.Count;
            var desiredClusterCount = clusterCount > 0 ? clusterCount : DefaultClusterCount;

            // 4. 初始质心：按 (次数降序, 十六进制串序数序) 排候选；第一个取最频颜色，
            //    之后顺序扫描取「到已选全部质心最小 ΔE ≥ 12」的第一个；扫完没有就取下一个未选。
            var candidates = CountByColor(sampled);
            var initialCentroids = SelectInitialCentroids(candidates, desiredClusterCount);

            // 5. Lloyd 迭代：最多 30 轮。
            var centroids = initialCentroids;
            var nextCounts = new List<int>();
            var assignment = new int[sampledPixelCount];
            for (var iteration = 0; iteration < MaximumIterationCount; iteration++)
            {
                // 分配：每个采样点归到 ΔE 最近的质心；距离相等时归到下标小的那一簇（严格小于才换）。
                for (var p = 0; p < sampledPixelCount; p++)
                {
                    var bestIndex = 0;
                    var bestDistance = Distance(sampled[p], centroids[0]);
                    for (var c = 1; c < centroids.Count; c++)
                    {
                        var distance = Distance(sampled[p], centroids[c]);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestIndex = c;
                        }
                    }

                    assignment[p] = bestIndex;
                }

                // 更新：质心 = 该簇采样点在 Lab 空间取均值后转回 sRGB；空簇直接丢弃，不重播种。
                var sumLightness = new double[centroids.Count];
                var sumA = new double[centroids.Count];
                var sumB = new double[centroids.Count];
                var clusterCounts = new int[centroids.Count];
                for (var p = 0; p < sampledPixelCount; p++)
                {
                    var clusterIndex = assignment[p];
                    var lab = ToLab(sampled[p]);
                    sumLightness[clusterIndex] += lab.Lightness;
                    sumA[clusterIndex] += lab.A;
                    sumB[clusterIndex] += lab.B;
                    clusterCounts[clusterIndex]++;
                }

                var nextCentroids = new List<SrgbColor>();
                nextCounts = new List<int>();
                for (var c = 0; c < centroids.Count; c++)
                {
                    if (clusterCounts[c] == 0)
                    {
                        continue;
                    }

                    var meanLightness = sumLightness[c] / clusterCounts[c];
                    var meanA = sumA[c] / clusterCounts[c];
                    var meanB = sumB[c] / clusterCounts[c];
                    nextCentroids.Add(FromLab(meanLightness, meanA, meanB));
                    nextCounts.Add(clusterCounts[c]);
                }

                // 质心一轮不再变化（含丢簇）就提前停。
                var changed = nextCentroids.Count != centroids.Count;
                if (!changed)
                {
                    for (var c = 0; c < centroids.Count; c++)
                    {
                        if (nextCentroids[c] != centroids[c])
                        {
                            changed = true;
                            break;
                        }
                    }
                }

                centroids = nextCentroids;
                if (!changed)
                {
                    break;
                }
            }

            // 6. 输出：权重 = 簇点数 / 采样点数，四舍五入到四位小数；按 (权重降序, 十六进制序数序) 排。
            var swatches = new List<PaletteSwatch>();
            for (var c = 0; c < centroids.Count; c++)
            {
                var weight = Math.Round((double)nextCounts[c] / sampledPixelCount, 4, MidpointRounding.AwayFromZero);
                swatches.Add(new PaletteSwatch(centroids[c], weight, nextCounts[c]));
            }

            swatches.Sort((left, right) =>
            {
                var byWeight = right.Weight.CompareTo(left.Weight);
                if (byWeight != 0)
                {
                    return byWeight;
                }

                return string.CompareOrdinal(left.Color.ToHex(), right.Color.ToHex());
            });

            return new PaletteResult(swatches, sampledPixelCount, skippedTransparent, true, "");
        }

        /// <summary>sRGB 转 CIELAB（D65），返回 (L, a, b)。</summary>
        /// <param name="color">sRGB 颜色。</param>
        public static (double Lightness, double A, double B) ToLab(SrgbColor color)
        {
            var r = InverseGamma(color.Red / 255.0);
            var g = InverseGamma(color.Green / 255.0);
            var b = InverseGamma(color.Blue / 255.0);

            // 线性 RGB × sRGB→XYZ 矩阵（D65）。
            var x = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b;
            var y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
            var z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b;

            x /= WhiteX;
            y /= WhiteY;
            z /= WhiteZ;

            var fx = LabFunction(x);
            var fy = LabFunction(y);
            var fz = LabFunction(z);

            var lightness = 116.0 * fy - 16.0;
            var a = 500.0 * (fx - fy);
            var bValue = 200.0 * (fy - fz);
            return (lightness, a, bValue);
        }

        /// <summary>两色的 CIELAB ΔE76 距离。</summary>
        /// <param name="left">第一个颜色。</param>
        /// <param name="right">第二个颜色。</param>
        public static double Distance(SrgbColor left, SrgbColor right)
        {
            var leftLab = ToLab(left);
            var rightLab = ToLab(right);
            var deltaLightness = leftLab.Lightness - rightLab.Lightness;
            var deltaA = leftLab.A - rightLab.A;
            var deltaB = leftLab.B - rightLab.B;
            return Math.Sqrt(deltaLightness * deltaLightness + deltaA * deltaA + deltaB * deltaB);
        }

        /// <summary>把采样像素按颜色计数，按 (次数降序, 十六进制串序数序) 排序返回候选序列。</summary>
        private static List<SrgbColor> CountByColor(List<SrgbColor> sampled)
        {
            var counts = new Dictionary<int, int>();
            foreach (var color in sampled)
            {
                var key = (color.Red << 16) | (color.Green << 8) | color.Blue;
                counts.TryGetValue(key, out var count);
                counts[key] = count + 1;
            }

            var result = new List<SrgbColor>(counts.Count);
            foreach (var pair in counts)
            {
                var key = pair.Key;
                result.Add(new SrgbColor((byte)(key >> 16), (byte)(key >> 8), (byte)key));
            }

            result.Sort((left, right) =>
            {
                var byCount = counts[(left.Red << 16) | (left.Green << 8) | left.Blue]
                    .CompareTo(counts[(right.Red << 16) | (right.Green << 8) | right.Blue]);
                if (byCount != 0)
                {
                    return -byCount;
                }

                return string.CompareOrdinal(left.ToHex(), right.ToHex());
            });

            return result;
        }

        /// <summary>确定性播种初始质心；不同颜色数少于目标簇数时自然收缩。</summary>
        private static List<SrgbColor> SelectInitialCentroids(List<SrgbColor> candidates, int desiredClusterCount)
        {
            var count = Math.Min(desiredClusterCount, candidates.Count);
            var centroids = new List<SrgbColor>(count);
            var selected = new HashSet<int>();
            var first = candidates[0];
            centroids.Add(first);
            selected.Add(ColorKey(first));

            for (var i = 1; i < count; i++)
            {
                var chosen = -1;
                foreach (var candidate in candidates)
                {
                    if (selected.Contains(ColorKey(candidate)))
                    {
                        continue;
                    }

                    var minDistance = double.MaxValue;
                    foreach (var centroid in centroids)
                    {
                        var distance = Distance(centroid, candidate);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                        }
                    }

                    if (minDistance >= InitialCentroidMinimumDistance)
                    {
                        chosen = ColorKey(candidate);
                        break;
                    }
                }

                if (chosen < 0)
                {
                    // 扫完没有满足「隔开 12」的，就取候选序列里还没被选过的下一个。
                    foreach (var candidate in candidates)
                    {
                        if (!selected.Contains(ColorKey(candidate)))
                        {
                            chosen = ColorKey(candidate);
                            break;
                        }
                    }
                }

                if (chosen < 0)
                {
                    break;
                }

                centroids.Add(UnpackColor(chosen));
                selected.Add(chosen);
            }

            return centroids;
        }

        /// <summary>Lab 转回 sRGB：分量夹到 0..255 并四舍五入。</summary>
        private static SrgbColor FromLab(double lightness, double a, double b)
        {
            var fy = (lightness + 16.0) / 116.0;
            var fx = fy + a / 500.0;
            var fz = fy - b / 200.0;

            var x = WhiteX * InverseLabFunction(fx);
            var y = WhiteY * InverseLabFunction(fy);
            var z = WhiteZ * InverseLabFunction(fz);

            // XYZ → 线性 RGB（sRGB 矩阵的逆）。
            var rLinear = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
            var gLinear = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
            var bLinear = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;

            rLinear = Math.Max(0.0, Math.Min(1.0, rLinear));
            gLinear = Math.Max(0.0, Math.Min(1.0, gLinear));
            bLinear = Math.Max(0.0, Math.Min(1.0, bLinear));

            var red = (byte)Math.Round(Gamma(rLinear) * 255.0, MidpointRounding.AwayFromZero);
            var green = (byte)Math.Round(Gamma(gLinear) * 255.0, MidpointRounding.AwayFromZero);
            var blue = (byte)Math.Round(Gamma(bLinear) * 255.0, MidpointRounding.AwayFromZero);
            return new SrgbColor(red, green, blue);
        }

        /// <summary>sRGB 去伽马：c &lt;= 0.04045 时线性段，否则 2.4 次幂。</summary>
        private static double InverseGamma(double component)
        {
            return component <= 0.04045 ? component / 12.92 : Math.Pow((component + 0.055) / 1.055, 2.4);
        }

        /// <summary>Lab 用的 f(t)：t &gt; 0.008856 时取立方根，否则线性段。</summary>
        private static double LabFunction(double t)
        {
            return t > 0.008856 ? Math.Cbrt(t) : (7.787 * t + 16.0 / 116.0);
        }

        /// <summary>Lab 逆函数。</summary>
        private static double InverseLabFunction(double t)
        {
            return t > 6.0 / 29.0 ? t * t * t : (t - 16.0 / 116.0) / 7.787;
        }

        /// <summary>线性 RGB 加伽马回 sRGB。</summary>
        private static double Gamma(double linear)
        {
            return linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
        }

        /// <summary>RGB 三字节打包成 int 键。</summary>
        private static int ColorKey(SrgbColor color)
        {
            return (color.Red << 16) | (color.Green << 8) | color.Blue;
        }

        /// <summary>int 键解包成颜色。</summary>
        private static SrgbColor UnpackColor(int key)
        {
            return new SrgbColor((byte)(key >> 16), (byte)(key >> 8), (byte)key);
        }
    }
}
