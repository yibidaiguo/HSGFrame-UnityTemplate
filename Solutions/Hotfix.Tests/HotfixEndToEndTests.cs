using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HSGFrame.Hotfix;
using Template.Toolkit.Hotfix;
using Xunit;

namespace HSGFrame.Hotfix.Tests
{
    /// <summary>热更端到端测试：用真的本地服务器跑通整条更新链路。</summary>
    public class HotfixEndToEndTests
    {
        [Fact]
        public void Download_TwoPackages_ReportsSuccessAndWritesBoth()
        {
            var serverRoot = CreateTempRoot();
            var localRoot = CreateTempRoot();
            try
            {
                var version = "1.2.0";
                var packageA = Encoding.UTF8.GetBytes("package-a-content");
                var packageB = Encoding.UTF8.GetBytes("package-b-content");
                WriteServerPackage(serverRoot, version, "a.dll", packageA);
                WriteServerPackage(serverRoot, version, "b.dll", packageB);

                var manifest = new HotfixManifest(version, new List<HotfixPackageEntry>
                {
                    new HotfixPackageEntry("a", "a.dll", ComputeSha256Hex(packageA), packageA.Length),
                    new HotfixPackageEntry("b", "b.dll", ComputeSha256Hex(packageB), packageB.Length),
                });

                using var server = new HotfixFileServer(serverRoot, 0);
                server.Start();
                using var httpClient = new HttpClient();

                var downloader = new HotfixDownloader(
                    url => httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult(),
                    new FileSystemHotfixStorage(localRoot));

                var report = downloader.Download(server.BaseUrl, manifest);

                Assert.True(report.IsSuccess);
                Assert.Equal(2, report.DownloadedCount);

                var localStorage = new FileSystemHotfixStorage(localRoot);
                Assert.True(localStorage.HasPackage(version, "a.dll"));
                Assert.True(localStorage.HasPackage(version, "b.dll"));
            }
            finally
            {
                Directory.Delete(serverRoot, recursive: true);
                Directory.Delete(localRoot, recursive: true);
            }
        }

        /// <summary>中文包名要按 URL 转义再拼进地址，否则服务端按转义解回来的路径对不上。</summary>
        [Fact]
        public void Download_ChineseFileName_EscapesUrlSegments()
        {
            var localRoot = CreateTempRoot();
            try
            {
                var version = "1.2.0";
                var content = Encoding.UTF8.GetBytes("中文包内容");
                var manifest = new HotfixManifest(version, new List<HotfixPackageEntry>
                {
                    new HotfixPackageEntry("甲", "热更包_甲.dll", ComputeSha256Hex(content), content.Length),
                });

                var requestedUrls = new List<string>();
                var downloader = new HotfixDownloader(
                    url =>
                    {
                        requestedUrls.Add(url);
                        return content;
                    },
                    new FileSystemHotfixStorage(localRoot));

                var report = downloader.Download("http://127.0.0.1:9/", manifest);

                Assert.True(report.IsSuccess, report.Message);
                Assert.Single(requestedUrls);
                Assert.DoesNotContain("热更包", requestedUrls[0]);
                Assert.Contains("%E7%83%AD%E6%9B%B4%E5%8C%85", requestedUrls[0]);
                Assert.EndsWith(".dll", requestedUrls[0]);
            }
            finally
            {
                Directory.Delete(localRoot, recursive: true);
            }
        }

        [Fact]
        public void Download_LocalHashesMatchManifest()
        {
            var serverRoot = CreateTempRoot();
            var localRoot = CreateTempRoot();
            try
            {
                var version = "1.2.0";
                var content = Encoding.UTF8.GetBytes("hash-check-content");
                WriteServerPackage(serverRoot, version, "a.dll", content);

                var manifest = new HotfixManifest(version, new List<HotfixPackageEntry>
                {
                    new HotfixPackageEntry("a", "a.dll", ComputeSha256Hex(content), content.Length),
                });

                using var server = new HotfixFileServer(serverRoot, 0);
                server.Start();
                using var httpClient = new HttpClient();

                var downloader = new HotfixDownloader(
                    url => httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult(),
                    new FileSystemHotfixStorage(localRoot));

                var report = downloader.Download(server.BaseUrl, manifest);

                Assert.True(report.IsSuccess);
                var localStorage = new FileSystemHotfixStorage(localRoot);
                Assert.Equal(ComputeSha256Hex(content), localStorage.ComputePackageHash(version, "a.dll"));
            }
            finally
            {
                Directory.Delete(serverRoot, recursive: true);
                Directory.Delete(localRoot, recursive: true);
            }
        }

