using System.IO;
using UnityEngine;

namespace HSGFrame.Hotfix.Editor
{
    /// <summary>
    /// 从工程位置反推模板根目录，热更打包产物的默认落点由它推出来。
    /// </summary>
    /// <remarks>
    /// 这十几行与 <c>Toolkit.Editor</c> 里的 <c>TemplateRootLocator</c> 重复，是刻意的：
    /// 可选包一旦引用常驻的编辑器程序集，「摘掉包 = 零影响」就会反过来变成
    /// 「常驻那边改个名字 = 包编不过」。宁可重复这十几行，也不要那条反向依赖。
    /// </remarks>
    public static class HotfixOutputRootLocator
    {
        /// <summary>从 Application.dataPath 逐级向上找带 Tools/Gates/Config/gate-config.json 的那一级，找不到返回 null。</summary>
        public static string Find()
        {
            // 用 gate-config.json 这个标记文件认模板根，而不是按目录名认：
            // 模板被复制成别的项目名之后这个标记仍然成立，而目录名不再成立。
            var directory = new DirectoryInfo(Application.dataPath);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Tools", "Gates", "Config", "gate-config.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}
