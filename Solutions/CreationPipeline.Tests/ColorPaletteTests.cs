using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 确定性主色聚类的测试：一簇/两簇、确定性、像素顺序无关、透明边界、抽样、颜色工具。
    /// 聚类全程零随机（决策 58），同一张图两次跑必须逐字相同。
    /// </summary>
    public class ColorPaletteTests
    {
        /// <summary>纯单色图：一簇，权重 1.0，颜色就是那个单色。</summary>
        [Fact]
        public void SolidImageYieldsSingleSwatch()
        {
            var pixels = new byte[4 * 4 * 4];
            for (var i = 0; i < 16; i++)
            {
                pixels[i * 4] = 255;
                pixels[i * 4 + 3] = 255;
            }

            var result = ColorPalette.Cluster(new PngImage(4, 4, pixels), 8);

            Assert.True(result.Clustered, result.FailureReason);
            var swatch = Assert.Single(result.Swatches);
            Assert.Equal(255, swatch.Color.Red);
            Assert.Equal(0, swatch.Color.Green);
            Assert.Equal(0, swatch.Color.Blue);
            Assert.Equal(1.0, swatch.Weight, 4);
            Assert.Equal(16, swatch.SampleCount);
            Assert.Equal(16, result.SampledPixelCount);
        }

        /// <summary>两半各一色的图，clusterCount=2：两簇、两色都在、权重各约 0.5。</summary>
        [Fact]
        public void TwoHalvesYieldsTwoSwatches()
        {
            var pixels = new byte[16];
            // 像素 0、1 红；像素 2、3 蓝。
            SetPixel(pixels, 0, new SrgbColor(255, 0, 0));
            SetPixel(pixels, 1, new SrgbColor(255, 0, 0));
            SetPixel(pixels, 2, new SrgbColor(0, 0, 255));
            SetPixel(pixels, 3, new SrgbColor(0, 0, 255));

            var result = ColorPalette.Cluster(new PngImage(2, 2, pixels), 2);

            Assert.True(result.Clustered, result.FailureReason);
            Assert.Equal(2, result.Swatches.Count);
            var hexes = result.Swatches.Select(swatch => swatch.Color.ToHex()).OrderBy(hex => hex, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "#0000FF", "#FF0000" }, hexes);
            foreach (var swatch in result.Swatches)
            {
                Assert.Equal(0.5, swatch.Weight, 4);
            }
        }

        /// <summary>确定性：同一张图连跑两次，Swatches 的十六进制序列与权重逐个相等。</summary>
        [Fact]
        public void ClusteringIsDeterministic()
        {
            var pixels = new byte[4 * 4 * 4];
            var colors = new[] { new SrgbColor(200, 30, 40), new SrgbColor(10, 180, 90), new SrgbColor(250, 250, 10) };
            for (var i = 0; i < 16; i++)
            {
                SetPixel(pixels, i, colors[i % colors.Length]);
            }

            var first = ColorPalette.Cluster(new PngImage(4, 4, pixels), 8);
            var second = ColorPalette.Cluster(new PngImage(4, 4, pixels), 8);

            Assert.True(first.Clustered);
            Assert.Equal(first.Swatches.Count, second.Swatches.Count);
            for (var i = 0; i < first.Swatches.Count; i++)
            {
                Assert.Equal(first.Swatches[i].Color.ToHex(), second.Swatches[i].Color.ToHex());
                Assert.Equal(first.Swatches[i].Weight, second.Swatches[i].Weight);
            }
        }

        /// <summary>像素顺序无关：同样的颜色计数、不同空间排布，聚出来的色集合相同。</summary>
        [Fact]
        public void ClusteringIgnoresPixelArrangement()
        {
            var red = new SrgbColor(255, 0, 0);
            var blue = new SrgbColor(0, 0, 255);
            var layoutA = new byte[16];
            var layoutB = new byte[16];
            SetPixel(layoutA, 0, red);
            SetPixel(layoutA, 1, red);
            SetPixel(layoutA, 2, blue);
            SetPixel(layoutA, 3, blue);
            SetPixel(layoutB, 0, red);
            SetPixel(layoutB, 1, blue);
            SetPixel(layoutB, 2, red);
            SetPixel(layoutB, 3, blue);

            var resultA = ColorPalette.Cluster(new PngImage(2, 2, layoutA), 2);
            var resultB = ColorPalette.Cluster(new PngImage(2, 2, layoutB), 2);

            Assert.True(resultA.Clustered);
            Assert.True(resultB.Clustered);
            var hexesA = resultA.Swatches.Select(swatch => swatch.Color.ToHex()).OrderBy(hex => hex, StringComparer.Ordinal).ToList();
            var hexesB = resultB.Swatches.Select(swatch => swatch.Color.ToHex()).OrderBy(hex => hex, StringComparer.Ordinal).ToList();
            Assert.Equal(hexesA, hexesB);
        }

        /// <summary>全透明图：Clustered=false，原因非空，Swatches 为空，绝不返回空色板假装聚过。</summary>
        [Fact]
        public void FullyTransparentImageFailsClustering()
        {
            var pixels = new byte[16];
            for (var i = 0; i < 16; i += 4)
            {
                pixels[i + 3] = 0;
            }

            var result = ColorPalette.Cluster(new PngImage(2, 2, pixels), 8);

            Assert.False(result.Clustered);
            Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
            Assert.Empty(result.Swatches);
        }

        /// <summary>透明边界：alpha=127 跳过、alpha=128 算进去。</summary>
        [Fact]
        public void TransparentThresholdBoundary()
        {
            var pixels = new byte[16];
            SetPixel(pixels, 0, new SrgbColor(255, 0, 0), alpha: 127);
            SetPixel(pixels, 1, new SrgbColor(255, 0, 0), alpha: 128);
            SetPixel(pixels, 2, new SrgbColor(255, 0, 0), alpha: 255);
            SetPixel(pixels, 3, new SrgbColor(255, 0, 0), alpha: 255);

            var result = ColorPalette.Cluster(new PngImage(2, 2, pixels), 8);

            Assert.True(result.Clustered, result.FailureReason);
            Assert.Equal(1, result.SkippedTransparentCount);
            Assert.Equal(3, result.SampledPixelCount);
        }

        /// <summary>不同颜色数少于 clusterCount：簇数等于不同颜色数，不算失败。</summary>
        [Fact]
        public void FewerDistinctColorsThanRequestedIsNotFailure()
        {
            var pixels = new byte[16];
            SetPixel(pixels, 0, new SrgbColor(255, 0, 0));
            SetPixel(pixels, 1, new SrgbColor(0, 255, 0));
            SetPixel(pixels, 2, new SrgbColor(0, 0, 255));
            SetPixel(pixels, 3, new SrgbColor(255, 0, 0));

            var result = ColorPalette.Cluster(new PngImage(2, 2, pixels), 8);

            Assert.True(result.Clustered);
            Assert.Equal(3, result.Swatches.Count);
            Assert.Equal("", result.FailureReason);
        }

        /// <summary>大图走抽样：像素数远超上限时 SampledPixelCount 不超过上限，且连跑两次相同。</summary>
        [Fact]
        public void LargeImageIsSampledDeterministically()
        {
            var pixels = new byte[1000 * 1000 * 4];
            for (var i = 0; i < 1000 * 1000; i++)
            {
                pixels[i * 4] = 10;
                pixels[i * 4 + 1] = 20;
                pixels[i * 4 + 2] = 30;
                pixels[i * 4 + 3] = 255;
            }

            var first = ColorPalette.Cluster(new PngImage(1000, 1000, pixels), 8);
            var second = ColorPalette.Cluster(new PngImage(1000, 1000, pixels), 8);

            Assert.True(first.Clustered, first.FailureReason);
            Assert.True(first.SampledPixelCount <= ColorPalette.MaximumSampleCount);
            Assert.Equal(first.Swatches.Select(swatch => swatch.Color.ToHex()), second.Swatches.Select(swatch => swatch.Color.ToHex()));
            Assert.Equal(first.Swatches.Select(swatch => swatch.Weight), second.Swatches.Select(swatch => swatch.Weight));
        }

        /// <summary>ToHex / TryParseHex 往返；#RRGGBB 与 RRGGBB 两种写法都吃。</summary>
        [Fact]
        public void HexRoundTrip()
        {
            var color = new SrgbColor(0x1A, 0x2B, 0x3C);
            var hex = color.ToHex();

            Assert.Equal("#1A2B3C", hex);
            Assert.True(SrgbColor.TryParseHex(hex, out var parsed));
            Assert.Equal(color, parsed);
            Assert.True(SrgbColor.TryParseHex("1A2B3C", out var withoutHash));
            Assert.Equal(color, withoutHash);
        }

        /// <summary>TryParseHex 拒绝不合法字符与错误长度。</summary>
        [Fact]
        public void TryParseHexRejectsInvalidText()
        {
            Assert.False(SrgbColor.TryParseHex("#GGHHII", out _));
            Assert.False(SrgbColor.TryParseHex("#12345", out _));
            Assert.False(SrgbColor.TryParseHex("", out _));
            Assert.False(SrgbColor.TryParseHex("12345G", out _));
        }

        /// <summary>Distance：自己到自己是 0；黑到白远大于黑到深灰。</summary>
        [Fact]
        public void DistanceIsZeroToSelfAndGrowsWithContrast()
        {
            var black = new SrgbColor(0, 0, 0);
            var white = new SrgbColor(255, 255, 255);
            var darkGray = new SrgbColor(50, 50, 50);

            Assert.Equal(0.0, ColorPalette.Distance(black, black), 6);
            Assert.True(ColorPalette.Distance(black, white) > ColorPalette.Distance(black, darkGray));
        }

        /// <summary>ToLab 对纯白约为 (100, 0, 0)，容差 0.5。</summary>
        [Fact]
        public void ToLabOfWhiteIsNearOneHundredZeroZero()
        {
            var lab = ColorPalette.ToLab(new SrgbColor(255, 255, 255));

            Assert.InRange(lab.Lightness, 99.5, 100.5);
            Assert.InRange(lab.A, -0.5, 0.5);
            Assert.InRange(lab.B, -0.5, 0.5);
        }

        /// <summary>往像素缓冲里写一个 RGBA 像素。</summary>
        private static void SetPixel(byte[] pixels, int index, SrgbColor color, byte alpha = 255)
        {
            pixels[index * 4] = color.Red;
            pixels[index * 4 + 1] = color.Green;
            pixels[index * 4 + 2] = color.Blue;
            pixels[index * 4 + 3] = alpha;
        }
    }
}
