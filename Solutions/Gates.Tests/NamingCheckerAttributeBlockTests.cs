using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>公开类型摘要回扫跨越多行特性块的行为。</summary>
    [Collection("naming-checker-serial")]
    public class NamingCheckerAttributeBlockTests
    {
        [Fact]
        public void MultiLineAttributeDoesNotHideTheSummaryAboveIt()
        {
            // 回归：回扫原先只跳「以 [ 开头」的行，多行特性的末行是 `        false)]`，
            // 于是回扫停在那儿，把摘要明明在的类型报成缺摘要——逼着人把特性压成一行。
            var content = @"using System;

namespace Sample
{
    /// <summary>图的 JSON 编解码。</summary>
    [Obsolete(
        ""图的创作源只剩一个，这层只做运行时兼容投影，别再往这里加新入口。"",
        false)]
    public static class GraphJsonCodec
    {
    }
}
";
            RunInTempDirectory("Sample.cs", content, (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Empty(findings);
            });
        }

        [Fact]
        public void MultiLineAttributeWithoutSummaryIsStillReported()
        {
            var content = @"using System;

namespace Sample
{
    [Obsolete(
        ""这条特性上面没有任何摘要。"",
        false)]
    public static class GraphJsonCodec
    {
    }
}
";
            RunInTempDirectory("Sample.cs", content, (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Contains(findings, finding => finding.Reason.Contains("summary"));
            });
        }

        [Fact]
        public void BracketInsideAttributeStringDoesNotSkewTheCount()
        {
            // 字符串里的 ] 在 StripNonCode 之后已经是空格，配对计数不该被它带偏。
            var content = @"using System;

namespace Sample
{
    /// <summary>图的 JSON 编解码。</summary>
    [Obsolete(
        ""改用 GraphAdapter[0]，另见 nodes[] 一节]。"",
        false)]
    public static class GraphJsonCodec
    {
    }
}
";
            RunInTempDirectory("Sample.cs", content, (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Empty(findings);
            });
        }

        [Fact]
        public void NestedBracketsInsideAttributeAreBalanced()
        {
            var content = @"using System;

namespace Sample
{
    /// <summary>图的 JSON 编解码。</summary>
    [Sample(
        new[] { 1, 2 },
        Names = new[] { ""甲"" })]
    public static class GraphJsonCodec
    {
    }
}
";
            RunInTempDirectory("Sample.cs", content, (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Empty(findings);
            });
        }

        [Fact]
        public void StackedAttributeBlocksAreAllSkipped()
        {
            var content = @"using System;

namespace Sample
{
    /// <summary>图的 JSON 编解码。</summary>
    [Serializable]
    [Obsolete(
        ""第一条多行特性。"",
        false)]

    [Obsolete(
        ""第二条多行特性。"",
        false)]
    public static class GraphJsonCodec
    {
    }
}
";
            RunInTempDirectory("Sample.cs", content, (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Empty(findings);
            });
        }

        [Fact]
        public void SingleLineAttributeStillLetsTheSummaryThrough()
        {
            var content = @"using System;

namespace Sample
{
    /// <summary>图的 JSON 编解码。</summary>
    [Obsolete(""压成一行的写法不能因为这次改动反而失效。"", false)]
    public static class GraphJsonCodec
    {
    }
}
";
            RunInTempDirectory("Sample.cs", content, (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Empty(findings);
            });
        }

        [Fact]
        public void UnrelatedCodeLineAboveIsNotEatenAsAnAttribute()
        {
            // 上一行不是特性时回扫必须就地停下，不许顺着某个 ] 一路穿到更上面的摘要去。
            var content = @"using System;

namespace Sample
{
    /// <summary>另一个类型的摘要，不该被下面那个类型借走。</summary>
    public static class Neighbour
    {
        private static readonly int[] Values = new int[] { 1 };
    }

    public static class GraphJsonCodec
    {
    }
}
";
            RunInTempDirectory("Sample.cs", content, (configuration, relativePath) =>
            {
                var findings = NamingChecker.Check(new[] { relativePath }, configuration);

                Assert.Contains(findings, finding => finding.Reason.Contains("summary"));
            });
        }

        private static void RunInTempDirectory(string relativePath, string fileContent, Action<GateConfiguration, string> assert)
        {
            var directory = Path.Combine(Path.GetTempPath(), "gate-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
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

        private static GateConfiguration CreateConfiguration()
        {
            return new GateConfiguration
            {
                AbbreviationBlacklist = new List<string> { "Mgr", "Cfg", "Svc" },
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
