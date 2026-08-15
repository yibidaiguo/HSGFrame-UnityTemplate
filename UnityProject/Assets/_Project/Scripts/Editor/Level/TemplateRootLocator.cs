using System.IO;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>从工程位置反推模板根目录，让编辑器侧代码与仓库里的目录深度解耦。</summary>
    public static class TemplateRootLocator
    {
        /// <summary>从 Application.dataPath 逐级向上找带 Tools/Gates/Config/gate-config.json 的那一级，找不到返回 null。</summary>
        public static string Find()
        {
            // 用 gate-config.json 这个标记文件认模板根，而不是按目录名认：
            // 模板被复制成别的项目名之后这个标记仍然成立，而目录名 "Template" 或某个宿主项目名不再成立。
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
