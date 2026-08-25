using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>
    /// <c>/preview/</c> 路由的测试：模型预览页要发得出去，而且**只发得出 _Tasks/preview/ 底下的东西**。
    ///
    /// 越界那两条是这组测试的重点。这条路由是整个面板里唯一一处「按 URL 拼路径去读文件」的地方，
    /// 而仓库里就躺着 Tools/CreationPipeline/Config/local.json（飞书与下游的密钥）。
    /// 少一道根目录比对，一个 ../ 就把它读走了。
    /// </summary>
    public sealed class PreviewRouteTests : IDisposable
    {
        private readonly string _repositoryRoot;

        /// <summary>建一个临时仓库，摆好 _Tasks/preview 与一份「不该被读到」的密钥文件。</summary>
        public PreviewRouteTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "预览路由测试-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_repositoryRoot, "_Tasks", "preview", "REQ-0001"));
            Directory.CreateDirectory(Path.Combine(_repositoryRoot, "Tools", "CreationPipeline", "Config"));
            File.WriteAllText(
                Path.Combine(_repositoryRoot, "Tools", "CreationPipeline", "Config", "local.json"),
                "{\"飞书应用密钥\":\"这是密钥绝不许被发出去\"}",
                new UTF8Encoding(false));
        }

        /// <summary>清掉临时仓库。</summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(_repositoryRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>预览目录下的 HTML 发得出去，且内容类型是 text/html。</summary>
        [Fact]
        public async Task ServesPreviewHtml()
        {
            WritePreview("REQ-0001/viewer.html", "<!doctype html><title>预览</title>");
            using var server = StartServer();
            using var client = new HttpClient();

            var response = await client.GetAsync($"http://localhost:{server.Port}/preview/REQ-0001/viewer.html");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType.MediaType);
            Assert.Contains("预览", body);
        }

        /// <summary>
        /// .glb 的内容类型要给 model/gltf-binary。
        /// 给成 application/octet-stream 时浏览器会去下载而不是交给页面，
        /// 症状是「点开链接弹出一个下载框」——那种故障很难从「预览打不开」这句话反推。
        /// </summary>
        [Fact]
        public async Task ServesGlbWithModelContentType()
        {
            WritePreviewBytes("REQ-0001/M_Box.glb", new byte[] { 0x67, 0x6C, 0x54, 0x46 });
            using var server = StartServer();
            using var client = new HttpClient();

            var response = await client.GetAsync($"http://localhost:{server.Port}/preview/REQ-0001/M_Box.glb");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("model/gltf-binary", response.Content.Headers.ContentType.MediaType);
        }

        /// <summary>
        /// 用 ../ 往上爬读仓库里的密钥文件：发不出去，正文里绝不许出现密钥。
        ///
        /// **断言钉的是「没发出去」，不是某个具体状态码**。实测这一条根本到不了我们的处理函数——
        /// HttpListener 自己就把带 ..%2F 的路径挡掉了（回 403）。那是它的实现细节，
        /// 换个宿主就可能变成 404 甚至放行，所以这里钉「不是 200 且正文里没有密钥」，
        /// 具体挡在哪一层不钉。下面那条负责钉我们自己那一道。
        /// </summary>
        [Fact]
        public async Task RefusesToEscapePreviewRootWithDotDot()
        {
            using var server = StartServer();
            using var client = new HttpClient();

            var response = await client.GetAsync(
                $"http://localhost:{server.Port}/preview/..%2F..%2FTools%2FCreationPipeline%2FConfig%2Flocal.json");
            var body = await response.Content.ReadAsStringAsync();

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("这是密钥绝不许被发出去", body);
        }

        /// <summary>
        /// 用反斜杠往上爬：这一条**能穿过 HttpListener 到我们自己的处理函数**，
        /// 挡住它的是那道「解出来的绝对路径必须在预览根底下」的比对。
        ///
        /// 这条测试存在的意义就是钉住那道比对还在——上面那条钉不住它（请求根本到不了这里）。
        /// </summary>
        [Fact]
        public async Task RefusesToEscapePreviewRootWithBackslashes()
        {
            using var server = StartServer();
            using var client = new HttpClient();

            var response = await client.GetAsync(
                $"http://localhost:{server.Port}/preview/REQ-0001/..%5C..%5C..%5CTools%5CCreationPipeline%5CConfig%5Clocal.json");
            var body = await response.Content.ReadAsStringAsync();

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("这是密钥绝不许被发出去", body);
        }

        /// <summary>预览目录里没有的文件：404，不是 500。</summary>
        [Fact]
        public async Task ReturnsNotFoundForMissingPreviewFile()
        {
            using var server = StartServer();
            using var client = new HttpClient();

            var response = await client.GetAsync($"http://localhost:{server.Port}/preview/REQ-0001/没有这个.html");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        /// <summary>没配仓库根时这条路由一律 404：没有仓库根就无从谈起「预览目录在哪」。</summary>
        [Fact]
        public async Task ReturnsNotFoundWithoutRepositoryRoot()
        {
            using var server = new DashboardServer(new LogEventChannel(), 0);
            server.Start();
            using var client = new HttpClient();

            var response = await client.GetAsync($"http://localhost:{server.Port}/preview/whatever.html");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            server.Dispose();
        }

        private DashboardServer StartServer()
        {
            var server = new DashboardServer(
                new LogEventChannel(),
                0,
                _repositoryRoot,
                Path.Combine(_repositoryRoot, "Pools"),
                null);
            server.Start();
            return server;
        }

        private void WritePreview(string relativePath, string content)
        {
            var path = Path.Combine(_repositoryRoot, "_Tasks", "preview", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private void WritePreviewBytes(string relativePath, byte[] bytes)
        {
            var path = Path.Combine(_repositoryRoot, "_Tasks", "preview", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
        }
    }
}
