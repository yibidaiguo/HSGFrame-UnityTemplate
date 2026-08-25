using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>生成一页模型预览的结果：成了给落点，没成给不含猜测的原因。</summary>
    public sealed class ModelViewerResult
    {
        /// <summary>构造一次生成结果。</summary>
        /// <param name="succeeded">成没成。</param>
        /// <param name="pageFilePath">生成的 HTML 落点；失败时为空串。</param>
        /// <param name="isStandalone">是不是自包含单文件（模型与脚本都嵌在里面）。</param>
        /// <param name="byteCount">生成的 HTML 有多大，字节。</param>
        /// <param name="failureReason">失败原因；成功时为空串。</param>
        /// <param name="notes">给人看的说明，逐条。</param>
        public ModelViewerResult(
            bool succeeded,
            string pageFilePath,
            bool isStandalone,
            long byteCount,
            string failureReason,
            IReadOnlyList<string> notes)
        {
            Succeeded = succeeded;
            PageFilePath = pageFilePath ?? "";
            IsStandalone = isStandalone;
            ByteCount = byteCount;
            FailureReason = failureReason ?? "";
            Notes = notes ?? Array.Empty<string>();
        }

        /// <summary>成没成。</summary>
        public bool Succeeded { get; }

        /// <summary>生成的 HTML 落点；失败时为空串。</summary>
        public string PageFilePath { get; }

        /// <summary>是不是自包含单文件。</summary>
        public bool IsStandalone { get; }

        /// <summary>生成的 HTML 有多大，字节。</summary>
        public long ByteCount { get; }

        /// <summary>失败原因；成功时为空串。</summary>
        public string FailureReason { get; }

        /// <summary>给人看的说明，逐条。</summary>
        public IReadOnlyList<string> Notes { get; }
    }

    /// <summary>
    /// 把一个模型做成**能用鼠标拖着转的**网页预览。
    ///
    /// 与转台 GIF（<see cref="AnimatedPreview"/>）是两件事，都要留着：
    /// GIF 能直接贴进飞书消息里就地播，**但只能看它替你选的那一圈角度**；
    /// 这一页要点开链接才看得到，**但能自己转到想看的那一面**。
    /// 审「方向对不对」用 GIF，审「背面塌没塌、比例对不对」只能用这一页。
    ///
    /// **飞书卡片嵌不了 3D**——卡片只有文字、图片、按钮这几种块，
    /// 没有能跑脚本的容器。所以这条路只能是「卡片给一个链接，人点开」，
    /// 不要把它说成「卡片里能转」，那是做不到的。
    ///
    /// 渲染走 <c>&lt;model-viewer&gt;</c>（Google 的 Web Component，Apache-2.0），
    /// 不自己写 WebGL：相机控制、IBL 环境光、KTX2/Draco 解压这些都属于
    /// 「写得出来但养不起」，而且写错了的症状是「某个模型在某台机器上黑屏」。
    /// 脚本本体内置在 <c>Tools/Deps/web/</c>，不连 CDN——
    /// 这页是本机 HTTP 服务发出去的，一个要连外网才显示的页面坏起来就是一片空白。
    /// </summary>
    public static class ModelViewerPage
    {
        /// <summary>
        /// <c>&lt;model-viewer&gt;</c> 只吃 glTF 家族。
        /// **这正是模型生成那两条路要统一到 .glb 的现实理由**：
        /// 其中一条如果交出 .fbx，这页就打不开，而症状是「预览没有」不是「格式不对」。
        /// （具体是哪两条下游，看 Bridges/ 下的 driver.json——下游边界门禁不许在这里写它们的名字。）
        /// </summary>
        private static readonly string[] ViewableExtensions = { ".glb", ".gltf" };

        /// <summary>
        /// 自包含模式下模型的大小上限。超过就不许嵌。
        ///
        /// 嵌进来要走 base64，**体积会涨三分之一**，而浏览器打开一个几十 MB 的
        /// HTML 是要先把整个字符串读进内存的。与其生成一个打不开的页面，
        /// 不如当场说清楚、让调用方改用目录模式。
        /// </summary>
        public const long StandaloneModelByteLimit = 48L * 1024 * 1024;

        /// <summary>
        /// 生成预览页。
        /// </summary>
        /// <param name="modelPath">模型文件（.glb / .gltf）。</param>
        /// <param name="outputPath">HTML 落点；目录不存在会建出来。</param>
        /// <param name="viewerScriptPath">内置的 model-viewer 脚本路径（Tools/Deps/web/model-viewer.min.js）。</param>
        /// <param name="title">页面标题，给人看的一句话。</param>
        /// <param name="standalone">
        /// true 生成自包含单文件（脚本与模型都嵌进去，双击就能开）；
        /// false 生成目录形态（HTML 旁边放脚本与模型，靠 HTTP 服务发）。
        /// </param>
        public static ModelViewerResult Build(
            string modelPath,
            string outputPath,
            string viewerScriptPath,
            string title,
            bool standalone)
        {
            var notes = new List<string>();

            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                return Failure($"模型文件不在：{modelPath}");
            }

            var extension = Path.GetExtension(modelPath).ToLowerInvariant();
            if (Array.IndexOf(ViewableExtensions, extension) < 0)
            {
                return Failure(
                    $"这一页只能显示 glTF 家族（{string.Join(" / ", ViewableExtensions)}），"
                    + $"给的是「{extension}」。把模型转成 .glb 再来——"
                    + "生成那一步本来就该交出 .glb（模型生成的两条路都已经统一到它）。");
            }

            if (string.IsNullOrWhiteSpace(viewerScriptPath) || !File.Exists(viewerScriptPath))
            {
                return Failure(
                    $"内置的 model-viewer 脚本不在：{viewerScriptPath}；"
                    + "跑一次 pwsh -NoProfile -File Tools/Deps/fetch-web.ps1 把它取下来");
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return Failure("没给落点");
            }

            var modelBytes = new FileInfo(modelPath).Length;
            if (standalone && modelBytes > StandaloneModelByteLimit)
            {
                return Failure(
                    $"模型 {Describe(modelBytes)} 超过自包含模式的上限 {Describe(StandaloneModelByteLimit)}："
                    + "嵌进 HTML 要走 base64、体积再涨三分之一，生成出来多半是个打不开的页面。"
                    + "改用目录模式（Standalone=false）：HTML 旁边放模型，靠 HTTP 服务发。");
            }

            try
            {
                var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                string modelReference;
                string scriptTag;
                if (standalone)
                {
                    var mediaType = extension == ".gltf" ? "model/gltf+json" : "model/gltf-binary";
                    modelReference = "data:" + mediaType + ";base64,"
                        + Convert.ToBase64String(File.ReadAllBytes(modelPath));
                    scriptTag = "<script type=\"module\">\n"
                        + File.ReadAllText(viewerScriptPath) + "\n</script>";
                    notes.Add("自包含单文件：脚本与模型都嵌在里面，双击就能开，也能直接发给别人。");
                }
                else
                {
                    // 目录形态：脚本与模型拷到 HTML 旁边，按相对路径引。
                    // **不能只写个绝对路径了事**——这一页是要被 HTTP 服务发出去的，
                    // 而服务的根目录未必包含模型原来的位置。
                    var modelFileName = Path.GetFileName(modelPath);
                    var scriptFileName = Path.GetFileName(viewerScriptPath);
                    var siblingModel = Path.Combine(outputDirectory ?? ".", modelFileName);
                    var siblingScript = Path.Combine(outputDirectory ?? ".", scriptFileName);
                    if (!string.Equals(Path.GetFullPath(siblingModel), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(modelPath, siblingModel, overwrite: true);
                    }

                    File.Copy(viewerScriptPath, siblingScript, overwrite: true);
                    modelReference = Uri.EscapeDataString(modelFileName);
                    scriptTag = "<script type=\"module\" src=\"" + Uri.EscapeDataString(scriptFileName) + "\"></script>";
                    notes.Add($"目录形态：{modelFileName} 与 {scriptFileName} 拷到了 HTML 旁边，整个目录一起发才打得开。");
                }

                var html = BuildHtml(title, modelReference, scriptTag, modelBytes, Path.GetFileName(modelPath));
                File.WriteAllText(outputPath, html, new UTF8Encoding(false));

                var pageBytes = new FileInfo(outputPath).Length;
                notes.Add("飞书卡片嵌不了 3D，只能给链接让人点开——这一条是飞书的限制，不是这里没做。");
                return new ModelViewerResult(true, Path.GetFullPath(outputPath), standalone, pageBytes, "", notes);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is OutOfMemoryException)
            {
                return Failure($"写不下去：{exception.Message}");
            }
        }

        /// <summary>把字节数说成人话。</summary>
        private static string Describe(long byteCount)
        {
            if (byteCount >= 1024 * 1024)
            {
                return (byteCount / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            }

            return (byteCount / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }

        /// <summary>失败结果。</summary>
        private static ModelViewerResult Failure(string reason)
        {
            return new ModelViewerResult(false, "", false, 0, reason, Array.Empty<string>());
        }

        /// <summary>
        /// 拼页面。
        /// **相机与光照的默认值是有意选的**：camera-controls 让人能拖，
        /// auto-rotate 让页面一打开就在转（不转的话有人以为是张图），
        /// environment-image=neutral 给一个不带颜色倾向的环境光——
        /// 审模型要看的是形，环境光带色会让人把光的颜色当成贴图的颜色。
        /// </summary>
        private static string BuildHtml(
            string title,
            string modelReference,
            string scriptTag,
            long modelByteCount,
            string modelFileName)
        {
            var safeTitle = Escape(string.IsNullOrWhiteSpace(title) ? "模型预览" : title.Trim());
            var builder = new StringBuilder();
            builder.Append("<!doctype html>\n<html lang=\"zh-CN\">\n<head>\n");
            builder.Append("<meta charset=\"utf-8\">\n");
            builder.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            builder.Append("<title>").Append(safeTitle).Append("</title>\n");
            builder.Append("<style>\n");
            builder.Append(":root { color-scheme: light dark; }\n");
            builder.Append("body { margin: 0; font: 14px/1.6 system-ui, 'Microsoft YaHei', sans-serif;\n");
            builder.Append("  background: #1b1d21; color: #e8e8ea; display: flex; flex-direction: column; height: 100vh; }\n");
            builder.Append("header { padding: 12px 16px; border-bottom: 1px solid #33363c; }\n");
            builder.Append("h1 { margin: 0 0 2px; font-size: 15px; font-weight: 600; }\n");
            builder.Append("p { margin: 0; font-size: 12px; color: #9aa0a8; }\n");
            builder.Append("model-viewer { flex: 1; width: 100%; background: #26292e; }\n");
            builder.Append("footer { padding: 8px 16px; font-size: 12px; color: #9aa0a8; border-top: 1px solid #33363c; }\n");
            builder.Append("</style>\n</head>\n<body>\n");
            builder.Append("<header>\n<h1>").Append(safeTitle).Append("</h1>\n");
            builder.Append("<p>").Append(Escape(modelFileName)).Append("　")
                   .Append(Escape(Describe(modelByteCount)))
                   .Append("　按住左键拖动转视角，滚轮缩放，右键平移</p>\n</header>\n");
            builder.Append("<model-viewer src=\"").Append(modelReference).Append("\"\n");
            builder.Append("  alt=\"").Append(safeTitle).Append("\"\n");
            builder.Append("  camera-controls auto-rotate shadow-intensity=\"1\"\n");
            builder.Append("  environment-image=\"neutral\" touch-action=\"pan-y\">\n");
            builder.Append("</model-viewer>\n");
            builder.Append("<footer>这一页是给人审形的：转到背面、俯视、仰视各看一眼。");
            builder.Append("定方向用旁边那张转台动图就够了，不用打开这一页。</footer>\n");
            builder.Append(scriptTag).Append("\n</body>\n</html>\n");
            return builder.ToString();
        }

        /// <summary>HTML 转义：标题与文件名来自外部，直接拼进去等于给自己开一个注入口。</summary>
        private static string Escape(string text)
        {
            return (text ?? "")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
