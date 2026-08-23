using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 界面规格这一层：校验拦得住什么、资产清单怎么收敛、布局图幂不幂等。
    ///
    /// 钉的重点是**收敛**——一屏从「画面上能框出来的一百多个」降到「真要出的二十几个」，
    /// 靠的是类型模板、重复件、通用件三条，而不是给切图算法加阈值。
    /// </summary>
    public sealed class InterfaceSpecTests
    {
        /// <summary>一份合规的规格过校验。</summary>
        [Fact]
        public void ValidSpecPassesInspection()
        {
            using var workspace = new Workspace();
            var spec = ReadSpec(workspace, MinimalSpecJson);

            var findings = InterfaceSpecInspector.Inspect(spec, LoadCatalog(workspace));

            Assert.Empty(findings);
        }

        /// <summary>
        /// 元素 id 含缩写要在这一层就拦下。
        /// 这个 id 会**原样变成 C# 标识符**——拖到生成三件套之后再红的话，
        /// 报错指向的是一份生成物，人得倒推半天才找到源头（RPG 上真判过 18 条）。
        /// </summary>
        [Fact]
        public void ElementIdentifierWithAbbreviationIsReported()
        {
            using var workspace = new Workspace();
            var spec = ReadSpec(workspace, MinimalSpecJson.Replace("ButtonSort", "BtnSort"));

            var findings = InterfaceSpecInspector.Inspect(spec, LoadCatalog(workspace));

            Assert.Contains(findings, finding => finding.Reason.Contains("缩写"));
        }

        /// <summary>
        /// 「失败」写成一句话要判红。
        /// 背包满了/钱不够/网络断了三条的文案与处置完全不同，
        /// 合成一句「失败提示」等于没写，而程序照着这句写出来的就是三种失败一个提示框。
        /// </summary>
        [Fact]
        public void FailureWrittenAsOneSentenceIsReported()
        {
            using var workspace = new Workspace();
            var text = MinimalSpecJson.Replace(
                "\"失败\": [{ \"条件\": \"列表为空\", \"提示\": \"背包是空的\", \"处置\": \"不重排\" }]",
                "\"失败\": \"失败了给个提示\"");
            var spec = ReadSpec(workspace, text);

            var findings = InterfaceSpecInspector.Inspect(spec, LoadCatalog(workspace));

            Assert.Contains(findings, finding => finding.Reason.Contains("不是数组"));
        }

        /// <summary>Button 缺「验收」要判红——写不出可测的断言，说明这条还没想清楚。</summary>
        [Fact]
        public void MissingAcceptanceIsReported()
        {
            using var workspace = new Workspace();
            var spec = ReadSpec(workspace, MinimalSpecJson.Replace("\"验收\": \"点了会重排\",", ""));

            var findings = InterfaceSpecInspector.Inspect(spec, LoadCatalog(workspace));

            Assert.Contains(findings, finding => finding.Reason.Contains("验收"));
        }

        /// <summary>类型没有模板时判红，**不给通用模板兜底**——兜底等于默许任何拼错的类型名通过。</summary>
        [Fact]
        public void UnknownElementTypeIsReportedInsteadOfFallingBack()
        {
            using var workspace = new Workspace();
            var spec = ReadSpec(workspace, MinimalSpecJson.Replace("\"类型\": \"Button\"", "\"类型\": \"Buton\""));

            var findings = InterfaceSpecInspector.Inspect(spec, LoadCatalog(workspace));

            Assert.Contains(findings, finding => finding.Reason.Contains("没有对应的模板"));
        }

        /// <summary>父容器成环要判红——成环的话生成 UXML 时会无限递归。</summary>
        [Fact]
        public void ParentCycleIsReported()
        {
            using var workspace = new Workspace();
            var spec = ReadSpec(workspace, CycleSpecJson);

            var findings = InterfaceSpecInspector.Inspect(spec, LoadCatalog(workspace));

            Assert.Contains(findings, finding => finding.Reason.Contains("成环"));
        }

        /// <summary>
        /// 资产清单的三条收敛：不出图的类型、重复件只出一张、通用件落 Shared/。
        /// 这一条是整层存在的理由——六个元素、四十个格子、四个角纹样，真要出的只有三张
        /// （底图、排序按钮、格子底），Label / Container / Decoration 一张都不出。
        /// </summary>
        [Fact]
        public void ManifestConvergesToWhatActuallyNeedsGenerating()
        {
            using var workspace = new Workspace();
            var spec = ReadSpec(workspace, ConvergenceSpecJson);

            var manifest = InterfaceAssetManifest.Build(workspace.Root, spec, LoadCatalog(workspace));

            Assert.Equal(3, InterfaceAssetManifest.CountToGenerate(manifest));

            var slot = Find(manifest, "SlotItem");
            Assert.Equal(InterfaceAssetManifest.ActionGenerate, slot.Action);
            Assert.Equal(40, slot.RepeatCount);
            Assert.Contains("只出一张", slot.Reason);

            var button = Find(manifest, "ButtonSort");
            Assert.Contains("/Shared/", button.Destination);

            Assert.Equal(InterfaceAssetManifest.ActionSkip, Find(manifest, "LabelCapacity").Action);
            Assert.Equal(InterfaceAssetManifest.ActionSkip, Find(manifest, "PanelToolbar").Action);
            Assert.Equal(InterfaceAssetManifest.ActionSkip, Find(manifest, "DecorationCorner").Action);
        }

        /// <summary>布局图确定性：同一份规格渲两遍逐字节一样，否则幂等门禁就是摆设。</summary>
        [Fact]
        public void LayoutRenderIsDeterministic()
        {
            using var workspace = new Workspace();
            var spec = ReadSpec(workspace, ConvergenceSpecJson);

            Assert.Equal(LayoutImageRenderer.Render(spec), LayoutImageRenderer.Render(spec));
        }

        /// <summary>
        /// 布局图按父子深度排序：父先画、子后画。
        /// 不排的话，一个铺满全屏的底图写在清单最后就会把所有元素盖住。
        /// </summary>
        [Fact]
        public void LayoutDrawsParentsBeforeChildren()
        {
            using var workspace = new Workspace();
            var spec = ReadSpec(workspace, ConvergenceSpecJson);

            var svg = LayoutImageRenderer.Render(spec);

            // 底图写在清单最后一条，但它是顶层元素，必须先画。
            Assert.True(
                svg.IndexOf("BackgroundMain", StringComparison.Ordinal)
                < svg.IndexOf("PanelToolbar", StringComparison.Ordinal));
        }

        /// <summary>uidef 投影：控件类型由规格里的「类型」定，不再按名字猜；不出图的元素贴图留空。</summary>
        [Fact]
        public void ProjectionTakesControlTypeFromSpecAndLeavesTextureEmptyWhenNoImage()
        {
            using var workspace = new Workspace();
            var spec = ReadSpec(workspace, ConvergenceSpecJson);
            var manifest = InterfaceAssetManifest.Build(workspace.Root, spec, LoadCatalog(workspace));

            var elements = InterfaceSpecProjection.ToPanelElements(spec, manifest);

            var label = FindElement(elements, "LabelCapacity");
            Assert.Equal("Label", label.ElementType);
            Assert.Equal("", label.TexturePath);

            var button = FindElement(elements, "ButtonSort");
            Assert.Equal("Button", button.ElementType);
            Assert.Contains("T_ButtonSort.png", button.TexturePath);
        }

        /// <summary>面板标识名不重复贴 Panel 后缀。</summary>
        [Theory]
        [InlineData("Inventory", "InventoryPanel")]
        [InlineData("InventoryPanel", "InventoryPanel")]
        public void PanelIdentifierDoesNotDoubleTheSuffix(string panelName, string expected)
        {
            using var workspace = new Workspace();
            var spec = ReadSpec(workspace, MinimalSpecJson.Replace("\"面板\": \"Inventory\"", $"\"面板\": \"{panelName}\""));

            Assert.Equal(expected, InterfaceSpecProjection.PanelIdentifier(spec));
        }

        /// <summary>按 id 取清单里的一条。</summary>
        private static InterfaceAssetEntry Find(IReadOnlyList<InterfaceAssetEntry> manifest, string identifier)
        {
            foreach (var entry in manifest)
            {
                if (string.Equals(entry.ElementIdentifier, identifier, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            throw new InvalidOperationException($"清单里没有 {identifier}");
        }

        /// <summary>按 id 取投影里的一条。</summary>
        private static UiPanelElement FindElement(IReadOnlyList<UiPanelElement> elements, string identifier)
        {
            foreach (var element in elements)
            {
                if (string.Equals(element.IdentifierName, identifier, StringComparison.Ordinal))
                {
                    return element;
                }
            }

            throw new InvalidOperationException($"投影里没有 {identifier}");
        }

        /// <summary>把 JSON 写进临时仓库再读回来——走的是真正的读取路径，不是手搓对象。</summary>
        private static InterfaceSpec ReadSpec(Workspace workspace, string json)
        {
            var path = InterfaceSpec.FilePathFor(workspace.Root, "UI-0001");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json, new UTF8Encoding(false));

            Assert.True(InterfaceSpec.TryRead(path, out var spec, out var reason), reason);
            return spec;
        }

        /// <summary>把基线模板拷进临时仓库再加载——校验的正是仓库里那一份真数据。</summary>
        private static UiElementTemplateCatalog LoadCatalog(Workspace workspace)
        {
            var source = UiElementTemplateCatalog.BaselineFile(RepositoryRoot());
            var target = UiElementTemplateCatalog.BaselineFile(workspace.Root);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, overwrite: true);
            return UiElementTemplateCatalog.Load(workspace.Root, "");
        }

        /// <summary>从测试程序集往上找仓库根（认 Specifications 目录）。</summary>
        private static string RepositoryRoot()
        {
            var directory = AppContext.BaseDirectory;
            for (var step = 0; step < 8 && directory != null; step++)
            {
                if (Directory.Exists(Path.Combine(directory, "Specifications", "Baseline")))
                {
                    return directory;
                }

                directory = Path.GetDirectoryName(directory);
            }

            throw new InvalidOperationException("找不到仓库根");
        }

        private const string MinimalSpecJson = @"{
  ""id"": ""UI-0001"", ""面板"": ""Inventory"", ""标题"": ""背包"", ""状态"": ""草稿"",
  ""画布"": { ""宽"": 1920, ""高"": 1080 },
  ""元素"": [
    {
      ""id"": ""ButtonSort"", ""名称"": ""排序"", ""类型"": ""Button"",
      ""布局"": { ""位置"": [10, 10], ""尺寸"": [96, 96] },
      ""复用"": ""本界面专有"",
      ""状态"": [""常态"", ""禁用""],
      ""交互"": [{ ""事件"": ""点击"", ""动作"": ""重排"" }],
      ""成功"": ""列表重排"",
      ""失败"": [{ ""条件"": ""列表为空"", ""提示"": ""背包是空的"", ""处置"": ""不重排"" }],
      ""验收"": ""点了会重排"",
      ""schema版本"": ""1.0.0""
    }
  ]
}";

        private const string CycleSpecJson = @"{
  ""id"": ""UI-0001"", ""面板"": ""Inventory"", ""标题"": ""背包"", ""状态"": ""草稿"",
  ""画布"": { ""宽"": 100, ""高"": 100 },
  ""元素"": [
    { ""id"": ""AlphaBox"", ""类型"": ""Container"", ""名称"": ""甲"", ""父容器"": ""BetaBox"",
      ""布局"": { ""位置"": [0,0], ""尺寸"": [10,10] }, ""复用"": ""本界面专有"",
      ""边界"": [""空""], ""验收"": ""能显示"" },
    { ""id"": ""BetaBox"", ""类型"": ""Container"", ""名称"": ""乙"", ""父容器"": ""AlphaBox"",
      ""布局"": { ""位置"": [0,0], ""尺寸"": [10,10] }, ""复用"": ""本界面专有"",
      ""边界"": [""空""], ""验收"": ""能显示"" }
  ]
}";

        private const string ConvergenceSpecJson = @"{
  ""id"": ""UI-0001"", ""面板"": ""Inventory"", ""标题"": ""背包"", ""状态"": ""草稿"",
  ""画布"": { ""宽"": 1920, ""高"": 1080 },
  ""元素"": [
    { ""id"": ""PanelToolbar"", ""类型"": ""Container"", ""名称"": ""工具栏"", ""父容器"": ""BackgroundMain"",
      ""布局"": { ""位置"": [0,0], ""尺寸"": [1920,120] }, ""复用"": ""本界面专有"",
      ""边界"": [""窄时隐藏""], ""验收"": ""铺满宽度"" },
    { ""id"": ""ButtonSort"", ""类型"": ""Button"", ""名称"": ""排序"", ""父容器"": ""PanelToolbar"",
      ""布局"": { ""位置"": [1680,12], ""尺寸"": [96,96] }, ""复用"": ""通用"",
      ""状态"": [""常态""], ""交互"": [{ ""事件"": ""点击"", ""动作"": ""重排"" }],
      ""成功"": ""重排"", ""失败"": [{ ""条件"": ""空"", ""提示"": ""空的"", ""处置"": ""不动"" }],
      ""验收"": ""点了会重排"" },
    { ""id"": ""SlotItem"", ""类型"": ""Image"", ""名称"": ""格子"", ""父容器"": ""BackgroundMain"",
      ""布局"": { ""位置"": [80,200], ""尺寸"": [120,120] }, ""复用"": ""本界面专有"",
      ""重复"": 40, ""验收"": ""四十个格子共用一张底图"" },
    { ""id"": ""LabelCapacity"", ""类型"": ""Label"", ""名称"": ""容量"", ""父容器"": ""PanelToolbar"",
      ""布局"": { ""位置"": [40,40], ""尺寸"": [200,40] }, ""复用"": ""本界面专有"",
      ""数据"": { ""来源"": ""已用/总数"", ""刷新"": ""增减时"" }, ""验收"": ""形如 12/40"" },
    { ""id"": ""DecorationCorner"", ""类型"": ""Decoration"", ""名称"": ""角纹"", ""父容器"": ""BackgroundMain"",
      ""布局"": { ""位置"": [0,0], ""尺寸"": [160,160] }, ""复用"": ""本界面专有"",
      ""重复"": 4, ""验收"": ""四角对称"" },
    { ""id"": ""BackgroundMain"", ""类型"": ""Background"", ""名称"": ""底图"",
      ""布局"": { ""位置"": [0,0], ""尺寸"": [1920,1080] }, ""复用"": ""本界面专有"",
      ""验收"": ""铺满整屏"" }
  ]
}";

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "界面规格测试-" + Guid.NewGuid().ToString("N"));
            }

            public string Root { get; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
