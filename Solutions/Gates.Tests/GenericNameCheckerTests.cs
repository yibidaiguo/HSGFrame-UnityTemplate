using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>通用性检查器的测试：宿主项目专属名字在标识符、菜单路径、路径字面量里要报，在注释与面向用户的消息里放行。</summary>
    public class GenericNameCheckerTests
    {
        private const string SampleWithIdentifierRpg = @"using System;

namespace Sample
{
    public class ExampleHolder
    {
        private int myRPGCount;
    }
}
";

        private const string SampleWithRebuiltRpg = @"using System;

namespace Sample
{
    public class ExampleHolder
    {
        private int rebuiltRpgValue;
    }
}
";

        private const string SampleWithRpgInComment = @"using System;

namespace Sample
{
    /// <summary>这段来自 RPG 旧工程的迁移代码。</summary>
    public class ExampleHolder
    {
        // RPG 的旧逻辑在这里，改成通用实现后注释保留来源说明。
        private int itemCount;
    }
}
";

        private const string SampleWithRpgInMessage = @"using System;

namespace Sample
{
    public class ExampleHolder
    {
        private void Show()
        {
            Debug.Log(""欢迎来到 RPG 世界"");
        }
    }
}
";

        private const string SampleWithMenuItemRpg = @"using UnityEditor;

namespace Sample
{
    public static class ExampleMenu
    {
        [MenuItem(""RPG工具/xxx"")]
        private static void Open()
        {
        }
    }
}
";

        private const string SampleWithMenuItemClean = @"using UnityEditor;

namespace Sample
{
    public static class ExampleMenu
    {
        [MenuItem(""工具链/xxx"")]
        private static void Open()
        {
        }
    }
}
";

        private const string SampleWithCreateAssetMenu = @"using UnityEngine;

namespace Sample
{
    [CreateAssetMenu(menuName = ""RPG/Config"")]
    public class ExampleAsset : ScriptableObject
    {
    }
}
";

        private const string SampleWithPathLiteral = @"using System;

namespace Sample
{
    public class ExampleLoader
    {
        private const string ConfigPath = ""Assets/RPG/Config"";
    }
}
";

        private const string SampleWithCleanPathLiteral = @"using System;

namespace Sample
{
    public class ExampleLoader
    {
        private const string ConfigPath = ""Assets/Config/Level"";
    }
}
";

        private const string SampleWithSpacedLiteral = @"using System;

namespace Sample
{
    public class ExampleView
    {
        private void Draw()
        {
            var label = ""RPG 存档"";
        }
    }
}
";

        private const string SampleWithHsgsFrame = @"using System;

namespace Sample
{
    public class ExampleService
    {
        private int hsgsFrameCount;
    }
}
";

        [Fact]
        public void CheckReportsBlacklistedNameInIdentifier()
        {
            var findings = CheckSource(SampleWithIdentifierRpg, CreateConfiguration());

            Assert.Contains(findings, finding => finding.Reason.Contains("myRPGCount"));
        }

        [Fact]
        public void CheckReportsBlacklistedNameInRebuiltRpgIdentifier()
        {
            var findings = CheckSource(SampleWithRebuiltRpg, CreateConfiguration());

            Assert.Contains(findings, finding => finding.Reason.Contains("rebuiltRpgValue"));
        }

        [Fact]
        public void CheckAllowsBlacklistedNameInComment()
        {
            var findings = CheckSource(SampleWithRpgInComment, CreateConfiguration());

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckAllowsBlacklistedNameInUserFacingMessage()
        {
            var findings = CheckSource(SampleWithRpgInMessage, CreateConfiguration());

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckReportsBlacklistedNameInMenuItem()
        {
            var findings = CheckSource(SampleWithMenuItemRpg, CreateConfiguration());

            Assert.Contains(findings, finding => finding.Reason.Contains("RPG"));
        }

        [Fact]
        public void CheckAllowsCleanMenuItem()
        {
            var findings = CheckSource(SampleWithMenuItemClean, CreateConfiguration());

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckReportsBlacklistedNameInCreateAssetMenu()
        {
            var findings = CheckSource(SampleWithCreateAssetMenu, CreateConfiguration());

            Assert.Contains(findings, finding => finding.Reason.Contains("RPG"));
        }

        [Fact]
        public void CheckReportsBlacklistedNameInPathLiteral()
        {
            var findings = CheckSource(SampleWithPathLiteral, CreateConfiguration());

            Assert.Contains(findings, finding => finding.Reason.Contains("RPG"));
        }

        [Fact]
        public void CheckAllowsPathLiteralWithoutBlacklistedName()
        {
            var findings = CheckSource(SampleWithCleanPathLiteral, CreateConfiguration());

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckAllowsBlacklistedNameInStringContainingSpace()
        {
            var findings = CheckSource(SampleWithSpacedLiteral, CreateConfiguration());

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckAllowsHsgsFrameName()
        {
            var findings = CheckSource(SampleWithHsgsFrame, CreateConfiguration());

            Assert.Empty(findings);
        }

        [Fact]
        public void CheckSkipsWholeFileWhenExemptPathPrefixMatches()
        {
            var directory = Path.Combine(Path.GetTempPath(), "GenericNameCheckerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var hostDirectory = Path.Combine(directory, "HostProject");
                Directory.CreateDirectory(hostDirectory);
                var filePath = Path.Combine(hostDirectory, "Sample.cs");
                File.WriteAllText(filePath, SampleWithIdentifierRpg);

                var configuration = CreateConfiguration();
                configuration.GenericNameExemptPaths = new List<string> { "HostProject" };

                var findings = GenericNameChecker.Check(new[] { filePath }, configuration);

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void CheckAllowsIdentifierListedInExemption()
        {
            var configuration = CreateConfiguration();
            configuration.AbbreviationExemptIdentifiers = new List<string> { "myRPGCount" };

            var findings = CheckSource(SampleWithIdentifierRpg, configuration);

            Assert.DoesNotContain(findings, finding => finding.Reason.Contains("myRPGCount"));
        }

        [Fact]
        public void CheckReportsNothingWhenBlacklistIsEmpty()
        {
            var configuration = CreateConfiguration();
            configuration.GenericNameBlacklist = new List<string>();

            var findings = CheckSource(SampleWithIdentifierRpg, configuration);

            Assert.Empty(findings);
        }

        [Fact]
        public void FindingDisplayTextContainsAllFourElements()
        {
            var findings = CheckSource(SampleWithIdentifierRpg, CreateConfiguration());

            var finding = Assert.Single(findings);
            var text = finding.ToDisplayText();
            Assert.Contains("位置：", text);
            Assert.Contains("原因：", text);
            Assert.Contains("修复：", text);
            Assert.Contains("参考：", text);
        }

        private static IReadOnlyList<GateFinding> CheckSource(string source, GateConfiguration configuration)
        {
            var directory = Path.Combine(Path.GetTempPath(), "GenericNameCheckerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var filePath = Path.Combine(directory, "Sample.cs");
                File.WriteAllText(filePath, source);
                return GenericNameChecker.Check(new[] { filePath }, configuration);
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
                AbbreviationBlacklist = new List<string>(),
                AbbreviationExemptIdentifiers = new List<string>(),
                DirectoryNameBlacklist = new List<string>(),
                DirectoryNamePattern = "^[A-Za-z_][A-Za-z0-9_.]*$",
                DocumentLineLimit = 200,
                DocumentExemptions = new List<string>(),
                ChangedPathWhitelist = new List<string>(),
                TestFileGlobs = new List<string>(),
                GenericNameBlacklist = new List<string> { "RPG", "RebuiltRPG", "RebuiltRpg", "GameTemplateForAgent", "MyGame" },
                GenericNameExemptPaths = new List<string>()
            };
        }
    }
}
