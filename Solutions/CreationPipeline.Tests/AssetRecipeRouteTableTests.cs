using System;
using System.IO;
using System.Text;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 配方路由表：一条可以只写文生图（一个字符串），也可以文生图与图生图各写一份（一个对象）。
    /// 钉的重点是**带参考图时不许退回文生图那份**——退回去参考图会被悄悄丢掉。
    /// </summary>
    public sealed class AssetRecipeRouteTableTests
    {
        /// <summary>老写法（一个字符串）照旧当文生图读。</summary>
        [Fact]
        public void PlainStringRouteIsReadAsTextToImage()
        {
            using var workspace = new Workspace();
            WriteRoutes(workspace.Root, @"{ ""图标"": ""icon@v1"" }");

            var table = AssetRecipeRouteTable.Load(workspace.Root);

            Assert.True(table.TryResolve("oaiimage", "图标", out var recipe, out _));
            Assert.Equal("icon@v1", recipe);
        }

        /// <summary>写成对象时，有没有参考图各取各的那一份。</summary>
        [Fact]
        public void ReferenceImagePicksTheImageToImageRecipe()
        {
            using var workspace = new Workspace();
            WriteRoutes(workspace.Root, @"{ ""界面底图"": { ""文生图"": ""ui@v1"", ""图生图"": ""ui-edit@v1"" } }");

            var table = AssetRecipeRouteTable.Load(workspace.Root);

            Assert.True(table.TryResolve("oaiimage", "界面底图", withReferenceImage: false, out var textToImage, out _));
            Assert.Equal("ui@v1", textToImage);
            Assert.True(table.TryResolve("oaiimage", "界面底图", withReferenceImage: true, out var imageToImage, out _));
            Assert.Equal("ui-edit@v1", imageToImage);
        }

        /// <summary>
        /// 只配了文生图、人却给了参考图：**报错，不退回文生图那份**。
        /// 退回去的话图照出、钱照花，跟他给的那张却没关系，而他只会觉得模型不听话。
        /// 报错那句话要说清该去补什么。
        /// </summary>
        [Fact]
        public void MissingImageToImageRecipeFailsInsteadOfFallingBack()
        {
            using var workspace = new Workspace();
            WriteRoutes(workspace.Root, @"{ ""界面底图"": ""ui@v1"" }");

            var table = AssetRecipeRouteTable.Load(workspace.Root);

            Assert.False(table.TryResolve("oaiimage", "界面底图", withReferenceImage: true, out var recipe, out var reason));
            Assert.Equal("", recipe);
            Assert.Contains("图生图", reason);
            Assert.Contains("asset-recipe.json", reason);
        }

        /// <summary>只配了图生图、这次没参考图：一样报错，不拿图生图那份硬跑。</summary>
        [Fact]
        public void MissingTextToImageRecipeFails()
        {
            using var workspace = new Workspace();
            WriteRoutes(workspace.Root, @"{ ""界面底图"": { ""图生图"": ""ui-edit@v1"" } }");

            var table = AssetRecipeRouteTable.Load(workspace.Root);

            Assert.False(table.TryResolve("oaiimage", "界面底图", out _, out var reason));
            Assert.Contains("文生图", reason);
        }

        /// <summary>写路由表文件。</summary>
        /// <param name="repositoryRoot">临时仓库根。</param>
        /// <param name="oaiimageSection">oaiimage 那一节的 JSON 对象文本。</param>
        private static void WriteRoutes(string repositoryRoot, string oaiimageSection)
        {
            var path = AssetRecipeRouteTable.RouteFile(repositoryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var content = @"{ ""配方路由"": { ""oaiimage"": " + oaiimageSection + " } }";
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "配方路由测试-" + Guid.NewGuid().ToString("N"));
            }

            public string Root { get; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
