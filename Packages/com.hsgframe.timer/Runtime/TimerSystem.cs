using System;
using System.Collections.Generic;
using System.Linq;

namespace HSGFrame.Timer
{
    /// <summary>计时任务的种类。</summary>
    public enum TimerTaskKind
    {
        /// <summary>到点触发一次就结束。</summary>
        Once = 0,

        /// <summary>每隔一段时间触发一次，可限定次数或无限重复。</summary>
        Interval = 1,

        /// <summary>只累计时间，不自动触发。</summary>
        Stopwatch = 2
    }

    /// <summary>一条计时任务的运行时状态。</summary>
    public sealed class TimerTask
    {
        /// <summary>任务标识，同一系统内唯一。</summary>
        public string TaskId { get; set; }

        /// <summary>任务种类。</summary>
        public TimerTaskKind Kind { get; set; }

        /// <summary>触发间隔，单位秒；秒表类忽略此值。</summary>
        public float IntervalSeconds { get; set; }

        /// <summary>还要触发几次，-1 表示无限。</summary>
        public int RemainingRepeatCount { get; set; }

        /// <summary>本轮已累计的时间。</summary>
        public float ElapsedSeconds { get; set; }

        /// <summary>是否处于暂停状态。</summary>
        public bool IsPaused { get; set; }

        /// <summary>触发时执行的回调，参数是本任务已触发的总次数。</summary>
        public Action<int> OnTriggered { get; set; }

        /// <summary>已经触发过的总次数。</summary>
        public int TriggeredCount { get; set; }
    }

    /// <summary>计时器系统：按外部推进的时间驱动一组计时任务。</summary>
    public sealed class TimerSystem
    {
        private readonly Dictionary<string, TimerTask> _tasksById = new Dictionary<string, TimerTask>();

        // 时间由调用方 Tick 进来，而不是内部读 UnityEngine.Time：
        // 这样这一层能在 dotnet test 下跑，也能在服务器侧复用（旧工程的版本直接读 Time.time，搬不过来）。
        /// <summary>当前登记的任务数。</summary>
        public int TaskCount => _tasksById.Count;

        /// <summary>登记一个到点触发一次的任务，返回任务标识。</summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="delaySeconds">延迟秒数。</param>
        /// <param name="onTriggered">触发回调。</param>
        public string ScheduleOnce(string taskId, float delaySeconds, Action<int> onTriggered)
        {
            return Schedule(taskId, TimerTaskKind.Once, delaySeconds, repeatCount: 1, onTriggered);
        }

        /// <summary>登记一个按间隔重复触发的任务，重复次数传 -1 表示无限。</summary>
        /// <param name="taskId">任务标识。</param>
        /// <param name="intervalSeconds">间隔秒数。</param>
        /// <param name="repeatCount">重复次数，-1 表示无限。</param>
        /// <param name="onTriggered">触发回调。</param>
        public string ScheduleInterval(string taskId, float intervalSeconds, int repeatCount, Action<int> onTriggered)
        {
            return Schedule(taskId, TimerTaskKind.Interval, intervalSeconds, repeatCount, onTriggered);
        }

        /// <summary>登记一个只累计时间的秒表。</summary>
        /// <param name="taskId">任务标识。</param>
        public string StartStopwatch(string taskId)
        {
            return Schedule(taskId, TimerTaskKind.Stopwatch, intervalSeconds: 0f, repeatCount: -1, onTriggered: null);
        }

        /// <summary>推进时间，触发到点的任务并清理已完成的任务。</summary>
        /// <param name="deltaSeconds">本次推进的秒数。</param>
        public void Tick(float deltaSeconds)
        {
            // 负的 deltaSeconds 按 0 处理：时钟被回拨或调用方算错帧长时，
            // 让计时器原地不动，而不是把已经走过的时间倒退回去（倒退会让剩余时间凭空变长）。
            if (deltaSeconds < 0f)
            {
                deltaSeconds = 0f;
            }

            // 回调里可能又登记或取消任务，所以先取快照再遍历。
            foreach (var task in _tasksById.Values.ToList())
            {
                if (task.IsPaused)
                {
                    continue;
                }

                task.ElapsedSeconds += deltaSeconds;
                if (task.Kind == TimerTaskKind.Stopwatch || task.ElapsedSeconds < task.IntervalSeconds)
                {
                    continue;
                }

                // 一帧跨过多个间隔时只触发一次，多出来的时间留在 ElapsedSeconds 里由后续帧逐次消化。
                // 这是刻意的：卡顿一秒就把 60 次回调一口气打出去，只会让下一帧卡得更狠。

                task.ElapsedSeconds -= task.IntervalSeconds;
                task.TriggeredCount++;
                task.OnTriggered?.Invoke(task.TriggeredCount);

                if (task.RemainingRepeatCount > 0)
                {
                    task.RemainingRepeatCount--;
                }

                if (task.RemainingRepeatCount == 0)
                {
                    _tasksById.Remove(task.TaskId);
                }
            }
        }

        /// <summary>暂停一个任务，任务不存在时返回 false。</summary>
        /// <param name="taskId">任务标识。</param>
        public bool Pause(string taskId)
        {
            return SetPaused(taskId, true);
        }

        /// <summary>恢复一个任务，任务不存在时返回 false。</summary>
        /// <param name="taskId">任务标识。</param>
        public bool Resume(string taskId)
        {
            return SetPaused(taskId, false);
        }

        /// <summary>取消一个任务，任务不存在时返回 false。</summary>
        /// <param name="taskId">任务标识。</param>
        public bool Cancel(string taskId)
        {
            return _tasksById.Remove(taskId);
        }

        /// <summary>取剩余秒数；秒表返回已累计秒数，任务不存在返回 0。</summary>
        /// <param name="taskId">任务标识。</param>
        public float GetRemainingSeconds(string taskId)
        {
            if (!_tasksById.TryGetValue(taskId, out var task))
            {
                return 0f;
            }

            return task.Kind == TimerTaskKind.Stopwatch
                ? task.ElapsedSeconds
                : Math.Max(0f, task.IntervalSeconds - task.ElapsedSeconds);
        }

        /// <summary>查询任务是否还在登记中。</summary>
        /// <param name="taskId">任务标识。</param>
        public bool Contains(string taskId)
        {
            return _tasksById.ContainsKey(taskId);
        }

        private string Schedule(string taskId, TimerTaskKind kind, float intervalSeconds, int repeatCount, Action<int> onTriggered)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                throw new ArgumentException("任务标识不能为空", nameof(taskId));
            }

            // 重复次数为 0 表示「一次都不触发」。不拦住的话它会先触发一次再因为次数归零被移除，
            // 与调用方要的语义正好相反。
            if (repeatCount == 0)
            {
                _tasksById.Remove(taskId);
                return taskId;
            }

            // 同一个标识重复登记时后者覆盖前者：任务标识由调用方给，
            // 用同一个标识就是「换掉这个任务」的意思。
            _tasksById[taskId] = new TimerTask
            {
                TaskId = taskId,
                Kind = kind,
                IntervalSeconds = intervalSeconds,
                RemainingRepeatCount = repeatCount,
                OnTriggered = onTriggered
            };

            return taskId;
        }

        private bool SetPaused(string taskId, bool isPaused)
        {
            if (!_tasksById.TryGetValue(taskId, out var task))
            {
                return false;
            }

            task.IsPaused = isPaused;
            return true;
        }
    }
}
