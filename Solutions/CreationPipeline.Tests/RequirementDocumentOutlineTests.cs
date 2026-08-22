using System.Linq;
using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 需求文档拆成中性块的测试。这一族守的是两件事：
    /// **认得出规范里那份 index.md 的全部写法**，以及**认不出来的写法降级成段落而不是把整条链停住**。
    /// </summary>
    public class RequirementDocumentOutlineTests
    {
        /// <summary>规范第一节那份样例的骨架：frontmatter + 标题 + 小节 + 有序/无序 + 媒体 + 生成区。</summary>
        private const string SampleDocument = """
            ---
            需求id: REQ-0042
            标题: 七日签到
            同步:
              节点token: wikcnAAAA
            ---

            # 七日签到

            ## 目标
            次留提升。签到是新手期唯一的
            每日回访钩子。

            ## 验收标准
            1. 登录后自动弹出签到界面
            2. 第 7 天发放大奖

            ## 边界与不做
            - 首月不做补签
            - 不接排行榜

            ## 参考媒体
            ![签到主界面，第 3 天已领](media/signin-main.png)

            <!-- 生成区开始：勿手改，doc.render 重生成 -->
            ## 关联
            - 工作项：WI-0042-01
            <!-- 生成区结束 -->
            """;

        /// <summary>frontmatter 不进块流：它是给机器判定的字段，不是正文。</summary>
        [Fact]
        public void FrontMatterDoesNotBecomeBlocks()
        {
            var blocks = RequirementDocumentOutline.Build(SampleDocument);

            Assert.DoesNotContain(blocks, block => block.Text.Contains("wikcnAAAA"));
            Assert.DoesNotContain(blocks, block => block.Text.Contains("需求id"));
            Assert.Equal(RequirementDocumentOutline.KindHeading, blocks[0].Kind);
            Assert.Equal("七日签到", blocks[0].Text);
            Assert.Equal(1, blocks[0].Level);
        }

        /// <summary>二级标题读成层级 2。</summary>
        [Fact]
        public void SectionHeadingsCarryTheirLevel()
        {
            var blocks = RequirementDocumentOutline.Build(SampleDocument);

            var section = blocks.Single(block => block.Text == "验收标准");
            Assert.Equal(RequirementDocumentOutline.KindHeading, section.Kind);
            Assert.Equal(2, section.Level);
        }

        /// <summary>连着的几行普通文字合成一段，不是一行一块。</summary>
        [Fact]
        public void WrappedLinesJoinIntoOneParagraph()
        {
            var blocks = RequirementDocumentOutline.Build(SampleDocument);

            var paragraph = blocks.Single(block => block.Kind == RequirementDocumentOutline.KindParagraph);
            Assert.Equal("次留提升。签到是新手期唯一的 每日回访钩子。", paragraph.Text);
        }

        /// <summary>有序项去掉序号（下游自己会编号），无序项去掉那根横线。</summary>
        [Fact]
        public void ListItemsDropTheirMarkers()
        {
            var blocks = RequirementDocumentOutline.Build(SampleDocument);

            var ordered = blocks.Where(block => block.Kind == RequirementDocumentOutline.KindOrderedItem).ToList();
            Assert.Equal(new[] { "登录后自动弹出签到界面", "第 7 天发放大奖" }, ordered.Select(block => block.Text));

            var bullets = blocks.Where(block => block.Kind == RequirementDocumentOutline.KindBulletItem).ToList();
            Assert.Equal(new[] { "首月不做补签", "不接排行榜", "工作项：WI-0042-01" }, bullets.Select(block => block.Text));
        }

        /// <summary>媒体引用读成媒体块：说明进文本，相对路径进目标。</summary>
        [Fact]
        public void MediaReferenceKeepsCaptionAndPath()
        {
            var blocks = RequirementDocumentOutline.Build(SampleDocument);

            var media = blocks.Single(block => block.Kind == RequirementDocumentOutline.KindMedia);
            Assert.Equal("签到主界面，第 3 天已领", media.Text);
            Assert.Equal("media/signin-main.png", media.Target);
        }

        /// <summary>生成区的两条注释标记丢掉，中间的正文照留。</summary>
        [Fact]
        public void GeneratedRegionMarkersAreDroppedButContentStays()
        {
            var blocks = RequirementDocumentOutline.Build(SampleDocument);

            Assert.DoesNotContain(blocks, block => block.Text.Contains("生成区开始"));
            Assert.DoesNotContain(blocks, block => block.Text.Contains("生成区结束"));
            Assert.Contains(blocks, block => block.Text == "关联");
            Assert.Contains(blocks, block => block.Text == "工作项：WI-0042-01");
        }

        /// <summary>不是 media/ 的链接不算媒体块，当普通段落走——那是外链，本体不在仓库里。</summary>
        [Fact]
        public void LinkOutsideMediaDirectoryIsNotAMediaBlock()
        {
            var blocks = RequirementDocumentOutline.Build("[外部设计稿](https://example.invalid/a.png)");

            var only = Assert.Single(blocks);
            Assert.Equal(RequirementDocumentOutline.KindParagraph, only.Kind);
        }

        /// <summary>代码围栏整段成一个代码块，围栏行本身不进正文。</summary>
        [Fact]
        public void CodeFenceBecomesOneCodeBlock()
        {
            var blocks = RequirementDocumentOutline.Build("```json\n{\n  \"a\": 1\n}\n```");

            var only = Assert.Single(blocks);
            Assert.Equal(RequirementDocumentOutline.KindCode, only.Kind);
            Assert.Equal("{\n  \"a\": 1\n}", only.Text);
        }

        /// <summary>四级标题这种没见过的写法降级成段落，不报错——文档是人写的，不许因此停住整条链。</summary>
        [Fact]
        public void UnknownShapeFallsBackToParagraph()
        {
            var blocks = RequirementDocumentOutline.Build("#### 四级标题\n\n| 表 | 格 |");

            Assert.Equal(2, blocks.Count);
            Assert.All(blocks, block => Assert.Equal(RequirementDocumentOutline.KindParagraph, block.Kind));
        }

        /// <summary>没有 frontmatter 的文档整篇都是正文，不会被当成「第一行是横线」吃掉。</summary>
        [Fact]
        public void DocumentWithoutFrontMatterKeepsEverything()
        {
            var blocks = RequirementDocumentOutline.Build("# 标题\n\n正文");

            Assert.Equal(2, blocks.Count);
            Assert.Equal("标题", blocks[0].Text);
            Assert.Equal("正文", blocks[1].Text);
        }

        /// <summary>序列化再读回来是同一串块：信封两头靠这个对齐。</summary>
        [Fact]
        public void JsonRoundTripKeepsEveryField()
        {
            var blocks = RequirementDocumentOutline.Build(SampleDocument);

            var json = RequirementDocumentOutline.ToJsonArray(blocks).ToJsonString();
            var restored = RequirementDocumentOutline.FromJsonArray(JsonSerializer.Deserialize<JsonElement>(json));

            Assert.Equal(blocks.Count, restored.Count);
            for (var index = 0; index < blocks.Count; index++)
            {
                Assert.Equal(blocks[index].Kind, restored[index].Kind);
                Assert.Equal(blocks[index].Text, restored[index].Text);
                Assert.Equal(blocks[index].Level, restored[index].Level);
                Assert.Equal(blocks[index].Target, restored[index].Target);
            }
        }
    }
}
