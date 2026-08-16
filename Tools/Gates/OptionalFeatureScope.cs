using System.Collections.Generic;

namespace Template.Toolkit.Gates
{
    /// <summary>
    /// 一条可选功能的引用范围规则：这批程序集只有该功能包目录内的 asmdef 才许引用。
    /// 引用范围就是「可选」的定义——包外冒出一处引用，这个功能就摘不干净了。
    /// </summary>
    public sealed class OptionalFeatureScope
    {
        /// <summary>功能名，只用来出现在失败消息里，例如 hotfix。</summary>
        public string FeatureName { get; set; }

        /// <summary>该功能的包目录，模板根相对、正斜杠，例如 Packages/com.hsgframe.hotfix。</summary>
        public string PackageDirectory { get; set; }

        /// <summary>该功能独占的程序集名前缀；引用名等于前缀、或以「前缀.」开头都算命中。</summary>
        public IReadOnlyList<string> ReferencePrefixes { get; set; }
    }
}
