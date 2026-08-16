using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Template.Toolkit.UiScaffold;
using Xunit;

namespace Template.UiFramework.Tests
{
    /// <summary>uidef 嵌套元素、文本、样式类三项扩展的生成测试。</summary>
    public class NestedPanelScaffolderTests
    {
        [Fact]
        public void NestedElementRendersOpenAndCloseTags()
        {
            var uxml = ScaffoldNestedPanel(out _, out _);

            Assert.Contains("<ui:VisualElement class=\"settings-column\">", uxml);
            Assert.Contains("</ui:VisualElement>", uxml);
        }

        [Fact]
        public void ChildElementsIndentDeeperThanTheirContainer()
        {
            var uxml = ScaffoldNestedPanel(out _, out _);
            var lines = uxml.Replace("\r\n", "\n").Split('\n');

            var containerLine = lines.Single(line => line.Contains("class=\"settings-column\""));
            var childLine = lines.Single(line => line.Contains("name=\"VolumeSlider\""));

            Assert.True(
                LeadingSpaces(childLine) > LeadingSpaces(containerLine),
                $"子元素缩进（{LeadingSpaces(childLine)}）应当深于容器（{LeadingSpaces(containerLine)}）");
        }

        [Fact]
        public void LayoutOnlyContainerGetsNoGeneratedProperty()
        {
            ScaffoldNestedPanel(out var generatedCode, out _);

            // 内容列没有标识名，是纯布局容器：不该生成属性，但它的子元素照样要生成。
            Assert.DoesNotContain("public VisualElement  { get; private set; }", generatedCode);
            Assert.Contains("public Slider VolumeSlider { get; private set; }", generatedCode);
            Assert.Contains("public Button BackButton { get; private set; }", generatedCode);
        }

        [Fact]
        public void NestedChildrenAreBoundByNameRegardlessOfDepth()
        {
            ScaffoldNestedPanel(out var generatedCode, out _);

            Assert.Contains("VolumeSlider = root.Q<Slider>(\"VolumeSlider\");", generatedCode);
            Assert.Contains("BackButton = root.Q<Button>(\"BackButton\");", generatedCode);
        }

        [Fact]
        public void TextAttributeIsWrittenAndXmlEscaped()
        {
            var uxml = ScaffoldNestedPanel(out _, out _);

            Assert.Contains("text=\"设置\"", uxml);
            Assert.Contains("text=\"返回 &lt;主菜单&gt;\"", uxml);
        }

        [Fact]
        public void RootCarriesPanelRootPlusDeclaredClasses()
        {
            var uxml = ScaffoldNestedPanel(out _, out _);

            Assert.Contains("class=\"panel-root settings-root\"", uxml);
        }

        [Fact]
        public void EveryDeclaredStyleClassGetsAStyleSheetRule()
        {
            ScaffoldNestedPanel(out _, out var styleSheet);

            Assert.Contains(".panel-root {", styleSheet);
            Assert.Contains(".settings-root {", styleSheet);
            Assert.Contains(".settings-column {", styleSheet);
        }

        [Fact]
        public void DuplicateIdentifierNamesAreRejectedByValidate()
        {
            var definition = new UiPanelDefinitionSource
            {
                PanelIdentifierName = "BrokenPanel",
                Elements = new List<UiElementSource>
                {
                    new UiElementSource { IdentifierName = "SameName", ElementType = "Label" },
                    new UiElementSource
                    {
                        ElementType = "VisualElement",
                        Children = new List<UiElementSource>
                        {
                            new UiElementSource { IdentifierName = "SameName", ElementType = "Button" },
                        },
                    },
                },
            };

            var problems = definition.Validate();

            Assert.Single(problems);
            Assert.Contains("SameName", problems[0]);
        }

        [Fact]
        public void MissingElementTypeIsRejectedByValidate()
        {
            var definition = new UiPanelDefinitionSource
            {
                PanelIdentifierName = "BrokenPanel",
                Elements = new List<UiElementSource>
                {
                    new UiElementSource { ElementName = "没类型的元素", IdentifierName = "Orphan" },
                },
            };

            var problems = definition.Validate();

            Assert.Single(problems);
            Assert.Contains("没类型的元素", problems[0]);
        }

        [Fact]
        public void FlatDefinitionKeepsRenderingAsBefore()
        {
            // 向后兼容：老定义一个字段没加，生成物形状必须和扩展之前一致。
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot, "主界面.uidef.json");

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            var uxml = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.uxml"));
            Assert.Contains("<ui:VisualElement name=\"MainPanel\" class=\"panel-root\">", uxml);
            Assert.Contains("<ui:ProgressBar name=\"HealthBar\" />", uxml);
            Assert.DoesNotContain("</ui:ProgressBar>", uxml);
        }

        private static int LeadingSpaces(string line)
        {
            return line.Length - line.TrimStart(' ').Length;
        }

        private static string ScaffoldNestedPanel(out string generatedCode, out string styleSheet)
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot, "嵌套面板.uidef.json");

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            generatedCode = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "SettingsPanel.cs"));
            styleSheet = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "SettingsPanel.uss"));
            return File.ReadAllText(Path.Combine(fixture.OutputDirectory, "SettingsPanel.uxml"));
        }

        private static UiPanelDefinitionSource LoadDefinition(string templateRoot, string fileName)
        {
            var definitionPath = Path.Combine(templateRoot, "Solutions", "UiFramework.Tests", "TestData", fileName);
            return JsonSerializer.Deserialize<UiPanelDefinitionSource>(File.ReadAllText(definitionPath));
        }

        // 与 PanelScaffolderTests 同一套定位方式：从程序集目录逐级向上找门禁配置文件那一级当模板根。
        private static string FindTemplateRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Tools", "Gates", "Config", "gate-config.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            Assert.Fail("未找到包含 Tools/Gates/Config 的仓库根");
            return string.Empty;
        }

        private sealed class ScaffoldFixture : IDisposable
        {
            public ScaffoldFixture()
            {
                RepositoryRoot = FindTemplateRoot();
                OutputDirectory = Path.Combine(Path.GetTempPath(), "UiScaffoldNestedTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(OutputDirectory);
            }

            public string RepositoryRoot { get; }

            public string OutputDirectory { get; }

            public void Dispose()
            {
                if (Directory.Exists(OutputDirectory))
                {
                    Directory.Delete(OutputDirectory, recursive: true);
                }
            }
        }
    }
}
