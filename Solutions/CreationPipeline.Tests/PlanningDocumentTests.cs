using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 模块策划案这一层的测试：渲染生成区、不碰人写区、门禁认得出手改。
    ///
    /// 这一层最要紧的一条是**人写区不许被机器动**——人一旦发现自己写的东西
    /// 会被重渲染吃掉，就再也不往里写，剩下一份只有机器投影的空壳。
    /// </summary>
    public class PlanningDocumentTests
    {
        private const string Module = "Inventory";

        private static PoolTestWorkspace NewWorkspace()
        {
            var workspace = new PoolTestWorkspace();
            workspace.CopyPlanningDocumentBaseline();
            return workspace;
        }

        private static PlanningDocumentSpec Spec(PoolTestWorkspace workspace)
        {
            return PlanningDocumentSpec.Load(workspace.RepositoryRoot);
        }

        private static string Render(PoolTestWorkspace workspace)
        {
            var outcome = PlanningDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, Module, Spec(workspace), false);
            return outcome.DocumentText;
        }

        /// <summary>写一条挂在这个模块名下的需求。</summary>
        private static void WriteRequirement(
            PoolTestWorkspace workspace, string identifier, string title, string status)
        {
            workspace.WriteRequirement(identifier, new JsonObject
            {
                ["id"] = identifier,
                ["类型"] = "系统",
                ["状态"] = status,
                ["标题"] = title,
                ["专项"] = Module,
                ["依赖"] = new JsonArray(),
                ["锁定"] = false,
                ["schema版本"] = "1.0.0"
            }.ToJsonString());
        }

        /// <summary>规范从基线那段 JSON 里读得出来，五个必备键与四个必填小节都在。</summary>
        [Fact]
        public void LoadsTheContractFromTheBaselineMarkdown()
        {
            using var workspace = NewWorkspace();

            var specification = Spec(workspace);

            Assert.Equal(
                new[] { "模块", "标题", "状态", "文档版本", "权威侧" },
                specification.FrontMatterRequiredKeys);
            Assert.Equal(
                new[] { "目标用途", "玩法", "边界与不做", "往后要做成什么样" },
                specification.RequiredSections);
            Assert.Equal("现状", specification.GeneratedSection);
            Assert.Contains("配置表结构", specification.GeneratedSubsections);
        }

        /// <summary>新建时摆出人写区骨架，五个生成区子节一个不少。</summary>
        [Fact]
        public void CreatesSkeletonWithAllFiveGeneratedSubsections()
        {
            using var workspace = NewWorkspace();

            var text = Render(workspace);

            Assert.Contains("模块: Inventory", text);
            Assert.Contains("## 目标用途", text);
            Assert.Contains("## 往后要做成什么样", text);
            Assert.Contains("### 需求", text);
            Assert.Contains("### 界面与交互", text);
            Assert.Contains("### 配置表结构", text);
            Assert.Contains("### 参考图", text);
            Assert.Contains("### 代码公开面", text);
        }

        /// <summary>缺料写「暂无」，不是省略——省略读起来像「还没轮到」，而这里是「查过了，没有」。</summary>
        [Fact]
        public void WritesEmptyMarkerInsteadOfOmittingASubsection()
        {
            using var workspace = NewWorkspace();

            var text = Render(workspace);

            Assert.Contains("### 需求\n暂无", text.Replace("\r\n", "\n"));
        }

        /// <summary>需求认的是「专项」这一格，别的模块那条不算。</summary>
        [Fact]
        public void ListsOnlyRequirementsOfThisModule()
        {
            using var workspace = NewWorkspace();
            WriteRequirement(workspace, "REQ-0002", "补录背包", "已完成");
            workspace.WriteRequirement("REQ-0003", new JsonObject
            {
                ["id"] = "REQ-0003",
                ["类型"] = "系统",
                ["状态"] = "草稿",
                ["标题"] = "怪物掉落",
                ["专项"] = "Combat",
                ["依赖"] = new JsonArray(),
                ["锁定"] = false,
                ["schema版本"] = "1.0.0"
            }.ToJsonString());

            var text = Render(workspace);

            Assert.Contains("- REQ-0002 补录背包（已完成）", text);
            Assert.DoesNotContain("REQ-0003", text);
        }

        /// <summary>配置表结构照 Config/Schema 渲，参数名与类型都从 schema 来，不许人手抄。</summary>
        [Fact]
        public void RendersConfigTableStructureFromTheSchema()
        {
            using var workspace = NewWorkspace();
            var schemaDirectory = ConfigTableSchemaReader.SchemaDirectory(workspace.RepositoryRoot);
            Directory.CreateDirectory(schemaDirectory);
            File.WriteAllText(
                Path.Combine(schemaDirectory, "Bag.schema.json"),
                new JsonObject
                {
                    ["tableName"] = "背包",
                    ["tableIdentifierName"] = "Bag",
                    ["sheetName"] = "道具",
                    ["fields"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["displayName"] = "编号",
                            ["identifierName"] = "ItemId",
                            ["typeName"] = "Int32",
                            ["isPrimaryKey"] = true
                        },
                        new JsonObject
                        {
                            ["displayName"] = "堆叠上限",
                            ["identifierName"] = "StackLimit",
                            ["typeName"] = "Int32",
                            ["isPrimaryKey"] = false
                        }
                    }
                }.ToJsonString());

            Render(workspace);
            var path = PoolPaths.ModulePlanDocument(workspace.Root, Module);
            var declared = File.ReadAllText(path).Replace("配置表: []", "配置表: [Bag]");
            File.WriteAllText(path, declared);

            var text = Render(workspace);

            Assert.Contains("**背包**（`Bag` · 页签 道具）", text);
            Assert.Contains("| 参数名 | 标识名 | 类型 | 主键 |", text);
            Assert.Contains("| 编号 | ItemId | Int32 | 是 |", text);
            Assert.Contains("| 堆叠上限 | StackLimit | Int32 | — |", text);
        }

        /// <summary>声明了一张不存在的表时如实说，而不是把这一节渲成空。</summary>
        [Fact]
        public void SaysSoWhenADeclaredTableHasNoSchema()
        {
            using var workspace = NewWorkspace();
            Render(workspace);
            var path = PoolPaths.ModulePlanDocument(workspace.Root, Module);
            File.WriteAllText(path, File.ReadAllText(path).Replace("配置表: []", "配置表: [Ghost]"));

            var text = Render(workspace);

            Assert.Contains("找不到 Ghost 的 schema", text);
        }

        /// <summary>**人写区一个字都不碰**：重渲染之后人写的正文原样还在。</summary>
        [Fact]
        public void NeverTouchesTheHumanWrittenRegion()
        {
            using var workspace = NewWorkspace();
            Render(workspace);
            var path = PoolPaths.ModulePlanDocument(workspace.Root, Module);
            File.WriteAllText(
                path,
                File.ReadAllText(path).Replace(
                    "## 目标用途\n（待补）", "## 目标用途\n玩家身上带什么、能带多少。"));

            WriteRequirement(workspace, "REQ-0002", "补录背包", "已完成");
            var text = Render(workspace);

            Assert.Contains("玩家身上带什么、能带多少。", text);
            Assert.Contains("- REQ-0002 补录背包（已完成）", text);
        }

        /// <summary>同样的输入渲两遍无 diff——生成区要幂等，否则每次重渲染都是一次假改动。</summary>
        [Fact]
        public void IsIdempotent()
        {
            using var workspace = NewWorkspace();
            WriteRequirement(workspace, "REQ-0002", "补录背包", "已完成");
            Render(workspace);

            var outcome = PlanningDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, Module, Spec(workspace), false);

            Assert.False(outcome.IsChanged);
        }

        /// <summary>门禁：一份都没建不算违规——项目刚起步时本来就没有。</summary>
        [Fact]
        public void GateIsQuietWhenNoPlanExistsYet()
        {
            using var workspace = NewWorkspace();

            var findings = PlanningDocumentChecker.Check(workspace.RepositoryRoot, workspace.Root, Spec(workspace));

            Assert.Empty(findings);
        }

        /// <summary>门禁：手改生成区认得出来。</summary>
        [Fact]
        public void GateCatchesAHandEditedGeneratedRegion()
        {
            using var workspace = NewWorkspace();
            Render(workspace);
            var path = PoolPaths.ModulePlanDocument(workspace.Root, Module);
            File.WriteAllText(path, File.ReadAllText(path).Replace("### 需求", "### 需求（我手改的）"));

            var findings = PlanningDocumentChecker.Check(workspace.RepositoryRoot, workspace.Root, Spec(workspace));

            Assert.Contains(findings, finding => finding.Reason.Contains("生成区被手改过"));
        }

        /// <summary>门禁：「往后要做成什么样」留着占位符等于没写。</summary>
        [Fact]
        public void GateCatchesAnUnwrittenFutureSection()
        {
            using var workspace = NewWorkspace();
            Render(workspace);

            var findings = PlanningDocumentChecker.Check(workspace.RepositoryRoot, workspace.Root, Spec(workspace));

            Assert.Contains(findings, finding => finding.Reason.Contains("往后要做成什么样"));
        }

        /// <summary>门禁：配置表声明指不到 schema 时报出来。</summary>
        [Fact]
        public void GateCatchesAnUnresolvableConfigTable()
        {
            using var workspace = NewWorkspace();
            Render(workspace);
            var path = PoolPaths.ModulePlanDocument(workspace.Root, Module);
            File.WriteAllText(path, File.ReadAllText(path).Replace("配置表: []", "配置表: [Ghost]"));

            var findings = new List<PoolFinding>();
            PlanningDocumentChecker.CheckOne(
                workspace.RepositoryRoot, workspace.Root, Module, Spec(workspace), findings);

            Assert.Contains(findings, finding => finding.Reason.Contains("配置表声明「Ghost」"));
        }
    }
}
