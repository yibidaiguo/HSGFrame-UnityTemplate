using System;
using System.IO;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 按规格归一：缩放到规格尺寸、背景是纯色时抠透明。
    ///
    /// 盯两件事：**该做的做**（生图回来的尺寸从来对不上规格，不缩就入不了库），
    /// **不确定的别硬做**（抠不干净的透明比不透明更糟——看着像成了，实际留一圈脏边，
    /// 人要到把图摆进游戏里才发现）。
    /// </summary>
    public class AssetImageNormalizerTests
    {
        /// <summary>尺寸不对就缩到规格，并且说出来动了什么。</summary>
        [Fact]
        public void OversizedImageIsResizedToSpec()
        {
            var path = NewTemporaryPng(64, 64, (x, y) => new byte[] { 200, 30, 30, 255 });
            try
            {
                var outcome = AssetImageNormalizer.Normalize(path, 16, 16, needsTransparency: false);

                Assert.True(outcome.Succeeded);
                Assert.True(outcome.Changed);
                Assert.Contains(outcome.Notes, note => note.Contains("16×16"));

                var reread = PngDecoder.DecodeFile(path);
                Assert.True(reread.Succeeded);
                Assert.Equal(16, reread.Image.Width);
                Assert.Equal(16, reread.Image.Height);
            }
            finally
            {
                Delete(path);
            }
        }

        /// <summary>尺寸本来就对就一个字节都不动。</summary>
        [Fact]
        public void MatchingSizeIsLeftAlone()
        {
            var path = NewTemporaryPng(16, 16, (x, y) => new byte[] { 10, 10, 10, 255 });
            try
            {
                var outcome = AssetImageNormalizer.Normalize(path, 16, 16, needsTransparency: false);

                Assert.True(outcome.Succeeded);
                Assert.False(outcome.Changed);
            }
            finally
            {
                Delete(path);
            }
        }

        /// <summary>四角同色、主体色差够大时，背景抠成透明。</summary>
        [Fact]
        public void SolidBackgroundIsMadeTransparent()
        {
            // 白底，正中一块黑：四角同色、主体与背景拉得开。
            var path = NewTemporaryPng(32, 32, (x, y) =>
                x >= 12 && x < 20 && y >= 12 && y < 20
                    ? new byte[] { 0, 0, 0, 255 }
                    : new byte[] { 255, 255, 255, 255 });
            try
            {
                var outcome = AssetImageNormalizer.Normalize(path, 0, 0, needsTransparency: true);

                Assert.True(outcome.Succeeded);
                Assert.True(outcome.Changed);
                Assert.Empty(outcome.Remaining);

                var reread = PngDecoder.DecodeFile(path);
                Assert.True(reread.Succeeded);

                // 角上被抠透明了，中间那块主体还在。
                Assert.Equal(0, reread.Image.Pixels[3]);
                var centre = (((16 * 32) + 16) * 4) + 3;
                Assert.Equal(255, reread.Image.Pixels[centre]);
            }
            finally
            {
                Delete(path);
            }
        }

        /// <summary>
        /// 四角不同色（渐变、暗角）时**不抠**，并把「还差透明」如实报出来。
        /// </summary>
        [Fact]
        public void GradientBackgroundIsNotTouchedAndIsReported()
        {
            var path = NewTemporaryPng(32, 32, (x, y) =>
            {
                var shade = (byte)Math.Min(255, (x * 8) + (y * 2));
                return new byte[] { shade, shade, shade, 255 };
            });
            try
            {
                var outcome = AssetImageNormalizer.Normalize(path, 0, 0, needsTransparency: true);

                Assert.True(outcome.Succeeded);
                Assert.False(outcome.Changed);
                Assert.Contains(outcome.Remaining, note => note.Contains("没敢抠透明"));
            }
            finally
            {
                Delete(path);
            }
        }

        /// <summary>主体跟背景色太近时也不抠——一抠会把主体抠掉一块。</summary>
        [Fact]
        public void LowContrastImageIsNotTouched()
        {
            var path = NewTemporaryPng(32, 32, (x, y) => new byte[] { 100, 100, 100, 255 });
            try
            {
                var outcome = AssetImageNormalizer.Normalize(path, 0, 0, needsTransparency: true);

                Assert.False(outcome.Changed);
                Assert.Contains(outcome.Remaining, note => note.Contains("没敢动"));
            }
            finally
            {
                Delete(path);
            }
        }

        /// <summary>读不动的文件如实报，不许当成处理过了。</summary>
        [Fact]
        public void UnreadableFileIsReported()
        {
            var path = Path.Combine(Path.GetTempPath(), "归一测试-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllText(path, "这不是一张 PNG");
            try
            {
                var outcome = AssetImageNormalizer.Normalize(path, 16, 16, needsTransparency: false);

                Assert.False(outcome.Succeeded);
                Assert.NotEmpty(outcome.Remaining);
            }
            finally
            {
                Delete(path);
            }
        }

        /// <summary>造一张临时 PNG，像素由回调给。</summary>
        /// <summary>
        /// 主体本身带着背景那个色（白底上的白衣白发）时不许抠——漫延会顺着主体一路灌进去，
        /// 拆出来一身洞。真炸过：拆出来的立绘白衣白发全是破碎的挖空。
        ///
        /// 判据是「容差减半再灌一遍，两次差多少」：分得开的图两次几乎一样，
        /// 这种图容差稍一松就多吃掉一大片。
        /// </summary>
        [Fact]
        public void SubjectSharingBackgroundColourIsRefusedInsteadOfPunched()
        {
            // 纯白底 + 一块几乎纯白的主体（离白 7：紧容差 5 灌不动、松容差 10 灌得动），
            // 且主体贴着左边缘、与背景连通——正是会被灌穿的那种形状。
            // 两次容差差出一大截，稳定性判据就是靠这个把它拦下来的。
            var path = NewTemporaryPng(64, 64, (x, y) =>
                x < 40 && y > 8 && y < 56
                    ? new byte[] { 248, 248, 248, 255 }
                    : new byte[] { 255, 255, 255, 255 });
            try
            {
                var outcome = AssetImageNormalizer.Normalize(path, 0, 0, needsTransparency: true);

                Assert.False(outcome.Changed);
                Assert.Contains(outcome.Remaining, note => note.Contains("灌进主体") || note.Contains("没敢抠透明"));
            }
            finally
            {
                Delete(path);
            }
        }

        /// <summary>背景与主体分得开时照抠不误——收紧判据不能把本来能干的活也拦了。</summary>
        [Fact]
        public void WellSeparatedSubjectStillGetsItsBackgroundCleared()
        {
            var path = NewTemporaryPng(64, 64, (x, y) =>
                x > 16 && x < 48 && y > 16 && y < 48
                    ? new byte[] { 20, 40, 200, 255 }
                    : new byte[] { 255, 255, 255, 255 });
            try
            {
                var outcome = AssetImageNormalizer.Normalize(path, 0, 0, needsTransparency: true);

                Assert.True(outcome.Changed);
                Assert.Contains(outcome.Notes, note => note.Contains("抠成透明"));
            }
            finally
            {
                Delete(path);
            }
        }

        private static string NewTemporaryPng(int width, int height, Func<int, int, byte[]> pixelAt)
        {
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var colour = pixelAt(x, y);
                    var offset = ((y * width) + x) * 4;
                    pixels[offset] = colour[0];
                    pixels[offset + 1] = colour[1];
                    pixels[offset + 2] = colour[2];
                    pixels[offset + 3] = colour[3];
                }
            }

            var path = Path.Combine(Path.GetTempPath(), "归一测试-" + Guid.NewGuid().ToString("N") + ".png");
            Assert.True(PngEncoder.EncodeToFile(new PngImage(width, height, pixels), path, out _));
            return path;
        }

        /// <summary>删临时文件；删不掉就放着，不影响结论。</summary>
        private static void Delete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
            }
        }
    }
}
