using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 界面规格回写需求案这条链的测试：找归属、布局图进 media/、元素行为表进生成区。
    ///
    /// 这一层要盯住的是「**需求案里读不读得到界面**」——从前规格只落在
    /// Pools/Designs/Interfaces/ 与 _Generated/ 里，飞书上那份需求案永远停在建需求那一刻。
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

        /// <summary>出过功能图之后，元素行为表进需求案；换行与竖线就地拍平，不许把表格拆散。</summary>
        [Fact]
        public void GeneratedRegionCarriesTheElementBehaviourTable()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyRequirementDocumentBaseline();
            workspace.WriteRequirement("REQ-0042", SystemRequirementJson());
            WriteSpec(workspace, "UI-0001", "REQ-0042", OneButton());

            RequirementDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, "REQ-0042",
                RequirementDocumentSpec.Load(workspace.Root), false);

            var text = workspace.ReadRequirementDocument("REQ-0042");
            Assert.Contains("- 界面规格：UI-0001「签到主界面」", text);
            Assert.Contains("### 界面 UI-0001「签到主界面」", text);
            Assert.Contains("面板 `SignIn` · 画布 1280×720 · 元素 1 个", text);
            Assert.Contains("| 元素 | 类型 | 交互 | 成功 | 失败 | 边界 |", text);
            Assert.Contains(
                "| 领取按钮 | Button | 点一下领当天奖励 | 按钮变灰 并弹一条飘字 | 提示「今天领过了」 | 断签时\\|从第 1 天重计 |",
                text);
        }

        /// <summary>布局图只在真拷进 media/ 之后才写那一行——引一个不存在的路径比不放更难查。</summary>
        [Fact]
        public void ReferencesTheLayoutImageOnlyWhenItIsActuallyInMedia()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.CopyRequirementDocumentBaseline();
            workspace.WriteRequirement("REQ-0042", SystemRequirementJson());
            WriteSpec(workspace, "UI-0001", "REQ-0042", OneButton());
            var specification = RequirementDocumentSpec.Load(workspace.Root);

            RequirementDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, false);
            Assert.DoesNotContain("UI-0001-layout.png", workspace.ReadRequirementDocument("REQ-0042"));

            var raster = Path.Combine(workspace.Root, "layout.png");
            File.WriteAllBytes(raster, new byte[] { 1, 2, 3 });
            var media = InterfaceLayoutMedia.Publish(workspace.Root, "REQ-0042", "UI-0001", raster, out var reason);

            Assert.Equal("", reason);
            Assert.True(File.Exists(media));

            RequirementDocumentRenderer.Render(
                workspace.RepositoryRoot, workspace.Root, "REQ-0042", specification, false);
            Assert.Contains(
                "![UI-0001 白块布局图](media/UI-0001-layout.png)", workspace.ReadRequirementDocument("REQ-0042"));
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
