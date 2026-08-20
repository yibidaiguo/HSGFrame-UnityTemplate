using System;
using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>资产规格三层合并的语义测试：就近覆盖、可覆盖清单与收紧/放宽判定。</summary>
    public class AssetSpecCatalogTests
    {
        private const string BaselineJson = """
            {
              "资产类型": {
                "图标": {
                  "域": "资产.生图",
                  "规格": { "宽": 256, "最大面数": 3000, "需要透明": true },
                  "落点": "Assets/Game/Icons/",
                  "命名模式": "^icon_[a-z0-9_]+$",
                  "可覆盖": ["落点", "规格.宽"]
                }
              }
            }
            """;

        /// <summary>基线文件缺失时 Load 返回空目录并给出「基线文件不存在」这条 finding。</summary>
        [Fact]
        public void BaselineMissingReturnsEmptyCatalogWithOneFinding()
        {
            using var workspace = new Workspace();
            var catalog = AssetSpecCatalog.Load(workspace.Root, "");

            Assert.Empty(catalog.Types);
            var finding = Assert.Single(catalog.Findings);
            Assert.Contains("资产规格基线文件不存在", finding.Reason);
        }

        /// <summary>只有基线时读到全部类型，SourceLayer 是「基线」，规格键值扁平化正确。</summary>
        [Fact]
        public void BaselineOnlyLoadsTypeFromBaselineLayer()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);

            var catalog = AssetSpecCatalog.Load(workspace.Root, "");

            Assert.Single(catalog.Types);
            var icon = catalog.Find("图标");
            Assert.NotNull(icon);
            Assert.Equal("基线", icon.SourceLayer);
            Assert.Equal("资产.生图", icon.Domain);
            Assert.Equal("256", icon.Values["规格.宽"]);
            Assert.Empty(catalog.Findings);
        }

        /// <summary>项目层覆盖基线「可覆盖」清单里的键：生效、零 finding、SourceLayer 变「项目」。</summary>
        [Fact]
        public void ProjectOverridesKeyInOverridableList()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "资产类型": {
                    "图标": { "规格": { "宽": 512 } }
                  }
                }
                """);

            var catalog = AssetSpecCatalog.Load(workspace.Root, "");

            var icon = catalog.Find("图标");
            Assert.NotNull(icon);
            Assert.Equal("512", icon.Values["规格.宽"]);
            Assert.Equal("项目", icon.SourceLayer);
            Assert.Empty(catalog.Findings);
        }

        /// <summary>项目层把不可覆盖的数字键改小（收紧）：生效、零 finding。</summary>
        [Fact]
        public void ProjectTightensUnoverridableNumberKey()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "资产类型": {
                    "图标": { "规格": { "最大面数": 2000 } }
                  }
                }
                """);

            var catalog = AssetSpecCatalog.Load(workspace.Root, "");

            var icon = catalog.Find("图标");
            Assert.NotNull(icon);
            Assert.Equal("2000", icon.Values["规格.最大面数"]);
            Assert.Empty(catalog.Findings);
        }

        /// <summary>项目层把不可覆盖的数字键改大（放宽）：不生效、报 1 条。</summary>
        [Fact]
        public void ProjectWideningUnoverridableNumberKeyIsRejected()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "资产类型": {
                    "图标": { "规格": { "最大面数": 5000 } }
                  }
                }
                """);

            var catalog = AssetSpecCatalog.Load(workspace.Root, "");

            var icon = catalog.Find("图标");
            Assert.NotNull(icon);
            Assert.Equal("3000", icon.Values["规格.最大面数"]);
            var finding = Assert.Single(catalog.Findings);
            Assert.Contains("放宽", finding.Reason);
        }

        /// <summary>项目层把不可覆盖的布尔从 true 改成 false（放宽）：报 1 条、值保持 true。</summary>
        [Fact]
        public void ProjectWideningUnoverridableBooleanIsRejected()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "资产类型": {
                    "图标": { "规格": { "需要透明": false } }
                  }
                }
                """);

            var catalog = AssetSpecCatalog.Load(workspace.Root, "");

            var icon = catalog.Find("图标");
            Assert.NotNull(icon);
            Assert.Equal("true", icon.Values["规格.需要透明"]);
            var finding = Assert.Single(catalog.Findings);
            Assert.Contains("放宽", finding.Reason);
        }

        /// <summary>项目层新增一个基线没有的类型：生效、零 finding、SourceLayer 是「项目」。</summary>
        [Fact]
        public void ProjectAddsTypeMissingFromBaseline()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "资产类型": {
                    "立绘": {
                      "域": "资产.生图",
                      "规格": { "宽": 1024, "格式": "PNG" },
                      "落点": "Assets/Game/Portraits/",
                      "可覆盖": []
                    }
                  }
                }
                """);

            var catalog = AssetSpecCatalog.Load(workspace.Root, "");

            var portrait = catalog.Find("立绘");
            Assert.NotNull(portrait);
            Assert.Equal("项目", portrait.SourceLayer);
            Assert.Equal("1024", portrait.Values["规格.宽"]);
            Assert.Empty(catalog.Findings);
        }

        /// <summary>业务层覆盖项目层：业务层的值胜出、SourceLayer 是「业务」。</summary>
        [Fact]
        public void BusinessOverridesProjectLayerValue()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "资产类型": {
                    "图标": { "规格": { "宽": 512 } }
                  }
                }
                """);
            WriteBusiness(workspace.Root, "钓鱼", """
                {
                  "资产类型": {
                    "图标": { "规格": { "宽": 1024 } }
                  }
                }
                """);

            var catalog = AssetSpecCatalog.Load(workspace.Root, "钓鱼");

            var icon = catalog.Find("图标");
            Assert.NotNull(icon);
            Assert.Equal("1024", icon.Values["规格.宽"]);
            Assert.Equal("业务", icon.SourceLayer);
            Assert.Empty(catalog.Findings);
        }

        private static void WriteBaseline(string root)
        {
            WriteFile(SpecificationPaths.BaselineAssetSpecFile(root), BaselineJson);
        }

        private static void WriteProject(string root, string json)
        {
            WriteFile(SpecificationPaths.ProjectAssetSpecFile(root), json);
        }

        private static void WriteBusiness(string root, string moduleName, string json)
        {
            WriteFile(SpecificationPaths.BusinessAssetSpecFile(root, moduleName), json);
        }

        private static void WriteFile(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "资产规格目录测试-" + Guid.NewGuid().ToString("N"));
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
