using System;
using System.IO;
using System.Text;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 设计库这一层：定稿只收紧、来源不许机器编、索引扫得准、三档读取取对东西。
    ///
    /// 钉的重点是**风格会不会在没人察觉的情况下跑偏**——
    /// 模块偷偷引入新色、假定稿被当成事实继承、对不上的索引让「查过了没有」变成假话，
    /// 这三样都不会报错，只会在半年后表现成「这游戏看着不像一个人做的」。
    /// </summary>
    public sealed class DesignLibraryTests
    {
        /// <summary>模块引入项目色板之外的颜色要判红——风格跑偏几乎总是从「就这个模块特殊一下」开始的。</summary>
        [Fact]
        public void ModulePaletteOutsideProjectPaletteIsReported()
        {
            using var workspace = new Workspace();
            var project = WriteFinal(workspace, "", ProjectFinalJson);
            var module = WriteFinal(workspace, "Inventory", ModuleFinalJson.Replace("\"#7f8fa6\"", "\"#ff00ff\""));

            var findings = ArtStyleFinal.InspectNarrowing(project, module);

            Assert.Contains(findings, finding => finding.Reason.Contains("#ff00ff"));
        }

        /// <summary>模块删掉项目级负面清单里的一条要判红——取并集，只能往上加。</summary>
        [Fact]
        public void ModuleDroppingProjectNegativeEntryIsReported()
        {
            using var workspace = new Workspace();
            var project = WriteFinal(workspace, "", ProjectFinalJson);
            var module = WriteFinal(workspace, "Inventory", ModuleFinalJson.Replace("\"不要写实血腥\", ", ""));

            var findings = ArtStyleFinal.InspectNarrowing(project, module);

            Assert.Contains(findings, finding => finding.Reason.Contains("不要写实血腥"));
        }

        /// <summary>模块色板是项目色板的子集时放行——收紧本来就是允许的。</summary>
        [Fact]
        public void ModuleNarrowingWithinProjectPaletteIsAccepted()
        {
            using var workspace = new Workspace();
            var project = WriteFinal(workspace, "", ProjectFinalJson);
            var module = WriteFinal(workspace, "Inventory", ModuleFinalJson);

            Assert.Empty(ArtStyleFinal.InspectNarrowing(project, module));
        }

        /// <summary>
        /// 定稿来源只许「人定」或「选片带出」。
        /// 机器编的定稿会被往后所有资产当成事实继承——比空着更糟。
        /// </summary>
        [Theory]
        [InlineData("机器生成", true)]
        [InlineData("", true)]
        [InlineData("人定", false)]
        [InlineData("选片带出", false)]
        public void FabricatedStyleFinalIsReported(string origin, bool shouldReport)
        {
            using var workspace = new Workspace();
            var final = WriteFinal(workspace, "", ProjectFinalJson.Replace("\"来源\": \"人定\"", $"\"来源\": \"{origin}\""));

            var findings = ArtStyleFinal.InspectOrigin(final);

            Assert.Equal(shouldReport, findings.Count > 0);
        }

        /// <summary>还没有项目级定稿时，模块级怎么写都不算越界——不给「无中生有的父级」判红。</summary>
        [Fact]
        public void ModuleIsNotJudgedWhenProjectFinalIsAbsent()
        {
            using var workspace = new Workspace();
            var module = WriteFinal(workspace, "Inventory", ModuleFinalJson);

            Assert.Empty(ArtStyleFinal.InspectNarrowing(null, module));
        }

        /// <summary>索引以**落点里真有的文件**为准，模块名取自目录。</summary>
        [Fact]
        public void IndexScansWhatIsActuallyOnDisk()
        {
            using var workspace = new Workspace();
            WritePng(workspace, "Inventory/T_SlotItem.png");
            WritePng(workspace, "Shared/T_ButtonSort.png");

            var index = DesignLibraryIndex.Rebuild(workspace.Root, withPalette: false);

            Assert.Equal(2, index.Entries.Count);
            Assert.Contains(index.Entries, entry => entry.Naming == "T_SlotItem" && entry.Module == "Inventory");
            Assert.Contains(index.Entries, entry => entry.Naming == "T_ButtonSort" && entry.Module == "Shared");
        }

        /// <summary>重扫逐字节一样——扫盘顺序随文件系统而变，不排序的话幂等门禁永远红。</summary>
        [Fact]
        public void IndexRebuildIsDeterministic()
        {
            using var workspace = new Workspace();
            WritePng(workspace, "Inventory/T_SlotItem.png");
            WritePng(workspace, "Inventory/T_BackgroundMain.png");
            WritePng(workspace, "Shared/T_ButtonSort.png");

            var first = DesignLibraryIndex.Rebuild(workspace.Root, withPalette: false).Render();
            var second = DesignLibraryIndex.Rebuild(workspace.Root, withPalette: false).Render();

            Assert.Equal(first, second);
        }

        /// <summary>查同类时通用件也算——它们本来就是给全项目用的。</summary>
        [Fact]
        public void SharedAssetsCountAsSimilar()
        {
            using var workspace = new Workspace();
            WritePng(workspace, "Shared/T_ButtonSort.png");

            var index = DesignLibraryIndex.Rebuild(workspace.Root, withPalette: false);

            Assert.Single(index.FindSimilar("Inventory", "", 3));
        }

        /// <summary>
        /// 负面清单在锚点里取并集：项目级两条 + 模块级新加一条 = 三条。
        /// 只取模块那份的话，一个模块的疏忽就能把项目级约束整条丢掉。
        /// </summary>
        [Fact]
        public void AnchorMergesNegativeListsFromBothLayers()
        {
            using var workspace = new Workspace();
            WriteFinal(workspace, "", ProjectFinalJson);
            WriteFinal(workspace, "Inventory", ModuleFinalJson);

            var anchor = StyleAnchorResolver.Resolve(workspace.Root, "Inventory", "", 0);

            Assert.Equal(3, anchor.NegativeList.Count);
            Assert.Contains("不要赛博朋克霓虹", anchor.NegativeList);
            Assert.Contains("背包里不要出现武器", anchor.NegativeList);
        }

        /// <summary>库里什么都没有时判成冷启动——这是「先跟人聊」那条路的入口。</summary>
        [Fact]
        public void EmptyLibraryIsColdStart()
        {
            using var workspace = new Workspace();

            var anchor = StyleAnchorResolver.Resolve(workspace.Root, "Inventory", "", 1);

            Assert.True(anchor.IsColdStart);
            Assert.Contains(anchor.Notes, note => note.Contains("冷启动"));
        }

        /// <summary>有总设计层与定稿时不算冷启动，且提示词片段拼得出来。</summary>
        [Fact]
        public void DirectionAndFinalTogetherLeaveColdStart()
        {
            using var workspace = new Workspace();
            WriteDirection(workspace, "# 总设计\n\n低饱和冷色系，扁平。");
            WriteFinal(workspace, "", ProjectFinalJson);
            WriteFinal(workspace, "Inventory", ModuleFinalJson);

            var anchor = StyleAnchorResolver.Resolve(workspace.Root, "Inventory", "", 0);

            Assert.False(anchor.IsColdStart);
            Assert.Contains("低饱和冷色系", anchor.DirectionText);
            Assert.Contains("配色贴近", StyleAnchorResolver.ToPromptFragment(anchor));
        }

        /// <summary>一个锚点都没有时提示词片段是空串——让调用方如实说，而不是拼一段空话。</summary>
        [Fact]
        public void EmptyAnchorProducesNoPromptFragment()
        {
            using var workspace = new Workspace();

            var anchor = StyleAnchorResolver.Resolve(workspace.Root, "Inventory", "", 0);

            Assert.Equal("", StyleAnchorResolver.ToPromptFragment(anchor));
        }

        /// <summary>写一份定稿并读回来。</summary>
        private static ArtStyleFinal WriteFinal(Workspace workspace, string moduleName, string json)
        {
            var path = moduleName.Length == 0
                ? ArtStyleFinal.ProjectFilePath(workspace.Root)
                : ArtStyleFinal.ModuleFilePath(workspace.Root, moduleName);

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json, new UTF8Encoding(false));

            Assert.True(ArtStyleFinal.TryRead(path, moduleName, out var final, out var reason), reason);
            return final;
        }

        /// <summary>写总设计层。</summary>
        private static void WriteDirection(Workspace workspace, string text)
        {
            var path = DesignDirection.FilePathFor(workspace.Root);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        /// <summary>在扫描根下造一张最小 PNG。索引只看文件在不在，内容无所谓。</summary>
        private static void WritePng(Workspace workspace, string relativePath)
        {
            var path = Path.Combine(
                DesignLibraryIndex.ScanRoot(workspace.Root), relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        }

        private const string ProjectFinalJson = @"{
  ""契约版本"": ""1.0.0"", ""名称"": ""项目风格@v1"", ""版本"": 1, ""来源"": ""人定"",
  ""色板"": [""#2b3a4a"", ""#7f8fa6"", ""#d9e1ea""],
  ""负面清单"": [""不要赛博朋克霓虹"", ""不要写实血腥""],
  ""参考图"": []
}";

        private const string ModuleFinalJson = @"{
  ""契约版本"": ""1.0.0"", ""名称"": ""背包风格@v1"", ""版本"": 1, ""来源"": ""人定"",
  ""色板"": [""#2b3a4a"", ""#7f8fa6""],
  ""负面清单"": [""不要赛博朋克霓虹"", ""不要写实血腥"", ""背包里不要出现武器""],
  ""参考图"": []
}";

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "设计库测试-" + Guid.NewGuid().ToString("N"));
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
