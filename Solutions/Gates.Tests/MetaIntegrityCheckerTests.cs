using System;
using System.Collections.Generic;
using System.IO;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>.meta 完整性检查器的缺失、孤儿、跳过与根目录缺失测试。</summary>
    public class MetaIntegrityCheckerTests
    {
        [Fact]
        public void CheckReportsSingleFindingWhenAssetsRootDoesNotExist()
        {
            var missing = NewTempPath();

            var findings = MetaIntegrityChecker.Check(missing, CreateConfiguration());

            var finding = Assert.Single(findings);
            Assert.Contains("不存在", finding.Reason);
        }

        [Fact]
        public void CheckReportsNothingForEmptyDirectory()
        {
            var root = NewTempPath();
            Directory.CreateDirectory(root);
            try
            {
                var findings = MetaIntegrityChecker.Check(root, CreateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckReportsNothingForPairedAsset()
        {
            var root = NewTempPath();
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "Foo.cs"), "// asset\n");
                File.WriteAllText(Path.Combine(root, "Foo.cs.meta"), "guid: abc\n");

                var findings = MetaIntegrityChecker.Check(root, CreateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckReportsMissingMetaWithRelativeForwardSlashLocation()
        {
            var root = NewTempPath();
            Directory.CreateDirectory(root);
            try
            {
                var sub = Path.Combine(root, "Sub");
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(root, "Sub.meta"), "guid: sub\n");
                File.WriteAllText(Path.Combine(sub, "Foo.cs"), "// asset\n");

                var findings = MetaIntegrityChecker.Check(root, CreateConfiguration());

                var finding = Assert.Single(findings);
                Assert.Equal("Sub/Foo.cs", finding.Location);
                Assert.Contains("缺少", finding.Reason);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckReportsOrphanMetaWhenAssetIsGone()
        {
            var root = NewTempPath();
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "Foo.cs.meta"), "guid: abc\n");

                var findings = MetaIntegrityChecker.Check(root, CreateConfiguration());

                var finding = Assert.Single(findings);
                Assert.Contains("已不存在", finding.Reason);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckReportsMissingMetaForDirectory()
        {
            var root = NewTempPath();
            Directory.CreateDirectory(root);
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Sub"));

                var findings = MetaIntegrityChecker.Check(root, CreateConfiguration());

                var finding = Assert.Single(findings);
                Assert.Equal("Sub", finding.Location);
                Assert.Contains("缺少", finding.Reason);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckSkipsDsStore()
        {
            var root = NewTempPath();
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, ".DS_Store"), "x");

                var findings = MetaIntegrityChecker.Check(root, CreateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CheckSkipsWholeSubtreeWhenSegmentMatches()
        {
            var root = NewTempPath();
            Directory.CreateDirectory(root);
            try
            {
                var nested = Path.Combine(root, "SkipMe", "Inner");
                Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(nested, "Foo.cs"), "// asset\n");

                var configuration = CreateConfiguration();
                configuration.SourceScanSkipSegments = new List<string> { "SkipMe" };

                var findings = MetaIntegrityChecker.Check(root, configuration);

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string NewTempPath()
        {
            return Path.Combine(Path.GetTempPath(), "gate-tests", Guid.NewGuid().ToString("N"));
        }

        private static GateConfiguration CreateConfiguration()
        {
            return new GateConfiguration
            {
                SourceScanSkipSegments = new List<string>()
            };
        }
    }
}
