using HSGFrame.Timer;
using Xunit;

namespace HSGFrame.Timer.Tests
{
    /// <summary>计时器系统的边界与错误路径测试，钉住实现当前的真实语义。</summary>
    public class TimerSystemBoundaryTests
    {
        [Fact]
        public void TickWithZeroDeltaLeavesPendingTaskStateUnchanged()
        {
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleOnce("零延迟", 1.0f, _ => triggeredCount++);

            system.Tick(0.0f);

            Assert.Equal(0, triggeredCount);
            Assert.Equal(1.0f, system.GetRemainingSeconds("零延迟"), 3);
            Assert.True(system.Contains("零延迟"));
        }

        [Fact]
        public void TickWithNegativeDeltaLeavesTimeWhereItWas()
        {
            // 负 delta 按 0 处理。原实现直接把负数加进 ElapsedSeconds，剩余时间反而变长——
            // 时钟被回拨或调用方算错帧长时，计时器会越等越久。
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleOnce("负延迟", 2.0f, _ => triggeredCount++);

            system.Tick(-1.0f);

            Assert.Equal(0, triggeredCount);
            Assert.True(system.Contains("负延迟"));
            Assert.Equal(2.0f, system.GetRemainingSeconds("负延迟"), 3);
        }

        [Fact]
        public void SingleTickSpanningMultipleIntervalsTriggersOnlyOnce()
        {
            // Tick 用 if 而非 while：一次 Tick 只触发一次，跨过的多个间隔折叠成剩余累计时间，
            // 留到后续 Tick 再逐次触发。这里钉住「一次 Tick 5 秒、间隔 1 秒 → 只触发 1 次」。
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleInterval("每秒", 1.0f, -1, _ => triggeredCount++);

            system.Tick(5.0f);

            Assert.Equal(1, triggeredCount);
        }

        [Fact]
        public void OnceTaskWithZeroDelayTriggersOnNextTick()
        {
            // 延时 0 意味着 IntervalSeconds == 0，任何 Tick（含 0 秒）都会立刻触发。
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleOnce("零延时", 0.0f, _ => triggeredCount++);

            system.Tick(0.0f);

            Assert.Equal(1, triggeredCount);
            Assert.False(system.Contains("零延时"));
        }

        [Fact]
        public void IntervalTaskWithZeroRepeatCountNeverTriggers()
        {
            // 重复次数 0 表示一次都不触发。原实现不拦这一步，会先触发一次再因次数归零被移除，
            // 与调用方要的语义正好相反。
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleInterval("零重复", 1.0f, 0, _ => triggeredCount++);

            system.Tick(1.0f);

            Assert.Equal(0, triggeredCount);
            Assert.False(system.Contains("零重复"));
        }

        [Fact]
        public void RegisteringDuplicateTaskIdOverwritesPreviousTask()
        {
            // 登记用字典下标赋值：同一个 taskId 后注册的覆盖先注册的。
            var system = new TimerSystem();
            var firstTriggered = 0;
            var secondTriggered = 0;
            system.ScheduleOnce("重复标识", 1.0f, _ => firstTriggered++);
            system.ScheduleOnce("重复标识", 0.5f, _ => secondTriggered++);

            Assert.Equal(1, system.TaskCount);
            system.Tick(0.6f);

            Assert.Equal(0, firstTriggered);
            Assert.Equal(1, secondTriggered);
        }

        [Fact]
        public void PauseOnUnknownTaskReturnsFalseWithoutThrowing()
        {
            var system = new TimerSystem();

            Assert.False(system.Pause("不存在"));
        }

        [Fact]
        public void ResumeOnUnpausedTaskReturnsTrueAndLeavesItRunning()
        {
            // Resume 只把 IsPaused 置 false：对未暂停的任务返回 true，状态不变。
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleOnce("未暂停", 1.0f, _ => triggeredCount++);

            Assert.True(system.Resume("未暂停"));

            system.Tick(1.0f);
            Assert.Equal(1, triggeredCount);
        }

        [Fact]
        public void CancelOnUnknownTaskReturnsFalse()
        {
            var system = new TimerSystem();

            Assert.False(system.Cancel("不存在"));
        }

        [Fact]
        public void RemainingSecondsForUnknownTaskIsZero()
        {
            var system = new TimerSystem();

            Assert.Equal(0f, system.GetRemainingSeconds("不存在"), 3);
        }

        [Fact]
        public void PausedTaskIgnoresMultipleTicksAndResumesWhereItLeftOff()
        {
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleInterval("暂停的间隔", 1.0f, -1, _ => triggeredCount++);

            system.Tick(0.5f);
            Assert.True(system.Pause("暂停的间隔"));

            system.Tick(10.0f);
            system.Tick(10.0f);
            Assert.Equal(0, triggeredCount);

            Assert.True(system.Resume("暂停的间隔"));
            system.Tick(0.5f);

            Assert.Equal(1, triggeredCount);
        }

        [Fact]
        public void CallbackCanRegisterNewTaskWithoutCollectionModifiedException()
        {
            var system = new TimerSystem();
            var secondTriggered = 0;
            system.ScheduleOnce("第一个", 0.5f, _ =>
            {
                system.ScheduleOnce("第二个", 0.5f, __ => secondTriggered++);
            });

            system.Tick(0.5f);
            Assert.True(system.Contains("第二个"));

            system.Tick(0.5f);
            Assert.Equal(1, secondTriggered);
        }

        [Fact]
        public void CallbackCanCancelItselfWithoutException()
        {
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleOnce("自取消", 0.5f, _ =>
            {
                triggeredCount++;
                system.Cancel("自取消");
            });

            system.Tick(0.5f);

            Assert.Equal(1, triggeredCount);
            Assert.False(system.Contains("自取消"));
        }

        [Fact]
        public void StopwatchElapsedTimeGrowsWithEachTick()
        {
            var system = new TimerSystem();
            system.StartStopwatch("秒表累计");

            system.Tick(1.0f);
            Assert.Equal(1.0f, system.GetRemainingSeconds("秒表累计"), 3);

            system.Tick(2.5f);
            Assert.Equal(3.5f, system.GetRemainingSeconds("秒表累计"), 3);

            system.Tick(0.5f);
            Assert.Equal(4.0f, system.GetRemainingSeconds("秒表累计"), 3);
            Assert.True(system.Contains("秒表累计"));
        }

        [Fact]
        public void StopwatchDoesNotAccumulateWhilePaused()
        {
            var system = new TimerSystem();
            system.StartStopwatch("暂停秒表");

            system.Tick(1.0f);
            Assert.True(system.Pause("暂停秒表"));
            system.Tick(5.0f);
            Assert.Equal(1.0f, system.GetRemainingSeconds("暂停秒表"), 3);

            Assert.True(system.Resume("暂停秒表"));
            system.Tick(1.0f);
            Assert.Equal(2.0f, system.GetRemainingSeconds("暂停秒表"), 3);
        }
    }
}
