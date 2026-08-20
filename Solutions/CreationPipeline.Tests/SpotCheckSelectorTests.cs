using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>放行流水抽查选取器的测试：确定性、按比例铺开、边界与已抽查排除。</summary>
    public class SpotCheckSelectorTests
    {
        /// <summary>候选 10 条、比例 0.2：恰好 2 条，且两次调用结果逐个 id 相同（确定性，零随机）。</summary>
        [Fact]
        public void SelectTenAtTwentyPercentIsDeterministic()
        {
            using var workspace = new PoolTestWorkspace();
            AppendMany(workspace, 10);
            var ledger = ReleaseLedger.Load(workspace.Root);

            var first = SpotCheckSelector.Select(ledger, 0.2);
            var second = SpotCheckSelector.Select(ledger, 0.2);

            Assert.Equal(2, first.Count);
            Assert.Equal(first.Select(entry => entry.Identifier), second.Select(entry => entry.Identifier));
        }

        /// <summary>候选 10 条、比例 0.2：抽出来的两条不是相邻的前两条（均匀跨步铺开了采样面）。</summary>
        [Fact]
        public void SelectTenAtTwentyPercentSpreadsOut()
        {
            using var workspace = new PoolTestWorkspace();
            AppendMany(workspace, 10);
            var ledger = ReleaseLedger.Load(workspace.Root);

            var picked = SpotCheckSelector.Select(ledger, 0.2);

            var identifiers = picked.Select(entry => entry.Identifier).ToList();
            Assert.Equal(2, identifiers.Count);
            Assert.NotEqual(new[] { "RL-0001", "RL-0002" }, identifiers);
        }

        /// <summary>比例 0 返回空列表；比例 1 返回全部候选。</summary>
        [Fact]
        public void ZeroRatioYieldsNothingAndFullRatioYieldsAll()
        {
            using var workspace = new PoolTestWorkspace();
            AppendMany(workspace, 5);
            var ledger = ReleaseLedger.Load(workspace.Root);

            Assert.Empty(SpotCheckSelector.Select(ledger, 0.0));
            Assert.Empty(SpotCheckSelector.Select(ledger, -1.0));

            var all = SpotCheckSelector.Select(ledger, 1.0);
            Assert.Equal(5, all.Count);
        }

        /// <summary>候选 3 条、比例 0.1：向上取整后至少 1 条。</summary>
        [Fact]
        public void TinyRatioStillPicksAtLeastOne()
        {
            using var workspace = new PoolTestWorkspace();
            AppendMany(workspace, 3);
            var ledger = ReleaseLedger.Load(workspace.Root);

            var picked = SpotCheckSelector.Select(ledger, 0.1);

            Assert.Single(picked);
        }

        /// <summary>已抽查过的条目不进候选：抽完一条后再次选取只从未抽查里挑。</summary>
        [Fact]
        public void AlreadyCheckedEntriesAreExcluded()
        {
            using var workspace = new PoolTestWorkspace();
            AppendMany(workspace, 5);
            var ledger = ReleaseLedger.Load(workspace.Root);
            var first = ledger.Entries[0];
            ReleaseLedger.RecordSpotCheck(workspace.Root, first.Identifier, "合格", "抽查无误", "", out _);

            var reloaded = ReleaseLedger.Load(workspace.Root);
            var picked = SpotCheckSelector.Select(reloaded, 1.0);

            Assert.Equal(4, picked.Count);
            Assert.DoesNotContain(picked, entry => entry.Identifier == first.Identifier);
        }

        /// <summary>空流水（文件不存在）返回空列表，不抛。</summary>
        [Fact]
        public void EmptyLedgerYieldsNothing()
        {
            using var workspace = new PoolTestWorkspace();
            var ledger = ReleaseLedger.Load(workspace.Root);

            Assert.Empty(SpotCheckSelector.Select(ledger, 0.2));
        }

        private static void AppendMany(PoolTestWorkspace workspace, int count)
        {
            for (var i = 1; i <= count; i++)
            {
                ReleaseLedger.Append(
                    workspace.Root,
                    "REQ-" + i.ToString("D4"),
                    "低",
                    new[] { "业务" },
                    "2026-08-20T10:00:00+09:00",
                    "");
            }
        }
    }
}
