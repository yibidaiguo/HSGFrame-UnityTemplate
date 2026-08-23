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

        /// <summary>
        /// 漫延抠背景时的容差：与背景色每通道差值都在这个数以内算背景。
        ///
        /// **24 太松，真炸过**：白底上的白衣白发离纯白也就十几，一路被当成背景灌进去，
        /// 拆出来的立绘身上全是破洞。收到 10——宁可留一圈没抠净的边（看得见、能补），
        /// 也不许在主体上打洞（补不回来）。
        /// </summary>
        private const int FloodTolerance = 10;


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
                // **不许硬拉**：直接缩到目标宽高，长宽比一变内容就变形——
                // 一张 16:9 的界面图塞进 9:16 的画布，人物会被抻成竹竿。
                // 比例不同时等比缩放放进画布、四周补透明（UI 本来就要透明底，补边不伤内容）。
                var sameAspect = Math.Abs(
                    ((double)image.Width / image.Height) - ((double)targetWidth / targetHeight)) < 0.01;

                if (sameAspect)
                {
                    notes.Add($"尺寸 {image.Width}×{image.Height} 缩到 {targetWidth}×{targetHeight}");
                    image = Resize(image, targetWidth, targetHeight);
                }
                else
                {
                    notes.Add($"尺寸 {image.Width}×{image.Height} 与规格 {targetWidth}×{targetHeight} 比例不同，"
                        + "等比缩放后补透明边（没有硬拉变形）");
                    image = ResizeContain(image, targetWidth, targetHeight);
                }

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
        /// 等比缩放放进目标画布，四周补透明（contain）。
        /// **比例不同时用它，不用硬拉**：硬拉会把画面里的东西抻变形，
        /// 而补出来的透明边在 UI 里本来就是空的，不伤内容。
        /// </summary>
        /// <param name="source">原图。</param>
        /// <param name="width">目标宽。</param>
        /// <param name="height">目标高。</param>
        public static PngImage ResizeContain(PngImage source, int width, int height)
        {
            var scale = Math.Min((double)width / source.Width, (double)height / source.Height);
            var innerWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            var innerHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
            var inner = Resize(source, innerWidth, innerHeight);

            var pixels = new byte[width * height * 4];
            var offsetX = (width - innerWidth) / 2;
            var offsetY = (height - innerHeight) / 2;

            for (var y = 0; y < innerHeight; y++)
            {
                var targetY = y + offsetY;
                if (targetY < 0 || targetY >= height)
                {
                    continue;
                }

                for (var x = 0; x < innerWidth; x++)
                {
                    var targetX = x + offsetX;
                    if (targetX < 0 || targetX >= width)
                    {
                        continue;
                    }

                    var from = ((y * innerWidth) + x) * 4;
                    var to = ((targetY * width) + targetX) * 4;
                    pixels[to] = inner.Pixels[from];
                    pixels[to + 1] = inner.Pixels[from + 1];
                    pixels[to + 2] = inner.Pixels[from + 2];
                    pixels[to + 3] = inner.Pixels[from + 3];
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
            var total = image.Width * image.Height;

            // **先算，后决定**：容差减半再灌一遍，两次结果差多少。
            // 背景真跟主体分得开时，容差松一点紧一点都是灌到同一条边界上，两次几乎一样；
            // 而白底白衣那种「主体本身就是背景色」的图，容差稍一松就顺着衣服一路灌进去，
            // 两次差出一大截。这个差值就是判据——它拦的正是「拆出来一身洞」那一档。
            //
            // 从前那道守门（「有没有任何一个像素跟背景差得够远」）等于没有：
            // 画面里总有深色像素，它永远返回真，一次都没拦下过。
            var tight = FloodFill(image, background, FloodTolerance / 2, out _);
            var loose = FloodFill(image, background, FloodTolerance, out var pixels);

            if (loose > tight * StabilityFactor + (total * StabilitySlack))
            {
                note = "主体本身就带着背景那个色（白底白衣那种），漫延会顺着它灌进主体、拆出一身洞，没敢抠透明——"
                    + "这一张得让模型直接出透明底，本地抠不干净";
                return null;
            }

            if (loose == 0)
            {
                note = "四角虽同色，但从边上一个像素都灌不动，没什么可抠的";
                return null;
            }

            // 几乎整张都灌掉了，说明这张图通体就是背景色——抠完什么都不剩。
            // 稳定性那道判据拦不住这种：紧容差与松容差都是 100%，两次一模一样，看着最"稳"。
            if (loose >= total * FullyBackgroundRatio)
            {
                note = "整张图通体都是背景那个色，抠完什么都不剩，没敢动";
                return null;
            }

            var ratio = (double)loose / total;
            note = $"背景抠成透明（清掉 {ratio:P0} 的像素）";
            return new PngImage(image.Width, image.Height, pixels);
        }

        /// <summary>
        /// 容差放大到这个倍数时，清掉的面积还没超出「紧容差那次 × 本系数 + 一点余量」，
        /// 才认为背景与主体分得开。1.25 是留给抗锯齿边缘的——边缘那一圈半透明像素
        /// 本来就会随容差多吃掉一点，不该被当成「灌进主体」。
        /// </summary>
        private const double StabilityFactor = 1.25;

        /// <summary>灌掉这个比例以上就认为「整张都是背景」——抠完什么都不剩，不如不抠。</summary>
        private const double FullyBackgroundRatio = 0.98;

        /// <summary>稳定性判据的绝对余量，按全图面积的比例给。小图上一圈边缘占比不小，光靠倍数会误杀。</summary>
        private const double StabilitySlack = 0.02;

        /// <summary>
        /// 从四条边灌一次背景，返回清掉多少个像素。
        /// </summary>
        /// <param name="image">原图。</param>
        /// <param name="background">背景色。</param>
        /// <param name="tolerance">容差。</param>
        /// <param name="pixels">灌完的像素缓冲（原图的拷贝，背景处 alpha 置 0）。</param>
        private static int FloodFill(PngImage image, byte[] background, int tolerance, out byte[] pixels)
        {
            pixels = new byte[image.Width * image.Height * 4];
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = image.Pixels[index];
            }

            var visited = new bool[image.Width * image.Height];
            var queue = new Queue<int>();
            EnqueueBorder(image, queue, visited, background, tolerance);

            var cleared = 0;
            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % image.Width;
                var y = index / image.Width;
                pixels[(index * 4) + 3] = 0;
                cleared++;

                TryEnqueue(image, queue, visited, background, x - 1, y, tolerance);
                TryEnqueue(image, queue, visited, background, x + 1, y, tolerance);
                TryEnqueue(image, queue, visited, background, x, y - 1, tolerance);
                TryEnqueue(image, queue, visited, background, x, y + 1, tolerance);
            }

            return cleared;
        }

        /// <summary>把四条边上属于背景色的像素塞进队列当种子。</summary>
        private static void EnqueueBorder(
            PngImage image, Queue<int> queue, bool[] visited, byte[] background, int tolerance)
        {
            for (var x = 0; x < image.Width; x++)
            {
                TryEnqueue(image, queue, visited, background, x, 0, tolerance);
                TryEnqueue(image, queue, visited, background, x, image.Height - 1, tolerance);
            }

            for (var y = 0; y < image.Height; y++)
            {
                TryEnqueue(image, queue, visited, background, 0, y, tolerance);
                TryEnqueue(image, queue, visited, background, image.Width - 1, y, tolerance);
            }
        }

        /// <summary>坐标合法、没访问过、且颜色接近背景时入队。</summary>
        private static void TryEnqueue(
            PngImage image, Queue<int> queue, bool[] visited, byte[] background, int x, int y, int tolerance)
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

            if (!Close(PixelAt(image, x, y), background, tolerance))
            {
                return;
            }

            visited[index] = true;
            queue.Enqueue(index);
        }

        /// <summary>
        /// 把四周全透明的边裁掉，只留下真正有内容的那一块。
        ///
        /// 为什么这一步不可少：下游只出它自己那几档尺寸（1024 见方、1536×1024…），
        /// 而 UI 元素什么长宽比都有。一条 1565×54 的长条，模型会在 1536×1024 的画布上
        /// 把它画在中间，四周全是透明——不裁就直接按 1565×54 缩回去，
        /// 那条长条会被压成几个像素高，剩下的全是透明边，等于这张图废了。
        /// 裁到内容边界之后再缩，元素才是满的。
        ///
        /// 整张都透明时**原样返回**：那说明模型什么都没画出来，
        /// 裁成 0×0 只会让后面每一步都崩在一个跟真因无关的地方。
        /// </summary>
        /// <param name="image">要裁的图。</param>
        /// <param name="note">裁了多少，一句人话；没裁时为空串。</param>
        public static PngImage TrimTransparentBorder(PngImage image, out string note)
        {
            note = "";
            if (image == null || image.Width <= 0 || image.Height <= 0)
            {
                return image;
            }

            var left = image.Width;
            var top = image.Height;
            var right = -1;
            var bottom = -1;

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    if (image.Pixels[(((y * image.Width) + x) * 4) + 3] <= TrimAlphaThreshold)
                    {
                        continue;
                    }

                    if (x < left) { left = x; }
                    if (x > right) { right = x; }
                    if (y < top) { top = y; }
                    if (y > bottom) { bottom = y; }
                }
            }

            if (right < left || bottom < top)
            {
                note = "整张图都是透明的，没裁——模型这一张什么都没画出来";
                return image;
            }

            var width = right - left + 1;
            var height = bottom - top + 1;
            if (width == image.Width && height == image.Height)
            {
                return image;
            }

            var pixels = new byte[width * height * 4];
            for (var row = 0; row < height; row++)
            {
                var sourceOffset = ((((top + row) * image.Width) + left) * 4);
                var targetOffset = row * width * 4;
                for (var index = 0; index < width * 4; index++)
                {
                    pixels[targetOffset + index] = image.Pixels[sourceOffset + index];
                }
            }

            note = $"裁掉透明边：{image.Width}×{image.Height} → {width}×{height}";
            return new PngImage(width, height, pixels);
        }

        /// <summary>alpha 到这个值以内都算透明。留一点余量给抗锯齿边缘那圈接近全透的像素。</summary>
        private const byte TrimAlphaThreshold = 8;

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
