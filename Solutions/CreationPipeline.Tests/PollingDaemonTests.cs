using System;
using System.IO;
using System.Linq;
using System.Text;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>轮询 daemon 循环外壳的行为测试：跑满退出、停止文件、值守不取活、唤醒幂等与判定崩溃不致死。</summary>
    public sealed class PollingDaemonTests : IDisposable
    {
        private readonly string _repositoryRoot;
        private readonly string _poolRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根；池根就是它的 Pools 子目录。</summary>
        public PollingDaemonTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "轮询daemon测试-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_repositoryRoot);
            _poolRoot = Path.Combine(_repositoryRoot, "Pools");
        }

        /// <summary>固定时刻：2026-08-20 10:00 +08:00。</summary>
        private static readonly DateTimeOffset FixedMoment = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.FromHours(8));

        /// <summary>轮询模式配置，间隔 60 秒。</summary>
        private static EngineSettings PollingSettings()
        {
            return new EngineSettings(EngineMode.Polling, 60, 2, 500000, 60, "");
        }

        /// <summary>每轮返回递增时刻（间隔 61 秒），保证轮询间隔检查永远通过。</summary>
        private static Func<DateTimeOffset> AdvancingClock()
        {
            var index = 0;
            return () => FixedMoment.AddSeconds(61L * index++);
        }

        /// <summary>跑满 MaxRounds=3 后自己退出，账本里正好三行、零坏行。</summary>
        [Fact]
        public void RunsThreeRoundsAndWritesThreeLedgerLines()
        {
            EngineSettings.Save(_repositoryRoot, PollingSettings());
            new ExecutionQueue(null, "").Save(_poolRoot);

            var summary = PollingDaemon.Run(
                _repositoryRoot,
                new DaemonRunOptions { MaxRounds = 3, RoundDelayMilliseconds = 10, StopFilePath = "" },
                AdvancingClock(),
                milliseconds => { });

            Assert.Equal(3, summary.RoundsRun);
            Assert.Equal("跑满 3 轮", summary.StopReason);
            Assert.Equal(3, summary.Records.Count);

            var ledger = DaemonTickLedger.Read(_repositoryRoot);
            Assert.Equal(3, ledger.Count);
            Assert.Equal(0, DaemonTickLedger.LastReadBadLineCount);
        }

        /// <summary>
        /// 账本读不动时要留下原因：空列表 + LastReadFailureReason 非空。
        /// 「账本是空的」（正常，决策 77）与「账本读不动」（故障）必须分得开——
        /// 合并的话，哪天面板拿它印统计数字，读不动就会被印成「一切正常」。
        /// 造读不动的办法：另开一个句柄以 FileShare.None 占住账本文件，
        /// 这正是真实场景里会发生的事（另一个进程正在写）。
        /// </summary>
        [Fact]
        public void UnreadableLedgerReportsReasonInsteadOfLookingEmpty()
        {
            var ledgerPath = DaemonTickLedger.LedgerFile(_repositoryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath));
            File.WriteAllText(ledgerPath, "{}", new UTF8Encoding(false));

            using (File.Open(ledgerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var records = DaemonTickLedger.Read(_repositoryRoot);

                Assert.Empty(records);
                Assert.NotEqual("", DaemonTickLedger.LastReadFailureReason);
                Assert.Contains("读不动", DaemonTickLedger.LastReadFailureReason);
            }
        }

        /// <summary>账本不存在是正常状态：空列表，且**不留失败原因**。</summary>
        [Fact]
        public void MissingLedgerIsNormalAndLeavesNoFailureReason()
        {
            var records = DaemonTickLedger.Read(_repositoryRoot);

            Assert.Empty(records);
            Assert.Equal("", DaemonTickLedger.LastReadFailureReason);
        }

        /// <summary>正常跑完一轮，锁能删掉，汇总里的释放失败原因是空的。</summary>
        [Fact]
        public void SummaryCarriesEmptyReleaseReasonOnCleanRun()
        {
            EngineSettings.Save(_repositoryRoot, PollingSettings());
            new ExecutionQueue(null, "").Save(_poolRoot);

            var summary = PollingDaemon.Run(
                _repositoryRoot,
                new DaemonRunOptions { MaxRounds = 1, RoundDelayMilliseconds = 0, StopFilePath = "" },
                AdvancingClock(),
                milliseconds => { });

            Assert.Equal("", summary.ReleaseFailureReason);
            Assert.False(File.Exists(SingleInstanceLock.LockFile(_repositoryRoot)));
        }

        /// <summary>停止文件存在时第一轮开头就退出、RoundsRun=0、账本没有行，停止文件不被删。</summary>
        [Fact]
        public void StopFileExitsBeforeFirstRound()
        {
            EngineSettings.Save(_repositoryRoot, PollingSettings());
            new ExecutionQueue(null, "").Save(_poolRoot);
            var stopFile = Path.Combine(_repositoryRoot, "stop.txt");
            File.WriteAllText(stopFile, "stop", new UTF8Encoding(false));

            var summary = PollingDaemon.Run(
                _repositoryRoot,
                new DaemonRunOptions { MaxRounds = 5, RoundDelayMilliseconds = 10, StopFilePath = stopFile },
                AdvancingClock(),
                milliseconds => { });

            Assert.Equal(0, summary.RoundsRun);
            Assert.Equal($"收到停止信号：{stopFile}", summary.StopReason);
            Assert.Empty(summary.Records);
            Assert.True(File.Exists(stopFile), "停止文件该由放它的人清，daemon 不许删");
            Assert.Empty(DaemonTickLedger.Read(_repositoryRoot));
        }

        /// <summary>值守模式 + 队列有活，三轮都不取活（决策 10/56：值守压倒一切）。</summary>
        [Fact]
        public void StandbyModeNeverTakesWorkAcrossRounds()
        {
            EngineSettings.Save(_repositoryRoot, new EngineSettings(EngineMode.Standby, 60, 2, 500000, 60, ""));
            var queue = new ExecutionQueue(null, "");
            queue.Enqueue("REQ-0001", FixedMoment, "确认人已确认");
            queue.Save(_poolRoot);

            var summary = PollingDaemon.Run(
                _repositoryRoot,
                new DaemonRunOptions { MaxRounds = 3, RoundDelayMilliseconds = 10, StopFilePath = "" },
                AdvancingClock(),
                milliseconds => { });

            Assert.Equal(3, summary.RoundsRun);
            Assert.Equal(0, summary.TakenCount);
            Assert.All(summary.Records, record => Assert.False(record.ShouldTake));
        }

        /// <summary>唤醒信号被消费后归档，第二轮不再重复消费（幂等）。</summary>
        [Fact]
        public void WakeSignalConsumedAndNotRepeatedNextRound()
        {
            EngineSettings.Save(_repositoryRoot, PollingSettings());
            new ExecutionQueue(null, "").Save(_poolRoot);
            var signalDirectory = WakeSignalSource.SignalDirectory(_repositoryRoot);
            Directory.CreateDirectory(signalDirectory);
            var signalPath = Path.Combine(signalDirectory, "wake-1.json");
            File.WriteAllText(signalPath, "{}", new UTF8Encoding(false));

            var summary = PollingDaemon.Run(
                _repositoryRoot,
                new DaemonRunOptions { MaxRounds = 2, RoundDelayMilliseconds = 10, StopFilePath = "" },
                AdvancingClock(),
                milliseconds => { });

            Assert.Equal(2, summary.RoundsRun);
            Assert.Equal(1, summary.WakeConsumedCount);
            Assert.False(File.Exists(signalPath), "信号消费后原位置不该再有");
            Assert.True(File.Exists(Path.Combine(WakeSignalSource.ArchiveDirectory(_repositoryRoot), "wake-1.json")), "信号应归档到已处理目录");
            Assert.Single(summary.Records.Where(record => record.FromWake));
        }

        /// <summary>判定段抛异常那一轮记 Decided=false 且循环没死，下一轮照常跑成。</summary>
        [Fact]
        public void ThrowingTickRoundRecordsNotDecidedAndKeepsLooping()
        {
            EngineSettings.Save(_repositoryRoot, PollingSettings());
            new ExecutionQueue(null, "").Save(_poolRoot);

            // EngineSettings.Load 与 ExecutionQueue.Load 设计上永不抛（失败都折进失败原因），
            // 判定段里唯一能注入故障的是 clock——用它模拟判定段崩溃。
            var calls = 0;
            Func<DateTimeOffset> failingClock = () =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("时钟故障（模拟判定段崩溃）");
                }

                return FixedMoment.AddSeconds(61L * calls);
            };

            var summary = PollingDaemon.Run(
                _repositoryRoot,
                new DaemonRunOptions { MaxRounds = 3, RoundDelayMilliseconds = 10, StopFilePath = "" },
                failingClock,
                milliseconds => { });

            Assert.Equal(3, summary.RoundsRun);
            var first = summary.Records[0];
            Assert.False(first.Decided);
            Assert.False(first.ShouldTake, "判定没跑成时 ShouldTake 必须为 false（决策 42）");
            Assert.Contains("时钟故障", first.Reason);
            Assert.True(summary.Records[1].Decided, "第二轮判定应该照常跑成");
            Assert.Equal(3, DaemonTickLedger.Read(_repositoryRoot).Count);
        }

        /// <summary>清掉临时仓库根。</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_repositoryRoot))
                {
                    Directory.Delete(_repositoryRoot, true);
                }
            }
            catch (IOException)
            {
                // 临时目录删不掉不影响测试结论。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }
    }
}
