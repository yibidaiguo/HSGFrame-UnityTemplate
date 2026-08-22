using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一次「把脚本包装进宿主」的结局：成没成、一句人话、落到哪个目录、以及给人看的逐条说明。
    ///
    /// 这里的产物**落在仓库外**（宿主的安装目录下），`git diff` 一个字都看不见——
    /// 所以「装到哪」必须原样回报绝对路径，那是这次动作留下的唯一可核对的痕迹。
    ///
    /// 密钥红线（决策 5）：消息与说明里绝不许出现任何密钥的值。安装这条路本来就不碰密钥，
    /// 写进宿主的 link.json 里也只有仓库根路径一项。
    /// </summary>
    public sealed class ScriptInstallOutcome
    {
        /// <summary>
        /// 构造一次安装结局。
        /// </summary>
        /// <param name="succeeded">装成了没有。</param>
        /// <param name="message">一句人话：成了说装到哪，没成说为什么、下一步该干什么。</param>
        /// <param name="targetDirectory">宿主里的落点绝对路径；没装成时为空串。</param>
        /// <param name="lines">给人看的逐条说明。</param>
        public ScriptInstallOutcome(
            bool succeeded,
            string message,
            string targetDirectory,
            IReadOnlyList<string> lines)
        {
            Succeeded = succeeded;
            Message = message ?? "";
            TargetDirectory = targetDirectory ?? "";
            Lines = lines ?? Array.Empty<string>();
        }

        /// <summary>装成了没有。</summary>
        public bool Succeeded { get; }

        /// <summary>一句人话。</summary>
        public string Message { get; }

        /// <summary>宿主里的落点绝对路径；没装成时为空串。</summary>
        public string TargetDirectory { get; }

        /// <summary>给人看的逐条说明。</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>造一个失败结局。失败一律要说清下一步该干什么，不许只报「失败了」。</summary>
        /// <param name="message">失败原因 + 下一步。</param>
        /// <param name="lines">补充说明；可空。</param>
        public static ScriptInstallOutcome Failure(string message, IReadOnlyList<string> lines = null)
        {
            return new ScriptInstallOutcome(false, message, "", lines);
        }

        /// <summary>造一个成功结局。</summary>
        /// <param name="message">装了什么、装到哪。</param>
        /// <param name="targetDirectory">落点绝对路径。</param>
        /// <param name="lines">补充说明。</param>
        public static ScriptInstallOutcome Success(
            string message,
            string targetDirectory,
            IReadOnlyList<string> lines = null)
        {
            return new ScriptInstallOutcome(true, message, targetDirectory, lines);
        }
    }
}
