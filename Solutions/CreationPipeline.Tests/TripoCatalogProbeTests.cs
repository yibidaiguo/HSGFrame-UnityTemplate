using Template.Bridges.Tripo;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// tripo「清单从报错里读回来」那条路的解析测试。
    /// 它守的是一件容易塌的事：**读不出来时要报读不出来**，不许悄悄给一个空清单——
    /// 空清单在上层会被当成「探过了，这个下游就是没有模型」。
    /// </summary>
    public class TripoCatalogProbeTests
    {
        /// <summary>历史上真拿到过的那句 1004：四个值都要解析出来，并按序数序排。</summary>
        [Fact]
        public void ParsesAllowedValuesFromRealMessageShape()
        {
            const string message = "invalid model 'tripo-v3.1', allowed values: P1-20260311, v2.5-20250123, v3.0-20250812, v3.1-20260211";

            var values = TripoClient.ParseAllowedValues(message);

            Assert.Equal(new[] { "P1-20260311", "v2.5-20250123", "v3.0-20250812", "v3.1-20260211" }, values);
        }

        /// <summary>值上带引号或多余空白时照样解析得出来——服务端的措辞不归我们管。</summary>
        [Fact]
        public void TrimsQuotesAndWhitespaceAroundValues()
        {
            const string message = "invalid model, allowed values:  'a-1' ,  \"b-2\"  ";

            var values = TripoClient.ParseAllowedValues(message);

            Assert.Equal(new[] { "a-1", "b-2" }, values);
        }

        /// <summary>重复项去掉：清单是给人挑的，同一个值列两遍只会让人以为有两档。</summary>
        [Fact]
        public void DeduplicatesRepeatedValues()
        {
            const string message = "allowed values: x-1, x-1, y-2";

            var values = TripoClient.ParseAllowedValues(message);

            Assert.Equal(new[] { "x-1", "y-2" }, values);
        }

        /// <summary>没有那句标记（服务端改了措辞、或这压根不是 1004）时给空清单，由调用方报失败。</summary>
        [Theory]
        [InlineData("")]
        [InlineData("You don't have enough credit")]
        [InlineData("invalid model 'x'")]
        public void ReturnsEmptyWhenMarkerMissing(string message)
        {
            Assert.Empty(TripoClient.ParseAllowedValues(message));
        }
    }
}