        [Fact]
        public void Download_TamperedHash_FailsWithPackageNameAndHashes()
        {
            var serverRoot = CreateTempRoot();
            var localRoot = CreateTempRoot();
            try
            {
                var version = "1.2.0";
                var content = Encoding.UTF8.GetBytes("tamper-content");
                WriteServerPackage(serverRoot, version, "a.dll", content);
                var goodHash = ComputeSha256Hex(content);

                var manifest = new HotfixManifest(version, new List<HotfixPackageEntry>
                {
                    new HotfixPackageEntry("a", "a.dll", "deadbeef", content.Length),
                });

                using var server = new HotfixFileServer(serverRoot, 0);
                server.Start();
                using var httpClient = new HttpClient();

                var downloader = new HotfixDownloader(
                    url => httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult(),
                    new FileSystemHotfixStorage(localRoot));

                var report = downloader.Download(server.BaseUrl, manifest);

                Assert.False(report.IsSuccess);
                Assert.Contains("a.dll", report.Message);
                Assert.Contains("deadbeef", report.Message);
                Assert.Contains(goodHash, report.Message);

                var localStorage = new FileSystemHotfixStorage(localRoot);
                Assert.False(localStorage.HasPackage(version, "a.dll"));
            }
            finally
            {
                Directory.Delete(serverRoot, recursive: true);
                Directory.Delete(localRoot, recursive: true);
            }
        }

        [Fact]
        public async Task Server_MissingFile_Returns404_WithoutThrowing()
        {
            var serverRoot = CreateTempRoot();
            try
            {
                WriteServerPackage(serverRoot, "1.0.0", "present.dll", Encoding.UTF8.GetBytes("present"));
                using var server = new HotfixFileServer(serverRoot, 0);
                server.Start();
                using var httpClient = new HttpClient();

                var missing = await httpClient.GetAsync(server.BaseUrl + "not-there.dll");
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

                var present = await httpClient.GetAsync(server.BaseUrl + "1.0.0/present.dll");
                Assert.Equal(HttpStatusCode.OK, present.StatusCode);
            }
            finally
            {
                Directory.Delete(serverRoot, recursive: true);
            }
        }

        [Fact]
        public async Task Server_PathTraversal_DoesNotServeFileOutsideRoot()
        {
            var serverRoot = CreateTempRoot();
            var parentRoot = Path.GetDirectoryName(serverRoot);
            var secretName = "secret-" + Guid.NewGuid().ToString("N") + ".txt";
            var secretPath = Path.Combine(parentRoot, secretName);
            File.WriteAllText(secretPath, "top-secret");
            try
            {
                WriteServerPackage(serverRoot, "1.0.0", "normal.dll", Encoding.UTF8.GetBytes("normal"));
                using var server = new HotfixFileServer(serverRoot, 0);
                server.Start();
                var port = new Uri(server.BaseUrl).Port;

                var response = await SendRawRequestAsync(port, "/..%2f" + secretName);

                Assert.True(
                    response.StartsWith("HTTP/1.1 403", StringComparison.OrdinalIgnoreCase)
                        || response.StartsWith("HTTP/1.1 404", StringComparison.OrdinalIgnoreCase),
                    "期望 403 或 404，实际响应：" + response);
                Assert.DoesNotContain("top-secret", response);
            }
            finally
            {
                Directory.Delete(serverRoot, recursive: true);
                File.Delete(secretPath);
            }
        }

        [Fact]
        public void FullChain_DownloadLaunchThenRollback_RestoresPreviousVersion()
        {
            var serverRoot = CreateTempRoot();
            var localRoot = CreateTempRoot();
            try
            {
                var oldVersion = "1.0.0";
                var newVersion = "1.1.0";
                var oldContent = Encoding.UTF8.GetBytes("old");
                var newContent = Encoding.UTF8.GetBytes("new");

                var localStorage = new FileSystemHotfixStorage(localRoot);
                localStorage.WritePackage(oldVersion, "dll.bytes", oldContent);
                localStorage.WriteInstalledVersionText(oldVersion);

                WriteServerPackage(serverRoot, newVersion, "dll.bytes", newContent);
                var manifest = new HotfixManifest(newVersion, new List<HotfixPackageEntry>
                {
                    new HotfixPackageEntry("dll", "dll.bytes", ComputeSha256Hex(newContent), newContent.Length),
                });

                using var server = new HotfixFileServer(serverRoot, 0);
                server.Start();
                using var httpClient = new HttpClient();

                var downloader = new HotfixDownloader(
                    url => httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult(),
                    localStorage);
                var report = downloader.Download(server.BaseUrl, manifest);
                Assert.True(report.IsSuccess);

                var launcher = new HotfixLauncher(localStorage);
                var launchResult = launcher.Launch(manifest);
                Assert.True(launchResult.IsSuccess);
                Assert.Equal(newVersion, localStorage.ReadInstalledVersionText());

                var rollbackResult = launcher.Rollback();
                Assert.True(rollbackResult.IsSuccess);
                Assert.Equal(oldVersion, localStorage.ReadInstalledVersionText());
                Assert.False(localStorage.HasPackage(newVersion, "dll.bytes"));
            }
            finally
            {
                Directory.Delete(serverRoot, recursive: true);
                Directory.Delete(localRoot, recursive: true);
            }
        }

        private static void WriteServerPackage(string serverRoot, string version, string fileName, byte[] content)
        {
            var directory = Path.Combine(serverRoot, version);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, fileName), content);
        }

        private static async Task<string> SendRawRequestAsync(int port, string requestTarget)
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            await using var stream = client.GetStream();
            var request = $"GET {requestTarget} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            return await reader.ReadToEndAsync();
        }

        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "HotfixEndToEndTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string ComputeSha256Hex(byte[] content)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(content);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
