using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>测试基线锁在基线缺失、文件增删上的边界行为。</summary>
    public class TestBaselineLockBoundaryTests
    {
        [Fact]
        public void CheckReportsNewFileWhenBaselineDoesNotExist()
        {
            var root = NewTempDirectory();
            try
            {
                CreateTestFile(root);
                var configuration = CreateConfiguration();
                var baselinePath = Path.Combine(root, "test-baseline.json");

                var findings = TestBaselineLock.Check(root, configuration, baselinePath);

                Assert.NotEmpty(findings);
                Assert.Contains(findings, finding => finding.Reason.Contains("新增"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckReportsDisappearedFileWhenBaselineHasRemovedFile()
        {
            var root = NewTempDirectory();
            try
            {
                var testFile = CreateTestFile(root);
                var configuration = CreateConfiguration();
                var baselinePath = Path.Combine(root, "test-baseline.json");
                TestBaselineLock.WriteBaseline(root, configuration, baselinePath);
                File.Delete(testFile);

                var findings = TestBaselineLock.Check(root, configuration, baselinePath);

                Assert.NotEmpty(findings);
                Assert.Contains(findings, finding => finding.Reason.Contains("已消失"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckReportsNewFileNotInBaseline()
        {
            var root = NewTempDirectory();
            try
            {
                var configuration = CreateConfiguration();
                var baselinePath = Path.Combine(root, "test-baseline.json");
                TestBaselineLock.WriteBaseline(root, configuration, baselinePath);

                CreateTestFile(root);

                var findings = TestBaselineLock.Check(root, configuration, baselinePath);

                Assert.NotEmpty(findings);
                Assert.Contains(findings, finding => finding.Reason.Contains("新增"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckReportsNothingForEmptyBaselineAndNoTestFiles()
        {
            var root = NewTempDirectory();
            try
            {
                var configuration = CreateConfiguration();
                var baselinePath = Path.Combine(root, "test-baseline.json");
                TestBaselineLock.WriteBaseline(root, configuration, baselinePath);

                var findings = TestBaselineLock.Check(root, configuration, baselinePath);

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string CreateTestFile(string root)
        {
            var directory = Path.Combine(root, "Template", "Solutions", "Sample.Tests");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "Bar.cs");
            File.WriteAllText(file, "// v1\n");
            return file;
        }

        private static string NewTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "gate-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static GateConfiguration CreateConfiguration()
        {
            return new GateConfiguration
            {
                TestFileGlobs = new List<string> { "Template/Solutions/*.Tests/*.cs" }
            };
        }
    }
}
