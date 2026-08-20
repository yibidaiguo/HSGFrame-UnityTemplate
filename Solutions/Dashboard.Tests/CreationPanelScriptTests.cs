using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.Dashboard.Tests
{
    /// <summary>
    /// 面板脚本的健全性检查。这一族测试存在的理由：面板的 JS 住在 C# 的 verbatim 字符串里，
    /// 一个写错的引号转义（把空串写成两个双引号）就会吐出半个字面量，整份脚本语法错、
    /// 十五页一页都不渲染——而 C# 编译、单元测试、全量门禁**全都是绿的**，因为没人解析过那段 JS。
    /// P4 批次二起面板就是全白的，直到 P7 批次二验收时人真去开了一次才发现。
    /// </summary>
    public sealed class CreationPanelScriptTests
    {
        /// <summary>从面板 HTML 里抠出 script 标签之间的正文。</summary>
        private static string ExtractScript()
        {
            var match = Regex.Match(CreationPanelPage.Html, "<script>(.*?)</script>", RegexOptions.Singleline);
            Assert.True(match.Success, "面板 HTML 里没有 script 段");
            return match.Groups[1].Value;
        }

        /// <summary>
        /// 每一行的双引号与单引号都必须成对。这是个粗糙但有效的启发式：
        /// 面板脚本基本一行一句，跨行字符串不存在，所以落单的引号就意味着
        /// verbatim 字符串的转义写错了——那正是「吐出半个字面量」的长相。
        /// </summary>
        [Fact]
        public void EveryScriptLineHasBalancedQuotes()
        {
            var offenders = new List<string>();
            var lineNumber = 0;
            foreach (var rawLine in ExtractScript().Split('\n'))
            {
                lineNumber++;
                var line = rawLine.Trim();
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                var doubleCount = 0;
                var singleCount = 0;
                foreach (var character in line)
                {
                    if (character == '"')
                    {
                        doubleCount++;
                    }
                    else if (character == '\'')
                    {
                        singleCount++;
                    }
                }

                if (doubleCount % 2 != 0 || singleCount % 2 != 0)
                {
                    offenders.Add($"第 {lineNumber} 行引号落单：{line}");
                }
            }

            Assert.True(offenders.Count == 0, string.Join("\n", offenders));
        }

        /// <summary>页面表里点名的每个渲染函数，脚本里都要真的定义出来，否则那一页点开就是空白。</summary>
        [Fact]
        public void EveryPageHasItsRenderFunctionDefined()
        {
            var script = ExtractScript();
            var missing = new List<string>();
            foreach (Match match in Regex.Matches(script, @"渲染:\s*(\w+)"))
            {
                var functionName = match.Groups[1].Value;
                if (!script.Contains($"function {functionName}(", StringComparison.Ordinal))
                {
                    missing.Add(functionName);
                }
            }

            Assert.True(missing.Count == 0, "页面表点名了但脚本里没定义：" + string.Join("、", missing));
        }
    }
}
