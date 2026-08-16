using System;
using System.Collections.Generic;
using System.IO;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>测试基线锁的登记与篡改检出测试。</summary>
    public class TestBaselineLockTests
    {
        [Fact]
        public void WriteThenCheckReportsNoFindings()
        {
            var root = CreateTempDirectory();
            try
            {
                CreateTestFile(root);
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

        [Fact]
        public void ModifiedTestFileReportsSingleFindingWithPath()
        {
            var root = CreateTempDirectory();
            try
            {
                var testFile = CreateTestFile(root);
                var configuration = CreateConfiguration();
                var baselinePath = Path.Combine(root, "test-baseline.json");

                TestBaselineLock.WriteBaseline(root, configuration, baselinePath);
                File.AppendAllText(testFile, "// changed\n");

                var findings = TestBaselineLock.Check(root, configuration, baselinePath);

                Assert.Single(findings);
                Assert.Contains("Bar.cs", findings[0].Location);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void LineEndingChangeAloneReportsNoFindings()
        {
            var root = CreateTempDirectory();
            try
            {
                var testFile = CreateTestFile(root);
                var configuration = CreateConfiguration();
                var baselinePath = Path.Combine(root, "test-baseline.json");

                TestBaselineLock.WriteBaseline(root, configuration, baselinePath);
                File.WriteAllText(testFile, "// v1\r\n");

                var findings = TestBaselineLock.Check(root, configuration, baselinePath);

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void ByteOrderMarkAloneReportsNoFindings()
        {
            var root = CreateTempDirectory();
            try
            {
                var testFile = CreateTestFile(root);
                var configuration = CreateConfiguration();
                var baselinePath = Path.Combine(root, "test-baseline.json");

                TestBaselineLock.WriteBaseline(root, configuration, baselinePath);
                File.WriteAllText(testFile, "// v1\n", new System.Text.UTF8Encoding(true));

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

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "gates_baseline_" + Guid.NewGuid().ToString("N"));
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
