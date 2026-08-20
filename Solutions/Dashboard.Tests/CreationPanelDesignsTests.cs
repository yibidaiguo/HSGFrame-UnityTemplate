using System;
using System.IO;
using System.Text;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>面板设计池页读取器测试：全部用系统临时目录建池根，跑完自删。</summary>
    public sealed class CreationPanelDesignsTests : IDisposable
    {
        private readonly string _poolRoot;

        /// <summary>构造：在系统临时目录下建一个空池根。</summary>
        public CreationPanelDesignsTests()
        {
            _poolRoot = Path.Combine(Path.GetTempPath(), "面板设计池读取器测试-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>Designs 目录不存在时返回空列表，不抛。</summary>
        [Fact]
        public void MissingDesignsDirectoryReturnsEmptyWithoutThrowing()
        {
            Assert.Empty(CreationPanelReader.ReadDesigns(_poolRoot));
        }

        /// <summary>三类各放一个文件，读到三行，按分类再按名称排序。</summary>
        [Fact]
        public void ThreeCategoriesProduceRowsSortedByCategoryThenName()
        {
            WriteDesign("记录", "记录文档.json", """
                {
                  "名称": "记录文档"
                }
                """);
            WriteDesign("汇总", "汇总文档.json", """
                {
                  "名称": "汇总文档"
                }
                """);
            WriteDesign("定稿", "定稿文档.json", """
                {
                  "名称": "定稿文档"
                }
                """);

            var rows = CreationPanelReader.ReadDesigns(_poolRoot);

            Assert.Equal(3, rows.Count);
            Assert.Equal("定稿", rows[0].Category);
            Assert.Equal("定稿文档", rows[0].Name);
            Assert.Equal("汇总", rows[1].Category);
            Assert.Equal("汇总文档", rows[1].Name);
            Assert.Equal("记录", rows[2].Category);
            Assert.Equal("记录文档", rows[2].Name);
        }

        /// <summary>同一类里的文件按名称序数序排序。</summary>
        [Fact]
        public void RowsInsideCategoryAreSortedByName()
        {
            WriteDesign("汇总", "乙文档.json", """
                {
                  "名称": "乙"
                }
                """);
            WriteDesign("汇总", "甲文档.json", """
                {
                  "名称": "甲"
                }
                """);

            var rows = CreationPanelReader.ReadDesigns(_poolRoot);

            // 序数序按 Unicode 码位：「乙」(U+4E59) 在「甲」(U+7532) 之前。
            Assert.Equal(2, rows.Count);
            Assert.Equal("乙文档", rows[0].Name);
            Assert.Equal("甲文档", rows[1].Name);
        }

        /// <summary>坏 JSON 照样产一行且 IsReadable 为 false，其余字段空串。</summary>
        [Fact]
        public void BrokenDesignFileStillProducesRow()
        {
            // 坏 JSON 的内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据。
            WriteDesign("定稿", "坏设计.json", """
                {
                  not valid json at all
                """);

            var row = Assert.Single(CreationPanelReader.ReadDesigns(_poolRoot));

            Assert.Equal("定稿", row.Category);
            Assert.Equal("坏设计", row.Name);
            Assert.False(row.IsReadable);
            Assert.Equal("", row.Title);
            Assert.Equal("", row.Version);
            Assert.Equal("", row.Moment);
        }

        /// <summary>有「名称」无「标题」时 Title 取名称；版本与时间字段也读得到。</summary>
        [Fact]
        public void TitleFallsBackFromNameToTitleField()
        {
            WriteDesign("汇总", "设计甲.json", """
                {
                  "名称": "只有名称",
                  "版本": "v1",
                  "时间": "2026-01-01"
                }
                """);

            var row = Assert.Single(CreationPanelReader.ReadDesigns(_poolRoot));

            Assert.Equal("只有名称", row.Title);
            Assert.Equal("v1", row.Version);
            Assert.Equal("2026-01-01", row.Moment);
            Assert.True(row.IsReadable);
        }

        /// <summary>有「标题」无「名称」时 Title 取标题；没有「时间」时取「创建时间」。</summary>
        [Fact]
        public void TitleReadsTitleFieldWhenNameMissing()
        {
            WriteDesign("记录", "设计乙.json", """
                {
                  "标题": "只有标题",
                  "创建时间": "2026-02-02"
                }
                """);

            var row = Assert.Single(CreationPanelReader.ReadDesigns(_poolRoot));

            Assert.Equal("只有标题", row.Title);
            Assert.Equal("2026-02-02", row.Moment);
        }

        /// <summary>删除本测试建的临时目录；清理失败不影响测试结论。</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_poolRoot))
                {
                    Directory.Delete(_poolRoot, true);
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

        private void WriteDesign(string category, string fileName, string json)
        {
            var directory = Path.Combine(_poolRoot, "Designs", category);
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, fileName), json);
        }

        private static void WriteFile(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
