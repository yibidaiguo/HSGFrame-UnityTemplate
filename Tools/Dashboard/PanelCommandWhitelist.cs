using System;
using System.Collections.Generic;

namespace Template.Toolkit.Dashboard
{
    /// <summary>
    /// 面板命令白名单：只判定命令名是否属于 task.* / pool.* / bridge.* / engine.* / conflict.* / spec.*
    /// 六族之一、命令名里有没有不该出现的字符、整行有没有超长。纯判定不执行任何东西。
    /// </summary>
    public static class PanelCommandWhitelist
    {
        /// <summary>放行的命令族前缀，按判定顺序。</summary>
        public static readonly IReadOnlyList<string> AllowedPrefixes = new List<string>
        {
            "task.",
            "pool.",
            "bridge.",
            "engine.",
            "conflict.",
            "spec."
        };

        /// <summary>
        /// 判定一条命令行是否放行。
        /// 依次检查：命令行为空、命令名含非法字符、命令名不以白名单前缀开头、整行超过 500 字符。
        /// 参数部分不做白名单判定，只受整行长度上限约束。
        /// </summary>
        /// <param name="commandLine">面板传来的整条命令行。</param>
        /// <param name="commandName">取出的命令名（第一个空白之前的片段）；命令行为空时为空串。</param>
        /// <param name="rejectReason">拒绝原因；放行时为空串。</param>
        /// <returns>放行返回 true，否则 false。</returns>
        public static bool IsAllowed(string commandLine, out string commandName, out string rejectReason)
        {
            commandName = "";
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                rejectReason = "命令行为空";
                return false;
            }

            commandName = TakeFirstToken(commandLine);

            var badCharacter = FindBadCharacter(commandName);
            if (badCharacter != null)
            {
                rejectReason = $"命令名里有不该出现的字符：{badCharacter}";
                return false;
            }

            var matched = false;
            foreach (var prefix in AllowedPrefixes)
            {
                if (commandName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                rejectReason = $"命令「{commandName}」不在面板白名单里；白名单只放行 task. / pool. / bridge. / engine. / conflict. / spec. 六族";
                return false;
            }

            if (commandLine.Length > 500)
            {
                rejectReason = "命令行超过 500 字符";
                return false;
            }

            rejectReason = "";
            return true;
        }

        /// <summary>取第一个空白字符之前的片段当作命令名。</summary>
        private static string TakeFirstToken(string commandLine)
        {
            var endIndex = 0;
            while (endIndex < commandLine.Length && !char.IsWhiteSpace(commandLine[endIndex]))
            {
                endIndex++;
            }

            return commandLine.Substring(0, endIndex);
        }

        /// <summary>
        /// 找命令名里第一个不该出现的字符或序列；没有返回 null。
        /// 「..」是双字符序列，单独报；其余非法字符逐个查。
        /// </summary>
        private static string FindBadCharacter(string commandName)
        {
            if (commandName.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                return "..";
            }

            const string forbidden = "/\\&|;`$<>";
            foreach (var character in forbidden)
            {
                if (commandName.IndexOf(character) >= 0)
                {
                    return character.ToString();
                }
            }

            return null;
        }
    }
}
