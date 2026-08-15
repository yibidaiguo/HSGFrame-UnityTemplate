using System;

namespace GameTemplateForAgent.Audio
{
    /// <summary>音频通道：背景音乐或音效。</summary>
    public enum AudioChannel
    {
        /// <summary>背景音乐通道。</summary>
        Background,

        /// <summary>音效通道。</summary>
        Effect,
    }

    /// <summary>
    /// 混音状态：总音量、各通道音量与静音开关，算出每个通道最终生效的音量。
    /// 旧实现用 -1 表示「沿用默认音量」，那个约定容易出错；这里改为把入参显式钳到 0~1，
    /// 传 -1 或 2 都收敛到边界。
    /// </summary>
    public sealed class AudioMixerState
    {
        private float _globalVolume;
        private float _backgroundVolume;
        private float _effectVolume;
        private bool _isMuted;

        /// <summary>总音量，取值收敛在 0 到 1 之间。</summary>
        public float GlobalVolume
        {
            get => _globalVolume;
            set
            {
                var clamped = Clamp(value);
                if (clamped == _globalVolume)
                {
                    return;
                }

                _globalVolume = clamped;
                OnVolumeChanged(AudioChannel.Background);
                OnVolumeChanged(AudioChannel.Effect);
            }
        }

        /// <summary>背景音通道音量，取值收敛在 0 到 1 之间。</summary>
        public float BackgroundVolume
        {
            get => _backgroundVolume;
            set
            {
                var clamped = Clamp(value);
                if (clamped == _backgroundVolume)
                {
                    return;
                }

                _backgroundVolume = clamped;
                OnVolumeChanged(AudioChannel.Background);
            }
        }

        /// <summary>音效通道音量，取值收敛在 0 到 1 之间。</summary>
        public float EffectVolume
        {
            get => _effectVolume;
            set
            {
                var clamped = Clamp(value);
                if (clamped == _effectVolume)
                {
                    return;
                }

                _effectVolume = clamped;
                OnVolumeChanged(AudioChannel.Effect);
            }
        }

        /// <summary>静音开关。静音时最终音量恒为 0，但各通道音量的设定值原样留着。</summary>
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted == value)
                {
                    return;
                }

                _isMuted = value;
                OnVolumeChanged(AudioChannel.Background);
                OnVolumeChanged(AudioChannel.Effect);
            }
        }

        /// <summary>算某个通道最终生效的音量：静音时为 0，否则是总音量乘以该通道音量。</summary>
        public float ResolveVolume(AudioChannel channel)
        {
            if (_isMuted)
            {
                return 0f;
            }

            if (channel == AudioChannel.Background)
            {
                return _globalVolume * _backgroundVolume;
            }

            return _globalVolume * _effectVolume;
        }

        /// <summary>音量变化时触发，参数是发生变化的通道。</summary>
        public event Action<AudioChannel> VolumeChanged;

        private void OnVolumeChanged(AudioChannel channel)
        {
            VolumeChanged?.Invoke(channel);
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
