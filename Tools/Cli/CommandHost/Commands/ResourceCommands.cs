using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.FileServer;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>资源热更验收命令的参数。</summary>
    public sealed class ResourceVerifyArguments
    {
        /// <summary>要下发的资源包目录，也就是某个版本的产物目录。</summary>
        [Summary("要下发的资源包目录，形如 Bundles/StandaloneWindows64/DefaultPackage/1.0.0")]
        public string BundlesDirectory { get; set; }

        /// <summary>出好的客户端可执行文件。</summary>
        [Summary("出好的客户端可执行文件路径")]
        public string PlayerPath { get; set; }

        /// <summary>客户端把验收报告写到哪。</summary>
        [Summary("客户端把验收报告写到哪，缺省时落在资源包目录旁边")]
        public string ReportPath { get; set; }

        /// <summary>等客户端跑完的秒数上限。</summary>
        [Summary("等客户端跑完的秒数上限")]
        [DefaultValue(180)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>
    /// 资源热更验收命令：把一个版本的资源包挂到本地 http 上，起客户端让它真取版本、真下载、真加载，
    /// 再把客户端写出来的报告读回来。代码热更那条链路的对照物。
    /// </summary>
    public static class ResourceVerifyCommand
    {
        private const string ResourceLinePrefix = "资源热更 · ";

        /// <summary>起服务器、跑客户端、读报告。</summary>
        /// <param name="arguments">验收参数。</param>
        [EditorCommand("resource.verify")]
        [Summary("把资源包挂到本地 http 上，起客户端真下载真加载，读回它的验收报告")]
        public static CommandResult Execute(ResourceVerifyArguments arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments.BundlesDirectory) || !Directory.Exists(arguments.BundlesDirectory))
            {
                return CommandResult.Failure(ComposeError(
                    arguments.BundlesDirectory, "资源包目录不存在",
                    "先跑 Template.Toolkit.Editor.YooAssetBundleBuild.BuildFromCommandLine 出一次资源包",
                    "Bundles/StandaloneWindows64/DefaultPackage/1.0.0"));
            }

            if (string.IsNullOrWhiteSpace(arguments.PlayerPath) || !File.Exists(arguments.PlayerPath))
            {
                return CommandResult.Failure(ComposeError(
                    arguments.PlayerPath, "客户端可执行文件不存在",
                    "先跑 Template.Toolkit.Editor.PlayerBuildCommandLine.BuildWindows 出一次包",
                    "UnityProject/Build"));
            }

            var reportPath = string.IsNullOrWhiteSpace(arguments.ReportPath)
                ? Path.Combine(arguments.BundlesDirectory, "资源热更验收报告.txt")
                : arguments.ReportPath;
            var timeoutSeconds = arguments.TimeoutSeconds > 0 ? arguments.TimeoutSeconds : 180;

            if (File.Exists(reportPath))
            {
                File.Delete(reportPath);
            }

            using var server = new LocalDirectoryFileServer(arguments.BundlesDirectory, 0);
            server.Start();

            var playerArguments =
                $"-batchmode -nographics -resourceVerification \"{server.BaseUrl}\" " +
                $"-verificationReport \"{reportPath}\" -logFile \"{reportPath}.log\"";

            var (exitCode, outputLines) = ProcessRunner.Run(
                arguments.PlayerPath, playerArguments, Path.GetDirectoryName(arguments.PlayerPath));

            var lines = new List<string> { $"资源服务器：{server.BaseUrl}（根目录 {arguments.BundlesDirectory}）" };

            if (!File.Exists(reportPath))
            {
                lines.AddRange(outputLines.Take(20));
                return CommandResult.Failure(
                    $"客户端没有写出验收报告（退出码 {exitCode}，超时上限 {timeoutSeconds} 秒），报告应在 {reportPath}", lines);
            }

            var reportLines = File.ReadAllLines(reportPath);
            lines.AddRange(reportLines);

            var resourceLines = reportLines.Where(line => line.StartsWith(ResourceLinePrefix, StringComparison.Ordinal)).ToList();
            if (resourceLines.Count == 0)
            {
                return CommandResult.Failure("客户端的报告里没有资源热更那几行，验收没跑到", lines);
            }

            var failedLines = resourceLines.Where(line => line.Contains("未通过：", StringComparison.Ordinal)).ToList();
            if (failedLines.Count > 0 || exitCode != 0)
            {
                return CommandResult.Failure($"资源热更验收未通过（客户端退出码 {exitCode}，未通过 {failedLines.Count} 行）", lines);
            }

            return CommandResult.Success($"资源热更验收通过：客户端从 {server.BaseUrl} 真下载真加载", lines);
        }

        private static string ComposeError(string location, string reason, string fix, string reference)
        {
            return $"位置：{location}；原因：{reason}；修复：{fix}；参考：{reference}";
        }
    }
}
