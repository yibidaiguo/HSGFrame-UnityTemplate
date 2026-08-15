using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>文档长度检查器在阈值边界与豁免上的行为。</summary>
    public class DocumentLengthCheckerBoundaryTests
    {
        [Fact]
        public void CheckReportsNothingWhenLineCountEqualsLimit()
        {
            Run(200, 200, new List<string>(), findings => Assert.Empty(findings));
        }

        [Fact]
        public void CheckReportsSingleFindingWhenLineCountExceedsLimitByOne()
        {
            Run(200, 201, new List<string>(), findings => Assert.Single(findings));
        }

        [Fact]
        public void CheckReportsNothingForEmptyFile()
        {
            Run(200, 0, new List<string>(), findings => Assert.Empty(findings));
        }

        [Fact]
        public void CheckReportsNothingForExemptedDocument()
        {
            Run(200, 300, new List<string> { "Doc.md" }, findings => Assert.Empty(findings));
        }

        [Fact]
        public void CheckReportsForNonEmptyDocumentWhenLimitIsZero()
        {
            Run(0, 1, new List<string>(), findings => Assert.Single(findings));
        }

        private static void Run(int limit, int lineCount, List<string> exemptions, Action<IReadOnlyList<GateFinding>> assert)
        {
            var root = NewTempDirectory();
            try
            {
                var document = Path.Combine(root, "Doc.md");
                File.WriteAllLines(document, Enumerable.Repeat("line", lineCount));

                var configuration = new GateConfiguration
                {
                    DocumentLineLimit = limit,
                    DocumentExemptions = exemptions
                };

                assert(DocumentLengthChecker.Check(root, new[] { document }, configuration));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string NewTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "gate-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
