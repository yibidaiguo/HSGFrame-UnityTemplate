using System.Collections.Generic;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Gates.Tests
{
    /// <summary>StripNonCode 对原始字符串字面量（C# 11 的三引号串）的处理测试。</summary>
    public class RawStringLiteralStripTests
    {
        /// <summary>把一段源码逐行剥掉非代码部分，跨行状态自己带着走。</summary>
        private static IReadOnlyList<string> StripAll(params string[] lines)
        {
            var inBlockComment = false;
            var inVerbatimString = false;
            var rawStringFenceLength = 0;
            var result = new List<string>();
            foreach (var line in lines)
            {
                result.Add(NamingChecker.StripNonCode(line, ref inBlockComment, ref inVerbatimString, ref rawStringFenceLength));
            }

            return result;
        }

        /// <summary>跨行原始串里没被引号裹住的中文不算标识符——这正是原先漏掉的那一路。</summary>
        [Fact]
        public void MultiLineRawStringContentIsStripped()
        {
            var stripped = StripAll(
                "var text = \"\"\"",
                "## 目标",
                "次留提升。签到是新手期唯一的每日回访钩子。",
                "\"\"\";",
                "var kept = 3;");

            Assert.DoesNotContain("目标", stripped[1]);
            Assert.DoesNotContain("次留提升", stripped[2]);
            Assert.Contains("kept", stripped[4]);
        }

        /// <summary>单行原始串就地开就地闭，后面的代码照常查。</summary>
        [Fact]
        public void SingleLineRawStringClosesOnTheSameLine()
        {
            var stripped = StripAll("var text = \"\"\"里面的中文\"\"\"; var kept = 3;");

            Assert.DoesNotContain("里面的中文", stripped[0]);
            Assert.Contains("kept", stripped[0]);
        }

        /// <summary>栅栏是四个引号时，串里那三个连着的引号不算收尾。</summary>
        [Fact]
        public void LongerFenceIsNotClosedByShorterQuoteRun()
        {
            var stripped = StripAll(
                "var text = \"\"\"\"",
                "\"\"\" 这一行还在串里",
                "\"\"\"\";",
                "var kept = 3;");

            Assert.DoesNotContain("这一行还在串里", stripped[1]);
            Assert.Contains("kept", stripped[3]);
        }

        /// <summary>插值原始串（$"""…"""）走同一路。</summary>
        [Fact]
        public void InterpolatedRawStringIsStrippedToo()
        {
            var stripped = StripAll(
                "var text = $\"\"\"",
                "中文正文 {name}",
                "\"\"\";",
                "var kept = 3;");

            Assert.DoesNotContain("中文正文", stripped[1]);
            Assert.Contains("kept", stripped[3]);
        }
    }
}
