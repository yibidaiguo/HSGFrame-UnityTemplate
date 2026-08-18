namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 按引擎模式决定能不能自动取下一条队列任务。
    /// 值守模式是整条链路的安全阀：永不自动派活，哪怕队列里有一百条。
    /// </summary>
    public static class EngineDispatchRule
    {
        /// <summary>
        /// 按模式从队列取队首：
        /// 值守模式无条件返回 false（队列一个字都不许动）；轮询与唤醒模式取出队首返回 true。
        /// 注意：本方法只改内存里的队列对象，落盘由调用方自己 Save。
        /// </summary>
        /// <param name="settings">引擎配置，模式决定行为。</param>
        /// <param name="queue">执行队列。</param>
        /// <param name="entry">取出的队首条目；失败时为 null。</param>
        /// <param name="reason">结果说明文字。</param>
        public static bool TryTakeNext(EngineSettings settings, ExecutionQueue queue, out QueueEntry entry, out string reason)
        {
            if (settings.Mode == EngineMode.Standby)
            {
                entry = null;
                reason = "值守模式不自动派活，请人工跑 task.run";
                return false;
            }

            if (!queue.TryDequeue(out entry))
            {
                entry = null;
                reason = "队列为空";
                return false;
            }

            reason = $"{EngineSettings.ToChineseName(settings.Mode)}模式取出队首 {entry.RequirementIdentifier}";
            return true;
        }
    }
}
