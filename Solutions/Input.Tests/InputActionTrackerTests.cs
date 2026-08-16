using System;
using HSGFrame.Input;
using Xunit;

namespace HSGFrame.Input.Tests
{
    /// <summary>动作状态跟踪器的按下/按住/抬起状态迁移测试。</summary>
    public class InputActionTrackerTests
    {
        [Fact]
        public void FirstFramePressReportsPressed()
        {
            var tracker = CreateTracker();

            tracker.Tick(new[] { "Space" });

            Assert.Equal(InputActionPhase.Pressed, tracker.GetPhase("跳跃"));
            Assert.Equal(new[] { "跳跃" }, tracker.PressedActions);
        }

        [Fact]
        public void HoldingAcrossFramesReportsHeld()
        {
            var tracker = CreateTracker();

            tracker.Tick(new[] { "Space" });
            tracker.Tick(new[] { "Space" });

            Assert.Equal(InputActionPhase.Held, tracker.GetPhase("跳跃"));
        }

        [Fact]
        public void ReleaseFrameReportsReleased()
        {
            var tracker = CreateTracker();

            tracker.Tick(new[] { "Space" });
            tracker.Tick(new[] { "Space" });
            tracker.Tick(Array.Empty<string>());

            Assert.Equal(InputActionPhase.Released, tracker.GetPhase("跳跃"));
        }

        [Fact]
        public void ReleasedOnlyLastsOneFrameThenReturnsToIdle()
        {
            var tracker = CreateTracker();

            tracker.Tick(new[] { "Space" });
            tracker.Tick(new[] { "Space" });
            tracker.Tick(Array.Empty<string>());
            tracker.Tick(Array.Empty<string>());

            Assert.Equal(InputActionPhase.Idle, tracker.GetPhase("跳跃"));
        }

        [Fact]
        public void SecondaryKeyTriggersTheSameAction()
        {
            var tracker = CreateTracker();

            tracker.Tick(new[] { "JoystickButton0" });

            Assert.Equal(InputActionPhase.Pressed, tracker.GetPhase("跳跃"));
        }

        [Fact]
        public void TwoActionsPressedTogetherBothEnterPressedActions()
        {
            var tracker = CreateTracker();

            tracker.Tick(new[] { "Space", "KeyJ" });

            Assert.Equal(InputActionPhase.Pressed, tracker.GetPhase("跳跃"));
            Assert.Equal(InputActionPhase.Pressed, tracker.GetPhase("攻击"));
            Assert.Equal(new[] { "攻击", "跳跃" }, tracker.PressedActions);
        }

        [Fact]
        public void PressedActionsAreOrdinalSorted()
        {
            var table = new InputBindingTable(new[]
            {
                new InputBindingEntry { ActionName = "b", PrimaryKey = "B", SecondaryKey = "b2" },
                new InputBindingEntry { ActionName = "a", PrimaryKey = "A", SecondaryKey = "a2" },
                new InputBindingEntry { ActionName = "C", PrimaryKey = "KeyC", SecondaryKey = "c2" },
            });
            var tracker = new InputActionTracker(table);

            tracker.Tick(new[] { "B", "A", "KeyC" });

            Assert.Equal(new[] { "C", "a", "b" }, tracker.PressedActions);
        }

        [Fact]
        public void TickWithNullKeysDoesNotThrow()
        {
            var tracker = CreateTracker();

            tracker.Tick(null);

            Assert.Equal(InputActionPhase.Idle, tracker.GetPhase("跳跃"));
        }

        [Fact]
        public void UnknownActionReturnsIdle()
        {
            var tracker = CreateTracker();

            tracker.Tick(new[] { "Space" });

            Assert.Equal(InputActionPhase.Idle, tracker.GetPhase("不存在"));
        }

        [Fact]
        public void RebindTakesEffectOnNextTick()
        {
            var table = CreateTable();
            var tracker = new InputActionTracker(table);

            table.RebindPrimary("跳跃", "KeyQ");
            tracker.Tick(new[] { "KeyQ" });

            Assert.Equal(InputActionPhase.Pressed, tracker.GetPhase("跳跃"));
        }

        [Fact]
        public void PressingBothKeysOfOneActionCountsOnce()
        {
            var tracker = CreateTracker();

            tracker.Tick(new[] { "Space", "JoystickButton0" });

            Assert.Equal(InputActionPhase.Pressed, tracker.GetPhase("跳跃"));
            Assert.Single(tracker.PressedActions);
        }

        [Fact]
        public void TickActionsFirstFrameReportsPressed()
        {
            var tracker = new InputActionTracker();

            tracker.TickActions(new[] { "前进" });

            Assert.Equal(InputActionPhase.Pressed, tracker.GetPhase("前进"));
        }

        [Fact]
        public void TickActionsHoldingAcrossFramesReportsHeld()
        {
            var tracker = new InputActionTracker();

            tracker.TickActions(new[] { "前进" });
            tracker.TickActions(new[] { "前进" });

            Assert.Equal(InputActionPhase.Held, tracker.GetPhase("前进"));
        }

        [Fact]
        public void TickActionsReleaseFrameReportsReleased()
        {
            var tracker = new InputActionTracker();

            tracker.TickActions(new[] { "前进" });
            tracker.TickActions(Array.Empty<string>());

            Assert.Equal(InputActionPhase.Released, tracker.GetPhase("前进"));
        }

        [Fact]
        public void TickActionsReleasedOnlyLastsOneFrameThenReturnsToIdle()
        {
            var tracker = new InputActionTracker();

            tracker.TickActions(new[] { "前进" });
            tracker.TickActions(Array.Empty<string>());
            tracker.TickActions(Array.Empty<string>());

            Assert.Equal(InputActionPhase.Idle, tracker.GetPhase("前进"));
        }

        [Fact]
        public void TickActionsWithNullDoesNotThrow()
        {
            var tracker = new InputActionTracker();

            tracker.TickActions(null);

            Assert.Equal(InputActionPhase.Idle, tracker.GetPhase("前进"));
        }

        [Fact]
        public void TickOnParameterlessTrackerThrowsInvalidOperation()
        {
            var tracker = new InputActionTracker();

            Assert.Throws<InvalidOperationException>(() => tracker.Tick(new[] { "Space" }));
        }

        private static InputBindingTable CreateTable()
        {
            return new InputBindingTable(new[]
            {
                new InputBindingEntry { ActionName = "跳跃", PrimaryKey = "Space", SecondaryKey = "JoystickButton0" },
                new InputBindingEntry { ActionName = "攻击", PrimaryKey = "KeyJ", SecondaryKey = "KeyK" },
            });
        }

        private static InputActionTracker CreateTracker()
        {
            return new InputActionTracker(CreateTable());
        }
    }
}
