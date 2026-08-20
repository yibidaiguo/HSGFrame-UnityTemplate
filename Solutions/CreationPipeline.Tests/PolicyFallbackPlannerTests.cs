using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>策略回落规划与应用的测试：只收紧、只写项目层、合并写与空计划不写盘。</summary>
    public class PolicyFallbackPlannerTests
    {
        private const string BaselineJson = """
            {
              "策略": {
                "低.业务": "自动放行",
                "低.其他": "人审",
                "高.引擎": "人审"
              },
              "可覆盖": ["低.业务"],
              "建议数阈值": 3,
              "抽查比例": 0.2,
              "高危范围": ["框架", "引擎"]
            }
            """;

        /// <summary>Plan：低 + 业务（基线是自动放行）→ 进 ChangedKeys。</summary>
        [Fact]
        public void PlanPutsAutomaticKeyIntoChangedKeys()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            var plan = PolicyFallbackPlanner.Plan(catalog, "低", new[] { "业务" });

            Assert.Equal(new[] { "低.业务" }, plan.ChangedKeys);
            Assert.Empty(plan.AlreadyManualKeys);
            Assert.Single(plan.Notes);
        }

        /// <summary>Plan：高 + 引擎（基线是人审）→ 进 AlreadyManualKeys，ChangedKeys 空。</summary>
        [Fact]
        public void PlanPutsManualKeyIntoAlreadyManualKeys()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            var plan = PolicyFallbackPlanner.Plan(catalog, "高", new[] { "引擎" });

            Assert.Empty(plan.ChangedKeys);
            Assert.Equal(new[] { "高.引擎" }, plan.AlreadyManualKeys);
            Assert.Single(plan.Notes);
        }

        /// <summary>grade 或 scopes 为空 → 三个列表全空。</summary>
        [Fact]
        public void PlanWithEmptyGradeOrScopesYieldsEmptyLists()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");

            var emptyGrade = PolicyFallbackPlanner.Plan(catalog, "", new[] { "业务" });
            Assert.Empty(emptyGrade.ChangedKeys);
            Assert.Empty(emptyGrade.AlreadyManualKeys);
            Assert.Empty(emptyGrade.Notes);

            var emptyScopes = PolicyFallbackPlanner.Plan(catalog, "低", new string[0]);
            Assert.Empty(emptyScopes.ChangedKeys);
            Assert.Empty(emptyScopes.AlreadyManualKeys);
            Assert.Empty(emptyScopes.Notes);
        }

        /// <summary>Apply 之后重新 Load → Decide("低","业务") 变成「人审」。</summary>
        [Fact]
        public void ApplyTightensAutomaticKeyToManualReview()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");
            var plan = PolicyFallbackPlanner.Plan(catalog, "低", new[] { "业务" });

            var applied = PolicyFallbackPlanner.Apply(workspace.Root, plan);

            Assert.Equal(new[] { "低.业务" }, applied);
            var reloaded = ReleasePolicyCatalog.Load(workspace.Root, "");
            Assert.Equal("人审", reloaded.Decide("低", "业务"));
        }

        /// <summary>Apply 时项目层文件里已有的别的键在写完之后还在（合并写，不是覆盖写）。</summary>
        [Fact]
        public void ApplyPreservesExistingProjectKeys()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            WriteProject(workspace.Root, """
                {
                  "策略": {
                    "低.其他": "人审"
                  }
                }
                """);
            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");
            var plan = PolicyFallbackPlanner.Plan(catalog, "低", new[] { "业务", "其他" });

            PolicyFallbackPlanner.Apply(workspace.Root, plan);

            var text = File.ReadAllText(SpecificationPaths.ProjectReleasePolicyFile(workspace.Root));
            Assert.Contains("低.业务", text);
            Assert.Contains("低.其他", text);
            var reloaded = ReleasePolicyCatalog.Load(workspace.Root, "");
            Assert.Equal("人审", reloaded.Decide("低", "业务"));
            Assert.Equal("人审", reloaded.Decide("低", "其他"));
        }

        /// <summary>ChangedKeys 为空时 Apply → 返回空列表，且项目层文件没有被创建。</summary>
        [Fact]
        public void ApplyWithNoChangesCreatesNoFile()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");
            var plan = PolicyFallbackPlanner.Plan(catalog, "高", new[] { "引擎" });

            var applied = PolicyFallbackPlanner.Apply(workspace.Root, plan);

            Assert.Empty(applied);
            Assert.False(File.Exists(SpecificationPaths.ProjectReleasePolicyFile(workspace.Root)));
        }

        /// <summary>回落只写项目层：基线文件在 Apply 前后逐字未变。</summary>
        [Fact]
        public void ApplyNeverTouchesBaseline()
        {
            using var workspace = new Workspace();
            WriteBaseline(workspace.Root);
            var baselinePath = SpecificationPaths.BaselineReleasePolicyFile(workspace.Root);
            var beforeText = File.ReadAllText(baselinePath);
            var catalog = ReleasePolicyCatalog.Load(workspace.Root, "");
            var plan = PolicyFallbackPlanner.Plan(catalog, "低", new[] { "业务" });

            PolicyFallbackPlanner.Apply(workspace.Root, plan);

            Assert.Equal(beforeText, File.ReadAllText(baselinePath));
        }

        private static void WriteBaseline(string root)
        {
            WriteFile(SpecificationPaths.BaselineReleasePolicyFile(root), BaselineJson);
        }

        private static void WriteProject(string root, string json)
        {
            WriteFile(SpecificationPaths.ProjectReleasePolicyFile(root), json);
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
                Root = Path.Combine(Path.GetTempPath(), "策略回落测试-" + Guid.NewGuid().ToString("N"));
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
