using System;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>RequirementDocumentRenderer（doc.render）的渲染行为测试。</summary>
    public class RequirementDocumentRendererTests
    {
        /// <summary>一份「系统」类型的需求骨架，分类型必填字段齐全。</summary>
        private static string SystemRequirementJson()
        {
            return new JsonObject
            {
                ["id"] = "REQ-0042",
                ["类型"] = "系统",
                ["状态"] = "已确认",
                ["标题"] = "七日签到",
                ["目标"] = "次留提升。",
                ["玩法"] = "连续七天，每天领一次。",
                ["验收标准"] = new JsonArray { "登录后自动弹出签到界面", "第 7 天发放大奖" },
                ["关联设计记录"] = new JsonArray { "DR-0107" },
                ["依赖"] = new JsonArray(),
                ["锁定"] = false,
                ["schema版本"] = "1.0.0"
            }.ToJsonString();
        }

        private static (PoolTestWorkspace Workspace, RequirementDocumentSpec Specification) NewWorkspace()
        {
            var workspace = new PoolTestWorkspace();
            workspace.CopyRequirementDocumentBaseline();
            workspace.WriteRequirement("REQ-0042", SystemRequirementJson());
            return (workspace, RequirementDocumentSpec.Load(workspace.Root));
        }

        /// <summary>新建：frontmatter 六个必备键齐全，小节按序在位，验收标准渲成有序列表，生成区在末尾。</summary>
        [Fact]
        public void CreatesSkeletonFromRequirementJson()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                var outcome = RequirementDocumentRenderer.Render(
                    workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, false);

                Assert.True(outcome.IsCreated);
                Assert.True(outcome.IsChanged);
                Assert.Equal(new[] { "目标", "玩法", "验收标准", "边界与不做" }, outcome.AddedSections);

                var text = workspace.ReadRequirementDocument("REQ-0042");
                Assert.Contains("需求id: REQ-0042", text);
                Assert.Contains("权威侧: 项目", text);
                Assert.Contains("文档版本: 1", text);
                Assert.Contains("# 七日签到", text);
                Assert.Contains("1. 登录后自动弹出签到界面", text);
                Assert.Contains("2. 第 7 天发放大奖", text);
                Assert.Contains("- 设计记录：DR-0107", text);
                Assert.Contains("- 工作项：尚未规划", text);
                Assert.EndsWith(specification.GeneratedRegionEnd + "\n", text);
            }
        }

        /// <summary>连跑两次结果一模一样——生成器不幂等，幂等门禁就是摆设。</summary>
        [Fact]
        public void RenderingTwiceProducesIdenticalText()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RequirementDocumentRenderer.Render(workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, false);
                var first = workspace.ReadRequirementDocument("REQ-0042");

                var second = RequirementDocumentRenderer.Render(
                    workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, false);

                Assert.False(second.IsChanged);
                Assert.Equal(first, workspace.ReadRequirementDocument("REQ-0042"));
            }
        }

        /// <summary>刷新：人写的正文一个字不动，只补缺掉的小节与工程负责的 frontmatter 键。</summary>
        [Fact]
        public void RefreshKeepsHandWrittenProseAndOnlyAddsWhatIsMissing()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                workspace.WriteRequirementFile("REQ-0042", "index.md", """
                ---
                需求id: REQ-0042
                标题: 旧标题
                文档版本: 7
                权威侧: 飞书
                ---

                # 七日签到

                ## 目标
                这段是人自己写的，一个字都不许动。

                ## 玩法
                人写的玩法。
                """);

                var outcome = RequirementDocumentRenderer.Render(
                    workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, false);

                var text = workspace.ReadRequirementDocument("REQ-0042");
                Assert.False(outcome.IsCreated);
                Assert.Equal(new[] { "验收标准", "边界与不做" }, outcome.AddedSections);
                Assert.Contains("这段是人自己写的，一个字都不许动。", text);
                Assert.Contains("人写的玩法。", text);

                // 工程所有权的字段跟着骨架走，人自己那两个键原样留着。
                Assert.Contains("标题: 七日签到", text);
                Assert.DoesNotContain("标题: 旧标题", text);
                Assert.Contains("文档版本: 7", text);
                Assert.Contains("权威侧: 飞书", text);
            }
        }

        /// <summary>补出来的小节插在按规范排在它后面的那一节之前，不是一律甩到末尾。</summary>
        [Fact]
        public void InsertsMissingSectionInSpecifiedOrder()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                workspace.WriteRequirementFile("REQ-0042", "index.md", """
                ---
                需求id: REQ-0042
                ---

                # 七日签到

                ## 目标
                人写的目标。

                ## 验收标准
                1. 人写的验收标准

                ## 边界与不做
                首月不做补签。
                """);

                RequirementDocumentRenderer.Render(workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, false);

                var text = workspace.ReadRequirementDocument("REQ-0042");
                Assert.True(text.IndexOf("## 目标", StringComparison.Ordinal) < text.IndexOf("## 玩法", StringComparison.Ordinal));
                Assert.True(text.IndexOf("## 玩法", StringComparison.Ordinal) < text.IndexOf("## 验收标准", StringComparison.Ordinal));
            }
        }

        /// <summary>生成区被手改过：重渲染把它整段换回来，并把哈希写成新的。</summary>
        [Fact]
        public void RegeneratesTamperedGeneratedRegion()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RequirementDocumentRenderer.Render(workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, false);
                var tampered = workspace.ReadRequirementDocument("REQ-0042")
                    .Replace("- 工作项：尚未规划", "- 工作项：我自己手改的");
                workspace.WriteRequirementFile("REQ-0042", "index.md", tampered);

                RequirementDocumentRenderer.Render(workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, false);

                var text = workspace.ReadRequirementDocument("REQ-0042");
                Assert.DoesNotContain("我自己手改的", text);
                Assert.Contains("- 工作项：尚未规划", text);
            }
        }

        /// <summary>干跑算得出全文，但一个字节都不写盘。</summary>
        [Fact]
        public void DryRunDoesNotWriteFile()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                var outcome = RequirementDocumentRenderer.Render(
                    workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, true);

                Assert.True(outcome.IsChanged);
                Assert.Contains("# 七日签到", outcome.DocumentText);
                Assert.Equal("", workspace.ReadRequirementDocument("REQ-0042"));
            }
        }

        /// <summary>需求骨架不存在时抛 InvalidOperationException，消息里带路径。</summary>
        [Fact]
        public void MissingRequirementThrows()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                var exception = Assert.Throws<InvalidOperationException>(() => RequirementDocumentRenderer.Render(
                    workspace.RepositoryRoot, workspace.Root, "REQ-9999", specification, false));

                Assert.Contains("REQ-9999", exception.Message);
            }
        }
    }
}
