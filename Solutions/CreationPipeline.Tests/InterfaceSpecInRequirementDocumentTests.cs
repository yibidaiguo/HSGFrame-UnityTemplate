using System;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 界面规格与需求案的关系：**只留一行指针**。
    ///
    /// 元素行为表与布局图住在模块策划案里（一个模块一份，常驻）——
    /// 需求案做完就归档，把整屏契约铺在它里面的结果是同一个面板被 N 条需求
    /// 各存一份快照，谁是正本说不清。这一层要盯住的就是「别再往需求案里铺表」。
    /// </summary>
    public class InterfaceSpecInRequirementDocumentTests
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
                ["玩法"] = "连续七天，每天领一次。",
                ["验收标准"] = new JsonArray { "登录后自动弹出签到界面" },
                ["关联设计记录"] = new JsonArray(),
                ["依赖"] = new JsonArray(),
                ["锁定"] = false,
                ["schema版本"] = "1.0.0"
            }.ToJsonString();
        }

        /// <summary>往工作区写一份界面规格。</summary>
        /// <param name="workspace">测试工作区。</param>
        /// <param name="identifier">界面 id。</param>
        /// <param name="requirementIdentifier">来源需求 id。</param>
        /// <param name="elements">元素数组。</param>
        private static string WriteSpec(
            PoolTestWorkspace workspace, string identifier, string requirementIdentifier, JsonArray elements)
        {
            var directory = InterfaceSpec.Directory(workspace.RepositoryRoot);
            Directory.CreateDirectory(directory);

            var spec = new JsonObject
            {
                ["id"] = identifier,
                ["面板"] = "SignIn",
                ["标题"] = "签到主界面",
                ["来源需求"] = new JsonArray { requirementIdentifier },
                ["画布"] = new JsonObject { ["宽"] = 1280, ["高"] = 720 },
                ["状态"] = "草稿",
                ["元素"] = elements
            };

            var path = Path.Combine(directory, identifier + ".json");
            File.WriteAllText(path, spec.ToJsonString());
            return path;
        }

        /// <summary>一个按钮元素：正文里塞了换行与竖线，用来验表格拍平。</summary>
        private static JsonArray OneButton()
        {
            return new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "领取按钮",
                    ["名称"] = "领取按钮",
                    ["类型"] = "Button",
                    ["布局"] = new JsonObject { ["x"] = 10, ["y"] = 20, ["宽"] = 100, ["高"] = 40 },
                    ["复用"] = "新建",
                    ["交互"] = "点一下领当天奖励",
                    ["成功"] = "按钮变灰\n并弹一条飘字",
                    ["失败"] = "提示「今天领过了」",
                    ["边界"] = "断签时|从第 1 天重计",
                    ["验收"] = "第 7 天能领到大奖"
                }
            };
        }

        /// <summary>找归属认的是规格里的「来源需求」，别的需求那份不算。</summary>
        [Fact]
        public void FindsSpecsByTheirSourceRequirement()
        {
            using var workspace = new PoolTestWorkspace();
            WriteSpec(workspace, "UI-0001", "REQ-0042", OneButton());
            WriteSpec(workspace, "UI-0002", "REQ-0099", OneButton());

            var found = InterfaceSpec.FindByRequirement(workspace.RepositoryRoot, "REQ-0042", out var skipped);

            Assert.Single(found);
            Assert.Equal("UI-0001", found[0].Identifier);
            Assert.Empty(skipped);
        }

        /// <summary>坏掉的那份只跳过并说清理由，不连累别的，也不冒充「没有」（决策 42）。</summary>
        [Fact]
        public void SkipsUnreadableSpecAndSaysWhy()
        {
            using var workspace = new PoolTestWorkspace();
            WriteSpec(workspace, "UI-0001", "REQ-0042", OneButton());
            File.WriteAllText(
                Path.Combine(InterfaceSpec.Directory(workspace.RepositoryRoot), "UI-0003.json"), "{ 这不是 JSON");

            var found = InterfaceSpec.FindByRequirement(workspace.RepositoryRoot, "REQ-0042", out var skipped);

            Assert.Single(found);
            Assert.Single(skipped);
            Assert.Contains("UI-0003.json", skipped[0]);
        }

        /// <summary>目录不存在只是「还没出过功能图」，不是失败。</summary>
        [Fact]
        public void MissingDirectoryIsNotAFailure()
        {
            using var workspace = new PoolTestWorkspace();

            var found = InterfaceSpec.FindByRequirement(workspace.RepositoryRoot, "REQ-0042", out var skipped);

            Assert.Empty(found);
            Assert.Empty(skipped);
        }

        /// <summary>没出过功能图时生成区如实写「尚未出功能图」，而不是不提这回事。</summary>
        [Fact]
        public void GeneratedRegionSaysWhenThereIsNoInterfaceYet()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyRequirementDocumentBaseline();
            workspace.WriteRequirement("REQ-0042", SystemRequirementJson());

            RequirementDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, "REQ-0042",
                RequirementDocumentSpec.Load(workspace.Root), false);

            Assert.Contains("- 界面规格：尚未出功能图", workspace.ReadRequirementDocument("REQ-0042"));
        }

        /// <summary>出过功能图之后，需求案里只多一行指针——表在模块策划案那边。</summary>
        [Fact]
        public void GeneratedRegionCarriesOnlyAPointer()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyRequirementDocumentBaseline();
            workspace.WriteRequirement("REQ-0042", SystemRequirementJson());
            WriteSpec(workspace, "UI-0001", "REQ-0042", OneButton());

            RequirementDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, "REQ-0042",
                RequirementDocumentSpec.Load(workspace.Root), false);

            var text = workspace.ReadRequirementDocument("REQ-0042");
            Assert.Contains("- 界面规格：UI-0001「签到主界面」（元素 1 个）→ 详见模块策划案 SignIn", text);

            // 表不在这儿：铺在需求案里就等于给同一个面板存了 N 份快照，谁是正本说不清。
            Assert.DoesNotContain("| 元素 | 类型 | 交互 |", text);
            Assert.DoesNotContain("### 界面 UI-0001", text);
        }

        /// <summary>布局图落的是**模块策划案**的 media/，需求案那份里一张图都不引。</summary>
        [Fact]
        public void PutsTheLayoutImageInTheModulePlanNotTheRequirement()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyRequirementDocumentBaseline();
            workspace.WriteRequirement("REQ-0042", SystemRequirementJson());
            WriteSpec(workspace, "UI-0001", "REQ-0042", OneButton());

            var raster = Path.Combine(workspace.Root, "layout.png");
            File.WriteAllBytes(raster, new byte[] { 1, 2, 3 });
            var media = InterfaceLayoutMedia.PublishToModule(
                workspace.Root, "SignIn", "UI-0001", raster, out var reason);

            Assert.Equal("", reason);
            Assert.True(File.Exists(media));
            Assert.Contains(
                Path.Combine("Designs", "Modules", "SignIn", "media"), media, StringComparison.Ordinal);

            RequirementDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, "REQ-0042",
                RequirementDocumentSpec.Load(workspace.Root), false);
            Assert.DoesNotContain("UI-0001-layout.png", workspace.ReadRequirementDocument("REQ-0042"));
        }

        /// <summary>位图没渲出来时不算失败，只说清为什么没进去。</summary>
        [Fact]
        public void PublishSaysWhyWhenThereIsNoRaster()
        {
            using var workspace = new PoolTestWorkspace();

            var media = InterfaceLayoutMedia.Publish(
                workspace.Root, "REQ-0042", "UI-0001", Path.Combine(workspace.Root, "absent.png"), out var reason);

            Assert.Equal("", media);
            Assert.Contains("位图没渲出来", reason);
        }

        /// <summary>规格没归到需求上时直接跳过：这时拷进哪个 media/ 都是猜。</summary>
        [Fact]
        public void PublishSkipsWhenTheSpecHasNoRequirement()
        {
            using var workspace = new PoolTestWorkspace();
            var raster = Path.Combine(workspace.Root, "layout.png");
            File.WriteAllBytes(raster, new byte[] { 1 });

            var media = InterfaceLayoutMedia.Publish(workspace.Root, "", "UI-0001", raster, out var reason);

            Assert.Equal("", media);
            Assert.Contains("没归到需求", reason);
        }
    }
}
