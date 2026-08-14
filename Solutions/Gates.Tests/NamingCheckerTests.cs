using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>命名与注释规范检查器的缩写、中文摘要检查测试。</summary>
    public class NamingCheckerTests
    {
        private const string SampleWithAbbreviation = @"using System;

namespace Sample
{
    /// <summary>示例持有者。</summary>
    public class InventoryHolder
    {
        private int inventoryMgrCount;
    }
}
";

        private const string SampleWellNamed = @"using System;

namespace Sample
{
    /// <summary>示例服务。</summary>
    public class ExampleService
    {
        private readonly int itemCount;
    }
}
";

        private const string SampleMissingSummary = @"using System;

namespace Sample
{
    public class ExampleService
    {
    }
}
";

        [Fact]
        public void CheckReportsAbbreviationInSourceFile()
        {
            RunInTempDirectory(relativePath =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, CreateConfiguration());

                Assert.NotEmpty(findings);
                var text = string.Join("\n", findings.Select(finding => finding.ToDisplayText()));
                Assert.Contains("Mgr", text);
                Assert.Contains(relativePath, text);
            }, SampleWithAbbreviation);
        }

        [Fact]
        public void CheckReportsNoFindingsForWellNamedFile()
        {
            RunInTempDirectory(relativePath =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, CreateConfiguration());

                Assert.Empty(findings);
            }, SampleWellNamed);
        }

        [Fact]
        public void CheckReportsMissingChineseSummary()
        {
            RunInTempDirectory(relativePath =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, CreateConfiguration());

                Assert.NotEmpty(findings);
                Assert.Contains(findings, finding => finding.Reason.Contains("summary"));
            }, SampleMissingSummary);
        }

        private static void RunInTempDirectory(Action<string> assert, string fileContent)
        {
            var directory = Path.Combine(Path.GetTempPath(), "gates_naming_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var previousDirectory = Environment.CurrentDirectory;
            try
            {
                var relativePath = "Sample.cs";
                File.WriteAllText(Path.Combine(directory, relativePath), fileContent);
                Environment.CurrentDirectory = directory;
                assert(relativePath);
            }
            finally
            {
                Environment.CurrentDirectory = previousDirectory;
                Directory.Delete(directory, true);
            }
        }

        private static GateConfiguration CreateConfiguration()
        {
            return new GateConfiguration
            {
                AbbreviationBlacklist = new List<string> { "Mgr", "Cfg", "Svc", "Btn", "Idx", "Tmp", "Utils", "Ctx", "Param", "Attr", "Conf" },
                DirectoryNameBlacklist = new List<string> { "misc", "common", "utils", "helper", "stuff", "temp", "new" },
                DirectoryNamePattern = "^[A-Za-z_][A-Za-z0-9_.]*$",
                DocumentLineLimit = 200,
                DocumentExemptions = new List<string>(),
                ChangedPathWhitelist = new List<string>(),
                TestFileGlobs = new List<string>()
            };
        }
    }
}
