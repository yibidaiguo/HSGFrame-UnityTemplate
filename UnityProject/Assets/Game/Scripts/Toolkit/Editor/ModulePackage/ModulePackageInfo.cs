using System.Collections.Generic;

namespace Template.Toolkit.Editor
{
    /// <summary>
    /// 一个框架模块包的现状：盘上在不在、清单里装没装、它自己又声明了哪些依赖。
    /// 「装」的定义只有一个——<c>UnityProject/Packages/manifest.json</c> 的 dependencies 里有没有它的键；
    /// 包目录本身一直躺在 <c>Packages/</c> 下，卸载不删它，所以装回来永远是可能的。
    /// </summary>
    public sealed class ModulePackageInfo
    {
        /// <summary>包名，例如 com.hsgframe.timer；清单里的键就是它。</summary>
        public string PackageName { get; set; }

        /// <summary>显示名，取自 package.json 的 displayName；缺了就退回包名。</summary>
        public string DisplayName { get; set; }

        /// <summary>一句话说明，取自 package.json 的 description。</summary>
        public string Description { get; set; }

        /// <summary>版本号，取自 package.json 的 version。</summary>
        public string Version { get; set; }

        /// <summary>包目录的模板根相对路径、正斜杠；盘上没有这个包时为 null。</summary>
        public string DirectoryRelativePath { get; set; }

        /// <summary>盘上有没有这个包目录。</summary>
        public bool IsOnDisk { get; set; }

        /// <summary>清单的 dependencies 里有没有它。</summary>
        public bool IsInstalled { get; set; }

        /// <summary>装它时往清单里写的值，例如 <c>file:../../Packages/com.hsgframe.timer</c>。</summary>
        public string InstallExpression { get; set; }

        /// <summary>它在自己的 package.json 里声明的依赖。</summary>
        public IReadOnlyList<ModulePackageDependency> Dependencies { get; set; }
    }

    /// <summary>package.json 的 dependencies 里的一条依赖：依赖谁、要哪个版本。</summary>
    public sealed class ModulePackageDependency
    {
        /// <summary>被依赖的包名。</summary>
        public string PackageName { get; set; }

        /// <summary>版本表达式，可能是版本号，也可能是 git 地址。</summary>
        public string VersionExpression { get; set; }
    }
}
