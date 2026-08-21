using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>九宫格里的一格：左上角要画的 label 与要贴的图片路径。</summary>
    public sealed class ContactSheetCell
    {
        /// <summary>
        /// 构造一格。
        /// </summary>
        /// <param name="label">左上角要画的标签，只许 0-9、F、S、I、?、-、空格，长度 1..6。</param>
        /// <param name="imagePath">要贴进格子的图片路径（PNG）。</param>
        public ContactSheetCell(string label, string imagePath)
        {
            Label = label ?? "";
            ImagePath = imagePath ?? "";
        }

        /// <summary>左上角要画的标签。</summary>
        public string Label { get; }

        /// <summary>要贴进格子的图片路径。</summary>
        public string ImagePath { get; }
    }

    /// <summary>一次拼图的结果：成没成、写到哪、过程中发现的全部问题。</summary>
    public sealed class ContactSheetResult
    {
        /// <summary>
        /// 构造一次拼图结果。
        /// </summary>
        /// <param name="succeeded">拼成没有。</param>
        /// <param name="outputPath">输出文件路径。</param>
        /// <param name="findings">过程中发现的全部问题。</param>
        public ContactSheetResult(bool succeeded, string outputPath, IReadOnlyList<PoolFinding> findings)
        {
            Succeeded = succeeded;
            OutputPath = outputPath ?? "";
            Findings = findings ?? Array.Empty<PoolFinding>();
        }

        /// <summary>拼成没有。</summary>
        public bool Succeeded { get; }

        /// <summary>输出文件路径。</summary>
        public string OutputPath { get; }

        /// <summary>过程中发现的全部问题。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }
    }

    /// <summary>
    /// 九宫格拼图合成器：把一张张变体图（或模型的三视图）等比缩放进格子、贴上序号标签，
    /// 合成一张 PNG 供选片卡预览。解不出来的图不整张失败——画占位格 + 出一条 finding，绝不少一格
    /// （决策 46：格序与卡片按钮 1..N 死死对齐，少一格等于让人选错图）。
    /// </summary>
    public static class ContactSheetComposer
    {
        /// <summary>缺省格子边长，像素。</summary>
        public const int DefaultCellSideLength = 512;

        /// <summary>整张拼图的长边上限，像素。</summary>
        public const int MaximumSheetSideLength = 2048;

        /// <summary>格子的最小边长，像素。</summary>
        private const int MinimumCellSideLength = 64;

        /// <summary>格子的最大边长，像素。</summary>
        private const int MaximumCellSideLength = 512;

        /// <summary>格子内框相对格边的留白，像素（四边各留 4）。</summary>
        private const int CellMargin = 4;

        /// <summary>底色 RGBA：深灰 (43,43,43,255)。</summary>
        private const byte BackgroundR = 43;
        private const byte BackgroundG = 43;
        private const byte BackgroundB = 43;

        /// <summary>占位格的颜色 RGBA：暗红 (90,30,30,255)。</summary>
        private const byte PlaceholderR = 90;
        private const byte PlaceholderG = 30;
        private const byte PlaceholderB = 30;

        /// <summary>按格子数定列数：1 格以内给 1，否则 min(3, ceil(sqrt(N)))。</summary>
        /// <param name="cellCount">格子数。</param>
        public static int ColumnCountFor(int cellCount)
        {
            if (cellCount <= 1)
            {
                return 1;
            }

            var root = (int)Math.Ceiling(Math.Sqrt(cellCount));
            return Math.Min(3, root);
        }

        /// <summary>
        /// 按列数把一格一格图合成一张九宫格 PNG。失败（参数错、label 非法、写不出）不写文件；
        /// 个别图解不出来时画占位格 + finding，整体仍算成功。
        /// </summary>
        /// <param name="cells">要拼的格子，顺序即格序。</param>
        /// <param name="columnCount">列数，至少 1。</param>
        /// <param name="outputPath">输出 PNG 路径。</param>
        public static ContactSheetResult Compose(IReadOnlyList<ContactSheetCell> cells, int columnCount, string outputPath)
        {
            var findings = new List<PoolFinding>();

            if (cells == null || cells.Count == 0)
            {
                findings.Add(new PoolFinding(
                    outputPath ?? "",
                    "没有格子，拼不出九宫格",
                    "至少给一格（一个合格变体）",
                    "Doc/creation-pipeline-subdocs/06-art-pipeline.md"));
                return new ContactSheetResult(false, outputPath ?? "", findings);
            }

            if (columnCount < 1)
            {
                findings.Add(new PoolFinding(
                    outputPath ?? "",
                    $"列数 {columnCount} 必须至少为 1",
                    "传 >= 1 的列数",
                    "Doc/creation-pipeline-subdocs/06-art-pipeline.md"));
                return new ContactSheetResult(false, outputPath ?? "", findings);
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                findings.Add(new PoolFinding(
                    "",
                    "没给输出路径，写不了拼图",
                    "给一个 outputPath",
                    "Doc/creation-pipeline-subdocs/06-art-pipeline.md"));
                return new ContactSheetResult(false, "", findings);
            }

            // 先校验全部 label：图上没有字库，字靠内嵌点阵画，不合法就报错，绝不静默画成空白。
            for (var i = 0; i < cells.Count; i++)
            {
                var label = cells[i].Label;
                if (label == null || label.Length < 1 || label.Length > 6)
                {
                    findings.Add(new PoolFinding(
                        outputPath,
                        $"第 {i + 1} 格的 label「{label ?? ""}」长度不在 1..6",
                        "label 只许 1..6 个字符",
                        "Doc/creation-pipeline-subdocs/06-art-pipeline.md"));
                    return new ContactSheetResult(false, outputPath, findings);
                }

                foreach (var ch in label)
                {
                    if (!IsAllowedLabelChar(ch))
                    {
                        findings.Add(new PoolFinding(
                            outputPath,
                            $"第 {i + 1} 格的 label「{label}」里有不允许的字符「{ch}」",
                            "label 只许 0-9、F、S、I、?、-、空格",
                            "Doc/creation-pipeline-subdocs/06-art-pipeline.md"));
                        return new ContactSheetResult(false, outputPath, findings);
                    }
                }
            }

            var n = cells.Count;
            var rows = (n + columnCount - 1) / columnCount;
            var maxDimension = Math.Max(columnCount, rows);
            var cellSide = Math.Min(DefaultCellSideLength, MaximumSheetSideLength / maxDimension);
            cellSide = Math.Clamp(cellSide, MinimumCellSideLength, MaximumCellSideLength);

            var canvasWidth = columnCount * cellSide;
            var canvasHeight = rows * cellSide;
            var pixels = new byte[canvasWidth * canvasHeight * 4];
            FillBackground(pixels, canvasWidth, canvasHeight);

            for (var i = 0; i < n; i++)
            {
                var col = i % columnCount;
                var row = i / columnCount;
                var left = col * cellSide;
                var top = row * cellSide;
                var cell = cells[i];

                var decode = PngDecoder.DecodeFile(cell.ImagePath);
                if (decode.Succeeded)
                {
                    DrawImage(pixels, canvasWidth, canvasHeight, left, top, cellSide, decode.Image);
                }
                else
                {
                    FillRect(pixels, canvasWidth, canvasHeight, left, top, cellSide, cellSide, PlaceholderR, PlaceholderG, PlaceholderB, 255);
                    findings.Add(new PoolFinding(
                        cell.ImagePath,
                        $"变体「{Path.GetFileName(cell.ImagePath)}」解不出来：{decode.FailureReason}，这一格画成占位",
                        "九宫格只吃 PNG；把变体转成 PNG，或先渲出预览图",
                        "Doc/creation-pipeline-subdocs/06-art-pipeline.md"));
                }

                DrawLabel(pixels, canvasWidth, canvasHeight, left, top, cell.Label);
            }

            var image = new PngImage(canvasWidth, canvasHeight, pixels);
            if (!PngEncoder.EncodeToFile(image, outputPath, out var writeReason))
            {
                findings.Add(new PoolFinding(
                    outputPath,
                    $"拼图写不出去：{writeReason}",
                    "检查输出目录是否可写",
                    "Doc/creation-pipeline-subdocs/06-art-pipeline.md"));
                return new ContactSheetResult(false, outputPath, findings);
            }

            return new ContactSheetResult(true, outputPath, findings);
        }

        /// <summary>label 字符是否在允许集内：0-9、F、S、I、?、-、空格。</summary>
        private static bool IsAllowedLabelChar(char ch)
        {
            if (ch >= '0' && ch <= '9')
            {
                return true;
            }

            switch (ch)
            {
                case 'F':
                case 'S':
                case 'I':
                case '?':
                case '-':
                case ' ':
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>整张画布填底色 (43,43,43,255)。</summary>
        private static void FillBackground(byte[] pixels, int width, int height)
        {
            for (var i = 0; i < width * height; i++)
            {
                pixels[i * 4] = BackgroundR;
                pixels[i * 4 + 1] = BackgroundG;
                pixels[i * 4 + 2] = BackgroundB;
                pixels[i * 4 + 3] = 255;
            }
        }

        /// <summary>
        /// 把一张解出来的图等比缩放进格子：内框 = 边长 - 8，缩放到刚好填满内框，盒式平均，居中贴，按 alpha 合成。
        /// 小图也放大——图标资产常见 64/128 见方，不放大就在 512 的格子里缩成一个点，人眼根本看不出选的是什么。
        /// 放大时下面那个盒式循环每格只取到一个源像素，等价于最近邻，正是像素图要的清晰边缘。
        /// </summary>
        private static void DrawImage(byte[] pixels, int canvasWidth, int canvasHeight, int left, int top, int cellSide, PngImage source)
        {
            var inner = cellSide - 8;
            var sw = source.Width;
            var sh = source.Height;
            var scale = Math.Min((double)inner / sw, (double)inner / sh);
            var dw = (int)Math.Round(sw * scale);
            var dh = (int)Math.Round(sh * scale);
            if (dw < 1)
            {
                dw = 1;
            }

            if (dh < 1)
            {
                dh = 1;
            }

            var offsetX = left + (cellSide - dw) / 2;
            var offsetY = top + (cellSide - dh) / 2;
            var src = source.Pixels;

            for (var dy = 0; dy < dh; dy++)
            {
                var srcY0 = dy * sh / dh;
                var srcY1 = (dy + 1) * sh / dh;
                if (srcY1 <= srcY0)
                {
                    srcY1 = srcY0 + 1;
                }

                for (var dx = 0; dx < dw; dx++)
                {
                    var srcX0 = dx * sw / dw;
                    var srcX1 = (dx + 1) * sw / dw;
                    if (srcX1 <= srcX0)
                    {
                        srcX1 = srcX0 + 1;
                    }

                    // 盒式平均：目标像素映射回源矩形，取源像素的 RGBA 平均。
                    long sumR = 0;
                    long sumG = 0;
                    long sumB = 0;
                    long sumA = 0;
                    var count = 0;
                    for (var sy = srcY0; sy < srcY1; sy++)
                    {
                        for (var sx = srcX0; sx < srcX1; sx++)
                        {
                            var si = (sy * sw + sx) * 4;
                            sumR += src[si];
                            sumG += src[si + 1];
                            sumB += src[si + 2];
                            sumA += src[si + 3];
                            count++;
                        }
                    }

                    var avgR = (int)(sumR / count);
                    var avgG = (int)(sumG / count);
                    var avgB = (int)(sumB / count);
                    var avgA = (int)(sumA / count);

                    var targetX = offsetX + dx;
                    var targetY = offsetY + dy;
                    if (targetX < 0 || targetX >= canvasWidth || targetY < 0 || targetY >= canvasHeight)
                    {
                        continue;
                    }

                    // 结果 = 前景*a + 背景*(1-a)，a 取 0..1；直接合成到底色上。
                    var ti = (targetY * canvasWidth + targetX) * 4;
                    var bgR = pixels[ti];
                    var bgG = pixels[ti + 1];
                    var bgB = pixels[ti + 2];
                    pixels[ti] = (byte)((avgR * avgA + bgR * (255 - avgA)) / 255);
                    pixels[ti + 1] = (byte)((avgG * avgA + bgG * (255 - avgA)) / 255);
                    pixels[ti + 2] = (byte)((avgB * avgA + bgB * (255 - avgA)) / 255);
                    pixels[ti + 3] = 255;
                }
            }
        }

        /// <summary>把一块不透明的矩形直接写进画布。</summary>
        private static void FillRect(byte[] pixels, int width, int height, int left, int top, int w, int h, byte r, byte g, byte b, byte a)
        {
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var px = left + x;
                    var py = top + y;
                    if (px < 0 || px >= width || py < 0 || py >= height)
                    {
                        continue;
                    }

                    var i = (py * width + px) * 4;
                    pixels[i] = r;
                    pixels[i + 1] = g;
                    pixels[i + 2] = b;
                    pixels[i + 3] = a;
                }
            }
        }

        /// <summary>在格子左上角画 label：黑底条 + 白色 5×7 点阵字，放大 4 倍。</summary>
        private static void DrawLabel(byte[] pixels, int canvasWidth, int canvasHeight, int left, int top, string label)
        {
            const int scale = 4;
            const int spacing = 4;
            const int barHeight = 36;

            var glyphWidth = DotMatrixFont.GlyphWidth * scale;
            var textWidth = label.Length * glyphWidth + (label.Length - 1) * spacing;
            var barWidth = textWidth + 8;

            FillRect(pixels, canvasWidth, canvasHeight, left, top, barWidth, barHeight, 0, 0, 0, 255);

            var cursorX = left + 4;
            foreach (var ch in label)
            {
                var rows = DotMatrixFont.RowsFor(ch);
                for (var row = 0; row < DotMatrixFont.GlyphHeight; row++)
                {
                    for (var col = 0; col < DotMatrixFont.GlyphWidth; col++)
                    {
                        var bit = (rows[row] >> (DotMatrixFont.GlyphWidth - 1 - col)) & 1;
                        if (bit == 0)
                        {
                            continue;
                        }

                        for (var sy = 0; sy < scale; sy++)
                        {
                            for (var sx = 0; sx < scale; sx++)
                            {
                                var px = cursorX + col * scale + sx;
                                var py = top + 4 + row * scale + sy;
                                if (px < 0 || px >= canvasWidth || py < 0 || py >= canvasHeight)
                                {
                                    continue;
                                }

                                var i = (py * canvasWidth + px) * 4;
                                pixels[i] = 255;
                                pixels[i + 1] = 255;
                                pixels[i + 2] = 255;
                                pixels[i + 3] = 255;
                            }
                        }
                    }
                }

                cursorX += glyphWidth + spacing;
            }
        }

        /// <summary>
        /// 内嵌 5×7 点阵字模：16 个字符（0-9、F、S、I、?、-、空格）各存 7 个字节，
        /// 每字节低 5 位是一行的点（bit 4 是最左列）。字形够人眼认出即可，不求好看。
        /// </summary>
        private static class DotMatrixFont
        {
            /// <summary>字模宽，点。</summary>
            public const int GlyphWidth = 5;

            /// <summary>字模高，点。</summary>
            public const int GlyphHeight = 7;

            private static readonly Dictionary<char, byte[]> Glyphs = BuildGlyphs();

            /// <summary>取某字符的 7 行点阵；不认识的字给空格（空白）。</summary>
            public static byte[] RowsFor(char ch)
            {
                if (Glyphs.TryGetValue(ch, out var rows))
                {
                    return rows;
                }

                return Blank();
            }

            private static Dictionary<char, byte[]> BuildGlyphs()
            {
                return new Dictionary<char, byte[]>
                {
                    ['0'] = Glyph(0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110),
                    ['1'] = Glyph(0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
                    ['2'] = Glyph(0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111),
                    ['3'] = Glyph(0b11111, 0b00010, 0b00100, 0b00010, 0b00001, 0b10001, 0b01110),
                    ['4'] = Glyph(0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010),
                    ['5'] = Glyph(0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110),
                    ['6'] = Glyph(0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110),
                    ['7'] = Glyph(0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000),
                    ['8'] = Glyph(0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110),
                    ['9'] = Glyph(0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100),
                    ['F'] = Glyph(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000),
                    ['S'] = Glyph(0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110),
                    ['I'] = Glyph(0b01110, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
                    ['?'] = Glyph(0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b00000, 0b00100),
                    ['-'] = Glyph(0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000),
                    [' '] = Glyph(0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000)
                };
            }

            private static byte[] Glyph(byte r0, byte r1, byte r2, byte r3, byte r4, byte r5, byte r6)
            {
                return new[] { r0, r1, r2, r3, r4, r5, r6 };
            }

            private static byte[] Blank()
            {
                return new byte[GlyphHeight];
            }
        }
    }
}
