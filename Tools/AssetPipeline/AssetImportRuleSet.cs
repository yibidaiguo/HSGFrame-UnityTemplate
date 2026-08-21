using System;
using System.IO;

namespace Template.Toolkit.AssetPipeline
{
    /// <summary>按目录逐级向上查找并加载资产导入规则。</summary>
    public static class AssetImportRuleSet
    {
        /// <summary>从目录开始逐级向上找「import-rules.json」，找到即返回；找到文件系统根仍未找到则返回 null。</summary>
        /// <param name="directoryPath">起始目录。</param>
        public static AssetImportRule LoadForDirectory(string directoryPath)
        {
            return LoadForDirectory(directoryPath, scanRoot: null);
        }

        /// <summary>从目录开始逐级向上找「import-rules.json」，找到即返回；到扫描根（含自身）仍未找到则返回 null。</summary>
        /// <param name="directoryPath">起始目录。</param>
        /// <param name="scanRoot">向上查找的边界目录；为 null 或空时查到文件系统根。</param>
        public static AssetImportRule LoadForDirectory(string directoryPath, string scanRoot)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return null;
            }

            var currentDirectory = Path.GetFullPath(directoryPath);
            var boundaryDirectory = string.IsNullOrWhiteSpace(scanRoot) ? null : Path.GetFullPath(scanRoot);

            while (true)
            {
                var candidatePath = Path.Combine(currentDirectory, "import-rules.json");
                if (File.Exists(candidatePath))
                {
                    return AssetImportRule.LoadFromFile(candidatePath);
                }

                if (boundaryDirectory != null && PathsEqual(currentDirectory, boundaryDirectory))
                {
                    return null;
                }

                var parentDirectory = Directory.GetParent(currentDirectory);
                if (parentDirectory == null)
                {
                    return null;
                }

                currentDirectory = parentDirectory.FullName;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
