using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>可交互模型预览命令 model.viewer 的参数。</summary>
    public sealed class ModelViewerArguments
    {
        /// <summary>模型文件（.glb / .gltf）。</summary>
        [Summary("模型文件（.glb / .gltf）——别的格式这一页显示不了")]
        public string ModelPath { get; set; }

        /// <summary>HTML 落点；留空落在模型旁边的 viewer.html。</summary>
        [Summary("HTML 落点；留空落在模型旁边的 viewer.html")]
        [DefaultValue("")]
        public string OutputPath { get; set; }

        /// <summary>页面标题。</summary>
        [Summary("页面标题；留空用模型文件名")]
        [DefaultValue("")]
        public string Title { get; set; }

        /// <summary>自包含单文件还是目录形态。</summary>
        [Summary("true 生成自包含单文件（双击就能开，也能直接发人）；false 生成目录形态（HTML 旁放模型与脚本，靠 HTTP 服务发）")]
        [DefaultValue(true)]
        public bool Standalone { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }
    }

    /// <summary>
    /// 把一个模型做成能用鼠标拖着转的网页——**审「形」的那一步**。
    ///
    /// 与 <c>anim.preview</c> 的转台 GIF 是两件事，都要留着：
    /// GIF 贴进飞书消息里就地播，扫一眼定方向；这一页要点开，但能自己转到想看的那一面。
    /// 「背面塌没塌、比例对不对」只有这一页能回答。
    ///
    /// **飞书卡片嵌不了 3D**：卡片只有文字、图片、按钮这几种块，没有能跑脚本的容器。
    /// 所以这条路只能是「卡片给链接、人点开」。别把它说成「卡片里能转」。
    /// </summary>
    public static class ModelViewerCommands
    {
        /// <summary>内置的 model-viewer 脚本相对仓库根的位置。</summary>
        private const string ViewerScriptRelativePath = "Tools/Deps/web/model-viewer.min.js";

        /// <summary>缺省落点文件名。</summary>
        private const string DefaultFileName = "viewer.html";

        /// <summary>
        /// 生成一页可交互模型预览。
        /// </summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("model.viewer")]
        [Summary("把模型做成能用鼠标拖着转的网页，给人审形（飞书卡片只能给链接）")]
        public static CommandResult Execute(ModelViewerArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.ModelPath))
            {
                return CommandResult.Failure("参数 ModelPath 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            var modelPath = Path.GetFullPath(arguments.ModelPath.Trim());

            var outputPath = (arguments.OutputPath ?? "").Trim();
            if (outputPath.Length == 0)
            {
                var directory = Path.GetDirectoryName(modelPath) ?? ".";
                outputPath = Path.Combine(directory, DefaultFileName);
            }

            var title = (arguments.Title ?? "").Trim();
            if (title.Length == 0)
            {
                title = Path.GetFileNameWithoutExtension(modelPath);
            }

            var result = ModelViewerPage.Build(
                modelPath,
                Path.GetFullPath(outputPath),
                Path.Combine(repositoryRoot, ViewerScriptRelativePath),
                title,
                arguments.Standalone);

            var lines = new List<string>();
            foreach (var note in result.Notes)
            {
                lines.Add("　" + note);
            }

            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.FailureReason, lines);
            }

            lines.Add($"大小：{(result.ByteCount / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)} MB");
            lines.Add("落点：" + result.PageFilePath);
            lines.Add("直接看：用浏览器打开上面那个文件。");
            lines.Add("发给人看：面板起着的时候走 http://localhost:8766/preview/<相对 _Tasks/preview 的路径>。");

            return CommandResult.Success($"预览页好了：{result.PageFilePath}", lines);
        }
    }
}
