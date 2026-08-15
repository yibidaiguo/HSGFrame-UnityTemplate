using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>命名检查器测试串行集合：这些测试改 Environment.CurrentDirectory，必须与其他测试串行。</summary>
    [CollectionDefinition("naming-checker-serial", DisableParallelization = true)]
    public sealed class NamingCheckerCollection
    {
    }

    /// <summary>命名检查器在空输入、空黑名单与目录命名上的边界行为。</summary>
    [Collection("naming-checker-serial")]
    public class NamingCheckerBoundaryTests
    {
        private const string WithSummary = @"using System;

namespace Sample
{
    /// <summary>示例服务。</summary>
    public class ExampleService
    {
    }
}
";

        private const string WithoutSummary = @"using System;

namespace Sample
{
    public class ExampleService
    {
    }
}
";

        [Fact]
        public void CheckReportsNothingWhenScanRootDoesNotExist()
        {
            var missing = Path.Combine(Path.GetTempPath(), "gate-tests", Guid.NewGuid().ToString("N"));

            var files = NamingChecker.EnumerateSourceFiles(missing);
            var findings = NamingChecker.Check(files, CreateConfiguration());

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckReportsNothingForEmptyDirectory()
        {
            var root = NewTempDirectory();
            try
            {
                var findings = NamingChecker.Check(
                    NamingChecker.EnumerateSourceFiles(root),
                    CreateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckReportsNothingForCommentOnlyFile()
        {
            RunInTempDirectory("Sample.cs", "// 只有注释，没有任何代码。\n/* 块注释 */\n", (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Empty(findings);
            });
        }

        [Fact]
        public void CheckReportsNothingForAbbreviationWhenBlacklistIsEmpty()
        {
            var content = @"using System;

namespace Sample
{
    /// <summary>示例持有者。</summary>
    public class InventoryHolder
    {
        private int inventoryMgrCount;
    }
}
";
            RunInTempDirectory("Sample.cs", content, (configuration, relativePath) =>
            {
                configuration.AbbreviationBlacklist = new List<string>();

                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Empty(findings);
            });
        }

        [Fact]
        public void CheckReportsNothingForPublicTypeWithChineseSummary()
        {
            RunInTempDirectory("Sample.cs", WithSummary, (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Empty(findings);
            });
        }

        [Fact]
        public void CheckReportsMissingChineseSummaryOnPublicType()
        {
            RunInTempDirectory("Sample.cs", WithoutSummary, (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Contains(findings, finding => finding.Reason.Contains("summary"));
            });
        }

        [Fact]
        public void CheckReportsDirectoryNameInBlacklist()
        {
            RunInTempDirectory("misc/Foo.cs", WithSummary, (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                var finding = Assert.Single(findings);
                Assert.Contains("目录名", finding.Reason);
                Assert.Contains("misc", finding.Location);
            });
        }

        private static void RunInTempDirectory(string relativePath, string fileContent, Action<GateConfiguration, string> assert)
        {
            var directory = NewTempDirectory();
            var previousDirectory = Environment.CurrentDirectory;
            try
            {
                var fullPath = Path.Combine(directory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, fileContent);

                Environment.CurrentDirectory = directory;
                assert(CreateConfiguration(), relativePath);
            }
            finally
            {
                Environment.CurrentDirectory = previousDirectory;
                Directory.Delete(directory, true);
            }
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
                AbbreviationBlacklist = new List<string> { "Mgr", "Cfg", "Svc" },
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
