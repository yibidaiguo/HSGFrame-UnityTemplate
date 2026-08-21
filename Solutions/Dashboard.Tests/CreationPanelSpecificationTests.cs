using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Template.Toolkit.Dashboard;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>规范页读取器测试：全部用系统临时目录建仓库，跑完自删；同时验证读取器不写盘。</summary>
    public sealed class CreationPanelSpecificationTests : IDisposable
    {
        private readonly string _repositoryRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根。</summary>
        public CreationPanelSpecificationTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板规范测试-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_repositoryRoot);
        }

        /// <summary>三层目录都不存在时返回空列表，不抛异常。</summary>
        [Fact]
        public void MissingDirectoriesReturnEmptyList()
        {
            var rows = CreationPanelReader.ReadSpecifications(_repositoryRoot);

            Assert.Empty(rows);
        }

        /// <summary>基线两个文件、项目一个、业务两个模块各一个：行数与分层、模块归属对得上。</summary>
        [Fact]
        public void RowsMatchLayerAndModuleLayout()
        {
            WriteSpecFile("Baseline", "基线规则.json", """
                {
                  "规则": [{ "id": "r1" }, { "id": "r2" }]
                }
                """);
            WriteSpecFile("Baseline", "基线说明.md", "# 基线说明");
            WriteSpecFile("Project", "prereview-rules.json", """
                [ { "id": "p1" }, { "id": "p2" }, { "id": "p3" } ]
                """);
            WriteSpecFile(Path.Combine("Business", "钓鱼"), "钓鱼规则.json", """
                {
                  "规则": [{ "id": "f1" }]
                }
                """);
            WriteSpecFile(Path.Combine("Business", "钓鱼"), "钓鱼说明.md", "# 钓鱼说明");
            WriteSpecFile(Path.Combine("Business", "种田"), "种田规则.json", """
                {
                  "规则": [{ "id": "z1" }]
                }
                """);

            var rows = CreationPanelReader.ReadSpecifications(_repositoryRoot);

            Assert.Equal(6, rows.Count);
            // 层顺序：基线 → 项目 → 业务；业务按模块名序数序（种田 < 钓鱼），模块内按文件名序数序。
            Assert.Equal("基线", rows[0].Layer);
            Assert.Equal("基线", rows[1].Layer);
            Assert.Equal("项目", rows[2].Layer);
            Assert.Equal("业务", rows[3].Layer);
            Assert.Equal("种田", rows[3].ModuleName);
            Assert.Equal("业务", rows[4].Layer);
            Assert.Equal("钓鱼", rows[4].ModuleName);
            Assert.Equal("业务", rows[5].Layer);
            Assert.Equal("钓鱼", rows[5].ModuleName);
        }

        /// <summary>排序确定性：同一份目录连读两次，RelativePath 序列逐条相同。</summary>
        [Fact]
        public void ReadTwiceGivesSameRelativePathOrder()
        {
            WriteSpecFile("Baseline", "乙.json", """
            { "规则": [] }
            """);
            WriteSpecFile("Baseline", "甲.json", """
            { "规则": [] }
            """);
            WriteSpecFile(Path.Combine("Business", "模块乙"), "乙.json", """
            { "规则": [] }
            """);
            WriteSpecFile(Path.Combine("Business", "模块甲"), "甲.json", """
            { "规则": [] }
            """);

            var first = CreationPanelReader.ReadSpecifications(_repositoryRoot);
            var second = CreationPanelReader.ReadSpecifications(_repositoryRoot);

            Assert.Equal(first.Count, second.Count);
            for (var index = 0; index < first.Count; index++)
            {
                Assert.Equal(first[index].RelativePath, second[index].RelativePath);
            }
        }

        /// <summary>RelativePath 里没有反斜杠（Windows 上也要是 /）。</summary>
        [Fact]
        public void RelativePathUsesForwardSlashes()
        {
            WriteSpecFile("Baseline", "规则.json", """
            { "规则": [] }
            """);
            WriteSpecFile(Path.Combine("Business", "模块"), "规则.json", """
            { "规则": [] }
            """);

            var rows = CreationPanelReader.ReadSpecifications(_repositoryRoot);

            foreach (var row in rows)
            {
                Assert.DoesNotContain("\\", row.RelativePath);
                Assert.StartsWith("Specifications/", row.RelativePath);
            }
        }

        /// <summary>.txt 文件不收（只收 .json 与 .md）。</summary>
        [Fact]
        public void NonJsonOrMarkdownFilesAreIgnored()
        {
            WriteSpecFile("Baseline", "说明.txt", "not a spec");

            var rows = CreationPanelReader.ReadSpecifications(_repositoryRoot);

            Assert.Empty(rows);
        }

        /// <summary>顶层是数组的 .json：RuleCount 等于数组长度。</summary>
        [Fact]
        public void TopLevelArrayCountsRules()
        {
            WriteSpecFile("Baseline", "数组.json", """
                [ { "id": "a" }, { "id": "b" }, { "id": "c" } ]
                """);

            var rows = CreationPanelReader.ReadSpecifications(_repositoryRoot);

            var row = Assert.Single(rows);
            Assert.Equal(3, row.RuleCount);
            Assert.True(row.IsReadable);
        }

        /// <summary>顶层对象带「规则」数组的 .json：RuleCount 等于那个数组长度。</summary>
        [Fact]
        public void TopLevelObjectWithRulesCountsRules()
        {
            WriteSpecFile("Baseline", "对象规则.json", """
                {
                  "规则": [{ "id": "a" }, { "id": "b" }]
                }
                """);

            var rows = CreationPanelReader.ReadSpecifications(_repositoryRoot);

            var row = Assert.Single(rows);
            Assert.Equal(2, row.RuleCount);
        }

        /// <summary>顶层是对象且没有「规则」的 .json：RuleCount 为 -1。</summary>
        [Fact]
        public void TopLevelObjectWithoutRulesIsMinusOne()
        {
            WriteSpecFile("Baseline", "无规则.json", """
                {
                  "标题": "只有标题"
                }
                """);

            var rows = CreationPanelReader.ReadSpecifications(_repositoryRoot);

            var row = Assert.Single(rows);
            Assert.Equal(-1, row.RuleCount);
            Assert.True(row.IsReadable);
        }

        /// <summary>.md 文件：RuleCount 为 -1。</summary>
        [Fact]
        public void MarkdownRuleCountIsMinusOne()
        {
            WriteSpecFile("Baseline", "README.md", "# 说明");

            var rows = CreationPanelReader.ReadSpecifications(_repositoryRoot);

            var row = Assert.Single(rows);
            Assert.Equal(-1, row.RuleCount);
            Assert.True(row.IsReadable);
        }

        /// <summary>坏 JSON：IsReadable 为 false、原因非空，该行仍在。</summary>
        [Fact]
        public void BrokenJsonProducesUnreadableRow()
        {
            // 坏 JSON 的内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据。
            WriteSpecFile("Baseline", "坏规则.json", """
                {
                  not valid json at all
                """);

            var rows = CreationPanelReader.ReadSpecifications(_repositoryRoot);

            var row = Assert.Single(rows);
            Assert.False(row.IsReadable);
            Assert.False(string.IsNullOrEmpty(row.FailureReason));
            Assert.Equal(-1, row.RuleCount);
        }

        /// <summary>不写盘：读完之后临时目录里的文件数与内容与读之前完全一致。</summary>
        [Fact]
        public void ReadDoesNotWriteAnything()
        {
            WriteSpecFile("Baseline", "规则.json", """
                {
                  "规则": [{ "id": "a" }]
                }
                """);
            WriteSpecFile("Baseline", "README.md", "# 说明");
            WriteSpecFile(Path.Combine("Business", "模块"), "模块规则.json", """
                [ { "id": "m" } ]
                """);

            var before = Snapshot(_repositoryRoot);
            CreationPanelReader.ReadSpecifications(_repositoryRoot);
            var after = Snapshot(_repositoryRoot);

            Assert.Equal(before.Count, after.Count);
            foreach (var pair in before)
            {
                Assert.True(after.ContainsKey(pair.Key));
                Assert.Equal(pair.Value, after[pair.Key]);
            }
        }

        /// <summary>删除本测试建的临时目录；清理失败不影响测试结论。</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_repositoryRoot))
                {
                    Directory.Delete(_repositoryRoot, true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响测试结论，按契约静默。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }

        private void WriteSpecFile(string relativeDirectory, string fileName, string content)
        {
            var directory = Path.Combine(_repositoryRoot, "Specifications", relativeDirectory);
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, fileName), content);
        }

        /// <summary>把一棵目录树收成 相对路径 → 内容 的字典，用来比对读前读后。</summary>
        private Dictionary<string, string> Snapshot(string root)
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, filePath).Replace('\\', '/');
                snapshot[relative] = File.ReadAllText(filePath);
            }

            return snapshot;
        }

        private static void WriteFile(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
