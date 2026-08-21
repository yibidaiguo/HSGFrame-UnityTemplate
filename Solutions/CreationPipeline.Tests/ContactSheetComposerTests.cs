using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 九宫格拼图合成器的测试：尺寸、占位格、label 校验、确定性与列数。
    /// 测试图全部用 PngEncoder 现造，临时目录走仓库现有约定（PoolTestWorkspace 用完即删）。
    /// </summary>
    public sealed class ContactSheetComposerTests
    {
        /// <summary>4 格 2 列 → 输出 PNG 解码回来，宽 = 2×格边长、高 = 2×格边长。</summary>
        [Fact]
        public void FourCellsTwoColumnsProduceExpectedDimensions()
        {
            using var workspace = new PoolTestWorkspace();
            var cells = new List<ContactSheetCell>();
            for (var i = 1; i <= 4; i++)
            {
                cells.Add(new ContactSheetCell(i.ToString(), WriteSolidPng(workspace.Root, $"v{i}.png", 32, 32, 200, 40, 40)));
            }

            var output = Path.Combine(workspace.Root, "sheet.png");
            var result = ContactSheetComposer.Compose(cells, 2, output);

            Assert.True(result.Succeeded);
            var decoded = PngDecoder.DecodeFile(output);
            Assert.True(decoded.Succeeded, decoded.FailureReason);
            Assert.Equal(2 * ContactSheetComposer.DefaultCellSideLength, decoded.Image.Width);
            Assert.Equal(2 * ContactSheetComposer.DefaultCellSideLength, decoded.Image.Height);
        }

        /// <summary>5 格 3 列 → 行数 2，高 = 2×格边长。</summary>
        [Fact]
        public void FiveCellsThreeColumnsUseTwoRows()
        {
            using var workspace = new PoolTestWorkspace();
            var cells = new List<ContactSheetCell>();
            for (var i = 1; i <= 5; i++)
            {
                cells.Add(new ContactSheetCell(i.ToString(), WriteSolidPng(workspace.Root, $"v{i}.png", 32, 32, 40, 200, 40)));
            }

            var output = Path.Combine(workspace.Root, "sheet.png");
            var result = ContactSheetComposer.Compose(cells, 3, output);

            Assert.True(result.Succeeded);
            var decoded = PngDecoder.DecodeFile(output);
            Assert.True(decoded.Succeeded, decoded.FailureReason);
            Assert.Equal(3 * ContactSheetComposer.DefaultCellSideLength, decoded.Image.Width);
            Assert.Equal(2 * ContactSheetComposer.DefaultCellSideLength, decoded.Image.Height);
        }

        /// <summary>一格指向非 PNG 文件 → Succeeded 仍 true，Findings 恰 1 条，那一格中心是占位红 (90,30,30)。</summary>
        [Fact]
        public void NonPngCellYieldsPlaceholderAndSingleFinding()
        {
            using var workspace = new PoolTestWorkspace();
            var bad = Path.Combine(workspace.Root, "bad.png");
            File.WriteAllText(bad, "这不是一张 PNG");

            var cells = new List<ContactSheetCell>
            {
                new ContactSheetCell("1", bad),
                new ContactSheetCell("2", WriteSolidPng(workspace.Root, "v2.png", 32, 32, 100, 100, 100)),
                new ContactSheetCell("3", WriteSolidPng(workspace.Root, "v3.png", 32, 32, 100, 100, 100)),
                new ContactSheetCell("4", WriteSolidPng(workspace.Root, "v4.png", 32, 32, 100, 100, 100))
            };

            var output = Path.Combine(workspace.Root, "sheet.png");
            var result = ContactSheetComposer.Compose(cells, 2, output);

            Assert.True(result.Succeeded);
            var finding = Assert.Single(result.Findings);
            Assert.Contains("bad.png", finding.Reason);

            var decoded = PngDecoder.DecodeFile(output);
            Assert.True(decoded.Succeeded, decoded.FailureReason);

            // 坏格是第 0 格（col 0, row 0），中心像素应是占位红。
            var cellSide = ContactSheetComposer.DefaultCellSideLength;
            var center = ((cellSide / 2) * decoded.Image.Width + (cellSide / 2)) * 4;
            Assert.Equal(90, decoded.Image.Pixels[center]);
            Assert.Equal(30, decoded.Image.Pixels[center + 1]);
            Assert.Equal(30, decoded.Image.Pixels[center + 2]);
        }

        /// <summary>label 含中文 → Succeeded 为 false，且 outputPath 处没有文件生成。</summary>
        [Fact]
        public void ChineseLabelFailsWithoutWritingFile()
        {
            using var workspace = new PoolTestWorkspace();
            var cells = new List<ContactSheetCell>
            {
                new ContactSheetCell("中文", WriteSolidPng(workspace.Root, "v1.png", 32, 32, 100, 100, 100))
            };

            var output = Path.Combine(workspace.Root, "should-not-exist.png");
            var result = ContactSheetComposer.Compose(cells, 1, output);

            Assert.False(result.Succeeded);
            Assert.False(File.Exists(output));
        }

        /// <summary>同一组输入连拼两次 → 两次输出文件字节完全相同（确定性）。</summary>
        [Fact]
        public void ComposeIsDeterministic()
        {
            using var workspace = new PoolTestWorkspace();
            var cells = new List<ContactSheetCell>();
            for (var i = 1; i <= 4; i++)
            {
                cells.Add(new ContactSheetCell(i.ToString(), WriteSolidPng(workspace.Root, $"v{i}.png", 48, 32, 30, 160, 30)));
            }

            var output1 = Path.Combine(workspace.Root, "sheet1.png");
            var output2 = Path.Combine(workspace.Root, "sheet2.png");

            var first = ContactSheetComposer.Compose(cells, 2, output1);
            var second = ContactSheetComposer.Compose(cells, 2, output2);

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.Equal(File.ReadAllBytes(output1), File.ReadAllBytes(output2));
        }

        /// <summary>列数：1→1、4→2、5→3、9→3、10→3。</summary>
        [Theory]
        [InlineData(1, 1)]
        [InlineData(4, 2)]
        [InlineData(5, 3)]
        [InlineData(9, 3)]
        [InlineData(10, 3)]
        public void ColumnCountForChoosesExpected(int cellCount, int expected)
        {
            Assert.Equal(expected, ContactSheetComposer.ColumnCountFor(cellCount));
        }

        private static string WriteSolidPng(string root, string fileName, int width, int height, byte r, byte g, byte b, byte a = 255)
        {
            var pixels = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++)
            {
                pixels[i * 4] = r;
                pixels[i * 4 + 1] = g;
                pixels[i * 4 + 2] = b;
                pixels[i * 4 + 3] = a;
            }

            var path = Path.Combine(root, fileName);
            var ok = PngEncoder.EncodeToFile(new PngImage(width, height, pixels), path, out var reason);
            Assert.True(ok, "写测试 PNG 失败：" + reason);
            return path;
        }
    }
}
