using System;
using System.IO;
using System.Text.Json;
using Template.Toolkit.UiScaffold;
using Xunit;

namespace Template.UiFramework.Tests
{
    /// <summary>ui.scaffold 三件套生成与幂等性测试。</summary>
    public class PanelScaffolderTests
    {
        [Fact]
        public void ScaffoldWritesThreeFilesForMainPanel()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            var files = PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            Assert.Equal(3, files.Count);
            Assert.True(File.Exists(Path.Combine(fixture.OutputDirectory, "MainPanel.uxml")));
            Assert.True(File.Exists(Path.Combine(fixture.OutputDirectory, "MainPanel.uss")));
            Assert.True(File.Exists(Path.Combine(fixture.OutputDirectory, "MainPanel.cs")));
        }

        [Fact]
        public void UxmlContainsElementNamesAndButton()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            var uxml = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.uxml"));
            Assert.Contains("name=\"血条\"", uxml);
            Assert.Contains("ui:Button", uxml);
        }

        [Fact]
        public void CsContainsMainPanelClassAndNormalLayer()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);

            var cs = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.cs"));
            Assert.Contains("class MainPanel", cs);
            Assert.Contains("PanelLayer.Normal", cs);
        }

        [Fact]
        public void ScaffoldIsIdempotent()
        {
            using var fixture = new ScaffoldFixture();
            var definition = LoadDefinition(fixture.RepositoryRoot);

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);
            var firstUxml = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.uxml"));
            var firstUss = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.uss"));
            var firstCs = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.cs"));

            PanelScaffolder.Scaffold(fixture.RepositoryRoot, definition, fixture.OutputDirectory);
            var secondUxml = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.uxml"));
            var secondUss = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.uss"));
            var secondCs = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "MainPanel.cs"));

            Assert.Equal(firstUxml, secondUxml);
            Assert.Equal(firstUss, secondUss);
            Assert.Equal(firstCs, secondCs);
        }

        private static UiPanelDefinitionSource LoadDefinition(string repositoryRoot)
        {
            var definitionPath = Path.Combine(repositoryRoot, "Template", "Solutions", "UiFramework.Tests", "TestData", "主界面.uidef.json");
            var json = File.ReadAllText(definitionPath);
            return JsonSerializer.Deserialize<UiPanelDefinitionSource>(json);
        }

        // 测试工作目录不稳定，不能靠相对路径硬拼：从程序集目录逐级向上找含 Template 目录的那一级作为仓库根。
        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Template")))
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
                RepositoryRoot = FindRepositoryRoot();
                OutputDirectory = Path.Combine(Path.GetTempPath(), "UiScaffoldTests", Guid.NewGuid().ToString("N"));
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
