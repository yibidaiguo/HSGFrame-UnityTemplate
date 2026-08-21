using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>生图 driver 的配方与依赖清单路径拼装：配方目录、workflow、映射与依赖清单，全部以仓库根为起点，driver 名一律是参数。</summary>
    public static class RecipePaths
    {
        /// <summary>某 driver 的配方根目录：Bridges/&lt;driver&gt;/配方。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string RecipeRootDirectory(string repositoryRoot, string driverName)
        {
            return Path.Combine(repositoryRoot, "Bridges", driverName, "recipes");
        }

        /// <summary>某配方的目录：配方根目录/&lt;配方名&gt;。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="recipeName">配方名，如「图标@v5」。</param>
        public static string RecipeDirectory(string repositoryRoot, string driverName, string recipeName)
        {
            return Path.Combine(RecipeRootDirectory(repositoryRoot, driverName), recipeName);
        }

        /// <summary>某配方的 workflow 文件：配方目录/workflow.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="recipeName">配方名，如「图标@v5」。</param>
        public static string WorkflowFile(string repositoryRoot, string driverName, string recipeName)
        {
            return Path.Combine(RecipeDirectory(repositoryRoot, driverName, recipeName), "workflow.json");
        }

        /// <summary>某配方的映射文件：配方目录/mapping.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="recipeName">配方名，如「图标@v5」。</param>
        public static string MappingFile(string repositoryRoot, string driverName, string recipeName)
        {
            return Path.Combine(RecipeDirectory(repositoryRoot, driverName, recipeName), "mapping.json");
        }

        /// <summary>某 driver 的依赖清单文件：Bridges/&lt;driver&gt;/dependencies.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string DependencyManifestFile(string repositoryRoot, string driverName)
        {
            return Path.Combine(repositoryRoot, "Bridges", driverName, "dependencies.json");
        }
    }
}
