using System;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>冲突裁决流水测试：只追加、序号连续、按冲突查历史、Resolve 先写流水后改状态。</summary>
    public class ConflictDecisionLedgerTests
    {
        /// <summary>文件不存在 → 空流水、原因空串（空流水是正常状态）。</summary>
        [Fact]
        public void MissingFileLoadsEmptyLedgerWithEmptyReason()
        {
            using var workspace = new PoolTestWorkspace();

            var ledger = ConflictDecisionLedger.Load(workspace.Root);

            Assert.Empty(ledger.Records);
            Assert.Equal("", ledger.LoadFailureReason);
        }

        /// <summary>连续 Append 三条 → 序号 1、2、3，字段对得上。</summary>
        [Fact]
        public void AppendCreatesSequentialNumbers()
        {
            using var workspace = new PoolTestWorkspace();

            ConflictDecisionLedger.Append(workspace.Root, "CF-0001", "张三", "强制推送", "2026-08-20T10:00:00Z", "未决", "未决");
            ConflictDecisionLedger.Append(workspace.Root, "CF-0001", "李四", "改新的", "2026-08-20T11:00:00Z", "未决", "已裁决");
            ConflictDecisionLedger.Append(workspace.Root, "CF-0002", "王五", "改旧的", "2026-08-20T12:00:00Z", "未决", "已裁决");

            var ledger = ConflictDecisionLedger.Load(workspace.Root);

            Assert.Equal(3, ledger.Records.Count);
            Assert.Equal(1, ledger.Records[0].SequenceNumber);
            Assert.Equal(2, ledger.Records[1].SequenceNumber);
            Assert.Equal(3, ledger.Records[2].SequenceNumber);
            Assert.Equal("CF-0001", ledger.Records[0].ConflictIdentifier);
            Assert.Equal("改旧的", ledger.Records[2].Choice);
        }

        /// <summary>FindByConflict 只返回该冲突的，按序号升序。</summary>
        [Fact]
        public void FindByConflictReturnsOnlyThatConflictInOrder()
        {
            using var workspace = new PoolTestWorkspace();
            ConflictDecisionLedger.Append(workspace.Root, "CF-0001", "张三", "强制推送", "2026-08-20T10:00:00Z", "未决", "未决");
            ConflictDecisionLedger.Append(workspace.Root, "CF-0002", "李四", "改旧的", "2026-08-20T10:30:00Z", "未决", "已裁决");
            ConflictDecisionLedger.Append(workspace.Root, "CF-0001", "王五", "改新的", "2026-08-20T11:00:00Z", "未决", "已裁决");

            var history = ConflictDecisionLedger.Load(workspace.Root).FindByConflict("CF-0001");

            Assert.Equal(2, history.Count);
            Assert.All(history, record => Assert.Equal("CF-0001", record.ConflictIdentifier));
            Assert.Equal(1, history[0].SequenceNumber);
            Assert.Equal(3, history[1].SequenceNumber);
        }

        /// <summary>只追加：Append 第二条之后，第一条的七个字段逐个未变。</summary>
        [Fact]
        public void AppendNeverModifiesExistingRecord()
        {
            using var workspace = new PoolTestWorkspace();
            ConflictDecisionLedger.Append(workspace.Root, "CF-0001", "张三", "强制推送", "2026-08-20T10:00:00Z", "未决", "未决");
            var before = ConflictDecisionLedger.Load(workspace.Root).Records.Single();

            ConflictDecisionLedger.Append(workspace.Root, "CF-0002", "李四", "改旧的", "2026-08-20T11:00:00Z", "未决", "已裁决");

            var after = ConflictDecisionLedger.Load(workspace.Root).Records[0];
            Assert.Equal(before.SequenceNumber, after.SequenceNumber);
            Assert.Equal(before.ConflictIdentifier, after.ConflictIdentifier);
            Assert.Equal(before.ResolverName, after.ResolverName);
            Assert.Equal(before.Choice, after.Choice);
            Assert.Equal(before.Moment, after.Moment);
            Assert.Equal(before.StateBefore, after.StateBefore);
            Assert.Equal(before.StateAfter, after.StateAfter);
        }

        /// <summary>经 Resolve 强制推送再补选「改新的」→ 流水两条都在，第一条的选择仍是强制推送（被覆盖缺口已修）。</summary>
        [Fact]
        public void ForcePushThenCloseKeepsBothRecords()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "入库");
            ConflictList.Resolve(workspace.Root, entry.Identifier, "策划甲", "强制推送", "2026-08-19T10:00:00Z");
            ConflictList.Resolve(workspace.Root, entry.Identifier, "策划乙", "改新的", "2026-08-19T12:00:00Z");

            var ledger = ConflictDecisionLedger.Load(workspace.Root);

            Assert.Equal(2, ledger.Records.Count);
            Assert.Equal("强制推送", ledger.Records[0].Choice);
            Assert.Equal("改新的", ledger.Records[1].Choice);
            // 强制推送挂账：前后状态都是未决；补选销账：从未决到已裁决。
            Assert.Equal(ConflictEntry.PendingState, ledger.Records[0].StateAfter);
            Assert.Equal(ConflictEntry.PendingState, ledger.Records[1].StateBefore);
            Assert.Equal(ConflictEntry.ResolvedState, ledger.Records[1].StateAfter);
        }

        /// <summary>Resolve 被前置校验拒掉（重复强制推送）→ 流水一条都不许多。</summary>
        [Fact]
        public void RejectedResolveAddsNoRecord()
        {
            using var workspace = new PoolTestWorkspace();
            var entry = ConflictList.Append(workspace.Root, "DR-0058", "REQ-0042", "入库");
            ConflictList.Resolve(workspace.Root, entry.Identifier, "策划甲", "强制推送", "2026-08-19T10:00:00Z");

            var rejected = ConflictList.Resolve(workspace.Root, entry.Identifier, "策划乙", "强制推送", "2026-08-19T12:00:00Z");

            Assert.False(rejected.IsResolved);
            Assert.Single(ConflictDecisionLedger.Load(workspace.Root).Records);
        }
    }
}
