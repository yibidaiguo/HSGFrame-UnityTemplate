using System;
using HSGFrame.Resource;
using Xunit;

namespace HSGFrame.Resource.Tests
{
    /// <summary>资源引用账本的计数、归零待卸载与确认卸载测试。</summary>
    public class AssetReferenceLedgerTests
    {
        [Fact]
        public void AcquireOnceReturnsOne()
        {
            var ledger = new AssetReferenceLedger();

            var count = ledger.Acquire("Prefabs/火把");

            Assert.Equal(1, count);
        }

        [Fact]
        public void AcquireTwiceReturnsTwo()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("Prefabs/火把");

            var count = ledger.Acquire("Prefabs/火把");

            Assert.Equal(2, count);
        }

        [Fact]
        public void ReleaseOnceDropsBackToOne()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("Prefabs/火把");
            ledger.Acquire("Prefabs/火把");

            var count = ledger.Release("Prefabs/火把");

            Assert.Equal(1, count);
        }

        [Fact]
        public void ReleaseToZeroFiresReadyToUnload()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("Prefabs/火把");
            string unloadedKey = null;
            ledger.ReadyToUnload += key => unloadedKey = key;

            var count = ledger.Release("Prefabs/火把");

            Assert.Equal(0, count);
            Assert.Equal("Prefabs/火把", unloadedKey);
        }

        [Fact]
        public void ZeroedKeyEntersReadyToUnloadKeys()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("Prefabs/火把");
            ledger.Release("Prefabs/火把");

            Assert.Contains("Prefabs/火把", ledger.ReadyToUnloadKeys);
        }

        [Fact]
        public void ReacquiringZeroedKeyRemovesItFromReadyToUnloadKeys()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("Prefabs/火把");
            ledger.Release("Prefabs/火把");
            Assert.Contains("Prefabs/火把", ledger.ReadyToUnloadKeys);

            ledger.Acquire("Prefabs/火把");

            Assert.DoesNotContain("Prefabs/火把", ledger.ReadyToUnloadKeys);
            Assert.Equal(1, ledger.ReferenceCountOf("Prefabs/火把"));
        }

        [Fact]
        public void ConfirmUnloadedReducesTrackedAssetCount()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("Prefabs/火把");
            ledger.Release("Prefabs/火把");
            Assert.Equal(1, ledger.TrackedAssetCount);

            var confirmed = ledger.ConfirmUnloaded("Prefabs/火把");

            Assert.True(confirmed);
            Assert.Equal(0, ledger.TrackedAssetCount);
        }

        [Fact]
        public void ConfirmUnloadedOnLiveKeyReturnsFalseAndKeepsIt()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("Prefabs/火把");

            var confirmed = ledger.ConfirmUnloaded("Prefabs/火把");

            Assert.False(confirmed);
            Assert.Equal(1, ledger.TrackedAssetCount);
            Assert.Equal(1, ledger.ReferenceCountOf("Prefabs/火把"));
        }

        [Fact]
        public void OverReleaseThrowsInvalidOperationException()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("Prefabs/火把");
            ledger.Release("Prefabs/火把");

            var exception = Assert.Throws<InvalidOperationException>(() => ledger.Release("Prefabs/火把"));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
            Assert.Contains("Prefabs/火把", exception.Message);
        }

        [Fact]
        public void ReferenceCountOfUnknownKeyReturnsZero()
        {
            var ledger = new AssetReferenceLedger();

            Assert.Equal(0, ledger.ReferenceCountOf("Prefabs/火把"));
        }

        [Fact]
        public void FindUnknownKeyReturnsNull()
        {
            var ledger = new AssetReferenceLedger();

            Assert.Null(ledger.Find("Prefabs/火把"));
        }

        [Fact]
        public void AcquiredMinusReleasedAlwaysEqualsReferenceCount()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("Prefabs/火把");
            ledger.Acquire("Prefabs/火把");
            ledger.Acquire("Prefabs/火把");
            ledger.Release("Prefabs/火把");

            var record = ledger.Find("Prefabs/火把");

            Assert.Equal(2, record.ReferenceCount);
            Assert.Equal(record.ReferenceCount, record.AcquiredCount - record.ReleasedCount);
        }

        [Fact]
        public void AcquireWithEmptyKeyThrowsArgumentException()
        {
            var ledger = new AssetReferenceLedger();

            var exception = Assert.Throws<ArgumentException>(() => ledger.Acquire(""));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }

        [Fact]
        public void ReleaseWithEmptyKeyThrowsArgumentException()
        {
            var ledger = new AssetReferenceLedger();

            var exception = Assert.Throws<ArgumentException>(() => ledger.Release(""));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }

        [Fact]
        public void ReadyToUnloadKeysAreSortedByOrdinal()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("资源/1");
            ledger.Acquire("资源/10");
            ledger.Acquire("资源/2");
            ledger.Release("资源/1");
            ledger.Release("资源/10");
            ledger.Release("资源/2");

            var keys = ledger.ReadyToUnloadKeys;

            Assert.Equal(new[] { "资源/1", "资源/10", "资源/2" }, keys);
        }

        [Fact]
        public void ClearResetsAllCounts()
        {
            var ledger = new AssetReferenceLedger();
            ledger.Acquire("Prefabs/火把");
            ledger.Acquire("Prefabs/火把");
            ledger.Release("Prefabs/火把");

            ledger.Clear();

            Assert.Equal(0, ledger.TrackedAssetCount);
            Assert.Empty(ledger.ReadyToUnloadKeys);
            Assert.Equal(0, ledger.ReferenceCountOf("Prefabs/火把"));
        }
    }
}
