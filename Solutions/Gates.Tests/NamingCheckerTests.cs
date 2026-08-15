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

        [Fact]
        public void DirectoryStartingWithUnderscoreIsReportedWhenNotExempt()
        {
            RunInTempDirectory(_ =>
            {
                Directory.CreateDirectory("_Legacy");
                File.WriteAllText(Path.Combine("_Legacy", "Sample.cs"), SampleWellNamed);

                var findings = NamingChecker.Check(new[] { Path.Combine("_Legacy", "Sample.cs") }, CreateConfiguration());

                Assert.Contains(findings, finding => finding.Reason.Contains("以下划线开头"));
            }, SampleWellNamed);
        }

        [Fact]
        public void DirectoryStartingWithUnderscoreIsAllowedWhenExempt()
        {
            RunInTempDirectory(_ =>
            {
                Directory.CreateDirectory("_Legacy");
                File.WriteAllText(Path.Combine("_Legacy", "Sample.cs"), SampleWellNamed);

                var configuration = CreateConfiguration();
                configuration.UnderscoreExemptNames = new List<string> { "_Legacy" };
                var findings = NamingChecker.Check(new[] { Path.Combine("_Legacy", "Sample.cs") }, configuration);

                // 断言「这个目录名一条都没被报出来」，而不是只筛「以下划线开头」那句话：
                // 豁免逻辑一旦失效，目录会落到下面的正则那一支、报成「不符合命名规范」，
                // 只筛那一句的话这条测试照样绿——盲区正好盖住它要防的失败。
                Assert.DoesNotContain(findings, finding => finding.Reason.Contains("_Legacy"));
            }, SampleWellNamed);
        }

        [Fact]
        public void DirectoryExemptionIsCaseInsensitive()
        {
            RunInTempDirectory(_ =>
            {
                Directory.CreateDirectory("_Inbox");
                File.WriteAllText(Path.Combine("_Inbox", "Sample.cs"), SampleWellNamed);

                var configuration = CreateConfiguration();
                configuration.UnderscoreExemptNames = new List<string> { "_inbox" };
                var findings = NamingChecker.Check(new[] { Path.Combine("_Inbox", "Sample.cs") }, configuration);

                // 断言「这个目录名一条都没被报出来」，而不是只筛「以下划线开头」那句话：
                // 豁免逻辑一旦失效，目录会落到下面的正则那一支、报成「不符合命名规范」，
                // 只筛那一句的话这条测试照样绿——盲区正好盖住它要防的失败。
                Assert.DoesNotContain(findings, finding => finding.Reason.Contains("_Inbox"));
            }, SampleWellNamed);
        }

        [Fact]
        public void LetterLeadingDirectoryIsUnaffected()
        {
            RunInTempDirectory(_ =>
            {
                Directory.CreateDirectory("Modules");
                File.WriteAllText(Path.Combine("Modules", "Sample.cs"), SampleWellNamed);

                var findings = NamingChecker.Check(new[] { Path.Combine("Modules", "Sample.cs") }, CreateConfiguration());

                Assert.DoesNotContain(findings, finding => finding.Reason.Contains("以下划线开头"));
            }, SampleWellNamed);
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
                DirectoryNamePattern = "^[A-Za-z][A-Za-z0-9_.]*$",
                DocumentLineLimit = 200,
                DocumentExemptions = new List<string>(),
                ChangedPathWhitelist = new List<string>(),
                TestFileGlobs = new List<string>(),
                UnderscoreExemptNames = new List<string>()
            };
        }
    }
}
