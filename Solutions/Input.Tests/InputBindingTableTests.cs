using System;
using System.Linq;
using GameTemplateForAgent.Input;
using Xunit;

namespace GameTemplateForAgent.Input.Tests
{
    /// <summary>输入绑定表的查询、反查、改键与冲突检测测试。</summary>
    public class InputBindingTableTests
    {
        [Fact]
        public void FindReturnsBindingByActionName()
        {
            var table = CreateTable();

            var entry = table.Find("跳跃");

            Assert.NotNull(entry);
            Assert.Equal("Space", entry.PrimaryKey);
            Assert.Equal("JoystickButton0", entry.SecondaryKey);
            Assert.Null(table.Find("不存在"));
        }

        [Fact]
        public void FindActionByKeyFindsPrimaryKey()
        {
            var table = CreateTable();

            Assert.Equal("跳跃", table.FindActionByKey("Space"));
        }

        [Fact]
        public void FindActionByKeyFindsSecondaryKey()
        {
            var table = CreateTable();

            Assert.Equal("跳跃", table.FindActionByKey("JoystickButton0"));
        }

        [Fact]
        public void RebindPrimaryChangesPrimaryKeyAndReturnsNull()
        {
            var table = CreateTable();

            var conflict = table.RebindPrimary("跳跃", "KeyW");

            Assert.Null(conflict);
            Assert.Equal("KeyW", table.Find("跳跃").PrimaryKey);
        }

        [Fact]
        public void RebindSecondaryChangesSecondaryKeyAndReturnsNull()
        {
            var table = CreateTable();

            var conflict = table.RebindSecondary("跳跃", "KeyX");

            Assert.Null(conflict);
            Assert.Equal("KeyX", table.Find("跳跃").SecondaryKey);
        }

        [Fact]
        public void RebindPrimaryToOccupiedKeyReturnsConflictAndLeavesBindingUnchanged()
        {
            var table = CreateTable();

            var conflict = table.RebindPrimary("跳跃", "KeyJ"); // 「攻击」占着 KeyJ

            Assert.NotNull(conflict);
            Assert.Equal("Space", table.Find("跳跃").PrimaryKey);
            Assert.Equal("JoystickButton0", table.Find("跳跃").SecondaryKey);
        }

        [Fact]
        public void ConflictCarriesOccupyingActionNameAndConflictingKey()
        {
            var table = CreateTable();

            var conflict = table.RebindPrimary("跳跃", "KeyJ");

            Assert.NotNull(conflict);
            Assert.Equal("攻击", conflict.OccupyingActionName);
            Assert.Equal("KeyJ", conflict.ConflictingKey);
        }

        [Fact]
        public void RebindingPrimaryToItsOwnSecondaryKeyIsNotAConflict()
        {
            var table = CreateTable();

            var conflict = table.RebindPrimary("跳跃", "JoystickButton0");

            Assert.Null(conflict);
            Assert.Equal("JoystickButton0", table.Find("跳跃").PrimaryKey);
        }

        [Fact]
        public void NullOrEmptyKeysDoNotParticipateInConflictDetection()
        {
            var table = new InputBindingTable(new[]
            {
                new InputBindingEntry { ActionName = "甲", PrimaryKey = "A", SecondaryKey = null },
                new InputBindingEntry { ActionName = "乙", PrimaryKey = "B", SecondaryKey = "" },
            });

            Assert.Null(table.DetectConflict("甲", null));
            Assert.Null(table.DetectConflict("甲", ""));
            Assert.Null(table.RebindPrimary("甲", null));
            Assert.Null(table.FindActionByKey(null));
            Assert.Null(table.FindActionByKey(""));
        }

        [Fact]
        public void RebindWithUnknownActionThrowsArgumentExceptionWithFourElements()
        {
            var table = CreateTable();

            var exception = Assert.Throws<ArgumentException>(() => table.RebindPrimary("不存在", "KeyZ"));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }

        [Fact]
        public void ConstructorThrowsWhenKeyIsOccupiedByTwoActions()
        {
            var exception = Assert.Throws<ArgumentException>(() => new InputBindingTable(new[]
            {
                new InputBindingEntry { ActionName = "甲", PrimaryKey = "A", SecondaryKey = "X" },
                new InputBindingEntry { ActionName = "乙", PrimaryKey = "A", SecondaryKey = "Y" },
            }));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
            Assert.Contains("甲", exception.Message);
            Assert.Contains("乙", exception.Message);
        }

        [Fact]
        public void ResetToDefaultsRestoresReboundKeys()
        {
            var table = CreateTable();
            table.RebindPrimary("跳跃", "KeyW");
            table.RebindSecondary("攻击", "KeyL");

            table.ResetToDefaults();

            Assert.Equal("Space", table.Find("跳跃").PrimaryKey);
            Assert.Equal("KeyK", table.Find("攻击").SecondaryKey);
        }

        [Fact]
        public void SnapshotIsOrdinalSortedAndDetachedFromTable()
        {
            var table = new InputBindingTable(new[]
            {
                new InputBindingEntry { ActionName = "b动作", PrimaryKey = "B", SecondaryKey = "b2" },
                new InputBindingEntry { ActionName = "A动作", PrimaryKey = "A", SecondaryKey = "a2" },
                new InputBindingEntry { ActionName = "c动作", PrimaryKey = "C", SecondaryKey = "c2" },
            });

            var snapshot = table.Snapshot();
            Assert.Equal(new[] { "A动作", "b动作", "c动作" }, snapshot.Select(e => e.ActionName).ToArray());

            snapshot[0].PrimaryKey = "改掉";
            Assert.Equal("A", table.Find("A动作").PrimaryKey);
        }

        [Fact]
        public void DetectConflictReturnsNullForUnoccupiedKey()
        {
            var table = CreateTable();

            Assert.Null(table.DetectConflict("跳跃", "KeyZ"));
        }

        private static InputBindingTable CreateTable()
        {
            return new InputBindingTable(new[]
            {
                new InputBindingEntry { ActionName = "跳跃", PrimaryKey = "Space", SecondaryKey = "JoystickButton0" },
                new InputBindingEntry { ActionName = "攻击", PrimaryKey = "KeyJ", SecondaryKey = "KeyK" },
            });
        }
    }
}
