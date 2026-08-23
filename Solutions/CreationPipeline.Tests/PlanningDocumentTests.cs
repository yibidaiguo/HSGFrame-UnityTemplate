using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>PlanningDocument 的解析行为测试：frontmatter 三种形状、小节、生成区与哈希。</summary>
    public class PlanningDocumentTests
    {
        /// <summary>造一份规范对象：直接读工作区里那份真的基线，不另写假契约。</summary>
        private static PlanningDocumentSpec LoadSpecification(PoolTestWorkspace workspace)
        {
            workspace.CopyPlanningDocumentBaseline();
            return PlanningDocumentSpec.Load(workspace.Root);
        }

        /// <summary>标量、一层嵌套映射、对象列表三种形状都读得出来。</summary>
        [Fact]
        public void ParsesScalarsMapsAndObjectLists()
        {
            using var workspace = new PoolTestWorkspace();
            var specification = LoadSpecification(workspace);
            var text = """
            ---
            需求id: REQ-0042
            标题: 七日签到
            同步:
              节点token: wikcnABC
              链接: https://example.feishu.cn/wiki/wikcnABC
            媒体:
              - 路径: media/a.png
                说明: 主界面，第 3 天已领
              - 路径: media/b.mp4
                说明: 断签重计数的录屏
            ---

            # 七日签到
            """;

            Assert.True(PlanningDocument.TryParse(text, specification, out var document, out var reason));

            Assert.Equal("", reason);
            Assert.True(document.FrontMatter.IsPresent);
            Assert.Equal("REQ-0042", document.FrontMatter.Scalar("需求id"));
            Assert.Equal("wikcnABC", document.FrontMatter.Map("同步")["节点token"]);
            Assert.Equal(2, document.FrontMatter.List("媒体").Count);
            Assert.Equal("media/b.mp4", document.FrontMatter.List("媒体")[1]["路径"]);
            Assert.Equal("断签重计数的录屏", document.FrontMatter.List("媒体")[1]["说明"]);
        }

        /// <summary>值里 # 之后当注释；整个值加引号时井号照原样留着。</summary>
        [Fact]
        public void StripsCommentsButKeepsQuotedHashes()
        {
            using var workspace = new PoolTestWorkspace();
            var specification = LoadSpecification(workspace);
            var text = """
            ---
            权威侧: 项目                    # 飞书 | 项目
            标题: "第 3 天 #签到"
            ---
            """;

            Assert.True(PlanningDocument.TryParse(text, specification, out var document, out _));

            Assert.Equal("项目", document.FrontMatter.Scalar("权威侧"));
            Assert.Equal("第 3 天 #签到", document.FrontMatter.Scalar("标题"));
        }

        /// <summary>frontmatter 开了头没收尾时解析失败，原因里点明这件事。</summary>
        [Fact]
        public void UnclosedFrontMatterFailsToParse()
        {
            using var workspace = new PoolTestWorkspace();
            var specification = LoadSpecification(workspace);
            var text = """
            ---
            需求id: REQ-0042

            # 七日签到
            """;

            Assert.False(PlanningDocument.TryParse(text, specification, out _, out var reason));

            Assert.Contains("frontmatter", reason);
        }

        /// <summary>生成区里的小节标成 IsInGeneratedRegion，生成区正文不含两条标记行。</summary>
        [Fact]
        public void MarksSectionsInsideGeneratedRegion()
        {
            using var workspace = new PoolTestWorkspace();
            var specification = LoadSpecification(workspace);
            var text = """
            ---
            需求id: REQ-0042
            ---

            # 七日签到

            ## 目标
            次留提升。

            <!-- 生成区开始：勿手改，doc.render 重生成 -->
            ## 关联
            - 设计记录：DR-0107
            <!-- 生成区结束 -->
            """;

            Assert.True(PlanningDocument.TryParse(text, specification, out var document, out _));

            Assert.True(document.HasGeneratedRegion);
            Assert.Equal(2, document.Sections.Count);
            Assert.False(document.Sections[0].IsInGeneratedRegion);
            Assert.True(document.Sections[1].IsInGeneratedRegion);
            Assert.Equal(new[] { "## 关联", "- 设计记录：DR-0107" }, document.GeneratedRegionLines);
        }

        /// <summary>代码围栏里的 ## 与生成区标记都不算数——那是正文，不是结构。</summary>
        [Fact]
        public void IgnoresHeadingsAndMarkersInsideCodeFences()
        {
            using var workspace = new PoolTestWorkspace();
            var specification = LoadSpecification(workspace);
            var text = """
            ---
            需求id: REQ-0042
            ---

            # 七日签到

            ## 目标
            ```markdown
            ## 这不是小节
            <!-- 生成区开始：勿手改，doc.render 重生成 -->
            ```
            """;

            Assert.True(PlanningDocument.TryParse(text, specification, out var document, out _));

            Assert.Single(document.Sections);
            Assert.Equal("目标", document.Sections[0].Title);
            Assert.False(document.HasGeneratedRegion);
        }

        /// <summary>生成区哈希不受行尾空白与末尾空行影响，改一个字就变。</summary>
        [Fact]
        public void GeneratedRegionHashIgnoresTrailingWhitespaceOnly()
        {
            var baseline = PlanningDocument.HashGeneratedRegion(new[] { "## 关联", "- 设计记录：DR-0107" });
            var padded = PlanningDocument.HashGeneratedRegion(new[] { "## 关联", "- 设计记录：DR-0107   ", "", "" });
            var edited = PlanningDocument.HashGeneratedRegion(new[] { "## 关联", "- 设计记录：DR-0108" });

            Assert.Equal(baseline, padded);
            Assert.NotEqual(baseline, edited);
            Assert.StartsWith("sha256:", baseline);
        }
    }
}
