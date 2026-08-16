using System.Collections;
using HSGFrame.Audio;
using HSGFrame.Event;
using HSGFrame.Logging;
using NUnit.Framework;
using Template.Presentation.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Template.Tests.PlayMode
{
    /// <summary>框架各系统在真运行期的行为测试。这些用例要的是「真的过了一帧」，编辑模式下证不了。</summary>
    public sealed class FrameworkPlayModeTests
    {
        /// <summary>帧驱动壳挂进场景后，每帧回调真的按帧被调。</summary>
        [UnityTest]
        public IEnumerator FrameDriverTicksOncePerRenderedFrame()
        {
            var driver = FrameworkDriverBehaviour.Ensure();
            Assert.IsNotNull(driver, "帧驱动壳没建起来");

            var updateCount = 0;
            using (FrameworkDriverBehaviour.Registry.AddUpdateListener(() => updateCount++))
            {
                yield return null;
                yield return null;
                yield return null;
            }

            // 三次 yield return null 就是三帧，登记期间每帧一次。
            Assert.GreaterOrEqual(updateCount, 3, "每帧回调没有按帧触发");

            var countAfterDispose = updateCount;
            yield return null;
            Assert.AreEqual(countAfterDispose, updateCount, "注销之后仍然被调用了");
        }

        /// <summary>固定帧回调拿到的步长与引擎的 fixedDeltaTime 一致。</summary>
        [UnityTest]
        public IEnumerator FixedUpdateListenerReceivesEngineFixedDeltaTime()
        {
            FrameworkDriverBehaviour.Ensure();

            var receivedStep = -1f;
            using (FrameworkDriverBehaviour.Registry.AddFixedUpdateListener(step => receivedStep = step))
            {
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(Time.fixedDeltaTime, receivedStep, 0.0001f, "固定帧步长与引擎的对不上");
        }

        /// <summary>音效在运行期真的被播出去：AudioSource 进入播放状态。</summary>
        [UnityTest]
        public IEnumerator AudioSourceActuallyPlaysInPlayMode()
        {
            var clip = Resources.Load<AudioClip>("不存在的资源");
            Assert.IsNull(clip, "这一步只是确认 Resources 里没有同名资源，避免误判");

            var player = AudioPlayerBehaviour.Create();
            player.MixerState.GlobalVolume = 1f;
            player.MixerState.BackgroundVolume = 1f;

            // 现造一段 1 秒的静音 clip：测试要证的是「播放这条链路通」，不依赖具体音频资产。
            var generated = AudioClip.Create("测试音", 44100, 1, 44100, false);
            player.PlayBackground(generated);

            yield return null;

            var source = player.GetComponent<AudioSource>();
            Assert.IsTrue(source.isPlaying, "背景音没有进入播放状态");
            Assert.AreEqual(1f, source.volume, 0.001f, "音量没有按混音状态设上去");

            Object.Destroy(player.gameObject);
            yield return null;
        }

        /// <summary>日志经由 Unity 控制台落点真的写进了控制台。</summary>
        [UnityTest]
        public IEnumerator LoggerWritesThroughUnityConsoleSink()
        {
            var options = new LogFormatOptions { WriteLevel = true };
            // Logger 这个名字 UnityEngine 里也有一个，写全名免得歧义。
            var logger = new HSGFrame.Logging.Logger(options);
            var memory = new MemoryLogSink(8);
            logger.AddSink(memory);
            logger.AddSink(new UnityConsoleLogSink(options));

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("运行期日志"));
            logger.Information("运行期日志");

            yield return null;

            Assert.AreEqual(1, memory.Entries.Count, "内存落点没收到这条日志");
        }

        /// <summary>事件总线在运行期跨帧派发，订阅者按帧收到。</summary>
        [UnityTest]
        public IEnumerator EventBusDeliversAcrossFrames()
        {
            var bus = new EventBus();
            var received = 0;

            using (bus.Subscribe<int>("每帧事件", _ => received++))
            {
                for (var frame = 0; frame < 3; frame++)
                {
                    bus.Publish("每帧事件", frame);
                    yield return null;
                }
            }

            Assert.AreEqual(3, received, "跨帧派发的次数对不上");
        }
    }
}
