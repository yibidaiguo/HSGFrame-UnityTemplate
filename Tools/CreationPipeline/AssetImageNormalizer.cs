using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次按规格归一的结果：动了什么、还有什么没达标。</summary>
    public sealed class AssetImageNormalizeOutcome
    {
        /// <summary>构造一次归一结果。</summary>
        /// <param name="succeeded">这张图处理完了没有（读得动、写得回）。</param>
        /// <param name="changed">图有没有被真改过。</param>
        /// <param name="notes">动了什么，一条一句。</param>
        /// <param name="remaining">还差什么没达标——**必须报出来**，不许当成已达标。</param>
        public AssetImageNormalizeOutcome(
            bool succeeded, bool changed, IReadOnlyList<string> notes, IReadOnlyList<string> remaining)
        {
            Succeeded = succeeded;
            Changed = changed;
            Notes = notes ?? Array.Empty<string>();
            Remaining = remaining ?? Array.Empty<string>();
        }

        /// <summary>这张图处理完了没有。</summary>
        public bool Succeeded { get; }

        /// <summary>图有没有被真改过。</summary>
        public bool Changed { get; }

        /// <summary>动了什么，一条一句。</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>还差什么没达标。</summary>
        public IReadOnlyList<string> Remaining { get; }
    }

    /// <summary>
    /// 把生出来的图按资产规格归一：**缩放到规格尺寸**，以及在背景是纯色时**抠成透明**。
    ///
    /// 为什么要有：生图下游按自己的档位出图（要 256×256，回来的是 1254×1254 不透明），
    /// 而资产规格是硬的（图标 256×256、需要透明、二次幂）。少了这一步，
    /// 出多少张都入不了库——机检会逐张判红。
    ///
    /// **抠透明只做确定的那一种**：四角同色、且背景与主体色差够大时，从四角漫延着抠。
    /// 背景是渐变或暗角时**不抠，并把「还差透明」如实报出来**——
    /// 抠不干净的透明比不透明更糟：它看着像成了，实际留着一圈脏边，
    /// 而人要到把图摆进游戏里才发现。
    /// </summary>
    public static class AssetImageNormalizer
    {
        /// <summary>判定「四角同色」的容差：每通道差值都在这个数以内算同色。</summary>
        private const int CornerTolerance = 12;

        /// <summary>漫延抠背景时的容差：与背景色每通道差值都在这个数以内算背景。</summary>
        private const int FloodTolerance = 24;

        /// <summary>
        /// 背景与主体的色差至少要拉开这么多，才敢抠。
        /// 拉不开就说明主体本身也是那个色调，一抠会把主体抠掉一块。
        /// </summary>
        private const int SeparationThreshold = 60;

        /// <summary>
        /// 按规格归一一张图。
        /// </summary>
        /// <param name="filePath">图片路径，就地改写。</param>
        /// <param name="targetWidth">规格宽；0 或负数表示不管尺寸。</param>
        /// <param name="targetHeight">规格高；0 或负数表示不管尺寸。</param>
        /// <param name="needsTransparency">规格要不要透明。</param>
        public static AssetImageNormalizeOutcome Normalize(
            string filePath, int targetWidth, int targetHeight, bool needsTransparency)
        {
            var notes = new List<string>();
            var remaining = new List<string>();

            var decoded = PngDecoder.DecodeFile(filePath);
            if (!decoded.Succeeded)
            {
                return new AssetImageNormalizeOutcome(
                    false, false, notes, new[] { "读不动这张图：" + decoded.FailureReason });
            }

            var image = decoded.Image;
            var changed = false;

            if (needsTransparency)
            {
                var flooded = TryMakeBackgroundTransparent(image, out var transparencyNote);
                if (flooded != null)
                {
                    image = flooded;
                    changed = true;
                    notes.Add(transparencyNote);
                }
                else
                {
                    remaining.Add(transparencyNote);
                }
            }

            if (targetWidth > 0 && targetHeight > 0 && (image.Width != targetWidth || image.Height != targetHeight))
            {
                notes.Add($"尺寸 {image.Width}×{image.Height} 缩到 {targetWidth}×{targetHeight}");
                image = Resize(image, targetWidth, targetHeight);
                changed = true;
            }

            if (!changed)
            {
                return new AssetImageNormalizeOutcome(true, false, notes, remaining);
            }

            if (!PngEncoder.EncodeToFile(image, filePath, out var reason))
            {
                return new AssetImageNormalizeOutcome(false, false, notes, new[] { "改完写不回去：" + reason });
            }

            return new AssetImageNormalizeOutcome(true, true, notes, remaining);
        }

        /// <summary>
        /// 面积平均缩放（box filter）。**只做缩小方向的平均**，放大时退化成最近邻——
        /// 生图出来的图总比规格大，放大这一支基本用不上，写个能跑的就行，不值得为它上双三次。
        /// </summary>
        /// <param name="source">原图。</param>
        /// <param name="width">目标宽。</param>
        /// <param name="height">目标高。</param>
        public static PngImage Resize(PngImage source, int width, int height)
        {
            var pixels = new byte[width * height * 4];
            var scaleX = (double)source.Width / width;
            var scaleY = (double)source.Height / height;

            for (var y = 0; y < height; y++)
            {
                var startY = (int)(y * scaleY);
                var endY = Math.Max(startY + 1, (int)((y + 1) * scaleY));
                endY = Math.Min(endY, source.Height);

                for (var x = 0; x < width; x++)
                {
                    var startX = (int)(x * scaleX);
                    var endX = Math.Max(startX + 1, (int)((x + 1) * scaleX));
                    endX = Math.Min(endX, source.Width);

                    long red = 0;
                    long green = 0;
                    long blue = 0;
                    long alpha = 0;
                    long count = 0;

                    for (var sampleY = startY; sampleY < endY; sampleY++)
                    {
                        for (var sampleX = startX; sampleX < endX; sampleX++)
                        {
                            var offset = ((sampleY * source.Width) + sampleX) * 4;
                            red += source.Pixels[offset];
                            green += source.Pixels[offset + 1];
                            blue += source.Pixels[offset + 2];
                            alpha += source.Pixels[offset + 3];
                            count++;
                        }
                    }

                    if (count == 0)
                    {
                        count = 1;
                    }

                    var target = ((y * width) + x) * 4;
                    pixels[target] = (byte)(red / count);
                    pixels[target + 1] = (byte)(green / count);
                    pixels[target + 2] = (byte)(blue / count);
                    pixels[target + 3] = (byte)(alpha / count);
                }
            }

            return new PngImage(width, height, pixels);
        }

        /// <summary>
        /// 背景是纯色时从四角漫延着抠成透明；不敢抠时返回 null 并说明为什么。
        /// </summary>
        /// <param name="image">原图。</param>
        /// <param name="note">抠了的说明，或没敢抠的原因。</param>
        public static PngImage TryMakeBackgroundTransparent(PngImage image, out string note)
        {
            note = "";
            if (image.Width < 4 || image.Height < 4)
            {
                note = "图太小，没敢抠透明";
                return null;
            }

            var corners = new[]
            {
                PixelAt(image, 0, 0),
                PixelAt(image, image.Width - 1, 0),
                PixelAt(image, 0, image.Height - 1),
                PixelAt(image, image.Width - 1, image.Height - 1)
            };

            for (var index = 1; index < corners.Length; index++)
            {
                if (!Close(corners[0], corners[index], CornerTolerance))
                {
                    note = "四角不同色（背景多半是渐变或暗角），没敢抠透明——"
                        + "抠不干净的透明比不透明更糟：看着像成了，实际留一圈脏边，"
                        + "人要到把图摆进游戏里才发现";
                    return null;
                }
            }

            var background = corners[0];
            if (!HasEnoughSeparation(image, background))
            {
                note = "主体的颜色跟背景太近，抠了会把主体也抠掉一块，没敢动";
                return null;
            }

            var pixels = new byte[image.Width * image.Height * 4];
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = image.Pixels[index];
            }

            var visited = new bool[image.Width * image.Height];
            var queue = new Queue<int>();
            EnqueueBorder(image, queue, visited, background);

            var cleared = 0;
            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % image.Width;
                var y = index / image.Width;
                pixels[(index * 4) + 3] = 0;
                cleared++;

                TryEnqueue(image, queue, visited, background, x - 1, y);
                TryEnqueue(image, queue, visited, background, x + 1, y);
                TryEnqueue(image, queue, visited, background, x, y - 1);
                TryEnqueue(image, queue, visited, background, x, y + 1);
            }

            var ratio = (double)cleared / (image.Width * image.Height);
            note = $"背景抠成透明（清掉 {ratio:P0} 的像素）";
            return new PngImage(image.Width, image.Height, pixels);
        }

        /// <summary>把四条边上属于背景色的像素塞进队列当种子。</summary>
        private static void EnqueueBorder(PngImage image, Queue<int> queue, bool[] visited, byte[] background)
        {
            for (var x = 0; x < image.Width; x++)
            {
                TryEnqueue(image, queue, visited, background, x, 0);
                TryEnqueue(image, queue, visited, background, x, image.Height - 1);
            }

            for (var y = 0; y < image.Height; y++)
            {
                TryEnqueue(image, queue, visited, background, 0, y);
                TryEnqueue(image, queue, visited, background, image.Width - 1, y);
            }
        }

        /// <summary>坐标合法、没访问过、且颜色接近背景时入队。</summary>
        private static void TryEnqueue(
            PngImage image, Queue<int> queue, bool[] visited, byte[] background, int x, int y)
        {
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
            {
                return;
            }

            var index = (y * image.Width) + x;
            if (visited[index])
            {
                return;
            }

            if (!Close(PixelAt(image, x, y), background, FloodTolerance))
            {
                return;
            }

            visited[index] = true;
            queue.Enqueue(index);
        }

        /// <summary>图里有没有跟背景拉开足够色差的像素——没有就说明主体也是那个色调，不敢抠。</summary>
        private static bool HasEnoughSeparation(PngImage image, byte[] background)
        {
            for (var index = 0; index < image.Width * image.Height; index++)
            {
                var offset = index * 4;
                var distance = Math.Abs(image.Pixels[offset] - background[0])
                    + Math.Abs(image.Pixels[offset + 1] - background[1])
                    + Math.Abs(image.Pixels[offset + 2] - background[2]);
                if (distance >= SeparationThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>取一个像素的 RGBA。</summary>
        private static byte[] PixelAt(PngImage image, int x, int y)
        {
            var offset = ((y * image.Width) + x) * 4;
            return new[]
            {
                image.Pixels[offset],
                image.Pixels[offset + 1],
                image.Pixels[offset + 2],
                image.Pixels[offset + 3]
            };
        }

        /// <summary>两个颜色每通道差值都在容差以内算接近。</summary>
        private static bool Close(byte[] left, byte[] right, int tolerance)
        {
            return Math.Abs(left[0] - right[0]) <= tolerance
                && Math.Abs(left[1] - right[1]) <= tolerance
                && Math.Abs(left[2] - right[2]) <= tolerance;
        }
    }
}
