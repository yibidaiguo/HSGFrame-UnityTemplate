using System;
using System.Collections.Generic;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>改动路径白名单检查器在空名单与路径规范化上的边界行为。</summary>
    public class FileWhitelistCheckerBoundaryTests
    {
        [Fact]
        public void CheckAllowsEverythingWhenWhitelistIsEmptyArray()
        {
            var configuration = new GateConfiguration { ChangedPathWhitelist = new List<string>() };

            var findings = FileWhitelistChecker.Check(new[] { "Anything/Foo.cs" }, configuration);

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckAllowsEverythingWhenWhitelistIsNull()
        {
            var configuration = new GateConfiguration { ChangedPathWhitelist = null };

            var findings = FileWhitelistChecker.Check(new[] { "Anything/Foo.cs" }, configuration);

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckReportsNothingForEmptyChangedPaths()
        {
            var configuration = new GateConfiguration { ChangedPathWhitelist = new List<string> { "Template/" } };

            var findings = FileWhitelistChecker.Check(Array.Empty<string>(), configuration);

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckMatchesBackslashPathAgainstForwardSlashWhitelist()
        {
            var configuration = new GateConfiguration { ChangedPathWhitelist = new List<string> { "Template/" } };

            var findings = FileWhitelistChecker.Check(new[] { "Template\\Foo.cs" }, configuration);

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckMatchesCaseInsensitively()
        {
            var configuration = new GateConfiguration { ChangedPathWhitelist = new List<string> { "Template/" } };

            var findings = FileWhitelistChecker.Check(new[] { "template/Foo.cs" }, configuration);

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckSkipsBlankPaths()
        {
            var configuration = new GateConfiguration { ChangedPathWhitelist = new List<string> { "Template/" } };

            var findings = FileWhitelistChecker.Check(new[] { "", "   ", "Template/Foo.cs" }, configuration);

            Assert.Empty(findings);
        }
    }
}
