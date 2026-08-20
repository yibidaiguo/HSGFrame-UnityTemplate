using Xunit;
using Template.Bridges.Feishu;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 下游字段类型码（第二段映射）测试：把下游类型名映射成目标平台建表接口的数字码。
    /// 这是纯函数、脱离飞书可测。要点：每个下游类型映到对的数字；不认识的类型必须报错
    /// 而不是默默给 1——默默给 1 会让一列单选变成自由文本，而表建出来就撤不掉了；
    /// 单选/多选带选项列表，其余类型不带。
    /// </summary>
    public class FeishuFieldTypeTests
    {
        /// <summary>每个下游类型映到对的数字。</summary>
        [Theory]
        [InlineData("文本", 1)]
        [InlineData("多行文本", 1)]
        [InlineData("数字", 2)]
        [InlineData("单选", 3)]
        [InlineData("多选", 4)]
        [InlineData("复选框", 7)]
        public void KnownTypesMapToExpectedCodes(string downstreamType, int expectedCode)
        {
            var succeeded = FeishuFieldTypeCodec.TryMap(downstreamType, out var typeCode, out var reason);

            Assert.True(succeeded);
            Assert.Equal(expectedCode, typeCode);
            Assert.Equal("", reason);
        }

        /// <summary>不认识的类型要报错，不许默默给一个默认码。</summary>
        [Theory]
        [InlineData("日期")]
        [InlineData("附件")]
        [InlineData("引用")]
        public void UnknownTypesFailInsteadOfDefaultingToTextCode(string downstreamType)
        {
            var succeeded = FeishuFieldTypeCodec.TryMap(downstreamType, out var typeCode, out var reason);

            Assert.False(succeeded);
            Assert.Equal(0, typeCode);
            Assert.Contains("不认识的字段类型", reason);
            Assert.Contains("文本", reason);
        }

        /// <summary>空类型名同样失败（文案不同，但同样不是默默给 1）。</summary>
        [Fact]
        public void EmptyTypeFailsToo()
        {
            Assert.False(FeishuFieldTypeCodec.TryMap("", out var typeCode, out var reason));
            Assert.Equal(0, typeCode);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        /// <summary>单选/多选需要带选项列表，其余类型不带。</summary>
        [Theory]
        [InlineData("文本", false)]
        [InlineData("多行文本", false)]
        [InlineData("数字", false)]
        [InlineData("单选", true)]
        [InlineData("多选", true)]
        [InlineData("复选框", false)]
        public void OptionsRequirementFollowsType(string downstreamType, bool requiresOptions)
        {
            Assert.Equal(requiresOptions, FeishuFieldTypeCodec.RequiresOptions(downstreamType));
        }

        /// <summary>不认识的类型按「不带选项」处理，但映射本身必须先报错——不能走到带不带选项这一步。</summary>
        [Fact]
        public void UnknownTypeIsNotSilentlyTreatableAsAnything()
        {
            Assert.False(FeishuFieldTypeCodec.TryMap("不存在的类型", out _, out _));
        }
    }
}
