using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

        [Fact]
        public void DoubleStarGlobReachesFilesInSubdirectoriesUnderTestsProject()
        {
            var root = CreateTempDirectory();
            try
            {
                var deepDirectory = Path.Combine(root, "Solutions", "Sample.Tests", "sub");
                Directory.CreateDirectory(deepDirectory);
                File.WriteAllText(Path.Combine(deepDirectory, "deep.cs"), "// v1\n");

                var configuration = new GateConfiguration
                {
                    TestFileGlobs = new List<string> { "Solutions/*.Tests/**.cs" }
                };
                var baselinePath = Path.Combine(root, "test-baseline.json");

                TestBaselineLock.WriteBaseline(root, configuration, baselinePath);

                var keys = ReadBaselineKeys(baselinePath);
                Assert.Contains("Solutions/Sample.Tests/sub/deep.cs", keys);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void SingleStarGlobDoesNotCrossIntoSubdirectories()
        {
            var root = CreateTempDirectory();
            try
            {
                var deepDirectory = Path.Combine(root, "Solutions", "Sample.Tests", "sub");
                Directory.CreateDirectory(deepDirectory);
                File.WriteAllText(Path.Combine(deepDirectory, "deep.cs"), "// v1\n");

                var configuration = new GateConfiguration
                {
                    TestFileGlobs = new List<string> { "Solutions/*.Tests/*.cs" }
                };
                var baselinePath = Path.Combine(root, "test-baseline.json");

                TestBaselineLock.WriteBaseline(root, configuration, baselinePath);

                var keys = ReadBaselineKeys(baselinePath);
                Assert.DoesNotContain("Solutions/Sample.Tests/sub/deep.cs", keys);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckSolutionMembershipFlagsProjectNotInSolution()
        {
            var root = CreateTempDirectory();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Solutions"));
                File.WriteAllText(Path.Combine(root, "Solutions", "Template.sln"), "A.Tests.csproj\n");
                Directory.CreateDirectory(Path.Combine(root, "Solutions", "A.Tests"));
                File.WriteAllText(Path.Combine(root, "Solutions", "A.Tests", "A.Tests.csproj"), "<Project/>");
                Directory.CreateDirectory(Path.Combine(root, "Solutions", "B.Tests"));
                File.WriteAllText(Path.Combine(root, "Solutions", "B.Tests", "B.Tests.csproj"), "<Project/>");

                var problems = TestBaselineLock.CheckSolutionMembership(root);

                var problem = Assert.Single(problems);
                Assert.Contains("B.Tests.csproj", problem);
                Assert.DoesNotContain("A.Tests.csproj", problem);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckSolutionMembershipReportsNothingWhenAllProjectsAreInSolution()
        {
            var root = CreateTempDirectory();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Solutions"));
                File.WriteAllText(Path.Combine(root, "Solutions", "Template.sln"), "A.Tests.csproj\nB.Tests.csproj\n");
                Directory.CreateDirectory(Path.Combine(root, "Solutions", "A.Tests"));
                File.WriteAllText(Path.Combine(root, "Solutions", "A.Tests", "A.Tests.csproj"), "<Project/>");
                Directory.CreateDirectory(Path.Combine(root, "Solutions", "B.Tests"));
                File.WriteAllText(Path.Combine(root, "Solutions", "B.Tests", "B.Tests.csproj"), "<Project/>");

                var problems = TestBaselineLock.CheckSolutionMembership(root);

                Assert.Empty(problems);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckSolutionMembershipReportsNothingWhenSolutionsDirectoryIsMissing()
        {
            var root = CreateTempDirectory();
            try
            {
                var problems = TestBaselineLock.CheckSolutionMembership(root);

                Assert.Empty(problems);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckReportsSolutionMembershipProblemAlongsideBaselineFindings()
        {
            var root = CreateTempDirectory();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Solutions"));
                File.WriteAllText(Path.Combine(root, "Solutions", "Template.sln"), "A.Tests.csproj\n");
                Directory.CreateDirectory(Path.Combine(root, "Solutions", "B.Tests"));
                File.WriteAllText(Path.Combine(root, "Solutions", "B.Tests", "B.Tests.csproj"), "<Project/>");
                var configuration = CreateConfiguration();
                var baselinePath = Path.Combine(root, "test-baseline.json");
                TestBaselineLock.WriteBaseline(root, configuration, baselinePath);

                var findings = TestBaselineLock.Check(root, configuration, baselinePath);

                Assert.Contains(findings, finding => finding.Reason.Contains("dotnet test 不会跑它"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static List<string> ReadBaselineKeys(string baselinePath)
        {
            using (var document = JsonDocument.Parse(File.ReadAllText(baselinePath)))
            {
                var filesElement = document.RootElement.GetProperty("files");
                return filesElement.EnumerateObject().Select(property => property.Name).ToList();
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
