using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次拼精灵图集的结果。</summary>
    /// <param name="Succeeded">拼成没有。</param>
    /// <param name="SheetPath">图集 PNG 路径；没拼成时空串。</param>
    /// <param name="MetadataPath">图集描述 JSON 路径；没拼成时空串。</param>
    /// <param name="CellWidth">格子宽。</param>
    /// <param name="CellHeight">格子高。</param>
    /// <param name="Notes">过程里要说的话（尺寸不齐、某帧读不了……）。</param>
    /// <param name="FailureReason">失败原因；成功时空串。</param>
    public sealed record SpriteSheetResult(
        bool Succeeded,
        string SheetPath,
        string MetadataPath,
        int CellWidth,
        int CellHeight,
        IReadOnlyList<string> Notes,
        string FailureReason);

    /// <summary>
    /// 把一段帧序列拼成一张横排精灵图集（第二步，人审过之后才跑）。
    ///
    /// **格子按最大帧取**，每帧按锚点摆进格子里，不缩放：
    /// 缩放会让像素风的边糊掉，而这条链出的帧本来就同尺寸，尺寸不齐是异常不是常态——
    /// 所以不齐时照最大格留白并**在结果里说出来**，不是悄悄拉伸。
    ///
    /// 锚点决定每帧摆在格子的哪儿。人物动画每帧主体高度不同，
    /// 按左上角摆会让角色在原地上下跳；「底边中点」对的是脚，所以是缺省。
    /// </summary>
    public static class SpriteSheetComposer
    {
        /// <summary>图集描述的固定文件名。</summary>
        public const string MetadataFileName = "sheet.json";

        /// <summary>写盘选项：缩进、中文原样。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 拼一张横排图集。
        /// </summary>
        /// <param name="sequence">帧序列描述。</param>
        /// <param name="outputDirectory">输出目录。</param>
        /// <param name="sheetName">图集文件名（不带扩展名）。</param>
        public static SpriteSheetResult Compose(FrameSequence sequence, string outputDirectory, string sheetName)
        {
            var notes = new List<string>();
            if (sequence == null || sequence.FrameCount == 0)
            {
                return new SpriteSheetResult(false, "", "", 0, 0, notes, "帧序列是空的，拼不出图集");
            }

            var decoded = new List<PngImage>();
            foreach (var frame in sequence.Frames)
            {
                var result = PngDecoder.DecodeFile(frame.Path);
                if (!result.Succeeded)
                {
                    // 缺一帧就整次失败：拼出来少一帧的动画，播起来是「偶尔跳一下」，
                    // 那种错人会先怀疑是自己的播放代码，查很久才回到这里。
                    return new SpriteSheetResult(
                        false, "", "", 0, 0, notes,
                        $"第 {frame.Index} 帧读不了（{frame.Path}）：{result.FailureReason}");
                }

                decoded.Add(result.Image);
            }

            var cellWidth = 0;
            var cellHeight = 0;
            foreach (var image in decoded)
            {
                cellWidth = Math.Max(cellWidth, image.Width);
                cellHeight = Math.Max(cellHeight, image.Height);
            }

            var uneven = false;
            foreach (var image in decoded)
            {
                if (image.Width != cellWidth || image.Height != cellHeight)
                {
                    uneven = true;
                    break;
                }
            }

            if (uneven)
            {
                notes.Add($"帧尺寸不齐，格子按最大的 {cellWidth}×{cellHeight} 留，小的那几帧按锚点「{sequence.Anchor}」摆进去（没有缩放）");
            }

            var sheetWidth = cellWidth * decoded.Count;
            var pixels = new byte[(long)sheetWidth * cellHeight * 4];

            for (var index = 0; index < decoded.Count; index++)
            {
                var image = decoded[index];
                var offsetX = index * cellWidth + AnchorOffsetX(sequence.Anchor, cellWidth, image.Width);
                var offsetY = AnchorOffsetY(sequence.Anchor, cellHeight, image.Height);
                Blit(pixels, sheetWidth, image, offsetX, offsetY);
            }

            Directory.CreateDirectory(outputDirectory);
            var sheetPath = Path.Combine(outputDirectory, sheetName + ".png");
            if (!PngEncoder.EncodeToFile(new PngImage(sheetWidth, cellHeight, pixels), sheetPath, out var encodeReason))
            {
                return new SpriteSheetResult(false, "", "", cellWidth, cellHeight, notes, "图集写不出来：" + encodeReason);
            }

            var metadataPath = Path.Combine(outputDirectory, MetadataFileName);
            var metadata = new JsonObject
            {
                ["契约版本"] = FrameSequence.ContractVersion,
                ["图集"] = Path.GetFileName(sheetPath),
                ["种类"] = sequence.Kind,
                ["帧数"] = sequence.FrameCount,
                ["帧率"] = sequence.FrameRate,
                ["锚点"] = sequence.Anchor,
                ["格宽"] = cellWidth,
                ["格高"] = cellHeight,
                ["排布"] = "横排一行",
                ["_说明"] = "这份是给 Unity 侧切图用的：横排一行、每格 格宽×格高、第 N 格就是第 N 帧。"
                    + "切精灵与建 clip 走命令层（铁律 2），不要手写 .asset。"
            };

            File.WriteAllText(metadataPath, metadata.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return new SpriteSheetResult(true, sheetPath, metadataPath, cellWidth, cellHeight, notes, "");
        }

        /// <summary>按锚点算横向偏移：底边中点与中心都居中，左上角贴左。</summary>
        private static int AnchorOffsetX(string anchor, int cellWidth, int imageWidth)
        {
            return string.Equals(anchor, "左上角", StringComparison.Ordinal) ? 0 : (cellWidth - imageWidth) / 2;
        }

        /// <summary>按锚点算纵向偏移：底边中点贴底，中心居中，左上角贴顶。</summary>
        private static int AnchorOffsetY(string anchor, int cellHeight, int imageHeight)
        {
            if (string.Equals(anchor, "左上角", StringComparison.Ordinal))
            {
                return 0;
            }

            if (string.Equals(anchor, "中心", StringComparison.Ordinal))
            {
                return (cellHeight - imageHeight) / 2;
            }

            return cellHeight - imageHeight;
        }

        /// <summary>把一帧贴进图集缓冲区，整块覆盖（帧本身带 alpha，格子之间不重叠）。</summary>
        private static void Blit(byte[] target, int targetWidth, PngImage image, int offsetX, int offsetY)
        {
            for (var y = 0; y < image.Height; y++)
            {
                var sourceRow = (long)y * image.Width * 4;
                var targetRow = ((long)(y + offsetY) * targetWidth + offsetX) * 4;
                for (var x = 0; x < image.Width * 4; x++)
                {
                    target[targetRow + x] = image.Pixels[(int)(sourceRow + x)];
                }
            }
        }
    }
}
