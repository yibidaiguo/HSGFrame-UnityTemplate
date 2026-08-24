using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 预览动图的测试：合出来的 GIF 要**真的会动**——帧数对、循环开着、帧与帧的像素不一样。
    ///
    /// 「会不会动」不能靠肉眼看一眼就算数：一张只有一帧的 GIF、
    /// 或者每帧都一样的 GIF，摆在聊天里跟正常的看着毫无差别，
    /// 而它恰恰意味着这一步白做了。所以这里把它解回来逐帧比。
    /// </summary>
    public class AnimatedPreviewTests : IDisposable
    {
        /// <summary>这一轮的临时工作目录。</summary>
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "预览图测试-" + Guid.NewGuid().ToString("N"));

        /// <summary>建工作目录。</summary>
        public AnimatedPreviewTests()
        {
            Directory.CreateDirectory(_root);
        }

        /// <summary>删工作目录。</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 删不掉就算了，临时目录而已。
            }
        }

        /// <summary>合出来的 GIF：帧数对得上、循环开着、每帧画面不一样。</summary>
        [Fact]
        public void ComposedGifActuallyAnimates()
        {
            var paths = WriteFrames(6, 40, 40);
            var output = Path.Combine(_root, "preview.gif");

            var result = AnimatedPreview.Compose(paths, output, frameRate: 10);

            Assert.Equal("", result.FailureReason);
            Assert.Equal(6, result.FrameCount);
            Assert.True(result.ByteCount > 0);

            using var decoded = Image.Load<Rgba32>(output);
            Assert.Equal(6, decoded.Frames.Count);
            Assert.Equal(0, decoded.Metadata.GetGifMetadata().RepeatCount);

            // 逐帧比像素：全都一样的话这张图动不起来，而它看着与正常的没差别。
            var fingerprints = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < decoded.Frames.Count; index++)
            {
                fingerprints.Add(Fingerprint(decoded.Frames.CloneFrame(index)));
            }

            Assert.True(fingerprints.Count > 1, "每一帧画面都一样，这张 GIF 动不起来");
        }

        /// <summary>帧率换算成每帧停留：10 帧每秒 → 每帧 10 个百分之一秒。</summary>
        [Fact]
        public void FrameRateBecomesFrameDelay()
        {
            var paths = WriteFrames(3, 20, 20);
            var output = Path.Combine(_root, "delay.gif");

            AnimatedPreview.Compose(paths, output, frameRate: 10);

            using var decoded = Image.Load<Rgba32>(output);
            Assert.Equal(10, decoded.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay);
        }

        /// <summary>
        /// 帧率再高，每帧停留也不许低于 2——填 0 或 1 时绝大多数客户端会当成 10，
        /// 于是本想要快的动画播出来反而是慢的。
        /// </summary>
        [Fact]
        public void FrameDelayNeverDropsBelowTwo()
        {
            var paths = WriteFrames(3, 20, 20);
            var output = Path.Combine(_root, "fast.gif");

            AnimatedPreview.Compose(paths, output, frameRate: 120);

            using var decoded = Image.Load<Rgba32>(output);
            Assert.True(decoded.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay >= 2);
        }

        /// <summary>长边超过上限就等比缩，且**比例不变**（缩歪了人审的是另一个形状）。</summary>
        [Fact]
        public void OversizedFramesAreScaledKeepingAspectRatio()
        {
            var paths = WriteFrames(2, 1600, 800);
            var output = Path.Combine(_root, "big.gif");

            var result = AnimatedPreview.Compose(paths, output, frameRate: 8);

            Assert.Equal(AnimatedPreview.MaximumSide, result.Width);
            Assert.Equal(AnimatedPreview.MaximumSide / 2, result.Height);
            Assert.Contains(result.Notes, note => note.Contains("等比缩"));
        }

        /// <summary>
        /// 帧太多时**等间隔抽**，不许砍尾巴：砍尾巴会让动画少半截，
        /// 而人看不出是被砍了，只会以为动作本身没做完。
        /// </summary>
        [Fact]
        public void TooManyFramesAreSampledNotTruncated()
        {
            var count = AnimatedPreview.MaximumFrameCount + 30;
            var paths = WriteFrames(count, 16, 16);
            var output = Path.Combine(_root, "many.gif");

            var result = AnimatedPreview.Compose(paths, output, frameRate: 12);

            Assert.Equal(AnimatedPreview.MaximumFrameCount, result.FrameCount);
            Assert.Contains(result.Notes, note => note.Contains("等间隔抽"));

            // 抽样必须覆盖到最后一帧附近——只取前一段就等于砍了尾巴。
            using var decoded = Image.Load<Rgba32>(output);
            Assert.Equal(AnimatedPreview.MaximumFrameCount, decoded.Frames.Count);
        }

        /// <summary>一帧都没有时明确失败，不产出一个零帧的 GIF（那是假成功）。</summary>
        [Fact]
        public void NoFramesFailsInsteadOfWritingEmptyGif()
        {
            var output = Path.Combine(_root, "empty.gif");

            var result = AnimatedPreview.Compose(Array.Empty<string>(), output, frameRate: 12);

            Assert.NotEqual("", result.FailureReason);
            Assert.False(File.Exists(output));
        }

        /// <summary>目录里的帧按**文件名序**取，不按修改时间——按时间排出来的走路动画左右脚是乱的。</summary>
        [Fact]
        public void DirectoryFramesAreOrderedByName()
        {
            var directory = Path.Combine(_root, "seq");
            Directory.CreateDirectory(directory);

            // 故意让写盘顺序与文件名序相反。
            foreach (var index in new[] { 2, 1, 0 })
            {
                WriteFrame(Path.Combine(directory, $"frame_{index:D3}.png"), 12, 12, (byte)(index * 80));
                System.Threading.Thread.Sleep(15);
            }

            var output = Path.Combine(_root, "seq.gif");
            var result = AnimatedPreview.ComposeFromDirectory(directory, output, frameRate: 6);

            Assert.Equal("", result.FailureReason);
            Assert.Equal(3, result.FrameCount);

            using var decoded = Image.Load<Rgba32>(output);
            using var first = decoded.Frames.CloneFrame(0);
            // 名字最小的那张是 frame_000（红色分量 0），它必须排在最前面。
            Assert.True(first[0, 0].R < 60, "第一帧不是文件名最小的那张——排序按错了东西");
        }

        /// <summary>写一批互不相同的帧，返回路径（按播放顺序）。</summary>
        private IReadOnlyList<string> WriteFrames(int count, int width, int height)
        {
            var paths = new List<string>();
            for (var index = 0; index < count; index++)
            {
                var path = Path.Combine(_root, $"f_{index:D4}.png");
                WriteFrame(path, width, height, (byte)((index * 37) % 256));
                paths.Add(path);
            }

            return paths;
        }

        /// <summary>写一张纯色帧（红色分量当作这一帧的指纹）。</summary>
        private static void WriteFrame(string path, int width, int height, byte red)
        {
            using var image = new Image<Rgba32>(width, height, new Rgba32(red, 90, 160, 255));
            image.SaveAsPng(path);
        }

        /// <summary>给一帧算个粗指纹：够用来判「两帧一不一样」。</summary>
        private static string Fingerprint(Image<Rgba32> frame)
        {
            using (frame)
            {
                var sum = 0L;
                for (var y = 0; y < frame.Height; y += 3)
                {
                    for (var x = 0; x < frame.Width; x += 3)
                    {
                        var pixel = frame[x, y];
                        sum += pixel.R + (pixel.G * 3) + (pixel.B * 7);
                    }
                }

                return sum.ToString();
            }
        }
    }
}
