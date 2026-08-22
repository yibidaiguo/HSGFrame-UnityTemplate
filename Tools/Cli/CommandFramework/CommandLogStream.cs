using System;

namespace Template.Toolkit.CommandFramework
{
    /// <summary>
    /// 命令执行期间的实时日志流：命令边跑边把行交出去，宿主当场落盘。
    ///
    /// 为什么要有：命令的产出本来只有 <see cref="CommandResult.OutputLines"/>，宿主等命令
    /// **返回之后**才逐行打出去。一跑就完的命令没差别，常驻命令（assist.serve）却因此有两个真问题——
    /// 跑着的时候日志文件一个字都没有，看上去像没起来，实际在跑（排查那次「机器人不回话」就卡在这里）；
    /// 而攒下的行只增不减，2 秒一轮常驻一天就是四万行躺在内存里。
    ///
    /// 接不接流由宿主决定：宿主之外的调用方（单测、进程内宿主）不接，命令照旧把行留在 OutputLines 里。
    /// 所以命令实现要先问 <see cref="IsAttached"/> 再决定「交出去」还是「留下」——两边只留一份，不会重。
    ///
    /// 状态是静态的：命令宿主一个进程只跑一条命令，不存在两条命令抢同一个流的情形。
    /// </summary>
    public static class CommandLogStream
    {
        private static Action<string> _sink;

        /// <summary>流接上了没有。没接上时命令要把行留在自己的 OutputLines 里。</summary>
        public static bool IsAttached => _sink != null;

        /// <summary>接上日志流。宿主在调用命令前接上，命令返回后传 null 断开。</summary>
        /// <param name="sink">收行的去处，一行调一次；传 null 表示断开。</param>
        public static void Attach(Action<string> sink)
        {
            _sink = sink;
        }

        /// <summary>交一行出去。没接上时这一行会被丢掉——调用方有责任先问 <see cref="IsAttached"/>。</summary>
        /// <param name="line">日志行。</param>
        public static void Write(string line)
        {
            _sink?.Invoke(line);
        }
    }
}
