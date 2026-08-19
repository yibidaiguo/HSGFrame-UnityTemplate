using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>下游边界检查测试：读 driver 名、退化目录名、代码命中、跳过测试树、豁免放行。</summary>
    public class BridgeBoundaryCheckerTests
    {
        /// <summary>ReadDriverNames 读 driver.json 顶层「名称」字段，结果序数序。</summary>
        [Fact]
        public void ReadDriverNamesReadsNameField()
        {
            var root = CreateRoot();
            try
            {
                WriteDriverDescriptor(root, "feishu", "飞书助手");
                WriteDriverDescriptor(root, "comfyui", "comfyui");

                var names = BridgeBoundaryChecker.ReadDriverNames(root);

                Assert.Equal(new[] { "comfyui", "飞书助手" }, names.ToArray());
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>「名称」字段缺失时退化成目录名，照样能扫。</summary>
        [Fact]
        public void ReadDriverNamesFallsBackToDirectoryName()
        {
            var root = CreateRoot();
            try
            {
                WriteDriverDescriptor(root, "feishu", null);

                var names = BridgeBoundaryChecker.ReadDriverNames(root);

                Assert.Equal(new[] { "feishu" }, names.ToArray());
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>扫描根里一个 .cs 的某行出现 driver 名 → 一条发现，位置带行号。</summary>
        [Fact]
        public void CheckReportsDriverNameInSourceWithLineNumber()
        {
            var root = CreateRoot();
            try
            {
                WriteDriverDescriptor(root, "feishu", "feishu");
                WriteFile(root, "Tools/Engine/Sample.cs",
                    "namespace Demo {",
                    "    private const string Driver = \"feishu\";",
                    "}");

                var findings = BridgeBoundaryChecker.Check(root, new[] { "Tools/Engine" }, new GateConfiguration());

                var finding = Assert.Single(findings);
                Assert.Equal("Tools/Engine/Sample.cs:2", finding.Location);
                Assert.Contains("feishu", finding.Reason);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>.Tests 与 bin/ 下的文件不在引擎扫描范围，含 driver 名也不报。</summary>
        [Fact]
        public void CheckSkipsTestsAndBinTrees()
        {
            var root = CreateRoot();
            try
            {
                WriteDriverDescriptor(root, "feishu", "feishu");
                WriteFile(root, "Tools/Engine/Tools.Engine.Tests/SampleTests.cs", "const string D = \"feishu\";");
                WriteFile(root, "Tools/Engine/bin/Release/Sample.cs", "const string D = \"feishu\";");

                var findings = BridgeBoundaryChecker.Check(root, new[] { "Tools/Engine" }, new GateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>豁免项整串命中时放行——面板下游页渲染代码就是靠这个豁免的。</summary>
        [Fact]
        public void CheckHonorsExemptions()
        {
            var root = CreateRoot();
            try
            {
                WriteDriverDescriptor(root, "feishu", "feishu");
                WriteFile(root, "Tools/Dashboard/DownstreamRenderer.cs", "const string D = \"feishu\";");

                var configuration = new GateConfiguration
                {
                    BridgeBoundaryExemptions = new[] { "Tools/Dashboard/DownstreamRenderer.cs" }
                };

                var findings = BridgeBoundaryChecker.Check(root, new[] { "Tools/Dashboard" }, configuration);

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>建一个空的测试根目录。</summary>
        private static string CreateRoot()
        {
            return Path.Combine(Path.GetTempPath(), "BridgeBoundaryCheckerTests-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>写一份 driver 自述；name 传 null 时省略「名称」字段，验证退化目录名。</summary>
        private static void WriteDriverDescriptor(string root, string directoryName, string name)
        {
            var directory = Path.Combine(root, "Bridges", directoryName);
            Directory.CreateDirectory(directory);
            var template = """
                {
                  "__NAME__"
                  "port": ["需求编辑端"],
                  "形态": "线上",
                  "契约版本": ">=1.0 <2.0",
                  "实现": "bridge-demo",
                  "字段类型映射": { "string": "文本" }
                }
                """;
            var nameLine = name == null ? "" : $"  \"名称\": \"{name}\",";
            File.WriteAllText(
                Path.Combine(directory, "driver.json"),
                template.Replace("  \"__NAME__\"", nameLine));
        }

        /// <summary>在根下写一个相对路径文件，目录不存在先创建。</summary>
        private static void WriteFile(string root, string relativePath, params string[] lines)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllLines(fullPath, lines);
        }
    }
}
