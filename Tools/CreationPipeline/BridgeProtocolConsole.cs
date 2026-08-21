using System;
using System.IO;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 把桥进程的三条标准流钉成 UTF-8。**每个桥的 Main 第一行就要调**。
    ///
    /// 为什么必须显式钉：桥协议的 JSON 里键全是中文（「契约版本」「成功」「错误」），
    /// 而 Windows 上 Console 的默认编码跟当前代码页走。宿主与桥的代码页只要对不上，
    /// 收回来的就是乱码，调用方判「响应不合协议」——链路断在这里，报错却完全指不到编码上。
    ///
    /// 最阴的是它**跟谁启动的进程有关**：交互式 pwsh 里代码页往往已经是 UTF-8，跑得好好的；
    /// 换成后台常驻、计划任务、别的终端起来就必挂，而两边跑的是同一份代码、同一份配置。
    /// 「本地能跑线上不能跑」的经典款。
    ///
    /// 用 SetOut/SetIn 换流而不是赋值 Console.OutputEncoding：
    /// 流被重定向时改 Console.*Encoding 不一定作用到**已经打开的**那条流上，
    /// 而桥的三条流恰恰全是被重定向的。
    /// </summary>
    public static class BridgeProtocolConsole
    {
        /// <summary>协议编码：UTF-8 且**不带 BOM**——BOM 会变成 JSON 正文的第一个字符，一样解析不了。</summary>
        private static readonly Encoding ProtocolEncoding = new UTF8Encoding(false);

        /// <summary>
        /// 把 stdin / stdout / stderr 全换成 UTF-8 流。
        /// 拿不到标准流的场合（没有控制台）静默跳过：钉编码是为了让链路对得上，
        /// 不该反过来把进程弄崩。
        /// </summary>
        public static void PinUtf8()
        {
            try
            {
                Console.SetIn(new StreamReader(Console.OpenStandardInput(), ProtocolEncoding));

                // AutoFlush：协议响应写完就得到对面手里，不许留在缓冲里等进程退出。
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), ProtocolEncoding) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), ProtocolEncoding) { AutoFlush = true });
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
            }
        }
    }
}
