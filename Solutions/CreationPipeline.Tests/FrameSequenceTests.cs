using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 帧序列描述与精灵图集的测试。
    /// 盯三件事：帧的顺序按文件名而不是时间、描述读不了时不许降级成空序列、
    /// 锚点真的把每帧摆到了该在的位置（不然拼出来的人物会原地上下跳）。
    /// </summary>
    public class FrameSequenceTests
    {
        /// <summary>扫目录按**文件名序数序**排，不按写入顺序——并发写出来的帧时间戳跟播放顺序无关。</summary>
        [Fact]
        public void ScanOrdersByFileNameNotWriteTime()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = Path.Combine(workspace.Root, "frames");
            Directory.CreateDirectory(directory);

            // 故意倒着写：03 先落盘，01 最后落盘。
            WriteSolidPng(Path.Combine(directory, "f_03.png"), 4, 4);
            WriteSolidPng(Path.Combine(directory, "f_02.png"), 4, 4);
            WriteSolidPng(Path.Combine(directory, "f_01.png"), 4, 4);

            var sequence = FrameSequence.Scan(directory, "帧动画", 12, FrameSequence.DefaultAnchor, "测试");

            Assert.Equal(3, sequence.FrameCount);
            Assert.Equal("f_01.png", Path.GetFileName(sequence.Frames[0].Path));
            Assert.Equal("f_03.png", Path.GetFileName(sequence.Frames[2].Path));
        }

        /// <summary>扫出来的每帧带上真实尺寸。</summary>
        [Fact]
        public void ScanReadsFrameSize()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = Path.Combine(workspace.Root, "frames");
            Directory.CreateDirectory(directory);
            WriteSolidPng(Path.Combine(directory, "a.png"), 8, 6);

            var frame = Assert.Single(FrameSequence.Scan(directory, "帧动画", 12, FrameSequence.DefaultAnchor, "").Frames);

            Assert.Equal(8, frame.Width);
            Assert.Equal(6, frame.Height);
        }

        /// <summary>描述写盘再读回来，帧数、帧率、锚点一字不差。</summary>
        [Fact]
        public void DescriptionRoundTrips()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = Path.Combine(workspace.Root, "frames");
            Directory.CreateDirectory(directory);
            WriteSolidPng(Path.Combine(directory, "a.png"), 4, 4);
            WriteSolidPng(Path.Combine(directory, "b.png"), 4, 4);

            var saved = FrameSequence.Scan(directory, "3D动画", 24, "中心", "blender 转台 · 环绕");
            var path = saved.Save(directory);

            var loaded = FrameSequence.Load(path, out var reason);

            Assert.Equal("", reason);
            Assert.Equal(2, loaded.FrameCount);
            Assert.Equal(24, loaded.FrameRate);
            Assert.Equal("中心", loaded.Anchor);
            Assert.Equal("3D动画", loaded.Kind);
        }

        /// <summary>
        /// 描述文件不在时给 null + 原因，**不给空序列**：
        /// 空序列会让第二步拼出一张零帧图集还报成功。
        /// </summary>
        [Fact]
        public void MissingDescriptionIsNullWithReason()
        {
            using var workspace = new PoolTestWorkspace();

            var loaded = FrameSequence.Load(Path.Combine(workspace.Root, "没有这份.json"), out var reason);

            Assert.Null(loaded);
            Assert.Contains("不存在", reason);
        }

        /// <summary>时长按帧数除帧率算；帧率为 0 时给 0 而不是除零炸掉。</summary>
        [Fact]
        public void DurationHandlesZeroFrameRate()
        {
            var frames = new List<FrameSequenceEntry> { new FrameSequenceEntry(0, "a.png", 4, 4) };

            Assert.Equal(0d, new FrameSequence("帧动画", 0, "中心", frames, "").DurationSeconds);
            Assert.Equal(0.25d, new FrameSequence("帧动画", 4, "中心", frames, "").DurationSeconds, 3);
        }

        /// <summary>尺寸不齐要在那句人话里说出来——拼图集时那几帧会在格子里偏。</summary>
        [Fact]
        public void DescribeCallsOutUnevenSizes()
        {
            var frames = new List<FrameSequenceEntry>
            {
                new FrameSequenceEntry(0, "a.png", 8, 8),
                new FrameSequenceEntry(1, "b.png", 8, 6)
            };

            Assert.Contains("尺寸不齐", new FrameSequence("帧动画", 12, "中心", frames, "").Describe());
        }

        /// <summary>拼图集：横排一行，宽 = 格宽 × 帧数，高 = 格高。</summary>
        [Fact]
        public void ComposeLaysFramesInOneRow()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = Path.Combine(workspace.Root, "frames");
            Directory.CreateDirectory(directory);
            WriteSolidPng(Path.Combine(directory, "a.png"), 4, 4);
            WriteSolidPng(Path.Combine(directory, "b.png"), 4, 4);
            var sequence = FrameSequence.Scan(directory, "帧动画", 12, "中心", "");

            var result = SpriteSheetComposer.Compose(sequence, Path.Combine(workspace.Root, "sheet"), "atlas");

            Assert.True(result.Succeeded, result.FailureReason);
            var decoded = PngDecoder.DecodeFile(result.SheetPath);
            Assert.True(decoded.Succeeded);
            Assert.Equal(8, decoded.Image.Width);
            Assert.Equal(4, decoded.Image.Height);
        }

        /// <summary>
        /// 锚点「底边中点」把矮帧贴到格子底部：格高 6、帧高 4 时，
        /// 帧的第一行像素应落在第 2 行（6-4），上面两行是透明的。
        /// 这一条守的正是「人物不在原地上下跳」。
        /// </summary>
        [Fact]
        public void BottomAnchorAlignsShortFrameToBottom()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = Path.Combine(workspace.Root, "frames");
            Directory.CreateDirectory(directory);
            WriteSolidPng(Path.Combine(directory, "a.png"), 4, 6);
            WriteSolidPng(Path.Combine(directory, "b.png"), 4, 4);
            var sequence = FrameSequence.Scan(directory, "人物帧动画", 12, "底边中点", "");

            var result = SpriteSheetComposer.Compose(sequence, Path.Combine(workspace.Root, "sheet"), "atlas");

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Contains(result.Notes, note => note.Contains("尺寸不齐"));

            var image = PngDecoder.DecodeFile(result.SheetPath).Image;
            // 第二格（x 从 4 起）的顶行该是空的，倒数第二行该是实的。
            Assert.Equal(0, AlphaAt(image, 4, 0));
            Assert.Equal(255, AlphaAt(image, 4, 4));
        }

        /// <summary>缺一帧就整次失败，不许拼出一段少一帧的动画。</summary>
        [Fact]
        public void MissingFrameFailsTheWholeCompose()
        {
            using var workspace = new PoolTestWorkspace();
            var frames = new List<FrameSequenceEntry>
            {
                new FrameSequenceEntry(0, Path.Combine(workspace.Root, "不存在.png"), 4, 4)
            };
            var sequence = new FrameSequence("帧动画", 12, "中心", frames, "");

            var result = SpriteSheetComposer.Compose(sequence, Path.Combine(workspace.Root, "sheet"), "atlas");

            Assert.False(result.Succeeded);
            Assert.Contains("读不了", result.FailureReason);
        }

        /// <summary>空序列拼不出图集，且要说原因。</summary>
        [Fact]
        public void EmptySequenceFails()
        {
            using var workspace = new PoolTestWorkspace();

            var result = SpriteSheetComposer.Compose(
                new FrameSequence("帧动画", 12, "中心", Array.Empty<FrameSequenceEntry>(), ""),
                Path.Combine(workspace.Root, "sheet"),
                "atlas");

            Assert.False(result.Succeeded);
            Assert.Contains("空", result.FailureReason);
        }

        /// <summary>图集描述带上 Unity 侧切图要的那几项：格宽、格高、帧数、帧率。</summary>
        [Fact]
        public void MetadataCarriesSlicingNumbers()
        {
            using var workspace = new PoolTestWorkspace();
            var directory = Path.Combine(workspace.Root, "frames");
            Directory.CreateDirectory(directory);
            WriteSolidPng(Path.Combine(directory, "a.png"), 4, 4);
            var sequence = FrameSequence.Scan(directory, "帧动画", 15, "中心", "");

            var result = SpriteSheetComposer.Compose(sequence, Path.Combine(workspace.Root, "sheet"), "atlas");
            var text = File.ReadAllText(result.MetadataPath);

            Assert.Contains("\"格宽\": 4", text);
            Assert.Contains("\"帧率\": 15", text);
        }

        /// <summary>写一张全不透明的纯色 PNG。</summary>
        private static void WriteSolidPng(string filePath, int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 200;
                pixels[index + 1] = 100;
                pixels[index + 2] = 50;
                pixels[index + 3] = 255;
            }

            Assert.True(PngEncoder.EncodeToFile(new PngImage(width, height, pixels), filePath, out var reason), reason);
        }

        /// <summary>读某个像素的 alpha。</summary>
        private static byte AlphaAt(PngImage image, int x, int y)
        {
            return image.Pixels[(y * image.Width + x) * 4 + 3];
        }
    }
}
