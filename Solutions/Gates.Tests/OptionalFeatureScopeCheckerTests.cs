using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>
    /// 可选功能引用范围检查器的测试：包内引得、包外引不得，前缀不误伤同族的别的包。
    /// </summary>
    public class OptionalFeatureScopeCheckerTests
    {
        private const string PackageDirectory = "Packages/com.hsgframe.hotfix";

        /// <summary>包目录内的 asmdef 引热更程序集是它的本分，不该报。</summary>
        [Fact]
        public void ReferenceFromInsideThePackageIsAllowed()
        {
            var root = CreateTree();
            WriteAsmdef(root, PackageDirectory + "/HybridCLR/HSGFrame.Hotfix.HybridCLR.asmdef", "HybridCLR.Runtime");

            Assert.Empty(OptionalFeatureScopeChecker.Check(root, CreateConfiguration()));
        }

        /// <summary>常驻程序集引 HybridCLR 就是这道门禁要拦的东西。</summary>
        [Fact]
        public void ReferenceFromOutsideThePackageIsReported()
        {
            var root = CreateTree();
            const string bootPath = "UnityProject/Assets/Game/Scripts/Boot/Game.Boot.asmdef";
            WriteAsmdef(root, bootPath, "HybridCLR.Runtime");

            var findings = OptionalFeatureScopeChecker.Check(root, CreateConfiguration());

            var finding = Assert.Single(findings);
            Assert.Equal(bootPath, finding.Location);
            Assert.Contains("hotfix", finding.Reason);
            Assert.Contains("HybridCLR.Runtime", finding.Reason);
        }

        /// <summary>前缀是按「等于或以前缀点开头」判的，子程序集同样拦得住。</summary>
        [Fact]
        public void PrefixAlsoMatchesNestedAssemblyNames()
        {
            var root = CreateTree();
            WriteAsmdef(root, "UnityProject/Assets/Game/Scripts/View/Game.View.asmdef", "HSGFrame.Hotfix.Probe");

            Assert.Single(OptionalFeatureScopeChecker.Check(root, CreateConfiguration()));
        }

        /// <summary>同族的别的框架包不该被前缀误伤：HSGFrame.Save 与热更无关。</summary>
        [Fact]
        public void PrefixDoesNotHitOtherAssembliesInTheSameFamily()
        {
            var root = CreateTree();
            WriteAsmdef(root, "UnityProject/Assets/Game/Scripts/Boot/Game.Boot.asmdef", "HSGFrame.Save", "YooAsset");

            Assert.Empty(OptionalFeatureScopeChecker.Check(root, CreateConfiguration()));
        }

        /// <summary>没有配任何规则时这道门禁什么都不查，也不该抛。</summary>
        [Fact]
        public void NoScopesConfiguredChecksNothing()
        {
            var root = CreateTree();
            WriteAsmdef(root, "UnityProject/Assets/Game/Scripts/Boot/Game.Boot.asmdef", "HybridCLR.Runtime");

            Assert.Empty(OptionalFeatureScopeChecker.Check(root, new GateConfiguration()));
            Assert.Empty(OptionalFeatureScopeChecker.Check(
                root, new GateConfiguration { OptionalFeatureScopes = new List<OptionalFeatureScope>() }));
        }

        /// <summary>坏掉的 asmdef 跳过就好，不该把整道门禁带崩，别处的真违规照报。</summary>
        [Fact]
        public void BrokenAsmdefIsSkippedWithoutHidingRealViolations()
        {
            var root = CreateTree();
            WriteText(root, "UnityProject/Assets/Game/Scripts/Shared/Broken.asmdef", "{ 这不是 JSON");
            const string bootPath = "UnityProject/Assets/Game/Scripts/Boot/Game.Boot.asmdef";
            WriteAsmdef(root, bootPath, "HybridCLR.Runtime");

            var findings = OptionalFeatureScopeChecker.Check(root, CreateConfiguration());

            Assert.Equal(bootPath, Assert.Single(findings).Location);
        }

        private static GateConfiguration CreateConfiguration()
        {
            return new GateConfiguration
            {
                OptionalFeatureScopes = new List<OptionalFeatureScope>
                {
                    new OptionalFeatureScope
                    {
                        FeatureName = "hotfix",
                        PackageDirectory = PackageDirectory,
                        ReferencePrefixes = new[] { "HSGFrame.Hotfix", "HybridCLR" },
                    },
                },
            };
        }

        private static string CreateTree()
        {
            var root = Path.Combine(Path.GetTempPath(), "feature-scope-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            return root;
        }

        private static void WriteAsmdef(string root, string relativePath, params string[] references)
        {
            var quoted = string.Join(", ", references.Select(reference => "\"" + reference + "\""));
            WriteText(root, relativePath, "{ \"name\": \"探针树\", \"references\": [" + quoted + "] }");
        }

        private static void WriteText(string root, string relativePath, string content)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
        }
    }
}
