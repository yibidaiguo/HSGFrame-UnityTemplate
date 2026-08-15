using GameTemplateForAgent.Audio;
using UnityEngine;

namespace Template.Presentation.Framework
{
    /// <summary>音频播放壳：把纯 C# 的混音状态套到真实的 AudioSource 上。音量怎么算是框架层的事，这里只负责照它算出来的值设置引擎。</summary>
    [DisallowMultipleComponent]
    public sealed class AudioPlayerBehaviour : MonoBehaviour
    {
        private AudioSource _backgroundSource;
        private AudioSource _effectSource;

        /// <summary>本壳使用的混音状态，音量与静音都改它。</summary>
        public AudioMixerState MixerState { get; } = new AudioMixerState();

        /// <summary>建一个播放壳并跨场景保留。</summary>
        public static AudioPlayerBehaviour Create()
        {
            var host = new GameObject(nameof(AudioPlayerBehaviour));
            var player = host.AddComponent<AudioPlayerBehaviour>();
            // DontDestroyOnLoad 只在运行模式下有效，编辑模式下调它会抛异常。
            // 编辑器里的工具与验收脚本也会建这个壳，所以这里按模式分开处理。
            if (Application.isPlaying)
            {
                Object.DontDestroyOnLoad(host);
            }
            return player;
        }

        private void Awake()
        {
            EnsureSources();
        }

        // 两路 AudioSource 走懒初始化而不是只在 Awake 里建：编辑模式下 Awake 不会被调用，
        // 而编辑器里的工具与验收脚本同样要用这个壳。每个入口先叫一次这里，两种模式下都成立。
        private void EnsureSources()
        {
            if (_effectSource != null)
            {
                return;
            }

            _backgroundSource = gameObject.AddComponent<AudioSource>();
            _backgroundSource.playOnAwake = false;
            _backgroundSource.loop = true;

            _effectSource = gameObject.AddComponent<AudioSource>();
            _effectSource.playOnAwake = false;

            // 混音状态一变就把两路音量同步过去，省得每个调用点自己记得刷新。
            MixerState.VolumeChanged += ApplyChannelVolume;
            ApplyChannelVolume(AudioChannel.Background);
            ApplyChannelVolume(AudioChannel.Effect);
        }

        private void OnDestroy()
        {
            MixerState.VolumeChanged -= ApplyChannelVolume;
        }

        /// <summary>播一段背景音，音量取背景通道当前生效的值。</summary>
        /// <param name="clip">要播的音频。</param>
        public void PlayBackground(AudioClip clip)
        {
            EnsureSources();
            _backgroundSource.clip = clip;
            _backgroundSource.volume = MixerState.ResolveVolume(AudioChannel.Background);
            _backgroundSource.Play();
        }

        /// <summary>播一次音效，音量取音效通道当前生效的值。</summary>
        /// <param name="clip">要播的音频。</param>
        public void PlayEffectOnce(AudioClip clip)
        {
            EnsureSources();
            _effectSource.PlayOneShot(clip, MixerState.ResolveVolume(AudioChannel.Effect));
        }

        /// <summary>取某个通道当前设到 AudioSource 上的音量，供验收核对。</summary>
        /// <param name="channel">要查的通道。</param>
        public float ReadAppliedVolume(AudioChannel channel)
        {
            EnsureSources();
            return channel == AudioChannel.Background ? _backgroundSource.volume : _effectSource.volume;
        }

        private void ApplyChannelVolume(AudioChannel channel)
        {
            var volume = MixerState.ResolveVolume(channel);
            if (channel == AudioChannel.Background)
            {
                _backgroundSource.volume = volume;
            }
            else
            {
                _effectSource.volume = volume;
            }
        }
    }
}
