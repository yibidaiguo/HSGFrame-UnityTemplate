using System;
using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>意见库读写测试：只追加、坏文件跳过、非法可规则化性报错。</summary>
    public class ReviewOpinionBookTests
    {
        /// <summary>目录不存在 → 空库、LoadFailureReason 是空串（空意见库是正常状态）。</summary>
        [Fact]
        public void MissingDirectoryLoadsEmptyBook()
        {
            using var workspace = new PoolTestWorkspace();

            var book = ReviewOpinionBook.Load(workspace.Root);

            Assert.Empty(book.Opinions);
            Assert.Equal("", book.LoadFailureReason);
        }

        /// <summary>追加两条 → id 是 OP-0001、OP-0002，字段对得上。</summary>
        [Fact]
        public void AppendCreatesSequentialIdentifiers()
        {
            using var workspace = new PoolTestWorkspace();

            var first = ReviewOpinionBook.Append(workspace.Root, "空引用未防", "签到", "可代码化", "这里没判 null", "2026-08-20T10:00:00+09:00");
            var second = ReviewOpinionBook.Append(workspace.Root, "命名歧义", "任务", "可提示词化", "这个名字有歧义", "2026-08-20T10:00:01+09:00");

            Assert.Equal("OP-0001", first.Identifier);
            Assert.Equal("OP-0002", second.Identifier);
            Assert.Equal("空引用未防", first.Category);
            Assert.Equal("签到", first.ModuleName);
            Assert.Equal("可代码化", first.Rulability);
            Assert.Equal("这里没判 null", first.Quotation);

            var book = ReviewOpinionBook.Load(workspace.Root);
            Assert.Equal(2, book.Opinions.Count);
            Assert.Equal("OP-0001", book.Opinions[0].Identifier);
            Assert.Equal("OP-0002", book.Opinions[1].Identifier);
        }

        /// <summary>非法可规则化性 → 抛 InvalidOperationException，文案含三个合法值。</summary>
        [Fact]
        public void AppendRejectsIllegalRulability()
        {
            using var workspace = new PoolTestWorkspace();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ReviewOpinionBook.Append(workspace.Root, "空引用未防", "签到", "随便写", "这里没判 null", "2026-08-20T10:00:00+09:00"));

            Assert.Contains("可代码化", exception.Message);
            Assert.Contains("可提示词化", exception.Message);
            Assert.Contains("不可规则化", exception.Message);
        }

        /// <summary>追加不改写已有条目：第一条文件逐字未变。</summary>
        [Fact]
        public void AppendNeverRewritesExistingEntry()
        {
            using var workspace = new PoolTestWorkspace();

            ReviewOpinionBook.Append(workspace.Root, "空引用未防", "签到", "可代码化", "这里没判 null", "2026-08-20T10:00:00+09:00");
            var firstPath = Path.Combine(PoolPaths.ReviewOpinionDirectory(workspace.Root), "OP-0001.json");
            var before = File.ReadAllText(firstPath);

            ReviewOpinionBook.Append(workspace.Root, "命名歧义", "任务", "可提示词化", "另一个类别", "2026-08-20T10:00:01+09:00");

            Assert.Equal(before, File.ReadAllText(firstPath));
        }

        /// <summary>混一条坏文件 → 其余照常读出，原因累加进 LoadFailureReason。</summary>
        [Fact]
        public void BrokenFileIsSkippedAndReasonAccumulates()
        {
            using var workspace = new PoolTestWorkspace();
            ReviewOpinionBook.Append(workspace.Root, "空引用未防", "签到", "可代码化", "这里没判 null", "2026-08-20T10:00:00+09:00");
            // 坏 JSON 的内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据，
            // 裸中文写在这里会被当成「标识符含中文」判红。
            File.WriteAllText(
                Path.Combine(PoolPaths.ReviewOpinionDirectory(workspace.Root), "OP-0002.json"),
                "not-json",
                new UTF8Encoding(false));

            var book = ReviewOpinionBook.Load(workspace.Root);

            var opinion = Assert.Single(book.Opinions);
            Assert.Equal("OP-0001", opinion.Identifier);
            Assert.Contains("OP-0002.json", book.LoadFailureReason);
        }
    }
}
