using System;
using System.IO;
using System.Reflection;
using HSGFrame.GraphAdapter;
using Xunit;

namespace HSGFrame.GraphAdapter.Tests
{
    /// <summary>锁定兼容投影与正式资产创作 API 的边界。</summary>
    public class AuthoringBoundaryTests
    {
        [Fact]
        public void CompatibilityJsonCodecDirectsAuthorsToCanonicalAssetApi()
        {
            var codec = typeof(GraphDocument).Assembly.GetType(
                "HSGFrame.GraphAdapter.GraphJsonCodec",
                throwOnError: true);
            var obsolete = codec.GetCustomAttribute<ObsoleteAttribute>();

            Assert.NotNull(obsolete);
            Assert.False(obsolete.IsError);
            Assert.Contains("lossy compatibility projection", obsolete.Message);
            Assert.Contains("GraphAuthoringAssetAccess", obsolete.Message);
            Assert.Contains("NodeGraphAsset/BlackboardAsset as the sole source", obsolete.Message);
        }

        [Fact]
        public void RuntimeAssemblyDefinitionHasNoEditorDependency()
        {
            var root = FindTemplateRoot();
            var asmdefPath = Path.Combine(
                root,
                "Packages",
                "com.hsgframe.graphadapter",
                "Runtime",
                "HSGFrame.GraphAdapter.asmdef");

            var asmdef = File.ReadAllText(asmdefPath);

            Assert.DoesNotContain("NodeEditor.Editor", asmdef);
            Assert.Contains("NodeEditor.Runtime", asmdef);
        }

        private static string FindTemplateRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                && !File.Exists(Path.Combine(directory.FullName, "Tools", "Gates", "Config", "gate-config.json")))
            {
                directory = directory.Parent;
            }

            return directory == null
                ? throw new InvalidOperationException("找不到仓库根目录")
                : directory.FullName;
        }
    }
}
