namespace Template.Bridges.Oaiimage
{
    /// <summary>
    /// 从图片字节里读像素尺寸，认 PNG / JPEG / WEBP 三种；认不出返回 (0, 0)。
    ///
    /// 为什么不只认 PNG：generate 的响应载荷里「宽」「高」是硬要求，而中转背后挂什么模型不由我们决定——
    /// gpt-image-1 能按 output_format 回 jpeg / webp，dall-e 那一路回 png。
    /// 只认 PNG 的话另外两种会安静地报 0×0，而 0×0 看起来像「图坏了」，指不到「解析器不认这个格式」。
    /// </summary>
    public static class ImageDimensionReader
    {
        /// <summary>
        /// 读一张图的宽高；不认识的格式返回 (0, 0)。
        /// </summary>
        /// <param name="bytes">图片字节。</param>
        public static (int Width, int Height) Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16)
            {
                return (0, 0);
            }

            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                return ReadPng(bytes);
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return ReadJpeg(bytes);
            }

            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes.Length >= 12
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            {
                return ReadWebp(bytes);
            }

            return (0, 0);
        }

        /// <summary>PNG：IHDR 就在文件头，宽在字节 16、高在字节 20，大端。</summary>
        private static (int Width, int Height) ReadPng(byte[] bytes)
        {
            if (bytes.Length < 24
                || bytes[12] != 0x49 || bytes[13] != 0x48 || bytes[14] != 0x44 || bytes[15] != 0x52)
            {
                return (0, 0);
            }

            return (ReadBigEndianInt32(bytes, 16), ReadBigEndianInt32(bytes, 20));
        }

        /// <summary>
        /// JPEG：顺着段链往前走，遇到 SOF0/1/2/3/5/6/7/9/10/11/13/14/15 就地取宽高（大端 16 位，高在前）。
        /// SOS（0xDA）之后是压缩数据，再往下扫没有意义，就此收手。
        /// </summary>
        private static (int Width, int Height) ReadJpeg(byte[] bytes)
        {
            var offset = 2;
            while (offset + 9 < bytes.Length)
            {
                if (bytes[offset] != 0xFF)
                {
                    offset++;
                    continue;
                }

                var marker = bytes[offset + 1];
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    offset += 2;
                    continue;
                }

                if (marker == 0xDA || marker == 0xD9)
                {
                    return (0, 0);
                }

                var segmentLength = (bytes[offset + 2] << 8) | bytes[offset + 3];
                if (segmentLength < 2)
                {
                    return (0, 0);
                }

                if (IsStartOfFrame(marker))
                {
                    var height = (bytes[offset + 5] << 8) | bytes[offset + 6];
                    var width = (bytes[offset + 7] << 8) | bytes[offset + 8];
                    return (width, height);
                }

                offset += 2 + segmentLength;
            }

            return (0, 0);
        }

        /// <summary>这个段标记是不是一个带宽高的帧头（SOF）。0xC4 / 0xC8 / 0xCC 是霍夫曼表之类，不是。</summary>
        private static bool IsStartOfFrame(byte marker)
        {
            return marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
        }

        /// <summary>WEBP：三种子格式各有各的头——有损 VP8 、无损 VP8L、带扩展的 VP8X。</summary>
        private static (int Width, int Height) ReadWebp(byte[] bytes)
        {
            if (bytes.Length < 30)
            {
                return (0, 0);
            }

            // "VP8 "：宽高在关键帧头里，各 14 位，小端。
            if (bytes[12] == 0x56 && bytes[13] == 0x50 && bytes[14] == 0x38 && bytes[15] == 0x20)
            {
                var width = ((bytes[27] << 8) | bytes[26]) & 0x3FFF;
                var height = ((bytes[29] << 8) | bytes[28]) & 0x3FFF;
                return (width, height);
            }

            // "VP8L"：宽高各 14 位，压在 4 个字节里，都是「实际值减一」。
            if (bytes[12] == 0x56 && bytes[13] == 0x50 && bytes[14] == 0x38 && bytes[15] == 0x4C)
            {
                var packed = bytes[21] | (bytes[22] << 8) | (bytes[23] << 16) | (bytes[24] << 24);
                var width = (packed & 0x3FFF) + 1;
                var height = ((packed >> 14) & 0x3FFF) + 1;
                return (width, height);
            }

            // "VP8X"：画布宽高各 24 位小端，同样是「实际值减一」。
            if (bytes[12] == 0x56 && bytes[13] == 0x50 && bytes[14] == 0x38 && bytes[15] == 0x58)
            {
                var width = (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)) + 1;
                var height = (bytes[27] | (bytes[28] << 8) | (bytes[29] << 16)) + 1;
                return (width, height);
            }

            return (0, 0);
        }

        /// <summary>读大端 4 字节整数。</summary>
        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }
    }
}
