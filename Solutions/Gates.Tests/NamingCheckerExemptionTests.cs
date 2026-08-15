using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>缩写豁免名单的测试：第三方 API 成员名放行，我们自己的同词段标识符照报。</summary>
    public class NamingCheckerExemptionTests
    {
        // paramName 是上游 NodeGraph 的 UnitOverride 字段名，调用点绕不开写出它；
        // parameterName 是我们自己的命名，词段相同但不在豁免名单里，应当照常被报出来。
        private const string SampleWithThirdPartyMemberName = @"using System;

namespace Sample
{
    /// <summary>示例调用点。</summary>
    public class ExampleCaller
    {
        private void Attach()
        {
            var slot = new UnitOverride { paramName = ""actions"" };
        }
    }
}
";

        private const string SampleWithOwnAbbreviation = @"using System;

namespace Sample
{
    /// <summary>示例调用点。</summary>
    public class ExampleCaller
    {
        private string paramValue;
    }
}
";

        [Fact]
        public void ExemptIdentifierIsNotReported()
        {
            var configuration = CreateConfiguration();
            configuration.AbbreviationExemptIdentifiers = new List<string> { "paramName" };

            var findings = CheckSource(SampleWithThirdPartyMemberName, configuration);

            Assert.DoesNotContain(findings, finding => finding.Reason.Contains("paramName"));
        }

        [Fact]
        public void ExemptIdentifierIsReportedWhenTheListIsEmpty()
        {
            var configuration = CreateConfiguration();
            configuration.AbbreviationExemptIdentifiers = new List<string>();

            var findings = CheckSource(SampleWithThirdPartyMemberName, configuration);

            Assert.Contains(findings, finding => finding.Reason.Contains("paramName"));
        }

        [Fact]
        public void ExemptionIsMatchedWholeIdentifierRatherThanBySegment()
        {
            var configuration = CreateConfiguration();
            configuration.AbbreviationExemptIdentifiers = new List<string> { "paramName" };

            var findings = CheckSource(SampleWithOwnAbbreviation, configuration);

            Assert.Contains(findings, finding => finding.Reason.Contains("paramValue"));
        }

        [Fact]
        public void MissingExemptionListBehavesLikeAnEmptyOne()
        {
            var configuration = CreateConfiguration();
            configuration.AbbreviationExemptIdentifiers = null;

            var findings = CheckSource(SampleWithThirdPartyMemberName, configuration);

            Assert.Contains(findings, finding => finding.Reason.Contains("paramName"));
        }

        private static IReadOnlyList<GateFinding> CheckSource(string source, GateConfiguration configuration)
        {
            var directory = Path.Combine(Path.GetTempPath(), "NamingCheckerExemptionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var filePath = Path.Combine(directory, "Sample.cs");
                File.WriteAllText(filePath, source);
                return NamingChecker.Check(new[] { filePath }, configuration);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static GateConfiguration CreateConfiguration()
        {
            return new GateConfiguration
            {
                AbbreviationBlacklist = new List<string> { "Mgr", "Cfg", "Svc", "Btn", "Idx", "Tmp", "Utils", "Ctx", "Param", "Attr", "Conf" },
                DirectoryNameBlacklist = new List<string>(),
                DirectoryNamePattern = "^[A-Za-z_][A-Za-z0-9_.]*$",
                DocumentLineLimit = 200,
                DocumentExemptions = new List<string>(),
                ChangedPathWhitelist = new List<string>(),
                TestFileGlobs = new List<string>()
            };
        }
    }
}
