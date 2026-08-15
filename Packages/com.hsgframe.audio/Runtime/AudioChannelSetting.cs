namespace HSGFrame.Audio
{
    /// <summary>一条音频通道的音量设置，音量在构造时夹取到 0~1。</summary>
    public sealed class AudioChannelSetting
    {
        /// <summary>通道名称。</summary>
        public string ChannelName { get; }

        /// <summary>通道音量，取值 0~1。</summary>
        public float Volume { get; }

        /// <summary>以通道名与音量构造，音量超出 0~1 时夹取到边界。</summary>
        public AudioChannelSetting(string channelName, float volume)
        {
            ChannelName = channelName;
            Volume = Clamp(volume);
        }

        private static float Clamp(float volume)
        {
            if (volume < 0f)
            {
                return 0f;
            }

            if (volume > 1f)
            {
                return 1f;
            }

            return volume;
        }
    }
}
