using System;
using System.IO;
using SkiaSharp;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 把 SVG 转成 PNG。
    ///
    /// **为什么要这一步**：布局图落的是 SVG——它是文本，进 git 能 diff 出「哪个块动了」，
    /// 而 PNG 只能看出「变了」。但下游要位图：飞书文档的图片块塞不进 SVG。
    /// 两份都要，各有各的用处。
    ///
    /// **选「转一道」而不是「再画一遍」**：自己拿 SkiaSharp 重画一份 PNG 的话，
    /// 仓库里就有两套渲染器，改了一处忘了另一处，两张图迟早讲成两件事——
    /// 而人对着 PNG 确认功能位、程序读的却是 SVG 那一版，这种不一致最难查。
    /// 从同一份 SVG 转出来，一致是结构保证，不是纪律。
    /// </summary>
    public static class SvgRasterizer
    {
        /// <summary>
        /// 把一段 SVG 文本转成 PNG 字节。
        ///
        /// 转不动**不抛异常**，回 null 加一句原因：布局图是给人看的辅助产物，
        /// 它渲不出来不该让整条「出功能图」失败——规格本身还是好的。
        /// </summary>
        /// <param name="svgText">SVG 文本。</param>
        /// <param name="reason">转不动的原因；成功时为空串。</param>
        public static byte[] ToPng(string svgText, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(svgText))
            {
                reason = "SVG 是空的";
                return null;
            }

            try
            {
                using var svg = new Svg.Skia.SKSvg();
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgText));
                var picture = svg.Load(stream);

                if (picture == null)
                {
                    reason = "SVG 读不出画面——多半是它本身不合法";
                    return null;
                }

                var bounds = picture.CullRect;
                var width = (int)Math.Ceiling(bounds.Width);
                var height = (int)Math.Ceiling(bounds.Height);
                if (width <= 0 || height <= 0)
                {
                    reason = $"SVG 的画面尺寸算出来是 {width}×{height}，转不成图";
                    return null;
                }

                using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(bitmap))
                {
                    // **先铺白底**：布局图本来就是白底黑框，不铺的话透明区在飞书里
                    // 会跟着对方的主题变色，深色模式下白块与白字糊成一片。
                    canvas.Clear(SKColors.White);
                    canvas.DrawPicture(picture);
                }

                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                if (data == null)
                {
                    reason = "编码 PNG 失败";
                    return null;
                }

                return data.ToArray();
            }
            catch (Exception exception) when (exception is IOException
                || exception is InvalidOperationException
                || exception is NotSupportedException
                || exception is System.Xml.XmlException
                || exception is DllNotFoundException)
            {
                // XmlException 单列在这儿是有来由的：SVG 不合法时 Svg.Skia 抛的是它，
                // 而不是回一个空画面。不网住的话，一份坏 SVG 会把整条「出功能图」掀翻，
                // 而那句 "Data at the root level is invalid" 指不到「布局图没渲出来」上。
                // DllNotFoundException 单列出来是有原因的：SkiaSharp 要带原生库，
                // 缺了它的报错跟「SVG 不合法」长得完全不一样，别混成一句。
                reason = exception is System.Xml.XmlException
                    ? "SVG 本身不合法：" + exception.Message
                    : exception is DllNotFoundException
                    ? "SkiaSharp 的原生库没找到（" + exception.Message + "）——多半是这台机器上的运行时没带全"
                    : "SVG 转 PNG 失败：" + exception.Message;
                return null;
            }
        }
    }
}
