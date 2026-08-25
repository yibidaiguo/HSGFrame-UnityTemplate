using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次预览合成的结果：落点、几帧、多大、有没有问题。</summary>
    /// <param name="FilePath">GIF 落点；失败时为空串。</param>
    /// <param name="FrameCount">合进去几帧。</param>
    /// <param name="ByteCount">文件多大。</param>
    /// <param name="Width">画面宽。</param>
    /// <param name="Height">画面高。</param>
    /// <param name="FailureReason">失败原因；成功为空串。</param>
    /// <param name="Notes">过程里要说给人听的话（缩了没有、丢了没有）。</param>
    public sealed record AnimatedPreviewResult(
        string FilePath,
        int FrameCount,
        long ByteCount,
        int Width,
        int Height,
        string FailureReason,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// 把一串帧合成一张会动的 GIF——**这条链的「给人看一眼」那一步**。
    ///
    /// 三种来路最后都汇到这里，所以只有这一个合成器：
    /// - 2D 帧动画：出好的帧直接合；
    /// - 人物帧动画：同上；
    /// - 3D 动画：先按「转台」port 挑的下游渲成一圈帧（有贴图就带贴图，没有就是白模），再合。
    ///
    /// **这里不写具体是哪个下游的名字**，不是因为不知道，是因为下游边界门禁不许——
    /// driver 名只能是运行时参数（子文档 05）。想知道现在挂的是谁，看 Bridges/ 下各自的 driver.json。
    ///
    /// 为什么是 GIF 而不是视频：飞书消息里图片是**直接就地播**的，视频要点开。
    /// 这一步的全部意义是让人扫一眼就知道方向对不对，多一次点击就少一半人会看。
    ///
    /// 编解码走 ImageSharp，不自己写：GIF 的 LZW 与调色板量化属于「写得出来但养不起」，
    /// 出一个位错的症状是某台机器上某张图花掉，而查起来要从字节流读起。
    /// </summary>
    public static class AnimatedPreview
    {
        /// <summary>缺省帧率。</summary>
        public const int DefaultFrameRate = 12;

        /// <summary>长边上限：超过就等比缩。飞书里就地播的图不需要大，太大反而加载慢。</summary>
        public const int MaximumSide = 640;

        /// <summary>最多合多少帧。转台几百帧的话 GIF 会大到发不出去。</summary>
        public const int MaximumFrameCount = 120;

        /// <summary>
        /// 把一个目录里的 PNG 帧合成 GIF。
        ///
        /// **按文件名序数序排**，不按修改时间：帧常常是并发写出来的，
        /// 时间戳的先后跟播放顺序毫无关系（按时间排出来的走路动画左右脚是乱的）。
        /// </summary>
        /// <param name="frameDirectory">帧所在目录。</param>
        /// <param name="outputPath">GIF 落点。</param>
        /// <param name="frameRate">帧率，帧每秒。</param>
        public static AnimatedPreviewResult ComposeFromDirectory(
            string frameDirectory, string outputPath, int frameRate)
        {
            if (!Directory.Exists(frameDirectory))
            {
                return Failure($"帧目录不存在：{frameDirectory}");
            }

            var files = Directory.GetFiles(frameDirectory, "*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToList();

            return Compose(files, outputPath, frameRate);
        }

        /// <summary>
        /// 把指定的几张图合成 GIF。
        /// </summary>
        /// <param name="framePaths">逐帧路径，**顺序就是播放顺序**。</param>
        /// <param name="outputPath">GIF 落点。</param>
        /// <param name="frameRate">帧率，帧每秒。</param>
        public static AnimatedPreviewResult Compose(
            IReadOnlyList<string> framePaths, string outputPath, int frameRate)
        {
            var notes = new List<string>();
            if (framePaths == null || framePaths.Count == 0)
            {
                return Failure("一帧都没有，合不出预览图");
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return Failure("没给落点");
            }

            var effectiveRate = frameRate > 0 ? frameRate : DefaultFrameRate;

            // 帧太多就**等间隔抽**，不许直接砍掉尾巴：砍尾巴会让动画少半截，
            // 而人看不出是被砍了，只会以为动作本身没做完。
            var selected = framePaths.ToList();
            if (selected.Count > MaximumFrameCount)
            {
                var step = (double)selected.Count / MaximumFrameCount;
                selected = Enumerable.Range(0, MaximumFrameCount)
                    .Select(index => framePaths[(int)Math.Min(framePaths.Count - 1, Math.Floor(index * step))])
                    .ToList();
                notes.Add($"帧太多（{framePaths.Count} 张），等间隔抽成 {selected.Count} 张——动作还是整段，只是采样稀了");
            }

            try
            {
                using var first = Image.Load<Rgba32>(selected[0]);
                var (width, height, scaled) = TargetSize(first.Width, first.Height);
                if (scaled)
                {
                    notes.Add($"长边超过 {MaximumSide}，等比缩到 {width}×{height}");
                }

                using var animation = new Image<Rgba32>(width, height);

                // 每帧停留多久：GIF 的单位是 1/100 秒，**至少 2**——
                // 填 0 或 1 时绝大多数客户端会当成 10（一条流传很久的兼容行为），
                // 于是本想要快的动画播出来是慢的。
                var delay = Math.Max(2, (int)Math.Round(100.0 / effectiveRate));
                var dropped = 0;

                for (var index = 0; index < selected.Count; index++)
                {
                    Image<Rgba32> frame;
                    try
                    {
                        frame = Image.Load<Rgba32>(selected[index]);
                    }
                    catch (Exception exception) when (exception is UnknownImageFormatException || exception is InvalidImageContentException || exception is IOException)
                    {
                        // 单帧读不动只丢这一帧并**记一笔**，不判整段失败；
                        // 但绝不静默——丢了几帧而不说，人会以为动画本来就是卡的。
                        dropped++;
                        continue;
                    }

                    using (frame)
                    {
                        if (frame.Width != width || frame.Height != height)
                        {
                            frame.Mutate(context => context.Resize(width, height));
                        }

                        var metadata = frame.Frames.RootFrame.Metadata.GetGifMetadata();
                        metadata.FrameDelay = delay;

                        // 处置方式「还原成背景」：透明帧必须这样，
                        // 否则上一帧的像素留在底下，转一圈叠成一团糊影。
                        metadata.DisposalMethod = GifDisposalMethod.RestoreToBackground;
                        animation.Frames.AddFrame(frame.Frames.RootFrame);
                    }
                }

                if (dropped > 0)
                {
                    notes.Add($"有 {dropped} 帧读不动，已跳过");
                }

                // 新建 Image 时自带一张空白根帧，合完要把它去掉，否则开头闪一下白。
                if (animation.Frames.Count > 1)
                {
                    animation.Frames.RemoveFrame(0);
                }

                animation.Metadata.GetGifMetadata().RepeatCount = 0; // 0 = 无限循环

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                animation.SaveAsGif(outputPath);
                var byteCount = new FileInfo(outputPath).Length;
                return new AnimatedPreviewResult(
                    outputPath, animation.Frames.Count, byteCount, width, height, "", notes);
            }
            catch (Exception exception) when (exception is UnknownImageFormatException || exception is InvalidImageContentException || exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                return Failure("合预览图失败：" + exception.Message, notes);
            }
        }

        /// <summary>算目标尺寸：长边超过上限就等比缩，否则原样。</summary>
        private static (int Width, int Height, bool Scaled) TargetSize(int width, int height)
        {
            var longest = Math.Max(width, height);
            if (longest <= MaximumSide)
            {
                return (width, height, false);
            }

            var ratio = (double)MaximumSide / longest;
            return (Math.Max(1, (int)Math.Round(width * ratio)), Math.Max(1, (int)Math.Round(height * ratio)), true);
        }

        /// <summary>失败结果。</summary>
        private static AnimatedPreviewResult Failure(string reason, IReadOnlyList<string> notes = null)
        {
            return new AnimatedPreviewResult(
                "", 0, 0, 0, 0, reason, notes ?? Array.Empty<string>());
        }
    }
}
