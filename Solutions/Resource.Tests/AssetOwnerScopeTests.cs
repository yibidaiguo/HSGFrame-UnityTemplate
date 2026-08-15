using System;
using HSGFrame.Resource;
using Xunit;

namespace HSGFrame.Resource.Tests
{
    /// <summary>资源作用域的取用、提前释放与整体释放测试。</summary>
    public class AssetOwnerScopeTests
    {
        [Fact]
        public void AcquireInScopeRaisesLedgerCountToOne()
        {
            var ledger = new AssetReferenceLedger();
            var scope = new AssetOwnerScope(ledger, "火把宿主");
            scope.Acquire("Prefabs/火把");

            Assert.Equal(1, ledger.ReferenceCountOf("Prefabs/火把"));
        }

        [Fact]
        public void DisposeReturnsLedgerCountToZero()
        {
            var ledger = new AssetReferenceLedger();
            var scope = new AssetOwnerScope(ledger, "火把宿主");
            scope.Acquire("Prefabs/火把");

            scope.Dispose();

            Assert.Equal(0, ledger.ReferenceCountOf("Prefabs/火把"));
        }

        [Fact]
        public void AcquiringSameKeyTwiceReleasesTwiceOnDispose()
        {
            var ledger = new AssetReferenceLedger();
            var scope = new AssetOwnerScope(ledger, "火把宿主");
            scope.Acquire("Prefabs/火把");
            scope.Acquire("Prefabs/火把");
            Assert.Equal(2, ledger.ReferenceCountOf("Prefabs/火把"));

            scope.Dispose();

            Assert.Equal(0, ledger.ReferenceCountOf("Prefabs/火把"));
        }

        [Fact]
        public void HeldAssetKeysAreSortedByOrdinal()
        {
            var ledger = new AssetReferenceLedger();
            var scope = new AssetOwnerScope(ledger, "宿主");
            scope.Acquire("资源/10");
            scope.Acquire("资源/2");
            scope.Acquire("资源/1");

            var keys = scope.HeldAssetKeys;

            Assert.Equal(new[] { "资源/1", "资源/10", "资源/2" }, keys);
        }

        [Fact]
        public void EarlyReleaseRemovesKeyFromHeldAssetKeys()
        {
            var ledger = new AssetReferenceLedger();
            var scope = new AssetOwnerScope(ledger, "宿主");
            scope.Acquire("Prefabs/火把");
            scope.Acquire("Prefabs/盾牌");

            var released = scope.Release("Prefabs/火把");

            Assert.True(released);
            Assert.DoesNotContain("Prefabs/火把", scope.HeldAssetKeys);
            Assert.Equal(0, ledger.ReferenceCountOf("Prefabs/火把"));
            Assert.Equal(1, ledger.ReferenceCountOf("Prefabs/盾牌"));
        }

        [Fact]
        public void ReleasingKeyNotHeldReturnsFalse()
        {
            var ledger = new AssetReferenceLedger();
            var scope = new AssetOwnerScope(ledger, "宿主");

            var released = scope.Release("Prefabs/火把");

            Assert.False(released);
        }

        [Fact]
        public void DoubleDisposeIsSafe()
        {
            var ledger = new AssetReferenceLedger();
            var scope = new AssetOwnerScope(ledger, "宿主");
            scope.Acquire("Prefabs/火把");

            scope.Dispose();
            scope.Dispose();

            Assert.Equal(0, ledger.ReferenceCountOf("Prefabs/火把"));
        }

        [Fact]
        public void AcquireAfterDisposeThrowsObjectDisposedException()
        {
            var ledger = new AssetReferenceLedger();
            var scope = new AssetOwnerScope(ledger, "宿主");
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() => scope.Acquire("Prefabs/火把"));
        }

        [Fact]
        public void TwoScopesHoldingSameKeyRaiseCountToTwo()
        {
            var ledger = new AssetReferenceLedger();
            var first = new AssetOwnerScope(ledger, "第一个宿主");
            var second = new AssetOwnerScope(ledger, "第二个宿主");
            first.Acquire("Prefabs/火把");
            second.Acquire("Prefabs/火把");

            Assert.Equal(2, ledger.ReferenceCountOf("Prefabs/火把"));
        }

        [Fact]
        public void DisposingOneScopeLeavesCountAtOne()
        {
            var ledger = new AssetReferenceLedger();
            var first = new AssetOwnerScope(ledger, "第一个宿主");
            var second = new AssetOwnerScope(ledger, "第二个宿主");
            first.Acquire("Prefabs/火把");
            second.Acquire("Prefabs/火把");

            first.Dispose();

            Assert.Equal(1, ledger.ReferenceCountOf("Prefabs/火把"));
        }
    }
}
