using System.Collections.Generic;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>改动路径白名单检查器的放行与拦截测试。</summary>
    public class FileWhitelistCheckerTests
    {
        [Fact]
        public void CheckAllowsWhitelistedPathAndRejectsOthers()
        {
            var configuration = new GateConfiguration
            {
                ChangedPathWhitelist = new List<string> { "Template/" }
            };

            var findings = FileWhitelistChecker.Check(
                new[] { "Template/Foo.cs", "RPG_Unity/Foo.cs" },
                configuration);

            Assert.Single(findings);
            Assert.Contains("RPG_Unity/Foo.cs", findings[0].Location);
        }
    }
}
