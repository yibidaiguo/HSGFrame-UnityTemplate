using System;
using HSGFrame.Input;
using Xunit;

namespace HSGFrame.Input.Tests
{
    /// <summary>输入绑定 JSON 编解码的往返与错误路径测试。</summary>
    public class InputBindingCodecTests
    {
        [Fact]
        public void RoundTripPreservesAllBindings()
        {
            var table = new InputBindingTable(new[]
            {
                new InputBindingEntry { ActionName = "跳跃", PrimaryKey = "Space", SecondaryKey = "JoystickButton0" },
                new InputBindingEntry { ActionName = "攻击", PrimaryKey = "KeyJ", SecondaryKey = "KeyK" },
            });

            var json = InputBindingCodec.ToJson(table);
            var restored = InputBindingCodec.FromJson(json);

            Assert.Equal(new[] { "攻击", "跳跃" }, restored.ActionNames);
            Assert.Equal("Space", restored.Find("跳跃").PrimaryKey);
            Assert.Equal("KeyK", restored.Find("攻击").SecondaryKey);
        }

        [Fact]
        public void ChineseKeyNamesAreNotEscaped()
        {
            var table = new InputBindingTable(new[]
            {
                new InputBindingEntry { ActionName = "跳跃", PrimaryKey = "Space", SecondaryKey = "JoystickButton0" },
            });

            var json = InputBindingCodec.ToJson(table);

            Assert.Contains("绑定", json);
            Assert.Contains("动作", json);
            Assert.Contains("主键", json);
            Assert.Contains("副键", json);
            Assert.Contains("跳跃", json);
            Assert.DoesNotContain("\\u", json);
        }

        [Fact]
        public void MissingBindingsFieldThrowsInputBindingException()
        {
            var exception = Assert.Throws<InputBindingException>(
                () => InputBindingCodec.FromJson("{ \"别的\": [] }"));

            Assert.Contains("绑定", exception.Message);
        }

        [Fact]
        public void MissingActionFieldThrowsInputBindingException()
        {
            var exception = Assert.Throws<InputBindingException>(
                () => InputBindingCodec.FromJson("{ \"绑定\": [ { \"主键\": \"Space\" } ] }"));

            Assert.Contains("动作", exception.Message);
        }

        [Fact]
        public void MalformedJsonThrowsInputBindingExceptionWithFourElements()
        {
            var exception = Assert.Throws<InputBindingException>(
                () => InputBindingCodec.FromJson("{ 这不是JSON "));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }

        [Fact]
        public void EmptyBindingListRoundTrips()
        {
            var restored = InputBindingCodec.FromJson("{ \"绑定\": [] }");
            Assert.Empty(restored.ActionNames);

            var json = InputBindingCodec.ToJson(restored);
            var again = InputBindingCodec.FromJson(json);
            Assert.Empty(again.ActionNames);
        }
    }
}
