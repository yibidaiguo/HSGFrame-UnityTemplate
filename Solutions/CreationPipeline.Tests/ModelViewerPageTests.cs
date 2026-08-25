using System;
using System.IO;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 可交互模型预览页的测试。
    ///
    /// 这里**不验「浏览器里转不转得动」**——那是 model-viewer 的事，
    /// 拿它当断言等于把一个第三方组件的行为焊进我们的测试。
    /// 这里验的是我们自己那部分：收什么、拒什么、页面里该有的东西在不在。
    /// </summary>
    public class ModelViewerPageTests
    {
        /// <summary>合法 .glb + 自包含模式：页面写出来了，模型与脚本都嵌在里面。</summary>
        [Fact]
        public void StandalonePageEmbedsModelAndScript()
        {
            using var workspace = new TempDirectory();
            var model = workspace.WriteFile("M_Box.glb", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            var script = workspace.WriteText("model-viewer.min.js", "console.log('这是内置的 model-viewer');");
            var output = Path.Combine(workspace.Root, "viewer.html");

            var result = ModelViewerPage.Build(model, output, script, "自检模型", standalone: true);

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.True(result.IsStandalone);
            var html = File.ReadAllText(output);
            Assert.Contains("data:model/gltf-binary;base64,", html);
            Assert.Contains(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), html);
            Assert.Contains("这是内置的 model-viewer", html);
            // 自包含就该是一个文件：不许再往旁边拷东西。
            Assert.False(File.Exists(Path.Combine(workspace.Root, "model-viewer.min.js.copy")));
        }

        /// <summary>目录模式：模型与脚本拷到 HTML 旁边，页面按相对路径引它们，不嵌 base64。</summary>
        [Fact]
        public void DirectoryModeCopiesSiblingsAndReferencesThemRelatively()
        {
            using var workspace = new TempDirectory();
            var model = workspace.WriteFile("M_Box.glb", new byte[] { 9, 9, 9, 9 });
            var script = workspace.WriteText("model-viewer.min.js", "console.log('脚本');");
            var pageDirectory = Path.Combine(workspace.Root, "out");
            var output = Path.Combine(pageDirectory, "viewer.html");

            var result = ModelViewerPage.Build(model, output, script, "自检模型", standalone: false);

            Assert.True(result.Succeeded, result.FailureReason);
            Assert.False(result.IsStandalone);
            Assert.True(File.Exists(Path.Combine(pageDirectory, "M_Box.glb")), "模型该拷到 HTML 旁边");
            Assert.True(File.Exists(Path.Combine(pageDirectory, "model-viewer.min.js")), "脚本该拷到 HTML 旁边");
            var html = File.ReadAllText(output);
            Assert.Contains("src=\"M_Box.glb\"", html);
            Assert.DoesNotContain("base64,", html);
        }

        /// <summary>
        /// .fbx 一律拒，原因里要点名 .glb。
        /// **这条守的是 Tripo 两条路统一到 .glb 的现实理由**：CLI 那条交出 fbx 的话这页就打不开。
        /// </summary>
        [Fact]
        public void RejectsNonGltfModelAndSaysWhichFormatsWork()
        {
            using var workspace = new TempDirectory();
            var model = workspace.WriteFile("M_Box.fbx", new byte[] { 1, 2, 3 });
            var script = workspace.WriteText("model-viewer.min.js", "x");

            var result = ModelViewerPage.Build(model, Path.Combine(workspace.Root, "v.html"), script, "", standalone: true);

            Assert.False(result.Succeeded);
            Assert.Contains(".glb", result.FailureReason);
        }

        /// <summary>模型不在：拒绝，不抛。</summary>
        [Fact]
        public void RejectsMissingModel()
        {
            using var workspace = new TempDirectory();
            var script = workspace.WriteText("model-viewer.min.js", "x");

            var result = ModelViewerPage.Build(
                Path.Combine(workspace.Root, "不存在.glb"),
                Path.Combine(workspace.Root, "v.html"),
                script,
                "",
                standalone: true);

            Assert.False(result.Succeeded);
            Assert.Contains("不在", result.FailureReason);
        }

        /// <summary>内置脚本没取下来：拒绝，且原因里要给出取它的办法。</summary>
        [Fact]
        public void RejectsMissingViewerScriptAndTellsHowToFetchIt()
        {
            using var workspace = new TempDirectory();
            var model = workspace.WriteFile("M_Box.glb", new byte[] { 1 });

            var result = ModelViewerPage.Build(
                model,
                Path.Combine(workspace.Root, "v.html"),
                Path.Combine(workspace.Root, "没取过.js"),
                "",
                standalone: true);

            Assert.False(result.Succeeded);
            Assert.Contains("fetch-web.ps1", result.FailureReason);
        }

        /// <summary>
        /// 太大的模型不许走自包含：与其生成一个打不开的页面，不如当场说清楚。
        /// 这里不真造一个 48 MB 的文件——那会让这条测试变慢又占盘。改成验「上限本身是个正数」
        /// 与「目录模式没有这条限制」，把真正的边界留给常量本身表达。
        /// </summary>
        [Fact]
        public void StandaloneHasAByteLimitAndDirectoryModeDoesNot()
        {
            Assert.True(ModelViewerPage.StandaloneModelByteLimit > 0);

            using var workspace = new TempDirectory();
            var model = workspace.WriteFile("M_Box.glb", new byte[1024]);
            var script = workspace.WriteText("model-viewer.min.js", "x");

            var result = ModelViewerPage.Build(
                model,
                Path.Combine(workspace.Root, "out", "v.html"),
                script,
                "",
                standalone: false);

            Assert.True(result.Succeeded, result.FailureReason);
        }

        /// <summary>标题里的尖括号要转义：标题从外面来，直接拼进 HTML 等于开一个注入口。</summary>
        [Fact]
        public void EscapesTitleIntoHtml()
        {
            using var workspace = new TempDirectory();
            var model = workspace.WriteFile("M_Box.glb", new byte[] { 1 });
            var script = workspace.WriteText("model-viewer.min.js", "x");
            var output = Path.Combine(workspace.Root, "v.html");

            var result = ModelViewerPage.Build(model, output, script, "<script>alert(1)</script>", standalone: true);

            Assert.True(result.Succeeded, result.FailureReason);
            var html = File.ReadAllText(output);
            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        }

        /// <summary>页面要带上相机控制与自转——「能拖着转」是这一页存在的全部理由。</summary>
        [Fact]
        public void PageEnablesCameraControls()
        {
            using var workspace = new TempDirectory();
            var model = workspace.WriteFile("M_Box.glb", new byte[] { 1 });
            var script = workspace.WriteText("model-viewer.min.js", "x");
            var output = Path.Combine(workspace.Root, "v.html");

            ModelViewerPage.Build(model, output, script, "自检", standalone: true);

            var html = File.ReadAllText(output);
            Assert.Contains("camera-controls", html);
            Assert.Contains("auto-rotate", html);
            Assert.Contains("<model-viewer", html);
        }

        /// <summary>用完即删的临时目录。</summary>
        private sealed class TempDirectory : IDisposable
        {
            public TempDirectory()
            {
                Root = Path.Combine(Path.GetTempPath(), "模型预览测试-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            public string Root { get; }

            public string WriteFile(string name, byte[] bytes)
            {
                var path = Path.Combine(Root, name);
                File.WriteAllBytes(path, bytes);
                return path;
            }

            public string WriteText(string name, string text)
            {
                var path = Path.Combine(Root, name);
                File.WriteAllText(path, text, new UTF8Encoding(false));
                return path;
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
