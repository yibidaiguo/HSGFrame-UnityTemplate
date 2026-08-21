using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 离风格报告与定稿色板加载的测试：读不成的分支、坏项跳过、距离排序、跳过项必须报出来、
    /// top-N 截断、以及「只报告不自动行动」——Measure 一个字都不写盘。
    /// 测试用临时目录（Path.GetTempPath()），用完删除；PNG 在测试里现造（决策 4）。
    /// </summary>
    public class StyleDeviationReportTests
    {
        /// <summary>定稿文件不存在：Loaded=false，原因非空。</summary>
        [Fact]
        public void LoadMissingFinalFileFails()
        {
            using var workspace = new TempWorkspace();

            var palette = FinalPalette.Load(workspace.Root, "不存在的定稿");

            Assert.False(palette.Loaded);
            Assert.False(string.IsNullOrWhiteSpace(palette.LoadFailureReason));
        }

        /// <summary>定稿 JSON 解析失败：Loaded=false，原因非空。</summary>
        [Fact]
        public void LoadBrokenJsonFails()
        {
            using var workspace = new TempWorkspace();
            WriteFinalJson(workspace.Root, "坏JSON", "not valid json");

            var palette = FinalPalette.Load(workspace.Root, "坏JSON");

            Assert.False(palette.Loaded);
            Assert.False(string.IsNullOrWhiteSpace(palette.LoadFailureReason));
        }

        /// <summary>色板不是数组：Loaded=false，原因非空。</summary>
        [Fact]
        public void LoadPaletteNotArrayFails()
        {
            using var workspace = new TempWorkspace();
            // 含中文键的 JSON 写成多行 raw string，命名门禁才读得懂。
            WriteFinalJson(workspace.Root, "色板不是数组", """
            {
              "名称": "UI图标风格",
              "版本": 1,
              "色板": "not-an-array"
            }
            """);

            var palette = FinalPalette.Load(workspace.Root, "色板不是数组");

            Assert.False(palette.Loaded);
            Assert.False(string.IsNullOrWhiteSpace(palette.LoadFailureReason));
        }

        /// <summary>色板里有一个不合法的十六进制串：跳过那一个、其余照收，LoadFailureReason 非空（不静默吞掉）。</summary>
        [Fact]
        public void LoadSkipsInvalidHexButKeepsTheRestAndRecordsReason()
        {
            using var workspace = new TempWorkspace();
            WriteFinalJson(workspace.Root, "带坏项", """
            {
              "名称": "UI图标风格",
              "版本": 1,
              "色板": ["#1A2B3C", "#GGHHII", "#DDEeff"]
            }
            """);

            var palette = FinalPalette.Load(workspace.Root, "带坏项");

            Assert.True(palette.Loaded);
            Assert.Equal(2, palette.Colors.Count);
            Assert.Equal(new SrgbColor(0x1A, 0x2B, 0x3C), palette.Colors[0]);
            Assert.Equal(new SrgbColor(0xDD, 0xEE, 0xFF), palette.Colors[1]);
            Assert.False(string.IsNullOrWhiteSpace(palette.LoadFailureReason));
        }

        /// <summary>色板为空：Measure 时全部进 Skipped，Ranked 为空，原因含「定稿色板」，绝不给「距离 0」排名。</summary>
        [Fact]
        public void EmptyPaletteSendsEverythingToSkipped()
        {
            using var workspace = new TempWorkspace();
            WriteFinalJson(workspace.Root, "空色板", """
            {
              "名称": "UI图标风格",
              "版本": 1,
              "色板": []
            }
            """);
            var palette = FinalPalette.Load(workspace.Root, "空色板");

            var result = StyleDeviationAnalyzer.Measure(
                new[] { "any/a.png", "any/b.png" },
                palette,
                8,
                20);

            Assert.False(result.PaletteLoaded);
            Assert.Empty(result.Ranked);
            Assert.Equal(2, result.Skipped.Count);
            foreach (var entry in result.Skipped)
            {
                Assert.Contains("定稿色板", entry.SkipReason);
            }
        }

        /// <summary>与定稿色板完全同色的图：Deviation 约等于 0。</summary>
        [Fact]
        public void ImageMatchingPaletteHasNearZeroDeviation()
        {
            using var workspace = new TempWorkspace();
            WriteFinalJson(workspace.Root, "红色定稿", """
            {
              "名称": "红色定稿",
              "版本": 1,
              "色板": ["#FF0000"]
            }
            """);
            var imagePath = WriteSolidPng(workspace.Root, "同色.png", 255, 0, 0);
            var palette = FinalPalette.Load(workspace.Root, "红色定稿");

            var result = StyleDeviationAnalyzer.Measure(new[] { imagePath }, palette, 8, 20);

            Assert.True(result.PaletteLoaded);
            var entry = Assert.Single(result.Ranked);
            Assert.True(entry.Measured);
            Assert.True(entry.Deviation < 0.5, $"Deviation 应为约 0，实际 {entry.Deviation}");
        }

        /// <summary>与定稿色板差很远的图：Deviation 明显更大。</summary>
        [Fact]
        public void DistantImageHasLargerDeviation()
        {
            using var workspace = new TempWorkspace();
            WriteFinalJson(workspace.Root, "白色定稿", """
            {
              "名称": "白色定稿",
              "版本": 1,
              "色板": ["#FFFFFF"]
            }
            """);
            var blackPath = WriteSolidPng(workspace.Root, "黑.png", 0, 0, 0);
            var whitePath = WriteSolidPng(workspace.Root, "白.png", 255, 255, 255);
            var palette = FinalPalette.Load(workspace.Root, "白色定稿");

            var result = StyleDeviationAnalyzer.Measure(new[] { blackPath, whitePath }, palette, 8, 20);

            var blackDeviation = result.Ranked.Single(entry => entry.AssetPath == blackPath).Deviation;
            var whiteDeviation = result.Ranked.Single(entry => entry.AssetPath == whitePath).Deviation;
            Assert.True(blackDeviation > whiteDeviation, $"黑 {blackDeviation} 应明显大于白 {whiteDeviation}");
            Assert.True(blackDeviation > 50.0, $"黑白 ΔE 应很大，实际 {blackDeviation}");
        }

        /// <summary>三张图：Ranked 按 Deviation 降序（离风格的排前面）。</summary>
        [Fact]
        public void RankedEntriesAreSortedByDeviationDescending()
        {
            using var workspace = new TempWorkspace();
            WriteFinalJson(workspace.Root, "红色定稿", """
            {
              "名称": "红色定稿",
              "版本": 1,
              "色板": ["#FF0000"]
            }
            """);
            var redPath = WriteSolidPng(workspace.Root, "红.png", 255, 0, 0);
            var greenPath = WriteSolidPng(workspace.Root, "绿.png", 0, 255, 0);
            var bluePath = WriteSolidPng(workspace.Root, "蓝.png", 0, 0, 255);
            var palette = FinalPalette.Load(workspace.Root, "红色定稿");

            var result = StyleDeviationAnalyzer.Measure(new[] { redPath, greenPath, bluePath }, palette, 8, 20);

            Assert.Equal(3, result.Ranked.Count);
            // 蓝离红最远，绿次之，红自己最近：降序应为 蓝、绿、红。
            Assert.Equal(bluePath, result.Ranked[0].AssetPath);
            Assert.Equal(greenPath, result.Ranked[1].AssetPath);
            Assert.Equal(redPath, result.Ranked[2].AssetPath);
            for (var i = 1; i < result.Ranked.Count; i++)
            {
                Assert.True(result.Ranked[i - 1].Deviation >= result.Ranked[i].Deviation);
            }
        }

        /// <summary>一张坏 PNG 混在中间：它进 Skipped，其余两张照常进 Ranked，一张坏图不许毁掉整份报告。</summary>
        [Fact]
        public void BrokenPngGoesToSkippedOthersStillRanked()
        {
            using var workspace = new TempWorkspace();
            WriteFinalJson(workspace.Root, "红色定稿", """
            {
              "名称": "红色定稿",
              "版本": 1,
              "色板": ["#FF0000"]
            }
            """);
            var redPath = WriteSolidPng(workspace.Root, "红.png", 255, 0, 0);
            var brokenPath = Path.Combine(workspace.Root, "坏.png");
            File.WriteAllText(brokenPath, "this is not a png at all", new UTF8Encoding(false));
            var bluePath = WriteSolidPng(workspace.Root, "蓝.png", 0, 0, 255);
            var palette = FinalPalette.Load(workspace.Root, "红色定稿");

            var result = StyleDeviationAnalyzer.Measure(new[] { redPath, brokenPath, bluePath }, palette, 8, 20);

            Assert.Equal(2, result.Ranked.Count);
            var skipped = Assert.Single(result.Skipped);
            Assert.Equal(brokenPath, skipped.AssetPath);
            Assert.False(skipped.Measured);
            Assert.False(string.IsNullOrWhiteSpace(skipped.SkipReason));
        }

        /// <summary>topCount=1：Ranked 只有 1 条，且是距离最大的那张。</summary>
        [Fact]
        public void TopCountTruncatesKeepingTheFurthest()
        {
            using var workspace = new TempWorkspace();
            WriteFinalJson(workspace.Root, "红色定稿", """
            {
              "名称": "红色定稿",
              "版本": 1,
              "色板": ["#FF0000"]
            }
            """);
            var redPath = WriteSolidPng(workspace.Root, "红.png", 255, 0, 0);
            var bluePath = WriteSolidPng(workspace.Root, "蓝.png", 0, 0, 255);
            var palette = FinalPalette.Load(workspace.Root, "红色定稿");

            var result = StyleDeviationAnalyzer.Measure(new[] { redPath, bluePath }, palette, 8, 1);

            var entry = Assert.Single(result.Ranked);
            Assert.Equal(bluePath, entry.AssetPath);
        }

        /// <summary>不写盘：Measure 跑完之后，临时目录里的文件数与内容与跑之前完全一致。</summary>
        [Fact]
        public void MeasureDoesNotWriteAnyFiles()
        {
            using var workspace = new TempWorkspace();
            WriteFinalJson(workspace.Root, "红色定稿", """
            {
              "名称": "红色定稿",
              "版本": 1,
              "色板": ["#FF0000"]
            }
            """);
            var redPath = WriteSolidPng(workspace.Root, "红.png", 255, 0, 0);
            var bluePath = WriteSolidPng(workspace.Root, "蓝.png", 0, 0, 255);
            var palette = FinalPalette.Load(workspace.Root, "红色定稿");

            var before = Snapshot(workspace.Root);
            StyleDeviationAnalyzer.Measure(new[] { redPath, bluePath }, palette, 8, 20);
            var after = Snapshot(workspace.Root);

            Assert.Equal(before, after);
        }

        /// <summary>把目录里全部文件的内容快照下来（路径 + 内容），用于验证没写盘。</summary>
        private static List<string> Snapshot(string root)
        {
            var entries = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToList();
            entries.Sort(StringComparer.Ordinal);
            return entries.Select(entry => entry + "|" + File.ReadAllText(entry)).ToList();
        }

        /// <summary>把定稿 JSON 写到 Pools/Designs/Final/&lt;名&gt;/final.json 形状的路径。</summary>
        private static void WriteFinalJson(string poolRoot, string finalName, string json)
        {
            var directory = Path.Combine(poolRoot, "Designs", "Final", finalName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "final.json"), json, new UTF8Encoding(false));
        }

        /// <summary>在指定目录写一张 1×1 纯色 RGBA PNG 文件，返回路径。</summary>
        private static string WriteSolidPng(string directory, string fileName, byte red, byte green, byte blue, byte alpha = 255)
        {
            var scanlines = new byte[] { 0, red, green, blue, alpha };
            var path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, BuildSimpleRgbaPng(1, 1, scanlines));
            return path;
        }

        /// <summary>手工拼一张 1×1 RGBA（颜色类型 6、位深 8、None 滤波）PNG 字节。</summary>
        private static byte[] BuildSimpleRgbaPng(int width, int height, byte[] scanlines)
        {
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            var ihdr = new byte[13];
            WriteInt32BE(ihdr, 0, width);
            WriteInt32BE(ihdr, 4, height);
            ihdr[8] = 8;
            ihdr[9] = 6;
            AddChunk(bytes, "IHDR", ihdr);
            AddChunk(bytes, "IDAT", Compress(scanlines));
            AddChunk(bytes, "IEND", Array.Empty<byte>());
            return bytes.ToArray();
        }

        /// <summary>往块列表里追加一个块：长度（大端）+ 类型 + 数据 + CRC 占位。</summary>
        private static void AddChunk(List<byte> target, string type, byte[] data)
        {
            target.AddRange(BitConverter.GetBytes(data.Length).Reverse());
            target.AddRange(Encoding.ASCII.GetBytes(type));
            target.AddRange(data);
            target.AddRange(new byte[4]);
        }

        /// <summary>用 ZLibStream 压缩一段原始扫描线字节。</summary>
        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(data, 0, data.Length);
            }

            return output.ToArray();
        }

        /// <summary>写 4 字节大端整数。</summary>
        private static void WriteInt32BE(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }

        /// <summary>测试工作区：在系统临时目录下建一个用完即删的目录。</summary>
        private sealed class TempWorkspace : IDisposable
        {
            public TempWorkspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "创作管线测试-离风格-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            /// <summary>工作区根目录。</summary>
            public string Root { get; }

            /// <summary>递归删除工作区目录；清理失败不影响测试结论。</summary>
            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, true);
                    }
                }
                catch (IOException)
                {
                    // 清理失败不影响测试结论。
                }
                catch (UnauthorizedAccessException)
                {
                    // 同上。
                }
            }
        }
    }
}
