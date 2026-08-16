using System.Collections.Generic;

namespace Template.Boot
{
    /// <summary>
    /// 出包验收里的一项检查。各项自己挂进 <see cref="BuildVerificationRegistry"/>，
    /// 验收入口只按表跑、不认识任何一项的实现——可选功能（比如热更）因此能整块摘掉而不动入口。
    /// </summary>
    public interface IBuildVerification
    {
        /// <summary>这一项的名字，只用来在它抛异常时指出是谁抛的。</summary>
        string Name { get; }

        /// <summary>报告里的排序键，小的先跑；相同时按挂进来的先后。</summary>
        int Order { get; }

        /// <summary>跑这一项，把结论逐行追加进 reportLines。</summary>
        /// <param name="reportLines">全局结论收集器，只许追加，不许清空或改别人写过的行。</param>
        void Collect(List<string> reportLines);
    }
}
