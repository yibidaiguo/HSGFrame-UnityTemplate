using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 解码出来的一张 PNG：宽高与 RGBA8 像素。
    /// 像素按行优先、从上到下排列，长度 = 宽 × 高 × 4。
    /// </summary>
    public sealed class PngImage
    {
        /// <summary>构造一张解码出来的图。</summary>
        /// <param name="width">宽，像素。</param>
        /// <param name="height">高，像素。</param>
        /// <param name="pixels">RGBA8 像素，长度 = 宽 × 高 × 4。</param>
        public PngImage(int width, int height, IReadOnlyList<byte> pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        /// <summary>宽，像素。</summary>
        public int Width { get; }

        /// <summary>高，像素。</summary>
        public int Height { get; }

        /// <summary>RGBA8 像素，长度 = 宽 × 高 × 4，行优先、从上到下。</summary>
        public IReadOnlyList<byte> Pixels { get; }
    }

    /// <summary>解码结果：成功给图，失败给不含猜测的原因。</summary>
    public sealed class PngDecodeResult
    {
        /// <summary>构造一次解码结果。</summary>
        /// <param name="succeeded">成没成。</param>
        /// <param name="image">解码出来的图；失败时为 null。</param>
        /// <param name="failureReason">失败原因；成功时为空串。</param>
        public PngDecodeResult(bool succeeded, PngImage image, string failureReason)
        {
            Succeeded = succeeded;
            Image = image;
            FailureReason = failureReason ?? "";
        }

        /// <summary>成没成。</summary>
        public bool Succeeded { get; }

        /// <summary>解码出来的图；失败时为 null。</summary>
        public PngImage Image { get; }

        /// <summary>失败原因；成功时为空串。</summary>
        public string FailureReason { get; }
    }

    /// <summary>
    /// PNG 解码。
    ///
    /// **内部走 SixLabors.ImageSharp，不自己实现**。
    /// 从前这里是一份手写的解码器（zlib inflate + 五种行滤波 + 隔行扫描），
    /// 七百多行。它能跑，但那类代码是「写得出来但养不起」的典型：
    /// 出一个位错的症状是某张图在某台机器上花掉，而查起来要从字节流读起；
    /// 而且它只认自己实现过的那几种子格式，遇到没实现的一律报「不支持」——
    /// 那句话对拿着一张正常 PNG 的人毫无意义。
    ///
    /// 公开面一个字没改（<see cref="PngImage"/> / <see cref="PngDecodeResult"/> /
    /// <see cref="DecodeFile"/> / <see cref="Decode"/>），所以调用方一行都不用动。
    /// **失败仍旧带原因、绝不抛到调用方那边**：这一条比谁来解码更重要——
    /// 上游那些地方（离风格报告、拆图、资产归一）都指望「读不动就说读不动」。
    /// </summary>
    public static class PngDecoder
    {
        /// <summary>
        /// 从文件解一张 PNG。文件不存在、读不动、不是合法 PNG，都带原因返回，不抛。
        /// </summary>
        /// <param name="filePath">PNG 文件路径。</param>
        public static PngDecodeResult DecodeFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Failure("没给文件路径");
            }

            if (!File.Exists(filePath))
            {
                return Failure($"文件不存在：{filePath}");
            }

            try
            {
                return Decode(File.ReadAllBytes(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Failure($"读不出来：{filePath}（{exception.Message}）");
            }
        }

        /// <summary>
        /// 从字节流解一张 PNG，统一转成 RGBA8。
        /// </summary>
        /// <param name="bytes">PNG 字节流。</param>
        public static PngDecodeResult Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return Failure("字节流是空的");
            }

            // **先做一道廉价的结构检查再交给库**。理由是真踩出来的，而且是两条：
            //
            // 一、一个声称长度 0x7FFFFFFF（2 GB）而实际只有 8 字节的 IDAT 块，
            //     交给库会让它按声称的长度去读——整个进程卡死，不是报错，是卡死。
            // 二、隔行（Adam7）且有空扫描道的小图，会让 ImageSharp 2.1.13 死循环。
            //
            // 这条链解的 PNG 来自下游与人上传的附件，不能假设它们都是好的。
            // 这道检查不解码、只走块头与 IHDR 那 13 个字节，几微秒。
            if (!TryValidateChunkLayout(bytes, out var layoutReason))
            {
                return Failure(layoutReason);
            }

            try
            {
                using var image = Image.Load<Rgba32>(bytes);
                var pixels = new byte[(long)image.Width * image.Height * 4];
                image.CopyPixelDataTo(pixels);
                return new PngDecodeResult(true, new PngImage(image.Width, image.Height, pixels), "");
            }
            catch (Exception exception) when (exception is UnknownImageFormatException || exception is InvalidImageContentException || exception is NotSupportedException || exception is ArgumentException || exception is OverflowException)
            {
                // 解不动就**带原因返回**，不抛：调用方（离风格、拆图、资产归一）
                // 全都指望「读不动就说读不动」，抛出去会把一次读图失败变成一整条链崩掉。
                return Failure($"不是能解的 PNG：{exception.Message}");
            }
        }

        /// <summary>PNG 文件签名。</summary>
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// 隔行 PNG 至少要有这么宽/这么高，才轮得到解码器去解。
        ///
        /// **这个 5 不是拍脑袋的**。Adam7 把图拆成七道扫描：
        /// 第 2 道从 x=4 起步、第 3 道从 y=4 起步，所以宽或高不足 5 时至少有一道是空的。
        /// 实测 ImageSharp 2.1.13 遇到「有空道」的隔行图会**死循环**——
        /// 不是抛异常，是整个进程停在那儿（1×1 隔行图、字节完全正确，一样卡死）。
        /// 宽高都 ≥ 5 时七道全非空，实测全部正常。
        ///
        /// 所以这里宁可拒掉一张 4×4 的隔行图，也不能让一条链挂死：
        /// 拒了会说原因，挂死连日志都没有。真出现小隔行图再谈——
        /// 隔行本来就是给大图做渐进显示的，小图用它本身就不正常。
        /// </summary>
        private const int MinimumInterlacedSide = 5;

        /// <summary>
        /// 只走块头，检查每个块声称的长度装不装得下，外加 IHDR 里那几个会把解码器带沟里的字段。
        /// **不校验 CRC、不解压、不看像素**——那些是库的事，这里只挡两种会让库
        /// 「卡死而不是报错」的输入：声称长度是假的，和有空扫描道的隔行图。
        /// </summary>
        /// <param name="bytes">PNG 字节流。</param>
        /// <param name="reason">不合法时的原因；合法时为空串。</param>
        private static bool TryValidateChunkLayout(byte[] bytes, out string reason)
        {
            reason = "";
            if (bytes.Length < Signature.Length)
            {
                reason = "字节数还不够一个 PNG 签名";
                return false;
            }

            for (var index = 0; index < Signature.Length; index++)
            {
                if (bytes[index] != Signature[index])
                {
                    reason = "PNG 签名不对";
                    return false;
                }
            }

            var offset = Signature.Length;
            while (offset + 8 <= bytes.Length)
            {
                var length = (bytes[offset] << 24) | (bytes[offset + 1] << 16)
                    | (bytes[offset + 2] << 8) | bytes[offset + 3];
                if (length < 0)
                {
                    reason = "块长度是负数（高位被置了 1），这不是合法 PNG";
                    return false;
                }

                // 长度 + 类型 4 字节 + 数据 + CRC 4 字节，必须都还在流里。
                var needed = (long)offset + 8 + length + 4;
                if (needed > bytes.Length)
                {
                    reason = $"有一个块声称长度 {length}，超过剩余字节（还剩 {bytes.Length - offset - 8}）";
                    return false;
                }

                // IHDR 一定是第一个块，13 字节：宽 4 + 高 4 + 位深 1 + 色型 1 + 压缩 1 + 滤波 1 + 隔行 1。
                var isHeader = bytes[offset + 4] == 'I' && bytes[offset + 5] == 'H'
                    && bytes[offset + 6] == 'D' && bytes[offset + 7] == 'R';
                if (isHeader && length == 13)
                {
                    var data = offset + 8;
                    var width = (bytes[data] << 24) | (bytes[data + 1] << 16)
                        | (bytes[data + 2] << 8) | bytes[data + 3];
                    var height = (bytes[data + 4] << 24) | (bytes[data + 5] << 16)
                        | (bytes[data + 6] << 8) | bytes[data + 7];
                    var interlaced = bytes[data + 12] == 1;

                    if (interlaced && (width < MinimumInterlacedSide || height < MinimumInterlacedSide))
                    {
                        reason = $"隔行 PNG 小于 {MinimumInterlacedSide}×{MinimumInterlacedSide}（这张 {width}×{height}）："
                            + "Adam7 会有空扫描道，当前解码器遇到这种图会死循环，所以在这里拒掉";
                        return false;
                    }
                }

                offset += 8 + length + 4;
            }

            return true;
        }

        /// <summary>失败结果。</summary>
        private static PngDecodeResult Failure(string reason)
        {
            return new PngDecodeResult(false, null, reason);
        }
    }

    /// <summary>
    /// PNG 编码：把 RGBA8 像素写成 PNG。
    ///
    /// 内部同样走 ImageSharp（理由见 <see cref="PngDecoder"/>）。
    /// **压缩级别钉死**：同一份像素每次要编出同样的字节，
    /// 生成物幂等门禁比的就是文件内容——编码器每次给不同字节的话，
    /// 那道门禁会在什么都没改时变红，而红的原因跟改动毫无关系。
    /// </summary>
    public static class PngEncoder
    {
        /// <summary>
        /// 编码器设置：固定色彩类型、位深、压缩级别与过滤方式。
        /// **这四项一项都不能不写**——留给库的默认值意味着升一次库就可能换一套字节，
        /// 而生成物幂等门禁比的正是文件内容，那时它会在什么都没改时变红。
        /// </summary>
        private static readonly SixLabors.ImageSharp.Formats.Png.PngEncoder Encoder =
            new SixLabors.ImageSharp.Formats.Png.PngEncoder
            {
                ColorType = PngColorType.RgbWithAlpha,
                BitDepth = PngBitDepth.Bit8,
                CompressionLevel = PngCompressionLevel.DefaultCompression,
                FilterMethod = PngFilterMethod.Adaptive
            };

        /// <summary>
        /// 把一张图编成 PNG 字节流。
        /// </summary>
        /// <param name="image">要编码的图；像素长度必须是 宽×高×4。</param>
        /// <exception cref="ArgumentNullException">image 为 null 时抛。</exception>
        /// <exception cref="ArgumentException">宽高非正、或像素长度对不上时抛。</exception>
        public static byte[] Encode(PngImage image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            if (image.Width < 1 || image.Height < 1)
            {
                throw new ArgumentException($"宽高必须至少为 1，实际 {image.Width}×{image.Height}");
            }

            if (image.Pixels == null || image.Pixels.Count != (long)image.Width * image.Height * 4)
            {
                throw new ArgumentException("像素长度必须是宽×高×4");
            }

            var buffer = image.Pixels as byte[];
            if (buffer == null)
            {
                buffer = new byte[image.Pixels.Count];
                for (var index = 0; index < buffer.Length; index++)
                {
                    buffer[index] = image.Pixels[index];
                }
            }

            using var loaded = Image.LoadPixelData<Rgba32>(buffer, image.Width, image.Height);
            using var stream = new MemoryStream();
            loaded.Save(stream, Encoder);
            return stream.ToArray();
        }

        /// <summary>
        /// 把一张图编码并写到文件；目录不存在时建出来。写不下去时带原因返回 false，不抛。
        /// </summary>
        /// <param name="image">要编码的图。</param>
        /// <param name="filePath">落点。</param>
        /// <param name="reason">失败原因；成功时为空串。</param>
        public static bool EncodeToFile(PngImage image, string filePath, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(filePath))
            {
                reason = "没给落点";
                return false;
            }

            try
            {
                var bytes = Encode(image);
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(filePath, bytes);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            {
                reason = exception.Message;
                return false;
            }
        }
    }
}
