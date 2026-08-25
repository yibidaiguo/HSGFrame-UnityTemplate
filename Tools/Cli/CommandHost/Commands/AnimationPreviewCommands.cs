using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>预览图命令 anim.preview 的参数。</summary>
    public sealed class AnimationPreviewArguments
    {
        /// <summary>帧目录：里面的 PNG 按文件名序合成动图。给了模型就不用给它。</summary>
        [Summary("帧目录：里面的 PNG 按文件名序合成动图；给了 ModelPath 就不用给它")]
        [DefaultValue("")]
        public string FrameDirectory { get; set; }

        /// <summary>模型文件：给了就先渲一圈转台再合。2D 那两条路不用给。</summary>
        [Summary("模型文件（.glb/.fbx/.obj）：给了就先渲一圈转台再合成动图")]
        [DefaultValue("")]
        public string ModelPath { get; set; }

        /// <summary>动图落点；留空落在帧目录（或模型旁）的 preview.gif。</summary>
        [Summary("动图落点；留空落在帧目录旁的 preview.gif")]
        [DefaultValue("")]
        public string OutputPath { get; set; }

        /// <summary>帧率，帧每秒。</summary>
        [Summary("帧率，帧每秒")]
        [DefaultValue(12)]
        public int FrameRate { get; set; }

        /// <summary>转台渲几帧（只在给了模型时用）。</summary>
        [Summary("转台渲几帧（只在给了 ModelPath 时用）")]
        [DefaultValue(24)]
        public int TurntableFrameCount { get; set; }

        /// <summary>转台画面边长（只在给了模型时用）。</summary>
        [Summary("转台画面边长（只在给了 ModelPath 时用）")]
        [DefaultValue(512)]
        public int SideLength { get; set; }

        /// <summary>转台模式：环绕 / 自带动画。</summary>
        [Summary("转台模式：环绕（绕着转一圈）/ 自带动画（播模型自己的动作）")]
        [DefaultValue("环绕")]
        public string TurntableMode { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>单次下游调用超时秒数。</summary>
        [Summary("单次下游调用超时秒数")]
        [DefaultValue(900)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>
    /// 把动画/模型渲成一张**能转的图**，给人扫一眼看方向对不对。
    ///
    /// 这一步是「产物两步走，中间留人审」里的**中间那一步**：
    /// 逐帧 PNG 摆在目录里人是不会一张张点开看的，一张会动的图两秒就看完。
    /// 飞书消息里图片就地播，所以做成 GIF 而不是视频——多一次点击就少一半人会看。
    ///
    /// 三条来路一个出口：
    /// - **2D 帧动画 / 人物帧动画**：给帧目录，直接合。
    /// - **3D 动画**：给模型，先按「转台」port 挑的下游渲一圈（有贴图就带贴图渲，
    ///   没贴图就是白模——转台脚本渲的就是模型本来的样子，不额外上色），再合。
    ///
    /// 合出来的 GIF 是**本地文件**，助手把它挂到卡片的图片位上发飞书；
    /// 引擎不认识下游怎么传图，那是桥的事（决策 93）。
    ///
    /// **上面一个下游的名字都没写**，不是因为不知道，是因为下游边界门禁不许——
    /// driver 名只能是运行时参数（子文档 05）。想知道现在挂的是谁，看 Bridges/ 下的 driver.json。
    /// </summary>
    public static class AnimationPreviewCommands
    {
        /// <summary>转台走哪个 port。</summary>
        private const string ProcessPortName = "模型加工";

        /// <summary>缺省落点文件名。</summary>
        private const string DefaultFileName = "preview.gif";

        /// <summary>
        /// 渲一张预览动图。
        /// </summary>
        /// <param name="arguments">预览图命令参数。</param>
        [EditorCommand("anim.preview")]
        [Summary("把帧或模型渲成一张会转的 GIF，给人扫一眼审方向")]
        public static CommandResult Execute(AnimationPreviewArguments arguments)
        {
            if (arguments == null)
            {
                return CommandResult.Failure("参数为空");
            }

            var repositoryRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            var frameDirectory = (arguments.FrameDirectory ?? "").Trim();
            var modelPath = (arguments.ModelPath ?? "").Trim();
            var lines = new List<string>();

            if (frameDirectory.Length == 0 && modelPath.Length == 0)
            {
                return CommandResult.Failure("要么给 --FrameDirectory（2D 那两条路），要么给 --ModelPath（3D 那条路）");
            }

            if (frameDirectory.Length > 0 && modelPath.Length > 0)
            {
                // 两个都给就**停下来问**，不许自己挑一个：挑错了人拿到的是另一件东西的预览，
                // 而它看起来完全正常。
                return CommandResult.Failure("帧目录与模型只能给一个：给帧就直接合，给模型就先渲转台再合");
            }

            var frameRate = arguments.FrameRate > 0 ? arguments.FrameRate : AnimatedPreview.DefaultFrameRate;

            if (modelPath.Length > 0)
            {
                if (!TryRenderTurntable(repositoryRoot, modelPath, arguments, lines, out frameDirectory, out var renderFailure))
                {
                    return CommandResult.Failure(renderFailure, lines);
                }
            }

            var fullFrameDirectory = Path.GetFullPath(frameDirectory);
            var outputPath = (arguments.OutputPath ?? "").Trim();
            if (outputPath.Length == 0)
            {
                outputPath = Path.Combine(fullFrameDirectory, DefaultFileName);
            }

            var result = AnimatedPreview.ComposeFromDirectory(fullFrameDirectory, Path.GetFullPath(outputPath), frameRate);
            foreach (var note in result.Notes)
            {
                lines.Add("　" + note);
            }

            if (result.FailureReason.Length > 0)
            {
                return CommandResult.Failure(result.FailureReason, lines);
            }

            lines.Add($"帧数：{result.FrameCount}　尺寸：{result.Width}×{result.Height}　帧率：{frameRate}");
            lines.Add($"大小：{(result.ByteCount / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} KB");
            lines.Add("落点：" + result.FilePath);
            lines.Add("这张图是给人审方向的，别拿它当成品——成品还是那批逐帧 PNG。");

            return CommandResult.Success($"预览图好了：{result.FilePath}", lines);
        }

        /// <summary>
        /// 跑一趟转台，把帧渲到模型旁边的 turntable 目录里。
        /// </summary>
        private static bool TryRenderTurntable(
            string repositoryRoot,
            string modelPath,
            AnimationPreviewArguments arguments,
            List<string> lines,
            out string frameDirectory,
            out string failureReason)
        {
            frameDirectory = "";
            failureReason = "";

            var fullModelPath = Path.GetFullPath(modelPath);
            if (!File.Exists(fullModelPath))
            {
                failureReason = "模型文件不存在：" + fullModelPath;
                return false;
            }

            var outputDirectory = Path.Combine(
                Path.GetDirectoryName(fullModelPath) ?? ".",
                "turntable-" + Path.GetFileNameWithoutExtension(fullModelPath));

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["输入模型"] = fullModelPath,
                ["输出目录"] = outputDirectory,
                ["边长"] = arguments.SideLength > 0 ? arguments.SideLength : 512,
                ["帧数"] = arguments.TurntableFrameCount > 0 ? arguments.TurntableFrameCount : 24,
                ["模式"] = string.IsNullOrWhiteSpace(arguments.TurntableMode) ? "环绕" : arguments.TurntableMode.Trim()
            });

            var call = BridgeInvoker.InvokeByPort(
                repositoryRoot, ProcessPortName, "turntable", payload, arguments.TimeoutSeconds);
            if (!call.Result.Succeeded)
            {
                failureReason = "转台渲不出来（" + call.Result.ErrorCode + "）：" + call.Result.HumanText;
                return false;
            }

            lines.Add($"转台渲完：{outputDirectory}");
            frameDirectory = outputDirectory;
            return true;
        }
    }
}
