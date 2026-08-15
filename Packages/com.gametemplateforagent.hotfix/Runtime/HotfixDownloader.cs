using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameTemplateForAgent.Hotfix
{
    /// <summary>热更下载器：按清单逐包取字节、校验哈希、落盘。取字节的方式由调用方注入，本包与传输实现解耦。</summary>
    public sealed class HotfixDownloader
    {
        private readonly Func<string, byte[]> _fetchBytes;
        private readonly IHotfixPackageWriter _packageWriter;

        /// <summary>以取字节的委托与包写入器构造。</summary>
        /// <param name="fetchBytes">按 URL 取回字节，取不到时由它自己抛异常。</param>
        /// <param name="packageWriter">把校验通过的包写到本地。</param>
        public HotfixDownloader(Func<string, byte[]> fetchBytes, IHotfixPackageWriter packageWriter)
        {
            _fetchBytes = fetchBytes ?? throw new ArgumentNullException(nameof(fetchBytes));
            _packageWriter = packageWriter ?? throw new ArgumentNullException(nameof(packageWriter));
        }

        /// <summary>按清单把该版本的全部包取回本地，返回一份逐包结论。</summary>
        /// <param name="baseUrl">包所在的基地址，逐包拼成 baseUrl/版本号/文件名。</param>
        /// <param name="manifest">远端清单。</param>
        public HotfixDownloadReport Download(string baseUrl, HotfixManifest manifest)
        {
            var trimmedBaseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            var downloadedCount = 0;

            foreach (var package in manifest.Packages)
            {
                // 版本号与文件名要各自转义再拼进 URL：中文包名不转义时服务端收到的是
                // 原始字节，取不到包。服务端那一侧本来就 UnescapeDataString 过，两边才对得上。
                var url = $"{trimmedBaseUrl}/{Uri.EscapeDataString(manifest.VersionText)}"
                    + $"/{Uri.EscapeDataString(package.FileName)}";

                byte[] content;
                try
                {
                    content = _fetchBytes(url);
                }
                catch (IOException exception)
                {
                    return new HotfixDownloadReport(false, downloadedCount,
                        $"位置：{url}；原因：取包字节失败；修复：核对服务器地址与包路径；参考：{exception.Message}");
                }
                catch (InvalidOperationException exception)
                {
                    return new HotfixDownloadReport(false, downloadedCount,
                        $"位置：{url}；原因：取包字节失败；修复：核对服务器地址与包路径；参考：{exception.Message}");
                }

                var actualHash = ComputeSha256Hex(content);
                if (!string.Equals(actualHash, package.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    return new HotfixDownloadReport(false, downloadedCount,
                        $"位置：{package.FileName}；原因：内容哈希不匹配；修复：核对远端包与清单是否一致；参考：期望 {package.ContentHash}，实际 {actualHash}");
                }

                _packageWriter.WritePackage(manifest.VersionText, package.FileName, content);
                downloadedCount++;
            }

            return new HotfixDownloadReport(true, downloadedCount, $"已下载 {downloadedCount} 个包");
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

    /// <summary>一次下载的结论：成功与否、成功包数、失败原因。</summary>
    public sealed class HotfixDownloadReport
    {
        /// <summary>本次下载是否全部成功。</summary>
        public bool IsSuccess { get; }

        /// <summary>成功落盘的包数。</summary>
        public int DownloadedCount { get; }

        /// <summary>结论消息，失败时说明原因。</summary>
        public string Message { get; }

        /// <summary>以成功与否、成功包数与消息构造。</summary>
        public HotfixDownloadReport(bool isSuccess, int downloadedCount, string message)
        {
            IsSuccess = isSuccess;
            DownloadedCount = downloadedCount;
            Message = message;
        }
    }
}
