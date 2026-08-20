using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>放行策略三层合并的语义测试：就近覆盖、可覆盖清单与两值偏序的收紧/放宽/非法值判定。</summary>
    public class ReleasePolicyCatalogTests
    {
        private const string BaselineJson = """
            {
              "策略": {
                "低.业务": "自动放行",
                "低.其他": "人审",
                "常规.业务": "人审",
                "常规.引擎": "人审"
              },
              "可覆盖": ["低.业务", "低.其他", "常规.业务"],
              "建议数阈值": 3,
              "高危范围": ["框架", "引擎"]
            }
            """;

        /// <summary>基线文件缺失时 Load 返回空目录并给出「基线文件不存在」这条 finding，不抛。</summary>
        [Fact]
        public void BaselineMissingReturnsEmptyCatalogWithOneFinding()
        {
            using var workspace = new Workspace();
            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            Assert.Empty(catalog.Policies);
            var finding = Assert.Single(catalog.Findings);
            Assert.Contains("放行策略基线文件不存在", finding.Reason);
        }

        /// <summary>只有基线时读到全部策略键，SourceLayers 全是「基线」，零 finding。</summary>
        [Fact]
        public void BaselineOnlyLoadsAllKeysFromBaselineLayer()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);

            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            Assert.Equal(4, catalog.Policies.Count);
            Assert.Equal("自动放行", catalog.Policies["低.业务"]);
            Assert.Equal("人审", catalog.Policies["常规.引擎"]);
            Assert.All(catalog.SourceLayers.Values, layer => Assert.Equal("基线", layer));
            Assert.Empty(catalog.Findings);
        }

        /// <summary>项目层把「自动放行」收紧成「人审」：采纳，SourceLayers 变「项目」，零 finding。</summary>
        [Fact]
        public void ProjectTighteningIsAdopted()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "策略": {
                    "低.业务": "人审"
                  }
                }
                """);

            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            Assert.Equal("人审", catalog.Policies["低.业务"]);
            Assert.Equal("项目", catalog.SourceLayers["低.业务"]);
            Assert.Empty(catalog.Findings);
        }

        /// <summary>项目层把不在可覆盖里的「人审」放宽成「自动放行」：不采纳、仍人审、出一条 finding。</summary>
        [Fact]
        public void ProjectWideningUnoverridableKeyIsRejected()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "策略": {
                    "常规.引擎": "自动放行"
                  }
                }
                """);

            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            Assert.Equal("人审", catalog.Policies["常规.引擎"]);
            Assert.Equal("基线", catalog.SourceLayers["常规.引擎"]);
            var finding = Assert.Single(catalog.Findings);
            Assert.Contains("放宽", finding.Reason);
        }

        /// <summary>项目层把在可覆盖里的「人审」放宽成「自动放行」：采纳，SourceLayers 变「项目」。</summary>
        [Fact]
        public void ProjectWideningOverridableKeyIsAdopted()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "策略": {
                    "低.其他": "自动放行"
                  }
                }
                """);

            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            Assert.Equal("自动放行", catalog.Policies["低.其他"]);
            Assert.Equal("项目", catalog.SourceLayers["低.其他"]);
            Assert.Empty(catalog.Findings);
        }

        /// <summary>项目层写「可覆盖」「建议数阈值」「高危范围」：各出一条 finding 且不生效。</summary>
        [Fact]
        public void ProjectReservedKeysAreReportedAndIgnored()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "策略": {
                    "低.业务": "人审"
                  },
                  "可覆盖": ["常规.引擎"],
                  "建议数阈值": 9,
                  "高危范围": ["其他"]
                }
                """);

            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            Assert.Equal(3, catalog.Findings.Count);
            Assert.Contains(catalog.Findings, finding => finding.Reason.Contains("可覆盖"));
            Assert.Contains(catalog.Findings, finding => finding.Reason.Contains("建议数阈值"));
            Assert.Contains(catalog.Findings, finding => finding.Reason.Contains("高危范围"));

            Assert.Equal(new[] { "低.业务", "低.其他", "常规.业务" }, catalog.OverridableKeys);
            Assert.Equal(3, catalog.SuggestionThreshold);
            Assert.Equal(new[] { "框架", "引擎" }, catalog.HighRiskScopes);
            Assert.Equal("人审", catalog.Policies["低.业务"]);
        }

        /// <summary>值写成第三种字符串：一条 finding，该键沿用上层值。</summary>
        [Fact]
        public void ThirdValueIsIllegalAndKeepsUpperValue()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "策略": {
                    "低.业务": "暂缓"
                  }
                }
                """);

            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            Assert.Equal("自动放行", catalog.Policies["低.业务"]);
            var finding = Assert.Single(catalog.Findings);
            Assert.Contains("非法值", finding.Reason);
        }

        /// <summary>业务层就近覆盖项目层：业务层的值胜出，SourceLayers 是「业务」。</summary>
        [Fact]
        public void BusinessOverridesProjectLayerValue()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "策略": {
                    "低.业务": "人审"
                  }
                }
                """);
            WriteBusiness(workspace.Root, "钓鱼", """
                {
                  "策略": {
                    "低.业务": "自动放行"
                  }
                }
                """);

            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "钓鱼");

            Assert.Equal("自动放行", catalog.Policies["低.业务"]);
            Assert.Equal("业务", catalog.SourceLayers["低.业务"]);
            Assert.Empty(catalog.Findings);
        }

        /// <summary>Decide 查不到的键返回「人审」。</summary>
        [Fact]
        public void DecideReturnsManualReviewForMissingKey()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);

            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            Assert.Equal("人审", catalog.Decide("高", "业务"));
            Assert.Equal("人审", catalog.Decide("常规", "构建"));
            Assert.Equal("自动放行", catalog.Decide("低", "业务"));
        }

        private static void WriteBaseline(string root)
        {
            WriteFile(SpecificationPaths.BaselineReleasePolicyFile(root), BaselineJson);
        }

        private static void WriteProject(string root, string json)
        {
            WriteFile(SpecificationPaths.ProjectReleasePolicyFile(root), json);
        }

        private static void WriteBusiness(string root, string moduleName, string json)
        {
            WriteFile(SpecificationPaths.BusinessReleasePolicyFile(root, moduleName), json);
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
                Root = Path.Combine(Path.GetTempPath(), "放行策略目录测试-" + Guid.NewGuid().ToString("N"));
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
