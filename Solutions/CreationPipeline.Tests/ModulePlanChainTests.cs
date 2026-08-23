using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 界面 / 需求 / 模块策划案三者接起来之后的链路测试。
    ///
    /// 盯的是三条：界面按**模块**归属（不是按需求）、验收之后策划案会被重渲、
    /// 冷启动草案不许替人编「往后要做成什么样」。
    /// </summary>
    public class ModulePlanChainTests
    {
        private const string Module = "Inventory";

        private static PoolTestWorkspace NewWorkspace()
        {
            var workspace = new PoolTestWorkspace();
            workspace.CopyPlanningDocumentBaseline();
            return workspace;
        }

        private static void WriteRequirement(PoolTestWorkspace workspace, string identifier, string epic)
        {
            workspace.WriteRequirement(identifier, new JsonObject
            {
                ["id"] = identifier,
                ["类型"] = "系统",
                ["状态"] = "已确认",
                ["标题"] = "背包",
                ["专项"] = epic,
                ["依赖"] = new JsonArray(),
                ["锁定"] = false,
                ["schema版本"] = "1.0.0"
            }.ToJsonString());
        }

        private static void WriteSpec(
            PoolTestWorkspace workspace, string identifier, string panel, string module, string requirement)
        {
            var directory = InterfaceSpec.Directory(workspace.RepositoryRoot);
            Directory.CreateDirectory(directory);

            var spec = new JsonObject
            {
                ["id"] = identifier,
                ["面板"] = panel,
                ["模块"] = module,
                ["标题"] = panel + " 界面",
                ["来源需求"] = new JsonArray { requirement },
                ["画布"] = new JsonObject { ["宽"] = 1280, ["高"] = 720 },
                ["状态"] = "草稿",
                ["元素"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "CloseButton",
                        ["名称"] = "关闭按钮",
                        ["类型"] = "Button",
                        ["布局"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["宽"] = 80, ["高"] = 32 },
                        ["复用"] = "通用",
                        ["交互"] = "点击关闭面板",
                        ["成功"] = "面板收起",
                        ["失败"] = "待定",
                        ["状态"] = "常态",
                        ["边界"] = "无",
                        ["验收"] = "能点"
                    }
                }
            };

            File.WriteAllText(Path.Combine(directory, identifier + ".json"), spec.ToJsonString());
        }

        /// <summary>界面按「模块」归属：同一个模块的两块屏都要被列出来，面板名不同也算。</summary>
        [Fact]
        public void FindsEveryScreenOfAModuleEvenWhenPanelNamesDiffer()
        {
            using var workspace = NewWorkspace();
            WriteSpec(workspace, "UI-0001", "InventoryMain", Module, "REQ-0002");
            WriteSpec(workspace, "UI-0002", "InventorySettings", Module, "REQ-0003");
            WriteSpec(workspace, "UI-0003", "Shop", "Commerce", "REQ-0004");

            var found = InterfaceSpec.FindByModule(workspace.RepositoryRoot, Module, out var skipped);

            Assert.Equal(2, found.Count);
            Assert.Empty(skipped);
            Assert.Equal("UI-0001", found[0].Identifier);
            Assert.Equal("UI-0002", found[1].Identifier);
        }

        /// <summary>没写「模块」的老规格退回用面板名——那是它们当时的实际含义，不算猜。</summary>
        [Fact]
        public void FallsBackToThePanelNameWhenTheModuleFieldIsAbsent()
        {
            using var workspace = NewWorkspace();
            var directory = InterfaceSpec.Directory(workspace.RepositoryRoot);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "UI-0001.json"),
                new JsonObject
                {
                    ["id"] = "UI-0001",
                    ["面板"] = Module,
                    ["标题"] = "背包",
                    ["来源需求"] = new JsonArray { "REQ-0002" },
                    ["画布"] = new JsonObject { ["宽"] = 1280, ["高"] = 720 },
                    ["元素"] = new JsonArray()
                }.ToJsonString());

            var found = InterfaceSpec.FindByModule(workspace.RepositoryRoot, Module, out _);

            Assert.Single(found);
            Assert.Equal(Module, found[0].ModuleName);
        }

        /// <summary>元素行为表渲进的是模块策划案，界面那一节把每屏都列出来。</summary>
        [Fact]
        public void RendersTheElementTableIntoTheModulePlan()
        {
            using var workspace = NewWorkspace();
            WriteSpec(workspace, "UI-0001", "InventoryMain", Module, "REQ-0002");

            var outcome = PlanningDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, Module,
                PlanningDocumentSpec.Load(workspace.Root), false);

            Assert.Contains("**UI-0001「InventoryMain 界面」** · 画布 1280×720 · 元素 1 个", outcome.DocumentText);
            Assert.Contains("| 关闭按钮 | Button | 点击关闭面板 | 面板收起 | 待定 | 无 |", outcome.DocumentText);
        }

        /// <summary>验收那一刻靠需求的「专项」找到模块，把那份策划案重渲一遍。</summary>
        [Fact]
        public void RefreshesTheModulePlanWhenARequirementIsAccepted()
        {
            using var workspace = NewWorkspace();
            WriteRequirement(workspace, "REQ-0002", Module);

            var refreshed = ModulePlanRefresher.RefreshForRequirement(
                workspace.RepositoryRoot, workspace.Root, "REQ-0002", out var notes);

            Assert.True(refreshed);
            Assert.True(File.Exists(PoolPaths.ModulePlanDocument(workspace.Root, Module)));
            Assert.Contains(notes, note => note.Contains("模块策划案（Inventory）"));
        }

        /// <summary>没挂专项时如实说，不静默跳过——静默的后果是人以为更新过了。</summary>
        [Fact]
        public void SaysSoWhenTheRequirementHasNoEpic()
        {
            using var workspace = NewWorkspace();
            WriteRequirement(workspace, "REQ-0002", "");

            var refreshed = ModulePlanRefresher.RefreshForRequirement(
                workspace.RepositoryRoot, workspace.Root, "REQ-0002", out var notes);

            Assert.False(refreshed);
            Assert.Contains(notes, note => note.Contains("没挂专项"));
        }

        /// <summary>冷启动草案**一定**把「往后要做成什么样」留成占位符，哪怕模型回了内容。</summary>
        [Fact]
        public void NeverLetsTheModelWriteTheFutureSection()
        {
            var reply = new JsonObject
            {
                ["标题"] = "背包",
                ["目标用途"] = "玩家身上带什么、能带多少。",
                ["玩法"] = "按获得先后排格子。",
                ["边界与不做"] = "装备穿戴不归它管。",
                ["往后要做成什么样"] = "我编的未来规划：加分页、加自动整理、加跨存档仓库。"
            }.ToJsonString();

            Assert.True(PlanningDocumentDraftPrompt.TryParse(reply, out var sections, out var reason), reason);

            Assert.Equal(PlanningDocumentDraftPrompt.FuturePlaceholder, sections["往后要做成什么样"]);
            Assert.DoesNotContain("我编的未来规划", sections["往后要做成什么样"]);
        }

        /// <summary>没有「目标用途」就算解析失败——那是这份草案唯一不能缺的东西。</summary>
        [Fact]
        public void RejectsADraftWithoutTheStatedPurpose()
        {
            var reply = new JsonObject { ["玩法"] = "按获得先后排格子。" }.ToJsonString();

            Assert.False(PlanningDocumentDraftPrompt.TryParse(reply, out _, out var reason));
            Assert.Contains("目标用途", reason);
        }

        /// <summary>草案落盘之后再渲一次生成区，人写区原样保留。</summary>
        [Fact]
        public void KeepsTheDraftedProseWhenTheGeneratedRegionIsRendered()
        {
            using var workspace = NewWorkspace();
            var sections = new Dictionary<string, string>
            {
                ["标题"] = "背包",
                ["目标用途"] = "玩家身上带什么、能带多少。",
                ["玩法"] = "按获得先后排格子。",
                ["边界与不做"] = "装备穿戴不归它管。",
                ["往后要做成什么样"] = PlanningDocumentDraftPrompt.FuturePlaceholder
            };

            var specification = PlanningDocumentSpec.Load(workspace.Root);
            PlanningDocumentDraftWriter.Write(workspace.Root, Module, sections, specification);

            var outcome = PlanningDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, Module, specification, false);

            Assert.Contains("玩家身上带什么、能带多少。", outcome.DocumentText);
            Assert.Contains("装备穿戴不归它管。", outcome.DocumentText);
            Assert.Contains("## 现状", outcome.DocumentText);
        }

        /// <summary>冷启动不覆盖已有的那份：人写区里可能已经有人写了半天的东西。</summary>
        [Fact]
        public void RefusesToOverwriteAnExistingPlan()
        {
            using var workspace = NewWorkspace();
            var specification = PlanningDocumentSpec.Load(workspace.Root);
            var sections = new Dictionary<string, string> { ["目标用途"] = "第一版。" };
            PlanningDocumentDraftWriter.Write(workspace.Root, Module, sections, specification);

            Assert.Throws<IOException>(
                () => PlanningDocumentDraftWriter.Write(workspace.Root, Module, sections, specification));
        }
    }
}
