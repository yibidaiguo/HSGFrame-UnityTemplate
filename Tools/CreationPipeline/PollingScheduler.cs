using System;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次取活判定的结论：该不该取、取到谁、为什么、多久后再来一轮。</summary>
    public sealed class TickDecision
    {
        /// <summary>
        /// 构造一次取活判定结论。
        /// </summary>
        /// <param name="shouldTake">这一轮该不该取活。</param>
        /// <param name="entry">取到的队列条目，没取到为 null。</param>
        /// <param name="reason">结果说明文字，无论取没取到都要写清为什么。</param>
        /// <param name="nextTickSeconds">建议多久后再来一轮。</param>
        public TickDecision(bool shouldTake, QueueEntry entry, string reason, int nextTickSeconds)
        {
            ShouldTake = shouldTake;
            Entry = entry;
            Reason = reason ?? "";
            NextTickSeconds = nextTickSeconds;
        }

        /// <summary>这一轮该不该取活。</summary>
        public bool ShouldTake { get; }

        /// <summary>取到的队列条目，没取到为 null。</summary>
        public QueueEntry Entry { get; }

        /// <summary>结果说明文字，无论取没取到都要写清为什么。</summary>
        public string Reason { get; }

        /// <summary>建议多久后再来一轮。</summary>
        public int NextTickSeconds { get; }
    }

    /// <summary>
    /// 轮询调度：按引擎模式决定这一轮该不该取活。规则顺序即优先级，第一个命中的赢：
    /// 值守压倒一切、配置读不到按值守、唤醒只是提前一轮、间隔检查、最后才取活判定。
    /// 一次 tick 的全部判定逻辑在这里；循环本身是外壳（计划任务 / while 脚本），判定才是引擎。
    /// </summary>
    public static class PollingScheduler
    {
        /// <summary>
        /// 跑一轮取活判定。
        /// </summary>
        /// <param name="settings">引擎配置，模式与轮询间隔决定行为。</param>
        /// <param name="queue">执行队列。</param>
        /// <param name="now">当前时刻。</param>
        /// <param name="lastTickMoment">上一次取活判定的时刻。</param>
        /// <param name="isWakeUp">是否唤醒提前触发；true 时跳过间隔检查，判定逻辑与轮询同一条。</param>
        public static TickDecision Tick(
            EngineSettings settings,
            ExecutionQueue queue,
            DateTimeOffset now,
            DateTimeOffset lastTickMoment,
            bool isWakeUp)
        {
            // 1. 值守压倒一切：任何其他条件都不能让值守模式取活（锁定决策 10）。
            if (settings.Mode == EngineMode.Standby)
            {
                return new TickDecision(false, null, "值守模式，永不自动取活；要跑请人工执行", 0);
            }

            // 2. 配置读不出来按值守处理：EngineSettings.Load 读不到配置时本来就返回值守，
            //    这条是双保险，留着。
            if (settings.LoadFailureReason.Length > 0)
            {
                return new TickDecision(
                    false,
                    null,
                    $"{settings.LoadFailureReason}。配置读不到时按值守处理",
                    0);
            }

            // 3. 唤醒只是「提前一轮」，判定逻辑与轮询同一条；这里只跳过下面的间隔检查。
            //    第 1、2 步已经在上面跑过，唤醒不能绕过值守。
            if (!isWakeUp)
            {
                var elapsed = now - lastTickMoment;
                if (elapsed.TotalSeconds < settings.PollIntervalSeconds)
                {
                    var remaining = settings.PollIntervalSeconds - (int)elapsed.TotalSeconds;
                    if (remaining < 1)
                    {
                        remaining = 1;
                    }

                    return new TickDecision(false, null, $"距上次取活还差 {remaining} 秒", remaining);
                }
            }

            // 4. 取活判定本身：轮询与唤醒同一条路径（防漏）。
            var canTake = EngineDispatchRule.TryTakeNext(settings, queue, out var entry, out var reason);
            var prefix = isWakeUp ? "唤醒提前触发；" : "";
            return canTake
                ? new TickDecision(true, entry, prefix + reason, settings.PollIntervalSeconds)
                : new TickDecision(false, null, prefix + reason, settings.PollIntervalSeconds);
        }
    }
}
