using Template.Toolkit.Gates;
using Xunit;

namespace Template.Gates.Tests
{
    /// <summary>StripNonCode 对插值字符串的处理测试。</summary>
    public class InterpolatedStringStripTests
    {
        [Fact]
        public void InterpolatedStringWithNestedLiteralsIsStrippedWhole()
        {
            // 这一行原先会被误读：扫描器在洞里第一个引号处就以为字符串结束了，
            // 于是「已创建」「已更新」被当成标识符报成「含中文」。
            var line = "return $\"资产{(isNew ? \"已创建\" : \"已更新\")}：{count} 条\";";

            var stripped = Strip(line);

            Assert.DoesNotContain("已创建", stripped);
            Assert.DoesNotContain("已更新", stripped);
            Assert.Contains("return", stripped);
        }

        [Fact]
        public void CodeAfterAnInterpolatedStringIsStillScanned()
        {
            var line = "Log($\"值 {value}\"); var badMgr = 1;";

            var stripped = Strip(line);

            Assert.DoesNotContain("值", stripped);
            Assert.Contains("badMgr", stripped);
        }

        [Fact]
        public void EscapedBracesDoNotOpenAHole()
        {
            var line = "var text = $\"{{字面花括号}}\"; var kept = 2;";

            var stripped = Strip(line);

            Assert.DoesNotContain("字面花括号", stripped);
            Assert.Contains("kept", stripped);
        }

        [Fact]
        public void VerbatimInterpolatedStringEntersVerbatimMode()
        {
            var inBlockComment = false;
            var inVerbatimString = false;

            var stripped = NamingChecker.StripNonCode("var text = $@\"第一行", ref inBlockComment, ref inVerbatimString);

            Assert.True(inVerbatimString);
            Assert.DoesNotContain("第一行", stripped);
        }

        [Fact]
        public void EscapedQuotesDoNotEndTheInterpolatedString()
        {
            var line = "throw new Exception($\"原因：缺少 {name} 字段；参考：\\\"动作\\\": \\\"跳跃\\\"\");";

            var stripped = Strip(line);

            Assert.DoesNotContain("动作", stripped);
            Assert.DoesNotContain("跳跃", stripped);
        }

        [Fact]
        public void PlainStringsStillGetStripped()
        {
            var line = "var text = \"普通字符串\"; var kept = 3;";

            var stripped = Strip(line);

            Assert.DoesNotContain("普通字符串", stripped);
            Assert.Contains("kept", stripped);
        }

        private static string Strip(string line)
        {
            var inBlockComment = false;
            var inVerbatimString = false;
            return NamingChecker.StripNonCode(line, ref inBlockComment, ref inVerbatimString);
        }
    }
}
