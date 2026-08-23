using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 中性文档块 → 飞书 docx 块。与 <see cref="FeishuFieldTypeCodec"/> 同一个位置、同一个职责：
    /// **「飞书长什么样」这件事只许住在飞书桥里**，工具链那边出的是中性的
    /// <see cref="PlanningDocumentOutline"/>，一个 block_type 数字都不认识。
    ///
    /// block_type 取值是飞书 docx 定的：2 文本、3/4/5 一二三级标题、12 无序项、13 有序项、
    /// 14 代码块、15 引用。认不出来的中性类型一律降级成文本块——
    /// 一段话在下游长得朴素一点是小事，整篇文档因为一个块型没认出来推不上去是大事。
    /// </summary>
    public static class FeishuBlockCodec
    {
        /// <summary>飞书 docx 的块类型编号。</summary>
        private const int BlockTypeText = 2;

        private const int BlockTypeHeading1 = 3;
        private const int BlockTypeHeading2 = 4;
        private const int BlockTypeHeading3 = 5;
        private const int BlockTypeBullet = 12;
        private const int BlockTypeOrdered = 13;
        private const int BlockTypeCode = 14;
        private const int BlockTypeQuote = 15;

        /// <summary>图片块。素材要在块建出来之后再传（parent_type=docx_image）。</summary>
        private const int BlockTypeImage = 27;

        /// <summary>文件块。视频与其它附件都落它（parent_type=docx_file）。</summary>
        private const int BlockTypeFile = 23;

        /// <summary>按后缀认图片；认不出来的一律当文件——当图片传会被飞书拒收，当文件传最多是显示朴素点。</summary>
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
        };

        /// <summary>一处要在块建好之后补传的素材：它是第几个 child、本体在哪、当图片还是当文件传。</summary>
        public sealed class PendingMedia
        {
            /// <summary>构造一处待传素材。</summary>
            /// <param name="childIndex">它在 children 数组里的下标。</param>
            /// <param name="relativePath">相对需求目录的路径，如 media/x.png。</param>
            /// <param name="isImage">当图片传还是当文件传。</param>
            public PendingMedia(int childIndex, string relativePath, bool isImage)
            {
                ChildIndex = childIndex;
                RelativePath = relativePath ?? "";
                IsImage = isImage;
            }

            /// <summary>它在 children 数组里的下标。</summary>
            public int ChildIndex { get; }

            /// <summary>相对需求目录的路径。</summary>
            public string RelativePath { get; }

            /// <summary>当图片传还是当文件传。</summary>
            public bool IsImage { get; }

            /// <summary>素材挂在什么上：飞书要的 parent_type。</summary>
            public string ParentType
            {
                get { return IsImage ? "docx_image" : "docx_file"; }
            }
        }

        /// <summary>
        /// 把一串中性块翻成 docx 的 children 数组，直接挂进
        /// <c>POST /docx/v1/documents/{id}/blocks/{id}/children</c> 的请求体。
        /// </summary>
        /// <param name="blocks">中性块。</param>
        public static JsonArray ToChildren(IReadOnlyList<PlanningDocumentOutlineBlock> blocks)
        {
            return ToChildren(blocks, out _);
        }

        /// <summary>
        /// 同上，另外交出**待传素材清单**：图片与文件块只能先建空的、拿到 block_id 再把本体传上去，
        /// 所以这里只能告诉调用方「第几个 child 要补传哪个文件」，真传是写完块之后的事。
        /// </summary>
        /// <param name="blocks">中性块。</param>
        /// <param name="pendingMedia">要在块建好后补传的素材。</param>
        public static JsonArray ToChildren(
            IReadOnlyList<PlanningDocumentOutlineBlock> blocks,
            out IReadOnlyList<PendingMedia> pendingMedia)
        {
            var children = new JsonArray();
            var pending = new List<PendingMedia>();
            pendingMedia = pending;
            if (blocks == null)
            {
                return children;
            }

            foreach (var block in blocks)
            {
                if (string.Equals(block.Kind, PlanningDocumentOutline.KindMedia, StringComparison.Ordinal)
                    && block.Target.Length > 0)
                {
                    var isImage = ImageExtensions.Contains(Path.GetExtension(block.Target));
                    pending.Add(new PendingMedia(children.Count, block.Target, isImage));
                    children.Add(MediaChild(isImage));
                    continue;
                }

                children.Add(ToChild(block));
            }

            return children;
        }

        /// <summary>建一个空的图片 / 文件块：token 先留空，素材随后按 block_id 传上去补。</summary>
        /// <param name="isImage">图片块还是文件块。</param>
        private static JsonObject MediaChild(bool isImage)
        {
            return isImage
                ? new JsonObject { ["block_type"] = BlockTypeImage, ["image"] = new JsonObject { ["token"] = "" } }
                : new JsonObject { ["block_type"] = BlockTypeFile, ["file"] = new JsonObject { ["token"] = "" } };
        }

        /// <summary>翻一个块。</summary>
        /// <param name="block">中性块。</param>
        private static JsonObject ToChild(PlanningDocumentOutlineBlock block)
        {
            switch (block.Kind)
            {
                case PlanningDocumentOutline.KindHeading:
                    return HeadingChild(block);
                case PlanningDocumentOutline.KindOrderedItem:
                    return TextChild(BlockTypeOrdered, "ordered", block.Text);
                case PlanningDocumentOutline.KindBulletItem:
                    return TextChild(BlockTypeBullet, "bullet", block.Text);
                case PlanningDocumentOutline.KindQuote:
                    return TextChild(BlockTypeQuote, "quote", block.Text);
                case PlanningDocumentOutline.KindCode:
                    return TextChild(BlockTypeCode, "code", block.Text);
                case PlanningDocumentOutline.KindMedia:
                    return TextChild(BlockTypeText, "text", MediaLine(block));
                default:
                    return TextChild(BlockTypeText, "text", block.Text);
            }
        }

        /// <summary>标题块：层级 1/2/3 对应 heading1/2/3，超出三级的按三级放。</summary>
        private static JsonObject HeadingChild(PlanningDocumentOutlineBlock block)
        {
            switch (block.Level)
            {
                case 1:
                    return TextChild(BlockTypeHeading1, "heading1", block.Text);
                case 2:
                    return TextChild(BlockTypeHeading2, "heading2", block.Text);
                default:
                    return TextChild(BlockTypeHeading3, "heading3", block.Text);
            }
        }

        /// <summary>
        /// 媒体块暂时降级成一行带路径的文字。
        ///
        /// 真图要走「上传素材 → 建 image 块 → 把素材挂进那个块」三步，
        /// 而那三步与建节点吃的是同一份编辑权限——权限没到位之前一步都跑不了，
        /// 跑不了的东西不写（决策 91）。降级成文字至少让「这里有张图、图里是什么」跟着文档过去，
        /// 而不是悄悄丢掉。真做那三步的条件与做法记在待办账本里。
        /// </summary>
        private static string MediaLine(PlanningDocumentOutlineBlock block)
        {
            var caption = block.Text.Length == 0 ? "（无说明）" : block.Text;
            return $"［媒体］{caption}（{block.Target}，本体在仓库里，暂未随文档上传）";
        }

        /// <summary>拼一个「带一段纯文字」的块：块型编号 + 同名的属性对象 + 一个 text_run。</summary>
        private static JsonObject TextChild(int blockType, string propertyName, string content)
        {
            return new JsonObject
            {
                ["block_type"] = blockType,
                [propertyName] = new JsonObject
                {
                    ["elements"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["text_run"] = new JsonObject
                            {
                                ["content"] = content ?? ""
                            }
                        }
                    }
                }
            };
        }
    }
}
