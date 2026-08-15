namespace HSGFrame.Audio
{
    /// <summary>淡入淡出计划：给定起止音量与时长，按已经过的时间算当前音量，线性插值。</summary>
    public sealed class AudioFadePlan
    {
        private readonly float _fromVolume;
        private readonly float _toVolume;

        /// <summary>用起止音量与时长构造，时长为 0 或负数表示立刻到位。</summary>
        public AudioFadePlan(float fromVolume, float toVolume, float durationSeconds)
        {
            _fromVolume = fromVolume;
            _toVolume = toVolume;
            DurationSeconds = durationSeconds;
        }

        /// <summary>时长秒数。</summary>
        public float DurationSeconds { get; }

        /// <summary>按已经过的秒数算当前音量，超过时长后恒为目标音量，负的已过秒数按 0 处理。</summary>
        public float ResolveVolume(float elapsedSeconds)
        {
            if (DurationSeconds <= 0f)
            {
                return _toVolume;
            }

            var ratio = Clamp(elapsedSeconds / DurationSeconds);
            return _fromVolume + (_toVolume - _fromVolume) * ratio;
        }

        /// <summary>已经过的秒数是否已经走完全程。</summary>
        public bool IsComplete(float elapsedSeconds)
        {
            if (DurationSeconds <= 0f)
            {
                return true;
            }

            return elapsedSeconds >= DurationSeconds;
        }

        private static float Clamp(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }
    }
}
