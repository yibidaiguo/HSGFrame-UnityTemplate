using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>文档长度检查器的超限与豁免测试。</summary>
    public class DocumentLengthCheckerTests
    {
        [Fact]
        public void CheckReportsLongDocumentAndHonorsExemption()
        {
            var root = CreateTempDirectory();
            try
            {
                var document = Path.Combine(root, "Long.md");
                File.WriteAllLines(document, Enumerable.Repeat("line", 250));

                var limitOnly = new GateConfiguration
                {
                    DocumentLineLimit = 200,
                    DocumentExemptions = new List<string>()
                };
                var findings = DocumentLengthChecker.Check(root, new[] { document }, limitOnly);

                Assert.Single(findings);

                var exempt = new GateConfiguration
                {
                    DocumentLineLimit = 200,
                    DocumentExemptions = new List<string> { "Long.md" }
                };
                var exemptFindings = DocumentLengthChecker.Check(root, new[] { document }, exempt);

                Assert.Empty(exemptFindings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "gates_document_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
