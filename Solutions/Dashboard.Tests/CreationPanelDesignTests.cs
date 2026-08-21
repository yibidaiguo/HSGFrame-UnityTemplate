using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Template.Toolkit.CreationPipeline;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>面板设计池扩展（时间线/定稿预览）与离风格按需读取器测试：临时目录建池根与仓库，跑完自删；PNG 在测试里现造（决策 4）。</summary>
    public sealed class CreationPanelDesignTests : IDisposable
    {
        private readonly string _repositoryRoot;

        private readonly string _poolRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根与池根。</summary>
        public CreationPanelDesignTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板设计扩展测试-" + Guid.NewGuid().ToString("N"));
            _poolRoot = Path.Combine(_repositoryRoot, "Pools");
        }

        /// <summary>定稿行：色板从文件读出、数字版本对得上、非法十六进制串被跳过、参考图路径列表读出。</summary>
        [Fact]
        public void FinalRowReadsPaletteVersionAndSkipsInvalidHex()
        {
            WriteDesign("定稿", "风格A.json", """
                {
                  "名称": "风格A",
                  "版本": 3,
                  "色板": ["#FF0000", "not-a-color", "00FF00", "zzz"],
                  "参考图": ["参考/图1.png", "参考/图2.png"]
                }
                """);

            var row = Assert.Single(CreationPanelReader.ReadDesigns(_poolRoot));

            Assert.Equal("定稿", row.Category);
            Assert.Equal(new[] { "#FF0000", "00FF00" }, row.PaletteColors);
            Assert.Equal(3, row.FinalVersion);
            Assert.Equal(new[] { "参考/图1.png", "参考/图2.png" }, row.ReferenceImages);
        }

        /// <summary>记录行有「时间」字段 → MomentFromFileTime 为 false 且时间是那个值。</summary>
        [Fact]
        public void RecordWithMomentFieldKeepsItAndMarksNotFromFile()
        {
            WriteDesign("记录", "记录甲.json", """
                {
                  "名称": "记录甲",
                  "时间": "2026-04-04"
                }
                """);

            var row = Assert.Single(CreationPanelReader.ReadDesigns(_poolRoot));

            Assert.False(row.MomentFromFileTime);
            Assert.Equal("2026-04-04", row.Moment);
        }

        /// <summary>记录行没有「时间」字段 → MomentFromFileTime 为 true 且时间非空（文件最后写入时间）。</summary>
        [Fact]
        public void RecordWithoutMomentFallsBackToFileTime()
        {
            WriteDesign("记录", "记录乙.json", """
                {
                  "名称": "记录乙"
                }
                """);

            var row = Assert.Single(CreationPanelReader.ReadDesigns(_poolRoot));

            Assert.True(row.MomentFromFileTime);
            Assert.False(string.IsNullOrEmpty(row.Moment));
        }

        /// <summary>排序：定稿在前，同类内新的在前（Moment 降序）。</summary>
        [Fact]
        public void SortedByCategoryThenMomentDescending()
        {
            WriteDesign("定稿", "定稿A.json", """
                {
                  "名称": "定稿A",
                  "时间": "2026-01-01"
                }
                """);
            WriteDesign("汇总", "汇总旧.json", """
                {
                  "名称": "汇总旧",
                  "时间": "2026-02-01"
                }
                """);
            WriteDesign("汇总", "汇总新.json", """
                {
                  "名称": "汇总新",
                  "时间": "2026-03-01"
                }
                """);
            WriteDesign("记录", "记录A.json", """
                {
                  "名称": "记录A",
                  "时间": "2026-04-01"
                }
                """);

            var rows = CreationPanelReader.ReadDesigns(_poolRoot);

            Assert.Equal(4, rows.Count);
            Assert.Equal("定稿A", rows[0].Name);
            Assert.Equal("汇总新", rows[1].Name);
            Assert.Equal("汇总旧", rows[2].Name);
            Assert.Equal("记录A", rows[3].Name);
        }

        /// <summary>坏 JSON 的设计文件仍然产行（决策 43），时间退化到文件时间。</summary>
        [Fact]
        public void BrokenDesignStillProducesRow()
        {
            // 坏 JSON 的内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据。
            WriteDesign("定稿", "坏设计.json", """
                {
                  not valid json at all
                """);

            var row = Assert.Single(CreationPanelReader.ReadDesigns(_poolRoot));

            Assert.Equal("定稿", row.Category);
            Assert.Equal("坏设计", row.Name);
            Assert.False(row.IsReadable);
            Assert.True(row.MomentFromFileTime);
            Assert.False(string.IsNullOrEmpty(row.Moment));
            Assert.Empty(row.PaletteColors);
        }

        /// <summary>ReadDeviation：预览图不存在 → Measured=false，原因含「预览图」。</summary>
        [Fact]
        public void DeviationWithoutPreviewFailsWithPreviewReason()
        {
            WriteRequest("REQ-0001", "ASSET-0001-01", """
                {
                  "id": "ASSET-0001-01",
                  "需求id": "REQ-0001",
                  "风格锚点": { "定稿": "风格A" }
                }
                """);

            var result = CreationPanelReader.ReadDeviation(_repositoryRoot, _poolRoot, "REQ-0001", "ASSET-0001-01");

            Assert.False(result.Measured);
            Assert.Contains("预览图", result.FailureReason);
        }

        /// <summary>ReadDeviation：资产请求没有风格锚点 → Measured=false，原因含「风格锚点」。</summary>
        [Fact]
        public void DeviationWithoutStyleAnchorFailsWithAnchorReason()
        {
            WriteRequest("REQ-0001", "ASSET-0001-01", """
                {
                  "id": "ASSET-0001-01",
                  "需求id": "REQ-0001"
                }
                """);
            WriteFile(AssetPaths.PreviewFile(_repositoryRoot, "REQ-0001", "ASSET-0001-01"), "not a png");

            var result = CreationPanelReader.ReadDeviation(_repositoryRoot, _poolRoot, "REQ-0001", "ASSET-0001-01");

            Assert.False(result.Measured);
            Assert.Contains("风格锚点", result.FailureReason);
        }

        /// <summary>ReadDeviation：定稿不存在 → Measured=false，原因非空。</summary>
        [Fact]
        public void DeviationWithMissingFinalFailsWithReason()
        {
            WriteRequest("REQ-0001", "ASSET-0001-01", """
                {
                  "id": "ASSET-0001-01",
                  "需求id": "REQ-0001",
                  "风格锚点": { "定稿": "不存在的定稿" }
                }
                """);
            WriteFile(AssetPaths.PreviewFile(_repositoryRoot, "REQ-0001", "ASSET-0001-01"), "not a png");

            var result = CreationPanelReader.ReadDeviation(_repositoryRoot, _poolRoot, "REQ-0001", "ASSET-0001-01");

            Assert.False(result.Measured);
            Assert.False(string.IsNullOrEmpty(result.FailureReason));
        }

        /// <summary>ReadDeviation：预览图与定稿色板同色 → Measured=true 且 Deviation 接近 0。</summary>
        [Fact]
        public void DeviationMatchingPaletteIsMeasuredNearZero()
        {
            WriteRequest("REQ-0001", "ASSET-0001-01", """
                {
                  "id": "ASSET-0001-01",
                  "需求id": "REQ-0001",
                  "风格锚点": { "定稿": "风格A" }
                }
                """);
            WritePreviewPng();
            WriteFinal("风格A", """
                {
                  "色板": ["#FF0000"]
                }
                """);

            var result = CreationPanelReader.ReadDeviation(_repositoryRoot, _poolRoot, "REQ-0001", "ASSET-0001-01");

            Assert.True(result.Measured, result.FailureReason);
            Assert.True(result.Deviation < 0.0001, $"离风格距离应接近 0，实际 {result.Deviation}");
            Assert.NotEmpty(result.Swatches);
        }

        /// <summary>ReadDeviation：不写盘——跑完之后临时目录里的文件数与内容与跑之前完全一致。</summary>
        [Fact]
        public void DeviationDoesNotWriteAnyFiles()
        {
            WriteRequest("REQ-0001", "ASSET-0001-01", """
                {
                  "id": "ASSET-0001-01",
                  "需求id": "REQ-0001",
                  "风格锚点": { "定稿": "风格A" }
                }
                """);
            WritePreviewPng();
            WriteFinal("风格A", """
                {
                  "色板": ["#FF0000"]
                }
                """);

            var before = SnapshotDirectory();
            var result = CreationPanelReader.ReadDeviation(_repositoryRoot, _poolRoot, "REQ-0001", "ASSET-0001-01");
            var after = SnapshotDirectory();

            Assert.True(result.Measured, result.FailureReason);
            Assert.Equal(before, after);
        }

        /// <summary>删除本测试建的临时目录；清理失败不影响测试结论。</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_repositoryRoot))
                {
                    Directory.Delete(_repositoryRoot, true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响测试结论，按契约静默。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }

        /// <summary>把临时仓库递归列成「相对路径 → 字节内容」快照，用于「不写盘」断言。</summary>
        private Dictionary<string, byte[]> SnapshotDirectory()
        {
            var snapshot = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(_repositoryRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(_repositoryRoot, file).Replace('\\', '/');
                snapshot[relative] = File.ReadAllBytes(file);
            }

            return snapshot;
        }

        /// <summary>
        /// 定稿是一稿一目录（Designs/Final/&lt;名&gt;/final.json，子文档 06 §五）：
        /// 只扫平铺 *.json 会永远扫不到真定稿，定稿预览恒显示「还没有定稿」。
        /// 名称取目录名，不取恒为「定稿」的文件名。
        /// </summary>
        [Fact]
        public void FinalInItsOwnDirectoryIsFoundAndNamedAfterTheDirectory()
        {
            WriteFinal("UI图标风格", """
            {
              "名称": "UI图标风格",
              "版本": 3,
              "色板": ["#FF0000", "#1A2B3C"],
              "参考图": ["参考/a.png"]
            }
            """);

            var row = Assert.Single(CreationPanelReader.ReadDesigns(_poolRoot));

            Assert.Equal("定稿", row.Category);
            Assert.Equal("UI图标风格", row.Name);
            Assert.Equal(3, row.FinalVersion);
            Assert.Equal(new[] { "#FF0000", "#1A2B3C" }, row.PaletteColors);
            Assert.Equal(new[] { "参考/a.png" }, row.ReferenceImages);
        }

        private void WriteDesign(string category, string fileName, string json)
        {
            WriteFile(Path.Combine(_poolRoot, "Designs", CategoryDirectory(category), fileName), json);
        }

        private void WriteRequest(string requirementIdentifier, string assetIdentifier, string json)
        {
            WriteFile(AssetPaths.AssetRequestFile(_repositoryRoot, requirementIdentifier, assetIdentifier), json);
        }

        private void WriteFinal(string finalName, string json)
        {
            WriteFile(Path.Combine(_poolRoot, "Designs", "Final", finalName, "final.json"), json);
        }

        /// <summary>分类展示标签 → 目录名：目录改成 ASCII 之后，夹具也要按目录名去造树。</summary>
        private static string CategoryDirectory(string category)
        {
            switch (category)
            {
                case "定稿": return "Final";
                case "汇总": return "Digest";
                case "记录": return "Records";
                default: return category;
            }
        }

        private void WritePreviewPng()
        {
            // 1×1 纯红（RGB 255,0,0），滤波 None。
            var bytes = BuildPng(1, 1, 8, 2, new byte[] { 0, 255, 0, 0 });
            var path = AssetPaths.PreviewFile(_repositoryRoot, "REQ-0001", "ASSET-0001-01");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, bytes);
        }

        private static void WriteFile(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        /// <summary>手工拼一个合法 PNG 字节流（照 PngImageTests 的辅助方法抄，仓库零二进制样例——决策 4）。</summary>
        private static byte[] BuildPng(
            int width,
            int height,
            int bitDepth,
            int colorType,
            byte[] scanlines)
        {
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            var ihdr = new byte[13];
            WriteInt32BE(ihdr, 0, width);
            WriteInt32BE(ihdr, 4, height);
            ihdr[8] = (byte)bitDepth;
            ihdr[9] = (byte)colorType;
            ihdr[10] = 0;
            ihdr[11] = 0;
            ihdr[12] = 0;
            AddChunk(bytes, "IHDR", ihdr);

            AddChunk(bytes, "IDAT", Compress(scanlines));
            AddChunk(bytes, "IEND", Array.Empty<byte>());
            return bytes.ToArray();
        }

        /// <summary>往块列表里追加一个块：长度（大端）+ 类型 + 数据 + 4 字节 CRC 占位（解码器不校验 CRC）。</summary>
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
    }
}
