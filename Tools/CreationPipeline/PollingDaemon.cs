using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>守护进程的运行选项：最大轮数、轮间延迟与停止文件路径。</summary>
    public sealed class DaemonRunOptions
    {
        /// <summary>最多跑几轮；0 表示无限。</summary>
        public int MaxRounds { get; set; }

        /// <summary>轮间延迟毫秒数。</summary>
        public int RoundDelayMilliseconds { get; set; }

        /// <summary>停止文件路径；非空且该文件存在时守护在下一轮开头退出。</summary>
        public string StopFilePath { get; set; }

        /// <summary>
        /// 池子根目录；空串时退化成仓库根下的 Pools。
        /// 留这个口子是为了跟 engine.tick / engine.wake 对齐——那几条命令都收 PoolRoot 参数，
        /// 守护把它写死会让同一台机器上两条命令看的是两个队列，而谁都不会发现。
        /// </summary>
        public string PoolRoot { get; set; }
    }

    /// <summary>一轮守护跑完的汇总：跑了几轮、取了几次活、消费了几次唤醒、停止原因与逐轮记录。</summary>
    public sealed class DaemonRunSummary
    {
        /// <summary>
        /// 构造一轮守护运行的汇总。
        /// </summary>
        /// <param name="roundsRun">实际跑完的轮数。</param>
        /// <param name="takenCount">取到活的轮数。</param>
        /// <param name="wakeConsumedCount">成功消费的唤醒信号数。</param>
        /// <param name="stopReason">停止原因，永远非空。</param>
        /// <param name="records">逐轮记录，顺序即轮次顺序。</param>
        /// <param name="releaseFailureReason">锁释放失败的原因；正常释放为空串。</param>
        public DaemonRunSummary(
            int roundsRun,
            int takenCount,
            int wakeConsumedCount,
            string stopReason,
            IReadOnlyList<DaemonTickRecord> records,
            string releaseFailureReason = "")
        {
            RoundsRun = roundsRun;
            TakenCount = takenCount;
            WakeConsumedCount = wakeConsumedCount;
            StopReason = stopReason ?? "";
            Records = records ?? Array.Empty<DaemonTickRecord>();
            ReleaseFailureReason = releaseFailureReason ?? "";
        }

        /// <summary>实际跑完的轮数。</summary>
        public int RoundsRun { get; }

        /// <summary>取到活的轮数。</summary>
        public int TakenCount { get; }

        /// <summary>成功消费的唤醒信号数。</summary>
        public int WakeConsumedCount { get; }

        /// <summary>停止原因，永远非空。</summary>
        public string StopReason { get; }

        /// <summary>逐轮记录，顺序即轮次顺序。</summary>
        public IReadOnlyList<DaemonTickRecord> Records { get; }

        /// <summary>
        /// 锁释放失败的原因；正常释放为空串。
        /// 删不掉的锁文件会让下一次启动走「接管陈旧锁」那条路——能自愈，
        /// 但自愈这件事本身要留痕，否则出问题时查不出锁曾经卡过。
        /// </summary>
        public string ReleaseFailureReason { get; }
    }

    /// <summary>
    /// 轮询 daemon 的循环外壳：拿单实例锁 → 每轮查停止文件 → 扫唤醒信号 → 跑一轮取活判定并记账。
    /// 循环支持「跑满 N 轮自己退出」，门禁只跑有限轮——决策 54「常驻进程没法验」的前提由此解掉。
    /// daemon 只做取活判定 + 记账 + 消费唤醒，不真执行工作项（决策 52 同源：规划器不执行）。
    /// </summary>
    public static class PollingDaemon
    {
        /// <summary>
        /// 跑守护循环：先拿单实例锁（拿不到立刻返回，原因就是锁的 reason），
        /// 然后按固定顺序循环：停止检查 → 唤醒扫描 → 取活判定（整段 try/catch）→ 记账 → 跑满检查 → 轮间延迟。
        /// 判定抛异常时产一条 Decided=false 的记录并继续下一轮，一轮崩掉不该让 daemon 死掉。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="options">运行选项。</param>
        /// <param name="clock">取时刻的注入点，测试用假的。</param>
        /// <param name="sleep">轮间延迟的注入点，测试用假的。</param>
        public static DaemonRunSummary Run(
            string repositoryRoot,
            DaemonRunOptions options,
            Func<DateTimeOffset> clock,
            Action<int> sleep)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (sleep == null)
            {
                throw new ArgumentNullException(nameof(sleep));
            }

            var records = new List<DaemonTickRecord>();

            // 1. 先拿单实例锁。拿不到立刻返回 RoundsRun=0，原因就是锁的 reason——
            //    锁被占是单实例该有的正常结果，不是失败（决策 55 同源）。
            if (!SingleInstanceLock.TryAcquire(repositoryRoot, out var instanceLock, out var lockReason))
            {
                return new DaemonRunSummary(0, 0, 0, lockReason, records);
            }

            using (instanceLock)
            {
                var roundsRun = 0;
                var takenCount = 0;
                var wakeConsumedCount = 0;
                var lastTickMoment = DateTimeOffset.MinValue;
                var stopReason = "";

                while (true)
                {
                    // 2. 每轮开头查停止文件；存在就退出。停止文件不许删（谁放的谁清）。
                    if (!string.IsNullOrEmpty(options.StopFilePath) && File.Exists(options.StopFilePath))
                    {
                        stopReason = $"收到停止信号：{options.StopFilePath}";
                        break;
                    }

                    // 3. 扫唤醒信号；有信号这一轮按唤醒提前触发，判定跑完之后才消费
                    //    （判定崩了信号要留着，否则信号丢了没人知道）。
                    var wakePoll = WakeSignalSource.Poll(repositoryRoot);
                    var isWakeUp = wakePoll.HasSignal;

                    // 4. 加载配置与队列，跑一轮取活判定。整段 try/catch：
                    //    抛了就产一条 Decided=false 的记录并继续下一轮，不许吞掉不记账。
                    DaemonTickRecord record;
                    try
                    {
                        var now = clock();
                        var settings = EngineSettings.Load(repositoryRoot);
                        var queue = ExecutionQueue.Load(ResolvePoolRoot(repositoryRoot, options.PoolRoot));
                        var decision = PollingScheduler.Tick(settings, queue, now, lastTickMoment, isWakeUp);
                        lastTickMoment = now;
                        record = new DaemonTickRecord(
                            roundsRun + 1,
                            true,
                            decision.ShouldTake,
                            decision.Entry?.RequirementIdentifier ?? "",
                            isWakeUp,
                            decision.Reason,
                            now.ToString("o"));
                    }
                    catch (Exception exception)
                    {
                        record = new DaemonTickRecord(
                            roundsRun + 1,
                            false,
                            false,
                            "",
                            isWakeUp,
                            $"判定没跑成：{exception.Message}",
                            DateTimeOffset.Now.ToString("o"));
                    }

                    // 判定跑完之后才消费唤醒信号。
                    if (isWakeUp && record.Decided)
                    {
                        var archivedPath = WakeSignalSource.Consume(repositoryRoot, wakePoll.SignalFilePath);
                        if (archivedPath.Length > 0)
                        {
                            wakeConsumedCount++;
                        }
                    }

                    // 5. 落这一轮的账本。
                    records.Add(record);
                    DaemonTickLedger.Append(repositoryRoot, record);
                    roundsRun++;
                    if (record.ShouldTake)
                    {
                        takenCount++;
                    }

                    // 6. 跑满指定轮数就退出。
                    if (options.MaxRounds > 0 && roundsRun >= options.MaxRounds)
                    {
                        stopReason = $"跑满 {options.MaxRounds} 轮";
                        break;
                    }

                    // 7. 轮间延迟。
                    sleep(options.RoundDelayMilliseconds);
                }

                // 锁**在这里显式释放**，为的是把释放失败带进汇总：
                // 写在 using 的花括号上等于释放发生在 return 之后，
                // ReleaseFailureReason 就永远读不到——删不掉的锁文件会让下一轮走
                // 「接管陈旧锁」那条路，能自愈，但自愈过程一点痕迹都不留。
                // Dispose 是幂等的，using 再调一次不会重复删。
                instanceLock.Dispose();
                return new DaemonRunSummary(
                    roundsRun,
                    takenCount,
                    wakeConsumedCount,
                    stopReason,
                    records,
                    instanceLock.ReleaseFailureReason);
            }
        }

        // 池子根目录：显式给了就用给的，没给退化成仓库根下的 Pools
        // （子文档 03 §五：engine-daemon 定时扫 Pools/队列.json）。
        private static string ResolvePoolRoot(string repositoryRoot, string configuredPoolRoot)
        {
            if (!string.IsNullOrWhiteSpace(configuredPoolRoot))
            {
                return Path.GetFullPath(configuredPoolRoot);
            }

            return Path.Combine(repositoryRoot, "Pools");
        }
    }
}
