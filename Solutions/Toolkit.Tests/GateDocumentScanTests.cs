using System;
using System.IO;
using System.Linq;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CommandHost.Commands;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>文档长度门禁对模板根目录的扫描测试：模板自带的文档不该逃过检查。</summary>
    public class GateDocumentScanTests
    {
        [Fact]
        public void DocumentGateSkipsTemplateDocumentsWhenTemplateRootIsAbsent()
        {
            var root = CreateFixture();
            try
            {
                var result = GateDocumentCommand.Execute(new GateDocumentArguments
                {
                    RepositoryRoot = root,
                    DocumentDirectory = "Doc",
                    ConfigurationPath = ConfigurationPathUnder(root)
                });

                // Doc/长文档.md 5 行 > 3 行，这条本来就该报，所以整体是失败；
                // 不传 TemplateRoot 时模板根不该被扫，CLAUDE.md 一行都不许出现。
                Assert.False(result.IsSuccess);
                Assert.DoesNotContain(
                    result.OutputLines,
                    line => line.Contains("CLAUDE.md", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void DocumentGateChecksTemplateDocumentsWhenTemplateRootIsGiven()
        {
            var root = CreateFixture();
            try
            {
                var result = GateDocumentCommand.Execute(new GateDocumentArguments
                {
                    RepositoryRoot = root,
                    DocumentDirectory = "Doc",
                    TemplateRoot = Path.Combine(root, "模板"),
                    ConfigurationPath = ConfigurationPathUnder(root)
                });

                // 模板根被扫进来后，超限的 CLAUDE.md 必须出现在发现列表里。
                Assert.False(result.IsSuccess);
                Assert.Contains(
                    result.OutputLines,
                    line => line.Contains("CLAUDE.md", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void DocumentGateSkipsLibraryUnderTemplateRoot()
        {
            var root = CreateFixture();
            try
            {
                var result = GateDocumentCommand.Execute(new GateDocumentArguments
                {
                    RepositoryRoot = root,
                    DocumentDirectory = "Doc",
                    TemplateRoot = Path.Combine(root, "模板"),
                    ConfigurationPath = ConfigurationPathUnder(root)
                });

                // 模板根下的 Library 是 Unity 生成物，99 行的 md 也轮不到本门禁管。
                Assert.False(result.IsSuccess);
                Assert.DoesNotContain(
                    result.OutputLines,
                    line => line.Contains("包里的.md", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string ConfigurationPathUnder(string root)
        {
            return Path.Combine(root, "模板", "Tools", "Gates", "Config", "gate-config.json");
        }

        private static string CreateFixture()
        {
            var root = Path.Combine(Path.GetTempPath(), "gates_document_scan_" + Guid.NewGuid().ToString("N"));

            var documentDirectory = Path.Combine(root, "Doc");
            Directory.CreateDirectory(documentDirectory);
            File.WriteAllLines(Path.Combine(documentDirectory, "长文档.md"), Enumerable.Repeat("line", 5));

            var configDirectory = Path.Combine(root, "模板", "Tools", "Gates", "Config");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(
                Path.Combine(configDirectory, "gate-config.json"),
                "{\"documentLineLimit\":3,\"documentExemptions\":[],\"sourceScanSkipSegments\":[]}");

            File.WriteAllLines(Path.Combine(root, "模板", "CLAUDE.md"), Enumerable.Repeat("line", 5));

            var libraryDirectory = Path.Combine(root, "模板", "Library");
            Directory.CreateDirectory(libraryDirectory);
            File.WriteAllLines(Path.Combine(libraryDirectory, "包里的.md"), Enumerable.Repeat("line", 99));

            return root;
        }
    }
}
