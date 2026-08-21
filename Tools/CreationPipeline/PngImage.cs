using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

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
        /// <summary>构造一个解码结果。</summary>
        /// <param name="succeeded">解码成功没有。</param>
        /// <param name="image">解码出来的图；失败时为 null。</param>
        /// <param name="failureReason">失败原因；成功时为空串。</param>
        public PngDecodeResult(bool succeeded, PngImage image, string failureReason)
        {
            Succeeded = succeeded;
            Image = image;
            FailureReason = failureReason ?? "";
        }

        /// <summary>解码成功没有。</summary>
        public bool Succeeded { get; }

        /// <summary>解码出来的图；失败时为 null。</summary>
        public PngImage Image { get; }

        /// <summary>失败原因，中文；成功时为空串。</summary>
        public string FailureReason { get; }
    }

    /// <summary>
    /// 最小 PNG 解码器：只吃 8/16 位非隔行的五种颜色类型（灰度/真彩/调色板/灰度+alpha/真彩+alpha），
    /// 其余形态如实拒绝，绝不硬解出一张错的图（决策 23）。
    /// 位深 1/2/4 只对调色板支持，按位拆包；位深 16 取每通道高字节降到 8 位。
    /// 本实现不校验 CRC——拒绝坏图也不比校验更安全，别以为校验过了。
    /// </summary>
    public static class PngDecoder
    {
        /// <summary>PNG 文件签名：89 50 4E 47 0D 0A 1A 0A。</summary>
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// 从文件解码一张 PNG。文件不存在或读不动时失败，原因照抄异常消息。
        /// </summary>
        /// <param name="filePath">PNG 文件路径。</param>
        public static PngDecodeResult DecodeFile(string filePath)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception exception) when (exception is FileNotFoundException
                || exception is DirectoryNotFoundException
                || exception is IOException
                || exception is UnauthorizedAccessException)
            {
                return Fail(exception.Message);
            }

            return Decode(bytes);
        }

        /// <summary>
        /// 从字节解码一张 PNG。
        /// </summary>
        /// <param name="bytes">PNG 文件字节。</param>
        public static PngDecodeResult Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < Signature.Length || !StartsWithSignature(bytes))
            {
                return Fail("PNG 签名不对，不是一张 PNG 文件");
            }

            // 收集关键块：IHDR 必须在第一个；IDAT 可能多个，按出现顺序拼起来再 inflate。
            byte[] ihdr = null;
            var palette = Array.Empty<byte>();
            var transparency = Array.Empty<byte>();
            var idatParts = new List<byte[]>();

            var offset = Signature.Length;
            while (offset < bytes.Length)
            {
                if (offset + 12 > bytes.Length)
                {
                    return Fail("PNG 块数据不完整，块头越界");
                }

                var length = ReadUInt32BE(bytes, offset);
                if ((ulong)offset + 8UL + (ulong)length + 4UL > (ulong)bytes.Length)
                {
                    return Fail($"块「{ReadChunkType(bytes, offset)}」声称长度 {length}，超出剩余字节，文件损坏");
                }

                var chunkType = ReadChunkType(bytes, offset);
                var dataStart = offset + 8;
                var dataLength = (int)length;
                var dataEnd = dataStart + dataLength;

                if (chunkType == "IHDR")
                {
                    if (ihdr != null)
                    {
                        return Fail("出现两个 IHDR 块，文件损坏");
                    }

                    ihdr = new byte[dataLength];
                    Array.Copy(bytes, dataStart, ihdr, 0, dataLength);
                }
                else if (chunkType == "PLTE")
                {
                    palette = new byte[dataLength];
                    Array.Copy(bytes, dataStart, palette, 0, dataLength);
                }
                else if (chunkType == "tRNS")
                {
                    transparency = new byte[dataLength];
                    Array.Copy(bytes, dataStart, transparency, 0, dataLength);
                }
                else if (chunkType == "IDAT")
                {
                    var part = new byte[dataLength];
                    Array.Copy(bytes, dataStart, part, 0, dataLength);
                    idatParts.Add(part);
                }
                else if (chunkType == "IEND")
                {
                    break;
                }

                offset = dataEnd + 4;
            }

            if (ihdr == null)
            {
                return Fail("缺 IHDR 块，无法得知尺寸与颜色形态");
            }

            return DecodeCore(ihdr, palette, transparency, idatParts);
        }

        /// <summary>
        /// 在 IHDR / PLTE / tRNS / IDAT 已收集齐的前提下做解码：
        /// 校验 IHDR 字段、拼 IDAT、inflate、逐行反滤波、统一转 RGBA8。
        /// </summary>
        private static PngDecodeResult DecodeCore(
            byte[] ihdr,
            byte[] palette,
            byte[] transparency,
            List<byte[]> idatParts)
        {
            if (ihdr.Length != 13)
            {
                return Fail($"IHDR 块长度应是 13 字节，实际 {ihdr.Length}，文件损坏");
            }

            var width = ReadInt32BE(ihdr, 0);
            var height = ReadInt32BE(ihdr, 4);
            var bitDepth = ihdr[8];
            var colorType = ihdr[9];
            var compressionMethod = ihdr[10];
            var filterMethod = ihdr[11];
            var interlace = ihdr[12];

            if (width <= 0 || height <= 0)
            {
                return Fail($"宽高必须是正整数，实际 {width}×{height}，文件损坏");
            }

            if (interlace != 0)
            {
                return Fail("暂不支持 Adam7 隔行 PNG");
            }

            if (compressionMethod != 0)
            {
                return Fail($"压缩法 {compressionMethod} 不是 0，不支持的压缩方式");
            }

            if (filterMethod != 0)
            {
                return Fail($"滤波法 {filterMethod} 不是 0，不支持的滤波方式");
            }

            var channelCount = ChannelCount(colorType);
            if (channelCount < 0)
            {
                return Fail($"颜色类型 {colorType} 不支持（PNG 只定义 0/2/3/4/6 五种）");
            }

            if (!IsSupportedBitDepth(colorType, bitDepth))
            {
                return Fail($"位深 {bitDepth} 与颜色类型 {colorType} 的组合不支持（调色板只吃 1/2/4/8 位，其余只吃 8/16 位）");
            }

            if (colorType == 3 && palette.Length == 0)
            {
                return Fail("颜色类型 3（调色板）缺 PLTE 块");
            }

            if (colorType != 3 && transparency.Length > 0)
            {
                // 灰度/真彩的 tRNS 是 2 字节透明色样本，灰度+alpha/真彩+alpha 规范上不允许 tRNS。
                // 本解码器只支持调色板的逐索引 alpha，其余形态如实拒绝，不把透明信息硬解成不透明。
                return Fail($"颜色类型 {colorType} 的 tRNS 块不支持（只支持调色板的逐索引 alpha）");
            }

            if (idatParts.Count == 0)
            {
                return Fail("缺 IDAT 块，无法解出图像数据");
            }

            // 调色板校验：3 字节一项；tRNS 在类型 3 下是逐索引的 alpha。
            var paletteEntryCount = 0;
            if (colorType == 3)
            {
                if (palette.Length == 0 || palette.Length % 3 != 0)
                {
                    return Fail($"PLTE 块长度应是 3 的倍数且非空，实际 {palette.Length}，文件损坏");
                }

                paletteEntryCount = palette.Length / 3;
                if (transparency.Length > paletteEntryCount)
                {
                    return Fail($"tRNS 块长度 {transparency.Length} 超过调色板条目数 {paletteEntryCount}，文件损坏");
                }
            }

            // 拼 IDAT 再 inflate（多个 IDAT 块按出现顺序拼，这是最常见的坑）。
            byte[] inflated;
            try
            {
                using var combined = new MemoryStream();
                foreach (var part in idatParts)
                {
                    combined.Write(part, 0, part.Length);
                }

                combined.Position = 0;
                using var zlib = new ZLibStream(combined, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                inflated = output.ToArray();
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is IOException)
            {
                return Fail($"图像数据解压失败：{exception.Message}");
            }

            var bitsPerPixel = channelCount * bitDepth;
            var rowBytes = (int)(((long)width * bitsPerPixel + 7) / 8);
            var expectedLength = (long)height * (1L + rowBytes);
            if (inflated.Length != expectedLength)
            {
                return Fail($"解压数据长度 {inflated.Length} 与期望 {expectedLength} 不符，文件损坏");
            }

            var pixelCount = (long)width * height;
            if (pixelCount * 4L > int.MaxValue)
            {
                return Fail($"图像 {width}×{height} 过大，无法在内存里展开成 RGBA8");
            }

            // bytesPerPixel 供滤波器用；位深小于 8 时按 1 算（PNG 规范）。
            var bytesPerPixel = bitDepth < 8 ? 1 : channelCount * (bitDepth / 8);

            var pixels = new byte[(int)(pixelCount * 4L)];
            var previousRow = new byte[rowBytes];
            var rowStart = 0;
            try
            {
                for (var y = 0; y < height; y++)
                {
                    var filterType = inflated[rowStart];
                    if (filterType > 4)
                    {
                        return Fail($"滤波器 {filterType} 不支持（只定义 0–4 五种）");
                    }

                    var currentRow = new byte[rowBytes];
                    UnfilterRow(inflated, rowStart + 1, currentRow, previousRow, rowBytes, filterType, bytesPerPixel);

                    WriteRgbaRow(currentRow, pixels, y, width, bitDepth, colorType, palette, paletteEntryCount, transparency);

                    previousRow = currentRow;
                    rowStart += 1 + rowBytes;
                }
            }
            catch (InvalidDataException exception)
            {
                return Fail(exception.Message);
            }

            return new PngDecodeResult(true, new PngImage(width, height, pixels), "");
        }

        /// <summary>按滤波器类型把滤波后的行还原成原始像素行；上下文的越界样本按 0 处理。</summary>
        private static void UnfilterRow(
            byte[] filtered,
            int filteredStart,
            byte[] currentRow,
            byte[] previousRow,
            int rowBytes,
            byte filterType,
            int bytesPerPixel)
        {
            for (var i = 0; i < rowBytes; i++)
            {
                var filteredValue = filtered[filteredStart + i];
                var left = i >= bytesPerPixel ? currentRow[i - bytesPerPixel] : (byte)0;
                var up = previousRow[i];
                byte predictor;
                switch (filterType)
                {
                    case 0:
                        predictor = 0;
                        break;
                    case 1:
                        predictor = left;
                        break;
                    case 2:
                        predictor = up;
                        break;
                    case 3:
                        predictor = (byte)((left + up) / 2);
                        break;
                    case 4:
                        var upperLeft = i >= bytesPerPixel ? previousRow[i - bytesPerPixel] : (byte)0;
                        predictor = PaethPredictor(left, up, upperLeft);
                        break;
                    default:
                        predictor = 0;
                        break;
                }

                currentRow[i] = (byte)(filteredValue + predictor);
            }
        }

        /// <summary>Paeth 预测器：从三个候选里挑梯度最平缓的那个。</summary>
        private static byte PaethPredictor(byte left, byte up, byte upperLeft)
        {
            var p = left + up - upperLeft;
            var pa = Math.Abs(p - left);
            var pb = Math.Abs(p - up);
            var pc = Math.Abs(p - upperLeft);
            if (pa <= pb && pa <= pc)
            {
                return left;
            }

            if (pb <= pc)
            {
                return up;
            }

            return upperLeft;
        }

        /// <summary>把一行原始像素转成 RGBA8 写进输出缓冲；16 位取高字节，调色板查 PLTE/tRNS。</summary>
        private static void WriteRgbaRow(
            byte[] row,
            byte[] pixels,
            int y,
            int width,
            int bitDepth,
            int colorType,
            byte[] palette,
            int paletteEntryCount,
            byte[] transparency)
        {
            var outputOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                byte red;
                byte green;
                byte blue;
                byte alpha;
                if (colorType == 3)
                {
                    var index = ReadPaletteIndex(row, x, bitDepth);
                    if (index >= paletteEntryCount)
                    {
                        throw new InvalidDataException($"像素索引 {index} 超出调色板条目数 {paletteEntryCount}，文件损坏");
                    }

                    red = palette[index * 3];
                    green = palette[index * 3 + 1];
                    blue = palette[index * 3 + 2];
                    alpha = index < transparency.Length ? transparency[index] : (byte)255;
                }
                else
                {
                    // 非调色板类型位深只有 8/16，每通道占 1 或 2 字节，取每通道开头那一字节。
                    var pixelStart = x * (bitDepth / 8) * ChannelCount(colorType);
                    switch (colorType)
                    {
                        case 0:
                            red = green = blue = ReadSample(row, pixelStart);
                            alpha = 255;
                            break;
                        case 2:
                            red = ReadSample(row, pixelStart);
                            green = ReadSample(row, pixelStart + bitDepth / 8);
                            blue = ReadSample(row, pixelStart + 2 * (bitDepth / 8));
                            alpha = 255;
                            break;
                        case 4:
                            red = green = blue = ReadSample(row, pixelStart);
                            alpha = ReadSample(row, pixelStart + bitDepth / 8);
                            break;
                        default:
                            red = ReadSample(row, pixelStart);
                            green = ReadSample(row, pixelStart + bitDepth / 8);
                            blue = ReadSample(row, pixelStart + 2 * (bitDepth / 8));
                            alpha = ReadSample(row, pixelStart + 3 * (bitDepth / 8));
                            break;
                    }
                }

                pixels[outputOffset] = red;
                pixels[outputOffset + 1] = green;
                pixels[outputOffset + 2] = blue;
                pixels[outputOffset + 3] = alpha;
                outputOffset += 4;
            }
        }

        /// <summary>读一个样本：位深 16 时每通道占 2 字节且高字节在前，取该通道开头那一字节即高字节。</summary>
        private static byte ReadSample(byte[] row, int pixelOffset)
        {
            return row[pixelOffset];
        }

        /// <summary>读一个调色板索引：位深 8 一字节；位深 1/2/4 按位拆包，每字节内高位在前。</summary>
        private static int ReadPaletteIndex(byte[] row, int x, int bitDepth)
        {
            if (bitDepth == 8)
            {
                return row[x];
            }

            var samplesPerByte = 8 / bitDepth;
            var byteIndex = x / samplesPerByte;
            var bitIndexInByte = x % samplesPerByte;
            var shift = 8 - bitDepth - bitIndexInByte * bitDepth;
            var mask = (1 << bitDepth) - 1;
            return (row[byteIndex] >> shift) & mask;
        }

        /// <summary>读 4 字节大端无符号整数。</summary>
        private static uint ReadUInt32BE(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24)
                | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        /// <summary>读 4 字节大端有符号整数。</summary>
        private static int ReadInt32BE(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24)
                | (bytes[offset + 1] << 16)
                | (bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        /// <summary>读块类型名（4 字节 ASCII）。</summary>
        private static string ReadChunkType(byte[] bytes, int offset)
        {
            return Encoding.ASCII.GetString(bytes, offset + 4, 4);
        }

        /// <summary>字节流是否以 PNG 签名开头。</summary>
        private static bool StartsWithSignature(byte[] bytes)
        {
            for (var i = 0; i < Signature.Length; i++)
            {
                if (bytes[i] != Signature[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>颜色类型 → 每像素通道数；未知类型返回 -1。</summary>
        private static int ChannelCount(int colorType)
        {
            switch (colorType)
            {
                case 0:
                case 3:
                    return 1;
                case 2:
                    return 3;
                case 4:
                    return 2;
                case 6:
                    return 4;
                default:
                    return -1;
            }
        }

        /// <summary>位深与颜色类型的组合是否支持：调色板吃 1/2/4/8，其余只吃 8/16。</summary>
        private static bool IsSupportedBitDepth(int colorType, int bitDepth)
        {
            if (colorType == 3)
            {
                return bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8;
            }

            return bitDepth == 8 || bitDepth == 16;
        }

        /// <summary>构造失败结果。</summary>
        private static PngDecodeResult Fail(string reason)
        {
            return new PngDecodeResult(false, null, reason);
        }
    }

    /// <summary>
    /// 最小 PNG 编码器：只出位深 8、颜色类型 6（真彩 + alpha）、非隔行这一种形态，
    /// 块顺序签名 → IHDR → IDAT → IEND，不写 tIME 也不写任何文本块——同输入必出逐字节相同的字节流
    /// （确定性，与决策 45 同源）。每行前置 filter 0（None），整块压成一个 IDAT。
    /// 每个块的 CRC32 按 IEEE 反射多项式 0xEDB88320 算对（解码器不校验，但浏览器与飞书会校验）。
    /// </summary>
    public static class PngEncoder
    {
        /// <summary>PNG 文件签名：89 50 4E 47 0D 0A 1A 0A。</summary>
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>CRC32 的 256 项查找表（IEEE 反射多项式 0xEDB88320）。</summary>
        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>
        /// 把一张 RGBA8 图编成 PNG 字节流。
        /// </summary>
        /// <param name="image">要编码的图。</param>
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

            // 每行一个 filter 字节 0（None）+ 宽×4 字节 RGBA，按行从上到下。
            var rowBytes = image.Width * 4;
            var scanlines = new byte[(rowBytes + 1) * image.Height];
            for (var y = 0; y < image.Height; y++)
            {
                var rowStart = y * (rowBytes + 1);
                scanlines[rowStart] = 0;
                var pixelStart = y * rowBytes;
                for (var x = 0; x < rowBytes; x++)
                {
                    scanlines[rowStart + 1 + x] = image.Pixels[pixelStart + x];
                }
            }

            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    zlib.Write(scanlines, 0, scanlines.Length);
                }

                compressed = output.ToArray();
            }

            var ihdr = new byte[13];
            WriteInt32BE(ihdr, 0, image.Width);
            WriteInt32BE(ihdr, 4, image.Height);
            ihdr[8] = 8;   // 位深
            ihdr[9] = 6;   // 颜色类型：真彩 + alpha
            ihdr[10] = 0;  // 压缩法
            ihdr[11] = 0;  // 滤波法
            ihdr[12] = 0;  // 非隔行

            var result = new List<byte>(Signature.Length + ihdr.Length + compressed.Length + 32);
            result.AddRange(Signature);
            AppendChunk(result, "IHDR", ihdr);
            AppendChunk(result, "IDAT", compressed);
            AppendChunk(result, "IEND", Array.Empty<byte>());
            return result.ToArray();
        }

        /// <summary>
        /// 把一张 RGBA8 图编码后写盘；编码失败或 IO 失败都转成 false + reason（原因照抄异常消息，不加猜测）。
        /// </summary>
        /// <param name="image">要编码的图。</param>
        /// <param name="filePath">目标文件路径。</param>
        /// <param name="reason">失败原因；成功时为空串。</param>
        public static bool EncodeToFile(PngImage image, string filePath, out string reason)
        {
            reason = "";
            try
            {
                var bytes = Encode(image);
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(filePath, bytes);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException
                || exception is IOException
                || exception is UnauthorizedAccessException
                || exception is NotSupportedException)
            {
                reason = exception.Message;
                return false;
            }
        }

        /// <summary>往块列表追加一个块：长度（大端）+ 类型 + 数据 + 4 字节 CRC32（对类型 + 数据算）。</summary>
        private static void AppendChunk(List<byte> target, string type, byte[] data)
        {
            var length = data.Length;
            target.Add((byte)(length >> 24));
            target.Add((byte)(length >> 16));
            target.Add((byte)(length >> 8));
            target.Add((byte)length);

            var typeBytes = Encoding.ASCII.GetBytes(type);
            target.AddRange(typeBytes);
            target.AddRange(data);

            var crc = ComputeCrc32(typeBytes, data);
            target.Add((byte)(crc >> 24));
            target.Add((byte)(crc >> 16));
            target.Add((byte)(crc >> 8));
            target.Add((byte)crc);
        }

        /// <summary>建 CRC32 查找表。</summary>
        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var c = i;
                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[i] = c;
            }

            return table;
        }

        /// <summary>对「类型 + 数据」算 CRC32（查表法）。</summary>
        private static uint ComputeCrc32(byte[] type, byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            for (var i = 0; i < type.Length; i++)
            {
                crc = CrcTable[(crc ^ type[i]) & 0xFF] ^ (crc >> 8);
            }

            for (var i = 0; i < data.Length; i++)
            {
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>写 4 字节大端有符号整数。</summary>
        private static void WriteInt32BE(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }
    }
}
