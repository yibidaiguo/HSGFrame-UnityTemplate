using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>RequirementDocumentChecker（gate.reqdoc）的六条判据测试。</summary>
    public class RequirementDocumentCheckerTests
    {
        private static string SystemRequirementJson()
        {
            return new JsonObject
            {
                ["id"] = "REQ-0042",
                ["类型"] = "系统",
                ["状态"] = "已确认",
                ["标题"] = "七日签到",
                ["目标"] = "次留提升。",
                ["玩法"] = "连续七天。",
                ["验收标准"] = new JsonArray { "登录后自动弹出签到界面" },
                ["关联设计记录"] = new JsonArray(),
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

        // 先用 doc.render 渲一份合规的，再按测试意图破坏它——测的是「改坏了会不会红」，
        // 而不是「我手写的这份样本合不合规」。
        private static void RenderThenEdit(
            PoolTestWorkspace workspace,
            RequirementDocumentSpec specification,
            System.Func<string, string> edit)
        {
            RequirementDocumentRenderer.Render(workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, false);
            if (edit != null)
            {
                workspace.WriteRequirementFile("REQ-0042", "index.md", edit(workspace.ReadRequirementDocument("REQ-0042")));
            }
        }

        private static IReadOnlyList<string> ReasonsOf(PoolTestWorkspace workspace, RequirementDocumentSpec specification)
        {
            return RequirementDocumentChecker.CheckOne(workspace.Root, "REQ-0042", specification)
                .Select(finding => finding.Reason)
                .ToList();
        }

        /// <summary>doc.render 刚渲出来的文档一条违规都没有——两边读的是同一份契约，本来就该对得上。</summary>
        [Fact]
        public void FreshlyRenderedDocumentPasses()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RenderThenEdit(workspace, specification, null);

                Assert.Empty(RequirementDocumentChecker.CheckOne(workspace.Root, "REQ-0042", specification));
            }
        }

        /// <summary>没有 index.md 不算违规：需求可以先有骨架后有文档。</summary>
        [Fact]
        public void MissingDocumentIsNotAViolation()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                Assert.Empty(RequirementDocumentChecker.CheckOne(workspace.Root, "REQ-0042", specification));
            }
        }

        /// <summary>一、frontmatter 必备键缺一条报一条。</summary>
        [Fact]
        public void ReportsMissingFrontMatterKey()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RenderThenEdit(workspace, specification, text => text.Replace("权威侧: 项目\n", ""));

                Assert.Contains(ReasonsOf(workspace, specification), reason => reason.Contains("缺必备键「权威侧」"));
            }
        }

        /// <summary>二、frontmatter 的需求id 与目录名对不上就红。</summary>
        [Fact]
        public void ReportsIdentifierMismatch()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RenderThenEdit(workspace, specification, text => text.Replace("需求id: REQ-0042", "需求id: REQ-0099"));

                Assert.Contains(ReasonsOf(workspace, specification), reason => reason.Contains("与所在目录名"));
            }
        }

        /// <summary>三、必填小节缺了报缺，顺序倒了报乱序。</summary>
        [Fact]
        public void ReportsMissingAndOutOfOrderSections()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RenderThenEdit(workspace, specification, text => text.Replace("## 边界与不做\n（待补）\n\n", ""));
                Assert.Contains(ReasonsOf(workspace, specification), reason => reason.Contains("缺必填小节「边界与不做」"));

                workspace.WriteRequirementFile("REQ-0042", "index.md", """
                ---
                需求id: REQ-0042
                标题: 七日签到
                类型: 系统
                状态: 已确认
                文档版本: 1
                权威侧: 项目
                ---

                # 七日签到

                ## 玩法
                连续七天。

                ## 目标
                次留提升。

                ## 验收标准
                1. 登录后自动弹出签到界面

                ## 边界与不做
                首月不做补签。
                """);

                Assert.Contains(ReasonsOf(workspace, specification), reason => reason.Contains("与规范定的顺序相反"));
            }
        }

        /// <summary>四、验收标准写成散文就红——阶段 4 逐条核，核不动散文。</summary>
        [Fact]
        public void ReportsProseAcceptanceCriteria()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RenderThenEdit(
                    workspace,
                    specification,
                    text => text.Replace("1. 登录后自动弹出签到界面", "登录之后要弹出来，已领的要灰掉，第七天还要发大奖。"));

                var reasons = ReasonsOf(workspace, specification);
                Assert.Contains(reasons, reason => reason.Contains("不是有序列表条目"));
                Assert.Contains(reasons, reason => reason.Contains("没有任何条目"));
            }
        }

        /// <summary>五、媒体：文件不存在、名字非 ASCII、缺说明，三样各报一条。</summary>
        [Fact]
        public void ReportsMediaProblems()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RenderThenEdit(workspace, specification, text => text
                    .Replace("权威侧: 项目", """
                    权威侧: 项目
                    媒体:
                      - 路径: media/signin-main.png
                        说明: 签到主界面，第 3 天已领
                      - 路径: media/没说明的图.png
                    """)
                    .Replace("## 边界与不做", "![断签录屏](media/missing.mp4)\n\n## 边界与不做"));

                var reasons = ReasonsOf(workspace, specification);
                Assert.Contains(reasons, reason => reason.Contains("media/signin-main.png") && reason.Contains("不存在"));
                Assert.Contains(reasons, reason => reason.Contains("没写说明"));
                Assert.Contains(reasons, reason => reason.Contains("非 ASCII"));
                Assert.Contains(reasons, reason => reason.Contains("media/missing.mp4") && reason.Contains("不存在"));
            }
        }

        /// <summary>五之二：媒体文件真放进去了就不报了。</summary>
        [Fact]
        public void RegisteredMediaThatExistsPasses()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                workspace.WriteRequirementFile("REQ-0042", "media/signin-main.png", "假装这是一张图");
                RenderThenEdit(workspace, specification, text => text
                    .Replace("权威侧: 项目", """
                    权威侧: 项目
                    媒体:
                      - 路径: media/signin-main.png
                        说明: 签到主界面，第 3 天已领、第 4 天可领
                    """));

                Assert.Empty(ReasonsOf(workspace, specification));
            }
        }

        /// <summary>六、生成区被手改：哈希对不上就红。</summary>
        [Fact]
        public void ReportsTamperedGeneratedRegion()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RenderThenEdit(workspace, specification, text => text.Replace("- 工作项：尚未规划", "- 工作项：我自己手改的"));

                Assert.Contains(ReasonsOf(workspace, specification), reason => reason.Contains("被手改过"));
            }
        }

        /// <summary>六之二：有生成区却没记哈希，也红——不然把哈希删了就等于绕过这一条。</summary>
        [Fact]
        public void ReportsGeneratedRegionWithoutHash()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RenderThenEdit(workspace, specification, text =>
                {
                    var lines = text.Split('\n').Where(line => !line.StartsWith("生成区hash:", System.StringComparison.Ordinal));
                    return string.Join("\n", lines);
                });

                Assert.Contains(ReasonsOf(workspace, specification), reason => reason.Contains("没有「生成区hash」"));
            }
        }

        /// <summary>CheckAll 把池子里每条需求都查一遍。</summary>
        [Fact]
        public void CheckAllCoversEveryRequirement()
        {
            var (workspace, specification) = NewWorkspace();
            using (workspace)
            {
                RenderThenEdit(workspace, specification, text => text.Replace("需求id: REQ-0042", "需求id: REQ-0099"));
                workspace.WriteRequirement("REQ-0043", SystemRequirementJson().Replace("REQ-0042", "REQ-0043"));
                RequirementDocumentRenderer.Render(workspace.RepositoryRoot, workspace.Root, "REQ-0043", specification, false);

                var findings = RequirementDocumentChecker.CheckAll(workspace.Root, specification);

                Assert.Single(findings);
                Assert.Contains("REQ-0042", findings[0].Location);
            }
        }
    }
}
