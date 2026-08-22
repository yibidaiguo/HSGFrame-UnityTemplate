using System.Collections.Generic;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 中性文档块 → 飞书 docx 块。与 <see cref="FeishuFieldTypeCodec"/> 同一个位置、同一个职责：
    /// **「飞书长什么样」这件事只许住在飞书桥里**，工具链那边出的是中性的
    /// <see cref="RequirementDocumentOutline"/>，一个 block_type 数字都不认识。
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

        /// <summary>
        /// 把一串中性块翻成 docx 的 children 数组，直接挂进
        /// <c>POST /docx/v1/documents/{id}/blocks/{id}/children</c> 的请求体。
        /// </summary>
        /// <param name="blocks">中性块。</param>
        public static JsonArray ToChildren(IReadOnlyList<RequirementDocumentOutlineBlock> blocks)
        {
            var children = new JsonArray();
            if (blocks == null)
            {
                return children;
            }

            foreach (var block in blocks)
            {
                children.Add(ToChild(block));
            }

            return children;
        }

        /// <summary>翻一个块。</summary>
        /// <param name="block">中性块。</param>
        private static JsonObject ToChild(RequirementDocumentOutlineBlock block)
        {
            switch (block.Kind)
            {
                case RequirementDocumentOutline.KindHeading:
                    return HeadingChild(block);
                case RequirementDocumentOutline.KindOrderedItem:
                    return TextChild(BlockTypeOrdered, "ordered", block.Text);
                case RequirementDocumentOutline.KindBulletItem:
                    return TextChild(BlockTypeBullet, "bullet", block.Text);
                case RequirementDocumentOutline.KindQuote:
                    return TextChild(BlockTypeQuote, "quote", block.Text);
                case RequirementDocumentOutline.KindCode:
                    return TextChild(BlockTypeCode, "code", block.Text);
                case RequirementDocumentOutline.KindMedia:
                    return TextChild(BlockTypeText, "text", MediaLine(block));
                default:
                    return TextChild(BlockTypeText, "text", block.Text);
            }
        }

        /// <summary>标题块：层级 1/2/3 对应 heading1/2/3，超出三级的按三级放。</summary>
        private static JsonObject HeadingChild(RequirementDocumentOutlineBlock block)
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
        private static string MediaLine(RequirementDocumentOutlineBlock block)
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
