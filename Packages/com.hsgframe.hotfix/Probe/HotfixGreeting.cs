namespace Template.Hotfix
{
    /// <summary>热更验证用的最小热更代码。它所在的程序集被 HybridCLR 当作热更程序集单独编译成 dll。</summary>
    public static class HotfixGreeting
    {
        /// <summary>返回一句问候语。热更链路的验收就是改这一行的返回值，再看客户端拿到的是不是新的那句。</summary>
        public static string Speak()
        {
            return "热更前的行为";
        }
    }
}
