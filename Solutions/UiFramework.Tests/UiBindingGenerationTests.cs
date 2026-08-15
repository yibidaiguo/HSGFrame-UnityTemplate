using System;
using System.IO;
using System.Text.Json;
using Template.Toolkit.UiScaffold;
using Xunit;

namespace Template.UiFramework.Tests
{
    /// <summary>UI 绑定代码生成与校验模式的回归测试。</summary>
    public class UiBindingGenerationTests
    {
        [Fact]
        public void GeneratedCsDeclaresOneTypedPropertyPerElement()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            var cs = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.cs"));
            Assert.Contains("public ProgressBar HealthBar { get; private set; }", cs);
            Assert.Contains("public Label CoinText { get; private set; }", cs);
            Assert.Contains("public Button InventoryButton { get; private set; }", cs);
        }

        [Fact]
        public void GeneratedCsDeclaresBindElementsMethod()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            var cs = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.cs"));
            Assert.Contains("public void BindElements(VisualElement root)", cs);
        }

        [Fact]
        public void BindElementsQueriesEachElementByName()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            var cs = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.cs"));
            Assert.Contains("HealthBar = root.Q<ProgressBar>(\"HealthBar\")", cs);
            Assert.Contains("CoinText = root.Q<Label>(\"CoinText\")", cs);
            Assert.Contains("InventoryButton = root.Q<Button>(\"InventoryButton\")", cs);
        }

        [Fact]
        public void GeneratedCsUsesUnityEngineUiElements()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            var cs = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.cs"));
            Assert.Contains("using UnityEngine.UIElements;", cs);
        }

        [Fact]
        public void GeneratedCsHeaderOmitsDeletedTemplateDirectory()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            var cs = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.cs"));
            Assert.DoesNotContain("Template/", cs);
        }

        [Fact]
        public void EmptyPanelStillDeclaresEmptyBindElements()
        {
            using var fixture = new ScaffoldFixture();
            var definition = new UiPanelDefinitionSource
            {
                PanelName = "空面板",
                PanelIdentifierName = "EmptyPanel",
            };

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            var cs = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "EmptyPanel.cs"));
            Assert.DoesNotContain("{ get; private set; }", cs);
            Assert.Contains("public void BindElements(VisualElement root)", cs);
            Assert.DoesNotContain("root.Q", cs);
        }

        [Fact]
        public void VerifyPassesForFreshlyScaffoldedPanel()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            Assert.Empty(PanelScaffolder.Verify(fixture.RepositoryRoot, definition, fixture.OutputDirectory));
        }

        [Fact]
        public void VerifyReportsDriftAfterGeneratedCsIsEdited()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);
            var csPath = Path.Combine(fixture.OutputDirectory, "MainPanel.cs");
            File.AppendAllText(csPath, "// 人为改动\n");

            var problems = PanelScaffolder.Verify(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            Assert.NotEmpty(problems);
            Assert.Contains(problems, problem => problem.Contains("MainPanel.cs"));
        }

        [Fact]
        public void VerifyReportsMissingProduct()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);
            File.Delete(Path.Combine(fixture.OutputDirectory, "MainPanel.cs"));

            var problems = PanelScaffolder.Verify(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            Assert.NotEmpty(problems);
            Assert.Contains(problems, problem => problem.Contains("尚未生成"));
        }

        private static UiPanelDefinitionSource LoadDefinition(string templateRoot)
        {
            var definitionPath = Path.Combine(templateRoot, "Solutions", "UiFramework.Tests", "TestData", "主界面.uidef.json");
            var json = File.ReadAllText(definitionPath);
            return JsonSerializer.Deserialize<UiPanelDefinitionSource>(json);
        }

        // 测试工作目录不稳定，不能靠相对路径硬拼：从程序集目录逐级向上找带 Tools/Gates/Config 的那一级作为模板根——
        // 模板被复制成别的项目名之后，这个标记仍然成立，而目录名 "Template" 不再成立。
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

            Assert.Fail("未找到包含 Template 目录的仓库根");
            return string.Empty;
        }

        private sealed class ScaffoldFixture : IDisposable
        {
            public ScaffoldFixture()
            {
                RepositoryRoot = FindTemplateRoot();
                OutputDirectory = Path.Combine(Path.GetTempPath(), "UiBindingGenerationTests", Guid.NewGuid().ToString("N"));
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
