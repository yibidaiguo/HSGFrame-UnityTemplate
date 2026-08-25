using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public void DecodesSixteenBitDownToEightBit()
        {
            // 像素 0x1234, 0x00AB, 0xCDEF。
            //
            // **这条断言原来钉的是「取高字节」**（0x12/0x00/0xCD），那是当年手写解码器
            // 的做法——直接截断。换成库之后它按比例缩放（0xFFFF→0xFF 是线性映射），
            // 结果与截断差 1。缩放比截断对：截断会让纯白 0xFFFF 之外的高值整体偏暗。
            //
            // 所以这里改成钉**行为**：16 位能解、落到 8 位、值与高字节相差不超过 1。
            // 钉具体算法等于把实现细节焊进测试，换实现就假红。
            var scanlines = new byte[] { 0, 0x12, 0x34, 0x00, 0xAB, 0xCD, 0xEF };
            var bytes = BuildPng(1, 1, 16, 2, scanlines);

            var result = PngDecoder.Decode(bytes);

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Equal(4, result.Image.Pixels.Count);
            Assert.InRange(result.Image.Pixels[0], (byte)0x11, (byte)0x13);
            Assert.InRange(result.Image.Pixels[1], (byte)0x00, (byte)0x01);
            Assert.InRange(result.Image.Pixels[2], (byte)0xCC, (byte)0xCE);
            Assert.Equal(255, result.Image.Pixels[3]);
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

        /// <summary>隔行（Adam7）PNG 能解，且逐像素与非隔行的同一张图相同。</summary>
        [Fact]
        public void DecodesInterlacedPng()
        {
            // **这条原来钉的是「拒绝隔行」**。那不是规格，是当年手写解码器没实现 Adam7 而已——
            // 拿着一张正常隔行 PNG 的人得到「不支持」这句话，毫无意义。换库之后它本来就能解。
            //
            // 钉法是**和非隔行的同一张图比**：这样断言的是「隔行解对了」，
            // 而不是「解出了某一串我抄下来的数字」。后者换实现就假红。
            var pixels = SampleRgba(8, 8);
            var interlaced = BuildPng(8, 8, 8, 6, Adam7Scanlines(8, 8, pixels), interlace: true);
            var plain = BuildPng(8, 8, 8, 6, PlainScanlines(8, 8, pixels));

            var interlacedResult = PngDecoder.Decode(interlaced);
            var plainResult = PngDecoder.Decode(plain);

            Assert.True(interlacedResult.Succeeded, interlacedResult.FailureReason);
            Assert.True(plainResult.Succeeded, plainResult.FailureReason);
            Assert.Equal(8, interlacedResult.Image.Width);
            Assert.Equal(8, interlacedResult.Image.Height);
            Assert.Equal(pixels, interlacedResult.Image.Pixels);
            Assert.Equal(plainResult.Image.Pixels, interlacedResult.Image.Pixels);
        }

        /// <summary>
        /// 小于 5×5 的隔行 PNG：拒绝。**这条守的是「不许挂死」**。
        ///
        /// ImageSharp 2.1.13 遇到有空 Adam7 扫描道的隔行图会死循环——
        /// 1×1 隔行图、字节完全正确，一样把进程停在那儿。这不是假设：
        /// 这一条最早就是以「跑测试 600 秒不结束」的形式出现的。
        /// 所以解码器在交给库之前把这种图拦下来，这条测试钉的就是那道拦截还在。
        ///
        /// 用 xunit 的超时兜底：万一拦截被人删了，这条会**超时失败**而不是把整轮测试拖死。
        /// 写成 async 是被 xunit 逼的——它的 Timeout 只对返回 Task 的测试生效，
        /// 同步测试上写了不报错也不生效（会直接判失败，说「只支持 async」）。
        /// </summary>
        [Fact(Timeout = 15000)]
        public async Task RejectsTinyInterlacedPngThatWouldHangTheDecoder()
        {
            var pixels = SampleRgba(1, 1);
            var bytes = BuildPng(1, 1, 8, 6, Adam7Scanlines(1, 1, pixels), interlace: true);

            var result = await Task.Run(() => PngDecoder.Decode(bytes));

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

            // 钉的是**拒绝**这件事，不钉原因里出现哪个词：
            // 原因文案来自解码器，换一个解码器就换一套措辞（现在是
            // 「PNG Image does not contain a data chunk」），
            // 而「拒不拒绝」才是这条要守的东西。原因非空也要守——
            // 拒了却说不出为什么，跟没拒一样难查。
            Assert.False(result.Succeeded);
            Assert.NotEqual("", result.FailureReason);
        }

        /// <summary>颜色类型 3 缺 PLTE 块：拒绝，原因含「PLTE」。</summary>
        [Fact]
        public void RejectsPaletteWithoutPlte()
        {
            // 类型 3 但不给 PLTE 块。
            var scanlines = new byte[] { 0, 0, 1 };
            var bytes = BuildPng(2, 1, 8, 3, scanlines, palette: null);

            var result = PngDecoder.Decode(bytes);

            // 同上：钉拒绝与「说得出原因」，不钉措辞。
            Assert.False(result.Succeeded);
            Assert.NotEqual("", result.FailureReason);
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

        /// <summary>真彩（颜色类型 2）带 tRNS：认这份透明色，命中的像素 alpha 解成 0。</summary>
        [Fact]
        public void HonoursTransparencyForNonPaletteColorType()
        {
            // **这条原来钉的是「拒绝」**，理由同隔行那条：不是规格，是手写解码器没实现。
            // 非调色板的 tRNS 给的是「哪个颜色算透明」，真彩是 3 个 16 位分量共 6 字节。
            //
            // 两个像素：第一个 (255,0,0) 正好是 tRNS 点名的颜色 → alpha 0；
            // 第二个 (0,255,0) 没被点名 → alpha 255。一条断言同时守住「认」和「只认该认的」。
            var scanlines = new byte[] { 0, 255, 0, 0, 0, 255, 0 };
            var transparency = new byte[] { 0, 255, 0, 0, 0, 0 };
            var bytes = BuildPng(2, 1, 8, 2, scanlines, transparency: transparency);

            var result = PngDecoder.Decode(bytes);

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.Equal(new byte[] { 255, 0, 0, 0, 0, 255, 0, 255 }, result.Image.Pixels);
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
        /// <summary>
        /// 拼一个 PNG 块：长度 + 类型 + 数据 + **真 CRC**。
        ///
        /// 从前这里写的是四个零字节。当时的解码器不校验 CRC，所以样本照样能解——
        /// 于是这一整套测试喂的其实都不是合法 PNG，只是「那一版解码器恰好收」。
        /// 换成真解码器（ImageSharp 会校验）之后，它们一次全红，红得完全正确。
        /// </summary>
        private static void AddChunk(List<byte> target, string type, byte[] data)
        {
            var typeAndData = new List<byte>();
            typeAndData.AddRange(Encoding.ASCII.GetBytes(type));
            typeAndData.AddRange(data);

            target.AddRange(BitConverter.GetBytes(data.Length).Reverse());
            target.AddRange(typeAndData);

            var crc = Crc32(typeAndData.ToArray(), 0, typeAndData.Count);
            target.AddRange(BitConverter.GetBytes(crc).Reverse());
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

        /// <summary>Adam7 七道扫描的起点与步长（x 起点、y 起点、x 步长、y 步长）。</summary>
        private static readonly int[][] Adam7 =
        {
            new[] { 0, 0, 8, 8 },
            new[] { 4, 0, 8, 8 },
            new[] { 0, 4, 4, 8 },
            new[] { 2, 0, 4, 4 },
            new[] { 0, 2, 2, 4 },
            new[] { 1, 0, 2, 2 },
            new[] { 0, 1, 1, 2 }
        };

        /// <summary>造一张可复现的 RGBA 样图：值只跟坐标有关，不用随机数，红了能一眼看出错在哪个像素。</summary>
        private static byte[] SampleRgba(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    pixels[i] = (byte)(x * 31 + 1);
                    pixels[i + 1] = (byte)(y * 29 + 2);
                    pixels[i + 2] = (byte)(x * y + 3);
                    pixels[i + 3] = 255;
                }
            }

            return pixels;
        }

        /// <summary>把 RGBA 像素铺成非隔行扫描线（每行一个 0 滤波字节 + 整行 RGBA）。</summary>
        private static byte[] PlainScanlines(int width, int height, byte[] pixels)
        {
            var output = new List<byte>();
            for (var y = 0; y < height; y++)
            {
                output.Add(0);
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    output.AddRange(new[] { pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3] });
                }
            }

            return output.ToArray();
        }

        /// <summary>
        /// 把 RGBA 像素铺成 Adam7 隔行扫描线。
        /// **空的道整道不写**（PNG 规格如此：某一道宽或高算出来是 0 就一个字节都不占），
        /// 写了反而是错的——多出来的字节会被下一道当成自己的数据。
        /// </summary>
        private static byte[] Adam7Scanlines(int width, int height, byte[] pixels)
        {
            var output = new List<byte>();
            foreach (var pass in Adam7)
            {
                int xOffset = pass[0], yOffset = pass[1], xStep = pass[2], yStep = pass[3];
                var passWidth = Math.Max(0, (width - xOffset + xStep - 1) / xStep);
                var passHeight = Math.Max(0, (height - yOffset + yStep - 1) / yStep);
                if (passWidth == 0 || passHeight == 0)
                {
                    continue;
                }

                for (var row = 0; row < passHeight; row++)
                {
                    output.Add(0);
                    for (var column = 0; column < passWidth; column++)
                    {
                        var i = ((yOffset + row * yStep) * width + (xOffset + column * xStep)) * 4;
                        output.AddRange(new[] { pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3] });
                    }
                }
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
