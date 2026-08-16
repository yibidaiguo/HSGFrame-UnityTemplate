using System;
using System.Collections.Generic;

namespace Template.Toolkit.Scaffold
{
    /// <summary>一次可选功能摘除的结果。</summary>
    public sealed class FeatureRemovalResult
    {
        /// <summary>是否摘除成功。</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>面向人的结果消息，失败时说明是哪一项不满足。</summary>
        public string Message { get; private set; }

        /// <summary>
        /// 每一处落到文件系统的改动各一行。
        /// 它是这条命令的自述：产出必须能被 git diff 看见，这份清单要能跟 diff 对得上。
        /// </summary>
        public IReadOnlyList<string> ChangedPaths { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// 构造一个成功结果。
        /// </summary>
        /// <param name="message">结果消息。</param>
        /// <param name="changedPaths">逐处改动说明。</param>
        public static FeatureRemovalResult Success(string message, IReadOnlyList<string> changedPaths)
        {
            return new FeatureRemovalResult
            {
                IsSuccess = true,
                Message = message,
                ChangedPaths = changedPaths ?? Array.Empty<string>(),
            };
        }

        /// <summary>
        /// 构造一个失败结果。
        /// </summary>
        /// <param name="message">失败原因，按四要素写。</param>
        public static FeatureRemovalResult Failure(string message)
        {
            return new FeatureRemovalResult
            {
                IsSuccess = false,
                Message = message,
                ChangedPaths = Array.Empty<string>(),
            };
        }
    }
}
