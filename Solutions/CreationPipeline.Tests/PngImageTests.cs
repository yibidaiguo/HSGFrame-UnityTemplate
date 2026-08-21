using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 最小 PNG 解码器的测试：合法 PNG 逐像素对得上，不支持的形态如实拒绝。
    /// 测试数据全部在测试里现造（ZLibStream 压缩 + 手工拼块），仓库零二进制样例（决策 4）。
    /// </summary>
    public class PngImageTests
    {
        /// <summary>2×2 真彩（颜色类型 2，位深 8）：输入 RGB 12 字节，解出 RGBA 16 字节逐个对得上。</summary>
        [Fact]
        public void DecodesTrueColorRgb()
        {
            // 行 0：像素 (10,20,30)、(40,50,60)；行 1：像素 (70,80,90)、(100,110,120)。滤波 0（None）。
            var scanlines = new byte[]
            {
                0, 10, 20, 30, 40, 50, 60,
                0, 70, 80, 90, 100, 110, 120
            };
            var bytes = BuildPng(2, 2, 8, 2, scanlines);

            var result = PngDecoder.Decode(bytes);

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Equal(2, result.Image.Width);
            Assert.Equal(2, result.Image.Height);
            Assert.Equal(16, result.Image.Pixels.Count);
            var expected = new byte[]
            {
                10, 20, 30, 255,
                40, 50, 60, 255,
                70, 80, 90, 255,
                100, 110, 120, 255
            };
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], result.Image.Pixels[i]);
            }
        }

        /// <summary>2×2 RGBA（颜色类型 6）含 alpha：alpha 逐个对得上。</summary>
        [Fact]
        public void DecodesTrueColorWithAlpha()
        {
            var scanlines = new byte[]
            {
                0, 1, 2, 3, 4, 5, 6, 7, 8,
                0, 9, 10, 11, 12, 13, 14, 15, 16
            };
            var bytes = BuildPng(2, 2, 8, 6, scanlines);

            var result = PngDecoder.Decode(bytes);

            Assert.True(result.Succeeded, result.FailureReason);
            var expected = new byte[]
            {
                1, 2, 3, 4, 5, 6, 7, 8,
                9, 10, 11, 12, 13, 14, 15, 16
            };
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], result.Image.Pixels[i]);
            }
        }

        /// <summary>灰度（颜色类型 0，位深 8）：铺成 R=G=B，alpha 补 255。</summary>
        [Fact]
        public void DecodesGrayscalePaddedToRgb()
        {
            var scanlines = new byte[] { 0, 128, 64 };
            var bytes = BuildPng(2, 1, 8, 0, scanlines);

            var result = PngDecoder.Decode(bytes);

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Equal(new byte[] { 128, 128, 128, 255, 64, 64, 64, 255 }, result.Image.Pixels);
        }

        /// <summary>调色板（颜色类型 3，位深 8）：索引按 PLTE 查表对得上。</summary>
        [Fact]
        public void DecodesPaletteIndexed()
        {
            var scanlines = new byte[] { 0, 0, 2 };
            var palette = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255 };
            var bytes = BuildPng(2, 1, 8, 3, scanlines, palette);

            var result = PngDecoder.Decode(bytes);

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, result.Image.Pixels);
        }

        /// <summary>调色板 + tRNS：tRNS 按索引给 alpha，没给到的索引补 255。</summary>
        [Fact]
        public void DecodesPaletteWithTransparency()
        {
            var scanlines = new byte[] { 0, 0, 1 };
            var palette = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255 };
            var transparency = new byte[] { 128 };
            var bytes = BuildPng(2, 1, 8, 3, scanlines, palette, transparency);

            var result = PngDecoder.Decode(bytes);

            Assert.True(result.Succeeded, result.FailureReason);
            // 索引 0 的 alpha 取 tRNS 的 128；索引 1 超出 tRNS 长度补 255。
            Assert.Equal(new byte[] { 255, 0, 0, 128, 0, 255, 0, 255 }, result.Image.Pixels);
        }

        /// <summary>位深 16 真彩：取每通道高字节，结果对得上。</summary>
        [Fact]
        public void DecodesSixteenBitByTakingHighByte()
        {
            // 像素 0x1234, 0x00AB, 0xCDEF → 高字节 0x12, 0x00, 0xCD。
            var scanlines = new byte[] { 0, 0x12, 0x34, 0x00, 0xAB, 0xCD, 0xEF };
            var bytes = BuildPng(1, 1, 16, 2, scanlines);

            var result = PngDecoder.Decode(bytes);

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Equal(new byte[] { 0x12, 0x00, 0xCD, 255 }, result.Image.Pixels);
        }

        /// <summary>多个 IDAT 块：把压缩流切成两段分别装块，解出来与单块逐字节相同。</summary>
        [Fact]
        public void ConcatenatesMultipleIdatChunks()
        {
            var scanlines = new byte[]
            {
                0, 10, 20, 30, 40, 50, 60,
                0, 70, 80, 90, 100, 110, 120
            };
            var single = BuildPng(2, 2, 8, 2, scanlines, splitIdat: false);
            var split = BuildPng(2, 2, 8, 2, scanlines, splitIdat: true);

            var singleResult = PngDecoder.Decode(single);
            var splitResult = PngDecoder.Decode(split);

            Assert.True(singleResult.Succeeded, singleResult.FailureReason);
            Assert.True(splitResult.Succeeded, splitResult.FailureReason);
            Assert.Equal(singleResult.Image.Pixels, splitResult.Image.Pixels);
        }

        /// <summary>五种滤波器各造一行（None/Sub/Up/Average/Paeth），解出来逐像素对得上。</summary>
        [Fact]
        public void UnfiltersAllFiveFilterTypes()
        {
            // 5 行 × 2 像素 RGB，每行一种滤波器。滤波字节是手工按滤波器定义算好的。
            var scanlines = new byte[]
            {
                // 行 0 None：原始像素 (10,20,30),(40,50,60)
                0, 10, 20, 30, 40, 50, 60,
                // 行 1 Sub：原始 (70,80,90),(100,110,120)，滤波 = 原始 - 同像素前一通道
                1, 70, 80, 90, 30, 30, 30,
                // 行 2 Up：原始 (130,140,150),(160,170,180)，滤波 = 原始 - 上一行
                2, 60, 60, 60, 60, 60, 60,
                // 行 3 Average：原始 (190,200,210),(220,230,240)，滤波 = 原始 - floor((左+上)/2)
                3, 125, 130, 135, 45, 45, 45,
                // 行 4 Paeth：原始 (250,20,30),(40,50,60)，滤波 = 原始 - Paeth(左,上,左上)
                4, 60, 76, 76, 46, 30, 30
            };
            var bytes = BuildPng(2, 5, 8, 2, scanlines);

            var result = PngDecoder.Decode(bytes);

            Assert.True(result.Succeeded, result.FailureReason);
            var expected = new byte[]
            {
                10, 20, 30, 255, 40, 50, 60, 255,
                70, 80, 90, 255, 100, 110, 120, 255,
                130, 140, 150, 255, 160, 170, 180, 255,
                190, 200, 210, 255, 220, 230, 240, 255,
                250, 20, 30, 255, 40, 50, 60, 255
            };
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], result.Image.Pixels[i]);
            }
        }

        /// <summary>隔行 PNG（interlace=1）：拒绝，原因含「隔行」。</summary>
        [Fact]
        public void RejectsInterlacedPng()
        {
            var scanlines = new byte[] { 0, 1, 2, 3 };
            var bytes = BuildPng(1, 1, 8, 6, scanlines, interlace: true);

            var result = PngDecoder.Decode(bytes);

            Assert.False(result.Succeeded);
            Assert.Contains("隔行", result.FailureReason);
        }

        /// <summary>签名坏：拒绝。</summary>
        [Fact]
        public void RejectsBadSignature()
        {
            var scanlines = new byte[] { 0, 1, 2, 3 };
            var bytes = BuildPng(1, 1, 8, 6, scanlines);
            bytes[0] = 0x00;

            var result = PngDecoder.Decode(bytes);

            Assert.False(result.Succeeded);
            Assert.Contains("签名", result.FailureReason);
        }

        /// <summary>缺 IDAT 块：拒绝，原因含「IDAT」。</summary>
        [Fact]
        public void RejectsMissingIdat()
        {
            var bytes = BuildPng(1, 1, 8, 6, new byte[] { 0, 1, 2, 3, 4 }, omitIdat: true);

            var result = PngDecoder.Decode(bytes);

            Assert.False(result.Succeeded);
            Assert.Contains("IDAT", result.FailureReason);
        }

        /// <summary>颜色类型 3 缺 PLTE 块：拒绝，原因含「PLTE」。</summary>
        [Fact]
        public void RejectsPaletteWithoutPlte()
        {
            // 类型 3 但不给 PLTE 块。
            var scanlines = new byte[] { 0, 0, 1 };
            var bytes = BuildPng(2, 1, 8, 3, scanlines, palette: null);

            var result = PngDecoder.Decode(bytes);

            Assert.False(result.Succeeded);
            Assert.Contains("PLTE", result.FailureReason);
        }

        /// <summary>块声称的长度超出剩余字节：拒绝，不抛异常。</summary>
        [Fact]
        public void RejectsChunkLengthBeyondRemainingBytesWithoutThrowing()
        {
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            var ihdr = new byte[13];
            WriteInt32BE(ihdr, 0, 1);
            WriteInt32BE(ihdr, 4, 1);
            ihdr[8] = 8;
            ihdr[9] = 6;
            AddChunk(bytes, "IHDR", ihdr);

            // 一个声称长度 0x7FFFFFFF 的 IDAT 块，物理上只有 8 字节。
            bytes.AddRange(BitConverter.GetBytes(0x7FFFFFFF).Reverse());
            bytes.AddRange(Encoding.ASCII.GetBytes("IDAT"));
            bytes.AddRange(new byte[4]);

            var result = PngDecoder.Decode(bytes.ToArray());

            Assert.False(result.Succeeded);
            Assert.Contains("剩余字节", result.FailureReason);
        }

        /// <summary>文件不存在：拒绝，Succeeded 为 false。</summary>
        [Fact]
        public void DecodeFileMissingFileFails()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), "创作管线测试-不存在-" + Guid.NewGuid().ToString("N") + ".png");

            var result = PngDecoder.DecodeFile(missingPath);

            Assert.False(result.Succeeded);
        }

        /// <summary>非调色板颜色类型带 tRNS：拒绝，不把透明信息硬解成不透明。</summary>
        [Fact]
        public void RejectsTransparencyForNonPaletteColorType()
        {
            // 类型 2（真彩）带 2 字节 tRNS：本解码器不支持，应如实拒绝。
            var scanlines = new byte[] { 0, 255, 0, 0 };
            var bytes = BuildPng(1, 1, 8, 2, scanlines, transparency: new byte[] { 0x00, 0x00 });

            var result = PngDecoder.Decode(bytes);

            Assert.False(result.Succeeded);
            Assert.Contains("tRNS", result.FailureReason);
        }

        /// <summary>编解码往返：3×2 渐变图 → Encode → Decode → 逐像素相等。</summary>
        [Fact]
        public void EncodeDecodeRoundTripsPixels()
        {
            var pixels = new byte[3 * 2 * 4];
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 3; x++)
                {
                    var i = (y * 3 + x) * 4;
                    pixels[i] = (byte)(x * 100);
                    pixels[i + 1] = (byte)(y * 100);
                    pixels[i + 2] = (byte)(x * y * 50);
                    pixels[i + 3] = 255;
                }
            }

            var encoded = PngEncoder.Encode(new PngImage(3, 2, pixels));
            var decoded = PngDecoder.Decode(encoded);

            Assert.True(decoded.Succeeded, decoded.FailureReason);
            Assert.Equal(3, decoded.Image.Width);
            Assert.Equal(2, decoded.Image.Height);
            Assert.Equal(pixels, decoded.Image.Pixels);
        }

        /// <summary>确定性：同一张图连编两次，字节数组逐字节相同。</summary>
        [Fact]
        public void EncodeIsDeterministic()
        {
            var pixels = new byte[4 * 4 * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)(i * 7);
            }

            var image = new PngImage(4, 4, pixels);
            var first = PngEncoder.Encode(image);
            var second = PngEncoder.Encode(image);

            Assert.Equal(first, second);
        }

        /// <summary>带 alpha 的像素往返后 alpha 不变。</summary>
        [Fact]
        public void EncodeDecodePreservesAlpha()
        {
            var pixels = new byte[]
            {
                10, 20, 30, 0,
                40, 50, 60, 128,
                70, 80, 90, 255,
                11, 22, 33, 77
            };

            var encoded = PngEncoder.Encode(new PngImage(2, 2, pixels));
            var decoded = PngDecoder.Decode(encoded);

            Assert.True(decoded.Succeeded, decoded.FailureReason);
            Assert.Equal(pixels, decoded.Image.Pixels);
        }

        /// <summary>编出来的每个块的 CRC32 都按 IEEE 0xEDB88320 算对（浏览器与飞书会校验）。</summary>
        [Fact]
        public void EncodedPngHasValidChunkCrcs()
        {
            var pixels = new byte[] { 255, 0, 0, 255 };
            var bytes = PngEncoder.Encode(new PngImage(1, 1, pixels));

            var offset = 8;
            var chunkCount = 0;
            while (offset < bytes.Length)
            {
                var length = ReadInt32BE(bytes, offset);
                var typeAndDataLength = 4 + length;
                var crcOffset = offset + 8 + length;
                var expected = ReadUInt32BE(bytes, crcOffset);
                var actual = Crc32(bytes, offset + 4, typeAndDataLength);
                Assert.Equal(expected, actual);
                chunkCount++;
                offset = crcOffset + 4;
            }

            Assert.True(chunkCount >= 3, "至少应有 IHDR / IDAT / IEND 三个块");
        }

        /// <summary>按位直算 CRC32（与编码器的查表法独立实现，用来交叉验证查表没写错）。</summary>
        private static uint Crc32(byte[] data, int offset, int count)
        {
            var crc = 0xFFFFFFFFu;
            for (var i = 0; i < count; i++)
            {
                crc ^= data[offset + i];
                for (var k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }

            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>读 4 字节大端有符号整数。</summary>
        private static int ReadInt32BE(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24)
                | (bytes[offset + 1] << 16)
                | (bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        /// <summary>读 4 字节大端无符号整数。</summary>
        private static uint ReadUInt32BE(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24)
                | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        /// <summary>手工拼一个合法 PNG 字节流；omitIdat 为 true 时不放 IDAT 块。</summary>
        private static byte[] BuildPng(
            int width,
            int height,
            int bitDepth,
            int colorType,
            byte[] scanlines,
            byte[] palette = null,
            byte[] transparency = null,
            bool interlace = false,
            bool splitIdat = false,
            bool omitIdat = false)
        {
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            var ihdr = new byte[13];
            WriteInt32BE(ihdr, 0, width);
            WriteInt32BE(ihdr, 4, height);
            ihdr[8] = (byte)bitDepth;
            ihdr[9] = (byte)colorType;
            ihdr[10] = 0;
            ihdr[11] = 0;
            ihdr[12] = (byte)(interlace ? 1 : 0);
            AddChunk(bytes, "IHDR", ihdr);

            if (palette != null)
            {
                AddChunk(bytes, "PLTE", palette);
            }

            if (transparency != null)
            {
                AddChunk(bytes, "tRNS", transparency);
            }

            if (!omitIdat)
            {
                var compressed = Compress(scanlines);
                if (splitIdat && compressed.Length >= 2)
                {
                    var split = compressed.Length / 2;
                    AddChunk(bytes, "IDAT", compressed.Take(split).ToArray());
                    AddChunk(bytes, "IDAT", compressed.Skip(split).ToArray());
                }
                else
                {
                    AddChunk(bytes, "IDAT", compressed);
                }
            }

            AddChunk(bytes, "IEND", Array.Empty<byte>());
            return bytes.ToArray();
        }

        /// <summary>往块列表里追加一个块：长度（大端）+ 类型 + 数据 + 4 字节 CRC 占位（本解码器不校验 CRC）。</summary>
        private static void AddChunk(List<byte> target, string type, byte[] data)
        {
            target.AddRange(BitConverter.GetBytes(data.Length).Reverse());
            target.AddRange(Encoding.ASCII.GetBytes(type));
            target.AddRange(data);
            target.AddRange(new byte[4]);
        }

        /// <summary>用 ZLibStream 压缩一段原始扫描线字节。</summary>
        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(data, 0, data.Length);
            }

            return output.ToArray();
        }

        /// <summary>写 4 字节大端整数。</summary>
        private static void WriteInt32BE(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }
    }
}
