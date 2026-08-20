using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次冲突裁决的结果：是否成功、裁决后的条目、触发的系统动作与失败原因。</summary>
    public sealed class ConflictResolutionResult
    {
        /// <summary>
        /// 构造一次裁决结果。
        /// </summary>
        /// <param name="isResolved">是否裁决成功。</param>
        /// <param name="entry">裁决后的那条冲突，失败时为 null。</param>
        /// <param name="systemActions">本次裁决触发的系统动作，中文待办文案。</param>
        /// <param name="reason">失败原因，成功时为空串。</param>
        internal ConflictResolutionResult(
            bool isResolved,
            ConflictEntry entry,
            IReadOnlyList<string> systemActions,
            string reason)
        {
            IsResolved = isResolved;
            Entry = entry;
            SystemActions = systemActions;
            Reason = reason;
        }

        /// <summary>是否裁决成功。</summary>
        public bool IsResolved { get; }

        /// <summary>裁决后的那条冲突，失败时为 null。</summary>
        public ConflictEntry Entry { get; }

        /// <summary>本次裁决触发的系统动作，中文待办文案；失败时为空。</summary>
        public IReadOnlyList<string> SystemActions { get; }

        /// <summary>失败原因，成功时为空串。</summary>
        public string Reason { get; }

        /// <summary>构造一个成功的裁决结果。</summary>
        /// <param name="entry">裁决后的那条冲突。</param>
        /// <param name="systemActions">触发的系统动作。</param>
        internal static ConflictResolutionResult Resolved(ConflictEntry entry, IReadOnlyList<string> systemActions)
        {
            return new ConflictResolutionResult(true, entry, systemActions, "");
        }

        /// <summary>构造一个失败的裁决结果。</summary>
        /// <param name="reason">失败原因。</param>
        internal static ConflictResolutionResult Failed(string reason)
        {
            return new ConflictResolutionResult(false, null, Array.Empty<string>(), reason);
        }
    }
}
