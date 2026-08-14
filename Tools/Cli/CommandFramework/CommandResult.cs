using System;
using System.Collections.Generic;

namespace Template.Toolkit.CommandFramework
{
    /// <summary>
    /// 命令的执行结果：是否成功、给人看的一句话消息，以及可逐行打印的结构化输出。
    /// </summary>
    public sealed class CommandResult
    {
        /// <summary>是否执行成功。</summary>
        public bool IsSuccess { get; private set; }

        /// <summary>面向人的结果消息。</summary>
        public string Message { get; private set; }

        /// <summary>结构化输出行，永远非 null。</summary>
        public IReadOnlyList<string> OutputLines { get; private set; }

        /// <summary>
        /// 构造一个成功结果。
        /// </summary>
        /// <param name="message">结果消息。</param>
        /// <param name="outputLines">结构化输出行，传 null 时存成空数组。</param>
        public static CommandResult Success(string message, IReadOnlyList<string> outputLines = null)
        {
            return new CommandResult
            {
                IsSuccess = true,
                Message = message,
                OutputLines = outputLines ?? Array.Empty<string>()
            };
        }

        /// <summary>
        /// 构造一个失败结果。
        /// </summary>
        /// <param name="message">结果消息。</param>
        /// <param name="outputLines">结构化输出行，传 null 时存成空数组。</param>
        public static CommandResult Failure(string message, IReadOnlyList<string> outputLines = null)
        {
            return new CommandResult
            {
                IsSuccess = false,
                Message = message,
                OutputLines = outputLines ?? Array.Empty<string>()
            };
        }
    }
}
