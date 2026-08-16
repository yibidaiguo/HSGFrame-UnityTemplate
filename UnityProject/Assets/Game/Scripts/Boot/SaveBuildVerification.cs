using System.Collections.Generic;

namespace Template.Boot
{
    /// <summary>
    /// 存档这一项验收。它不依赖任何可选包，所以由 Boot 自己挂进注册表——
    /// 热更那类可选功能在自己的包里挂，摘掉包时这一项照跑。
    /// </summary>
    public sealed class SaveBuildVerification : IBuildVerification
    {
        /// <summary>存档排在最前：它不依赖任何随包资产，别的项提前失败也不该把它一起吞掉。</summary>
        public const int VerificationOrder = 10;

        /// <summary>这一项的名字。</summary>
        public string Name => "存档";

        /// <summary>排序键。</summary>
        public int Order => VerificationOrder;

        /// <summary>跑一次存档往返与迁移链，追加一行结论。</summary>
        /// <param name="reportLines">结论收集器。</param>
        public void Collect(List<string> reportLines)
        {
            reportLines.Add("存档 · 运行时序列化与迁移链 —— " + SaveVerification.ProbeRoundTrip());
        }
    }
}
