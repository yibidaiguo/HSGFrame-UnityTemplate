namespace GameTemplateForAgent.Hotfix
{
    /// <summary>一次热更启动的结果：成功与否、是否需要更新、提示消息与版本信息。</summary>
    public sealed class HotfixLaunchResult
    {
        /// <summary>启动是否成功。</summary>
        public bool IsSuccess { get; }

        /// <summary>本次启动是否实际执行了更新。</summary>
        public bool NeedsUpdate { get; }

        /// <summary>结果消息，失败时说明原因，成功时说明「已是最新」或「更新完成」。</summary>
        public string Message { get; }

        /// <summary>启动后落盘的已装版本号文本。</summary>
        public string InstalledVersionText { get; }

        /// <summary>回滚到的版本号文本，未发生回滚时为空串。</summary>
        public string RolledBackTo { get; }

        private HotfixLaunchResult(bool isSuccess, bool needsUpdate, string message, string installedVersionText, string rolledBackTo)
        {
            IsSuccess = isSuccess;
            NeedsUpdate = needsUpdate;
            Message = message;
            InstalledVersionText = installedVersionText;
            RolledBackTo = rolledBackTo;
        }

        /// <summary>构造一个成功结果，rolledBackTo 仅在发生回滚时非空。</summary>
        public static HotfixLaunchResult Success(bool needsUpdate, string message, string installedVersionText, string rolledBackTo = "")
            => new HotfixLaunchResult(true, needsUpdate, message, installedVersionText, rolledBackTo);

        /// <summary>构造一个失败结果，rolledBackTo 为发生回滚时写回的版本，否则为空串。</summary>
        public static HotfixLaunchResult Fail(string message, string installedVersionText, string rolledBackTo)
            => new HotfixLaunchResult(false, false, message, installedVersionText, rolledBackTo);
    }
}
