using GameTemplateForAgent.Timer;
using Xunit;

namespace GameTemplateForAgent.Timer.Tests
{
    /// <summary>计时器系统的到点触发、重复、暂停与取消测试。</summary>
    public class TimerSystemTests
    {
        [Fact]
        public void OnceTaskTriggersExactlyOnceAndIsRemoved()
        {
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleOnce("开场提示", 1.0f, _ => triggeredCount++);

            system.Tick(0.5f);
            Assert.Equal(0, triggeredCount);

            system.Tick(0.6f);
            Assert.Equal(1, triggeredCount);
            Assert.False(system.Contains("开场提示"));
        }

        [Fact]
        public void IntervalTaskTriggersRequestedNumberOfTimes()
        {
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleInterval("每秒回血", 1.0f, 3, _ => triggeredCount++);

            for (var step = 0; step < 5; step++)
            {
                system.Tick(1.0f);
            }

            Assert.Equal(3, triggeredCount);
            Assert.False(system.Contains("每秒回血"));
        }

        [Fact]
        public void InfiniteIntervalTaskKeepsRunning()
        {
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleInterval("心跳", 0.5f, -1, _ => triggeredCount++);

            for (var step = 0; step < 4; step++)
            {
                system.Tick(0.5f);
            }

            Assert.Equal(4, triggeredCount);
            Assert.True(system.Contains("心跳"));
        }

        [Fact]
        public void PausedTaskDoesNotAdvance()
        {
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleOnce("延迟开门", 1.0f, _ => triggeredCount++);

            Assert.True(system.Pause("延迟开门"));
            system.Tick(2.0f);
            Assert.Equal(0, triggeredCount);

            Assert.True(system.Resume("延迟开门"));
            system.Tick(1.0f);
            Assert.Equal(1, triggeredCount);
        }

        [Fact]
        public void CancelledTaskNeverTriggers()
        {
            var system = new TimerSystem();
            var triggeredCount = 0;
            system.ScheduleOnce("被取消的任务", 1.0f, _ => triggeredCount++);

            Assert.True(system.Cancel("被取消的任务"));
            system.Tick(5.0f);

            Assert.Equal(0, triggeredCount);
            Assert.False(system.Cancel("被取消的任务"));
        }

        [Fact]
        public void StopwatchAccumulatesElapsedTimeWithoutTriggering()
        {
            var system = new TimerSystem();
            system.StartStopwatch("本局用时");

            system.Tick(1.5f);
            system.Tick(2.0f);

            Assert.Equal(3.5f, system.GetRemainingSeconds("本局用时"), 3);
            Assert.True(system.Contains("本局用时"));
        }

        [Fact]
        public void RemainingSecondsCountsDownForPendingTask()
        {
            var system = new TimerSystem();
            system.ScheduleOnce("倒计时", 2.0f, _ => { });

            system.Tick(0.5f);

            Assert.Equal(1.5f, system.GetRemainingSeconds("倒计时"), 3);
        }
    }
}
