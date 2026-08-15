using System.Collections.Generic;
using HSGFrame.Audio;
using Xunit;

namespace HSGFrame.Audio.Tests
{
    /// <summary>混音状态的音量钳制、最终音量计算与变化事件测试。</summary>
    public class AudioMixerStateTests
    {
        [Fact]
        public void GlobalVolumeClampsNegativeToZero()
        {
            var state = new AudioMixerState();

            state.GlobalVolume = -1f;

            Assert.Equal(0f, state.GlobalVolume);
        }

        [Fact]
        public void GlobalVolumeClampsAboveOneToOne()
        {
            var state = new AudioMixerState();

            state.GlobalVolume = 2f;

            Assert.Equal(1f, state.GlobalVolume);
        }

        [Fact]
        public void BackgroundVolumeClampsNegativeToZero()
        {
            var state = new AudioMixerState();

            state.BackgroundVolume = -1f;

            Assert.Equal(0f, state.BackgroundVolume);
        }

        [Fact]
        public void BackgroundVolumeClampsAboveOneToOne()
        {
            var state = new AudioMixerState();

            state.BackgroundVolume = 2f;

            Assert.Equal(1f, state.BackgroundVolume);
        }

        [Fact]
        public void EffectVolumeClampsNegativeToZero()
        {
            var state = new AudioMixerState();

            state.EffectVolume = -1f;

            Assert.Equal(0f, state.EffectVolume);
        }

        [Fact]
        public void EffectVolumeClampsAboveOneToOne()
        {
            var state = new AudioMixerState();

            state.EffectVolume = 2f;

            Assert.Equal(1f, state.EffectVolume);
        }

        [Fact]
        public void ResolveVolumeMultipliesGlobalByBackground()
        {
            var state = new AudioMixerState();
            state.GlobalVolume = 0.5f;
            state.BackgroundVolume = 0.4f;

            Assert.Equal(0.2f, state.ResolveVolume(AudioChannel.Background), 3);
        }

        [Fact]
        public void ResolveVolumeMultipliesGlobalByEffect()
        {
            var state = new AudioMixerState();
            state.GlobalVolume = 0.8f;
            state.EffectVolume = 0.25f;

            Assert.Equal(0.2f, state.ResolveVolume(AudioChannel.Effect), 3);
        }

        [Fact]
        public void ResolveVolumeReturnsZeroForBothChannelsWhenMuted()
        {
            var state = new AudioMixerState();
            state.GlobalVolume = 0.5f;
            state.BackgroundVolume = 0.8f;
            state.EffectVolume = 0.6f;
            state.IsMuted = true;

            Assert.Equal(0f, state.ResolveVolume(AudioChannel.Background));
            Assert.Equal(0f, state.ResolveVolume(AudioChannel.Effect));
        }

        [Fact]
        public void ChannelVolumeReadingsStayIntactWhileMuted()
        {
            var state = new AudioMixerState();
            state.GlobalVolume = 0.5f;
            state.BackgroundVolume = 0.7f;
            state.EffectVolume = 0.3f;
            state.IsMuted = true;

            Assert.Equal(0.5f, state.GlobalVolume);
            Assert.Equal(0.7f, state.BackgroundVolume);
            Assert.Equal(0.3f, state.EffectVolume);
        }

        [Fact]
        public void UnmutingRestoresOriginalResolvedVolume()
        {
            var state = new AudioMixerState();
            state.GlobalVolume = 0.5f;
            state.BackgroundVolume = 0.7f;
            state.IsMuted = true;
            Assert.Equal(0f, state.ResolveVolume(AudioChannel.Background));

            state.IsMuted = false;

            Assert.Equal(0.35f, state.ResolveVolume(AudioChannel.Background), 3);
        }

        [Fact]
        public void VolumeChangedFiresWhenBackgroundVolumeChanges()
        {
            var state = new AudioMixerState();
            var received = new List<AudioChannel>();
            state.VolumeChanged += channel => received.Add(channel);

            state.BackgroundVolume = 0.4f;

            Assert.Single(received);
            Assert.Equal(AudioChannel.Background, received[0]);
        }

        [Fact]
        public void VolumeChangedFiresWhenEffectVolumeChanges()
        {
            var state = new AudioMixerState();
            var received = new List<AudioChannel>();
            state.VolumeChanged += channel => received.Add(channel);

            state.EffectVolume = 0.4f;

            Assert.Single(received);
            Assert.Equal(AudioChannel.Effect, received[0]);
        }

        [Fact]
        public void VolumeChangedFiresBothChannelsWhenGlobalChanges()
        {
            var state = new AudioMixerState();
            var received = new List<AudioChannel>();
            state.VolumeChanged += channel => received.Add(channel);

            state.GlobalVolume = 0.3f;

            Assert.Equal(2, received.Count);
            Assert.Contains(AudioChannel.Background, received);
            Assert.Contains(AudioChannel.Effect, received);
        }

        [Fact]
        public void VolumeChangedDoesNotFireWhenValueUnchanged()
        {
            var state = new AudioMixerState();
            state.BackgroundVolume = 0.5f;
            var firedCount = 0;
            state.VolumeChanged += _ => firedCount++;

            state.BackgroundVolume = 0.5f;

            Assert.Equal(0, firedCount);
        }

        [Fact]
        public void MuteToggleFiresBothChannelsOnce()
        {
            var state = new AudioMixerState();
            var received = new List<AudioChannel>();
            state.VolumeChanged += channel => received.Add(channel);

            state.IsMuted = true;

            Assert.Equal(2, received.Count);
            Assert.Contains(AudioChannel.Background, received);
            Assert.Contains(AudioChannel.Effect, received);
        }

        [Fact]
        public void ResolveVolumeComparisonUsesDecimalTolerance()
        {
            var state = new AudioMixerState();
            state.GlobalVolume = 0.333f;
            state.BackgroundVolume = 0.333f;

            Assert.Equal(0.111f, state.ResolveVolume(AudioChannel.Background), 3);
        }
    }

    /// <summary>淡入淡出计划的时间推进与边界测试。</summary>
    public class AudioFadePlanTests
    {
        [Fact]
        public void ZeroDurationResolvesToTargetImmediately()
        {
            var plan = new AudioFadePlan(0f, 1f, 0f);

            Assert.Equal(1f, plan.ResolveVolume(0f));
        }

        [Fact]
        public void HalfwayElapsedResolvesToMidpoint()
        {
            var plan = new AudioFadePlan(0f, 1f, 2f);

            Assert.Equal(0.5f, plan.ResolveVolume(1f), 3);
        }

        [Fact]
        public void ElapsedBeyondDurationStaysAtTarget()
        {
            var plan = new AudioFadePlan(0f, 1f, 2f);

            Assert.Equal(1f, plan.ResolveVolume(5f));
        }

        [Fact]
        public void NegativeElapsedResolvesAsZero()
        {
            var plan = new AudioFadePlan(0.2f, 0.8f, 2f);

            Assert.Equal(0.2f, plan.ResolveVolume(-1f), 3);
        }

        [Fact]
        public void IsCompleteAtBoundary()
        {
            var plan = new AudioFadePlan(0f, 1f, 2f);

            Assert.False(plan.IsComplete(1.99f));
            Assert.True(plan.IsComplete(2f));
        }

        [Fact]
        public void FadeOutFromHigherToLowerVolume()
        {
            var plan = new AudioFadePlan(1f, 0f, 2f);

            Assert.Equal(0.5f, plan.ResolveVolume(1f), 3);
        }
    }
}
