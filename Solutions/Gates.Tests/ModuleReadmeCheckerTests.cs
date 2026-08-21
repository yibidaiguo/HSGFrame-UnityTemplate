using System;
using System.IO;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>模块自述门禁测试：缺 README、超行数、全部合规、模块目录不存在四种情形。</summary>
    public class ModuleReadmeCheckerTests
    {
        /// <summary>模块根没有 README.md 时要报一条，点名模块的目录。</summary>
        [Fact]
        public void ModuleWithoutReadmeIsReported()
        {
            var modulesRoot = CreateModulesTree("Combat");
            try
            {
                var findings = ModuleReadmeChecker.Check(modulesRoot, 40);

                var finding = Assert.Single(findings);
                Assert.Equal(
                    Path.Combine(modulesRoot, "Combat").Replace('/', Path.DirectorySeparatorChar),
                    finding.Location);
                Assert.Contains("缺 README.md", finding.Reason);
            }
            finally
            {
                Directory.Delete(modulesRoot, true);
            }
        }

        /// <summary>README 超过行数上限时要报一条，原因里写清实际行数与上限。</summary>
        [Fact]
        public void ReadmeOverMaxLinesIsReported()
        {
            var modulesRoot = CreateModulesTree("Combat");
            try
            {
                WriteReadme(modulesRoot, "Combat", 45);

                var findings = ModuleReadmeChecker.Check(modulesRoot, 40);

                var finding = Assert.Single(findings);
                Assert.Contains("45", finding.Reason);
                Assert.Contains("40", finding.Reason);
            }
            finally
            {
                Directory.Delete(modulesRoot, true);
            }
        }

        /// <summary>全部模块都有不超过上限的 README 时空清单，一条都不能报。</summary>
        [Fact]
        public void AllModulesCompliantReturnsNoFindings()
        {
            var modulesRoot = CreateModulesTree("Combat", "Level");
            try
            {
                WriteReadme(modulesRoot, "Combat", 10);
                WriteReadme(modulesRoot, "Level", 20);

                var findings = ModuleReadmeChecker.Check(modulesRoot, 40);

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(modulesRoot, true);
            }
        }

        /// <summary>模块目录不存在时返回空清单——新生成的项目可能还没有模块，跳过不是红。</summary>
        [Fact]
        public void MissingModulesDirectoryReturnsNoFindings()
        {
            var modulesRoot = Path.Combine(
                Path.GetTempPath(),
                "ModuleReadmeCheckerTests-" + Guid.NewGuid().ToString("N"),
                "Modules");
            Assert.False(Directory.Exists(modulesRoot));
            Assert.Empty(ModuleReadmeChecker.Check(modulesRoot, 40));
        }

        private static string CreateModulesTree(params string[] moduleNames)
        {
            var root = Path.Combine(Path.GetTempPath(), "ModuleReadmeCheckerTests-" + Guid.NewGuid().ToString("N"));
            foreach (var moduleName in moduleNames)
            {
                Directory.CreateDirectory(Path.Combine(root, moduleName));
            }

            return root;
        }

        private static void WriteReadme(string modulesRoot, string moduleName, int lineCount)
        {
            var lines = new string[lineCount];
            for (var i = 0; i < lineCount; i++)
            {
                lines[i] = "行 " + i;
            }

            File.WriteAllLines(
                Path.Combine(modulesRoot, moduleName, "README.md"),
                lines);
        }
    }
}
