using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using GameTemplateForAgent.Hotfix;
using Template.Toolkit.CommandFramework;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>热更清单生成命令的参数。</summary>
    public sealed class HotfixManifestArguments
    {
        /// <summary>要扫描的包目录，逐文件算 SHA256。</summary>
        [Summary("要扫描的包目录，逐文件算 SHA256")]
        public string PackageDirectory { get; set; }

        /// <summary>清单版本号，形如 1.2.3。</summary>
        [Summary("清单版本号，形如 1.2.3")]
        public string VersionText { get; set; }

        /// <summary>清单 JSON 的输出路径。</summary>
        [Summary("清单 JSON 的输出路径")]
        public string OutputPath { get; set; }
    }

    /// <summary>热更更新命令的参数。</summary>
    public sealed class HotfixUpdateArguments
    {
        /// <summary>包与清单所在的服务器基地址，形如 http://127.0.0.1:8123。</summary>
        [Summary("包与清单所在的服务器基地址，形如 http://127.0.0.1:8123")]
        public string BaseUrl { get; set; }

        /// <summary>本地热更根目录，包与已装版本号都写在这里。</summary>
        [Summary("本地热更根目录，包与已装版本号都写在这里")]
        public string LocalRoot { get; set; }

        /// <summary>清单文件名，相对 BaseUrl 取。</summary>
        [Summary("清单文件名，相对 BaseUrl 取")]
        [DefaultValue(DefaultManifestFileName)]
        public string ManifestPath { get; set; }

        /// <summary>清单文件的默认文件名。</summary>
        public const string DefaultManifestFileName = "热更清单.json";
    }

    /// <summary>热更回滚命令的参数。</summary>
    public sealed class HotfixRollbackArguments
    {
        /// <summary>本地热更根目录。</summary>
        [Summary("本地热更根目录")]
        public string LocalRoot { get; set; }
    }

    /// <summary>热更状态命令的参数。</summary>
    public sealed class HotfixStatusArguments
    {
        /// <summary>本地热更根目录。</summary>
        [Summary("本地热更根目录")]
        public string LocalRoot { get; set; }
    }

    /// <summary>热更四条命令：扫包出清单、拉取更新、回滚、看状态。</summary>
    public static class HotfixCommands
    {
        /// <summary>扫一个包目录生成清单 JSON。</summary>
        [EditorCommand("hotfix.manifest")]
        [Summary("扫一个包目录生成清单 JSON")]
        public static CommandResult Manifest(HotfixManifestArguments arguments)
        {
            if (!Directory.Exists(arguments.PackageDirectory))
            {
                return CommandResult.Failure(HotfixCommandSupport.ComposeError(
                    arguments.PackageDirectory, "包目录不存在", "确认包目录路径正确", "Build/HotfixPackages"));
            }

            if (!HotfixVersion.TryParse(arguments.VersionText, out _))
            {
                return CommandResult.Failure(HotfixCommandSupport.ComposeError(
                    arguments.VersionText, "版本号形状不是 1.2.3", "把版本号改成三段数字", "1.2.3"));
            }

            var entries = new List<HotfixPackageEntry>();
            foreach (var filePath in Directory.GetFiles(arguments.PackageDirectory))
            {
                var content = File.ReadAllBytes(filePath);
                entries.Add(new HotfixPackageEntry(
                    Path.GetFileNameWithoutExtension(filePath),
                    Path.GetFileName(filePath),
                    ComputeSha256Hex(content),
                    content.LongLength));
            }

            var manifest = new HotfixManifest(arguments.VersionText, entries);
            File.WriteAllText(arguments.OutputPath, HotfixManifestCodec.ToJson(manifest));
            return CommandResult.Success($"清单已生成：{arguments.OutputPath}（包条目 {entries.Count} 个）");
        }

        /// <summary>从服务器拉清单与包，校验后写入本地并更新已装版本。</summary>
        [EditorCommand("hotfix.update")]
        [Summary("从服务器拉清单与包，校验后写入本地并更新已装版本")]
        public static CommandResult Update(HotfixUpdateArguments arguments)
        {
            var storage = new FileSystemHotfixStorage(arguments.LocalRoot);
            using var httpClient = new HttpClient();

            // [DefaultValue] 只告诉命令框架「这个参数选填」，并不会把默认值填进实例：
            // 参数 JSON 里没写这一项时，这里拿到的是 null，拼出来的清单地址会退化成基地址本身。
            var manifestPath = string.IsNullOrWhiteSpace(arguments.ManifestPath)
                ? HotfixUpdateArguments.DefaultManifestFileName
                : arguments.ManifestPath;

            var manifestUrl = arguments.BaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(manifestPath);
            string manifestJson;
            try
            {
                manifestJson = httpClient.GetStringAsync(manifestUrl).GetAwaiter().GetResult();
            }
            catch (HttpRequestException exception)
            {
                return CommandResult.Failure(HotfixCommandSupport.ComposeError(
                    manifestUrl, "取清单失败", "核对服务器地址与清单文件名", exception.Message));
            }

            HotfixManifest manifest;
            try
            {
                manifest = HotfixManifestCodec.FromJson(manifestJson);
            }
            catch (HotfixManifestException exception)
            {
                return CommandResult.Failure(HotfixCommandSupport.ComposeError(
                    manifestUrl, "清单解析失败", "核对清单 JSON 格式", exception.Message));
            }

            Func<string, byte[]> fetchBytes = url => httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult();
            var downloader = new HotfixDownloader(fetchBytes, storage);
            var report = downloader.Download(arguments.BaseUrl, manifest);
            if (!report.IsSuccess)
            {
                return CommandResult.Failure(report.Message);
            }

            var launcher = new HotfixLauncher(storage);
            var result = launcher.Launch(manifest);
            if (!result.IsSuccess)
            {
                return CommandResult.Failure(result.Message);
            }

            return result.NeedsUpdate
                ? CommandResult.Success($"更新完成，版本 {manifest.VersionText}")
                : CommandResult.Success("已是最新");
        }

        /// <summary>回滚到上一个历史版本。</summary>
        [EditorCommand("hotfix.rollback")]
        [Summary("回滚到上一个历史版本")]
        public static CommandResult Rollback(HotfixRollbackArguments arguments)
        {
            var storage = new FileSystemHotfixStorage(arguments.LocalRoot);
            var result = new HotfixLauncher(storage).Rollback();
            return result.IsSuccess ? CommandResult.Success(result.Message) : CommandResult.Failure(result.Message);
        }

        /// <summary>报告本地已装版本与全部历史版本。</summary>
        [EditorCommand("hotfix.status")]
        [Summary("报告本地已装版本与全部历史版本")]
        public static CommandResult Status(HotfixStatusArguments arguments)
        {
            var storage = new FileSystemHotfixStorage(arguments.LocalRoot);
            var installed = storage.ReadInstalledVersionText();
            var versions = storage.ListInstalledVersions();

            var lines = new List<string>
            {
                "已装版本：" + (string.IsNullOrEmpty(installed) ? "（无）" : installed),
                "历史版本：" + (versions.Count == 0 ? "（无）" : string.Join("、", versions)),
            };

            return CommandResult.Success($"已装版本 {installed}，历史版本 {versions.Count} 个", lines);
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

    /// <summary>热更命令共用的失败消息拼接。</summary>
    internal static class HotfixCommandSupport
    {
        /// <summary>把四要素拼成一条失败消息。</summary>
        public static string ComposeError(string location, string reason, string fix, string reference)
        {
            return $"位置：{location}；原因：{reason}；修复：{fix}；参考：{reference}";
        }
    }
}
