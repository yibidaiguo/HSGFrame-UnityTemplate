using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>出帧命令 anim.frames 的参数。</summary>
    public sealed class AnimationFramesArguments
    {
        /// <summary>哪一路：帧动画 / 人物帧动画 / 3D动画。</summary>
        [Summary("哪一路：帧动画 / 人物帧动画 / 3D动画")]
        public string Kind { get; set; }

        /// <summary>
        /// 2D 那两路用哪个生图 driver。**缺省钉死 comfyui 而不是走域路由**：
        /// 这三份帧动画配方是 ComfyUI 工作流（recipes/ 下的 workflow.json），
        /// 线上驱动的配方目录是 presets/，同一个配方名在那边根本不存在。
        /// 交给域路由的话，「生图」首选是谁就发给谁，报出来的是「找不到预设文件」——
        /// 那句话指不到真正的问题（这一路本来就只有 ComfyUI 能跑）。
        /// </summary>
        [Summary("2D 那两路用哪个生图 driver；缺省 comfyui（这三份配方是 ComfyUI 工作流）")]
        [DefaultValue("comfyui")]
        public string Driver { get; set; }

        /// <summary>资产请求 JSON 路径（两条 2D 路要）。</summary>
        [Summary("资产请求 JSON 路径（两条 2D 路要）")]
        [DefaultValue("")]
        public string RequestPath { get; set; }

        /// <summary>参考图路径（人物帧动画那一路要：逐帧照着同一个人画）。</summary>
        [Summary("参考图路径（人物帧动画那一路要）")]
        [DefaultValue("")]
        public string ReferenceImagePath { get; set; }

        /// <summary>模型文件路径（3D动画那一路要）。</summary>
        [Summary("模型文件路径（3D动画那一路要）")]
        [DefaultValue("")]
        public string ModelPath { get; set; }

        /// <summary>输出目录；留空时 2D 按资产请求算正式落点，3D 必须给。</summary>
        [Summary("输出目录；留空时 2D 按资产请求算正式落点，3D 必须给")]
        [DefaultValue("")]
        public string OutputDirectory { get; set; }

        /// <summary>帧数（3D 那一路用；2D 的帧数是资产请求里的「变体数」）。</summary>
        [Summary("帧数（3D 那一路用）")]
        [DefaultValue(12)]
        public int FrameCount { get; set; }

        /// <summary>转台模式（3D 那一路用）：环绕 / 自带动画。</summary>
        [Summary("转台模式（3D 那一路用）：环绕 / 自带动画")]
        [DefaultValue("环绕")]
        public string TurntableMode { get; set; }

        /// <summary>渲染边长（3D 那一路用）。</summary>
        [Summary("渲染边长（3D 那一路用）")]
        [DefaultValue(512)]
        public int SideLength { get; set; }

        /// <summary>帧率，写进帧序列描述。</summary>
        [Summary("帧率，写进帧序列描述")]
        [DefaultValue(12)]
        public int FrameRate { get; set; }

        /// <summary>锚点：底边中点 / 中心 / 左上角。</summary>
        [Summary("锚点：底边中点 / 中心 / 左上角")]
        [DefaultValue("底边中点")]
        public string Anchor { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>单次下游调用超时秒数。</summary>
        [Summary("单次下游调用超时秒数")]
        [DefaultValue(900)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>拼图集命令 anim.assemble 的参数。</summary>
    public sealed class AnimationAssembleArguments
    {
        /// <summary>帧序列描述路径（anim.frames 产的 frames.json）。</summary>
        [Summary("帧序列描述路径（anim.frames 产的 frames.json）")]
        public string DescriptionPath { get; set; }

        /// <summary>输出目录；留空落帧目录旁边的 sheet/。</summary>
        [Summary("输出目录；留空落帧目录旁边的 sheet/")]
        [DefaultValue("")]
        public string OutputDirectory { get; set; }

        /// <summary>图集文件名（不带扩展名）；留空用帧目录名。</summary>
        [Summary("图集文件名（不带扩展名）；留空用帧目录名")]
        [DefaultValue("")]
        public string SheetName { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }
    }

    /// <summary>
    /// 帧动画两步走（子文档 06 的动画那一支）：
    ///
    /// **第一步 `anim.frames`**：出逐帧透明底 PNG + 一份帧序列描述（几帧 / 多快 / 以哪个点对齐），
    /// 摆给人看。三路共用这一条命令，差别只在帧从哪来——
    /// 帧动画与人物帧动画走生图配方，3D动画走 Blender 转台。
    ///
    /// **第二步 `anim.assemble`**：人看过之后才拼精灵图集。
    ///
    /// 为什么非要分成两步：这条链最贵的判断是「这批帧像不像一个动作」，而那是人的判断
    /// （§0：人只做判断，不做搬运）。一口气拼完的话，人拿到的是一张已经拼好的图集，
    /// 想改就得整批重来；分开之后他看的是帧本身，不满意就重出第一步，图集一次都没白拼。
    /// </summary>
    public static class AnimationCommands
    {
        /// <summary>三路的名字。</summary>
        private const string GenericKind = "帧动画";

        /// <summary>2D 人物那一路。</summary>
        private const string CharacterKind = "人物帧动画";

        /// <summary>3D 那一路。</summary>
        private const string ModelKind = "3D动画";

        /// <summary>转台住在哪个 port 下。</summary>
        private const string ModelProcessPortName = "模型加工";

        /// <summary>
        /// 第一步：出帧 + 写帧序列描述。
        /// </summary>
        /// <param name="arguments">出帧命令参数。</param>
        [EditorCommand("anim.frames")]
        [Summary("帧动画第一步：出逐帧透明底 PNG 与帧序列描述，摆给人看")]
        public static CommandResult Frames(AnimationFramesArguments arguments)
        {
            if (arguments == null)
            {
                return CommandResult.Failure("参数为空");
            }

            var kind = (arguments.Kind ?? "").Trim();
            if (kind != GenericKind && kind != CharacterKind && kind != ModelKind)
            {
                return CommandResult.Failure(
                    $"不认识的种类「{kind}」，只有：{GenericKind}、{CharacterKind}、{ModelKind}");
            }

            var anchor = string.IsNullOrWhiteSpace(arguments.Anchor) ? FrameSequence.DefaultAnchor : arguments.Anchor.Trim();
            if (!FrameSequence.AllowedAnchors.Contains(anchor, StringComparer.Ordinal))
            {
                return CommandResult.Failure(
                    $"不认识的锚点「{anchor}」，只有：{string.Join("、", FrameSequence.AllowedAnchors)}");
            }

            var repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            var frameRate = arguments.FrameRate > 0 ? arguments.FrameRate : FrameSequence.DefaultFrameRate;
            var lines = new List<string>();

            string frameDirectory;
            string source;
            if (kind == ModelKind)
            {
                if (!TryRunTurntable(arguments, repositoryRoot, lines, out frameDirectory, out source, out var turntableFailure))
                {
                    return CommandResult.Failure(turntableFailure, lines);
                }
            }
            else
            {
                if (!TryRunGeneration(arguments, kind, repositoryRoot, lines, out frameDirectory, out source, out var generateFailure))
                {
                    return CommandResult.Failure(generateFailure, lines);
                }
            }

            var sequence = FrameSequence.Scan(frameDirectory, kind, frameRate, anchor, source);
            if (sequence.FrameCount == 0)
            {
                // 出帧那一步报了成功、目录里却一张都没有：**这时候不许跟着报成功**。
                // 这正是任务书里那条「假成功比失败难查」——下一步会拼出一张空图集。
                return CommandResult.Failure(
                    $"出帧那一步报了成功，但 {Relative(repositoryRoot, frameDirectory)} 里一张 PNG 都没有", lines);
            }

            var descriptionPath = sequence.Save(frameDirectory);
            lines.Add(sequence.Describe());
            lines.Add("帧序列描述：" + Relative(repositoryRoot, descriptionPath));
            foreach (var frame in sequence.Frames)
            {
                lines.Add($"　第 {frame.Index} 帧　{Relative(repositoryRoot, frame.Path)}（{frame.Width}×{frame.Height}）");
            }

            lines.Add("");
            lines.Add("下一步：人看过这批帧之后，跑 anim.assemble --DescriptionPath "
                + Relative(repositoryRoot, descriptionPath) + " 才拼图集。");

            return CommandResult.Success(
                $"出帧完成：{sequence.FrameCount} 帧（{kind}）", lines);
        }

        /// <summary>
        /// 第二步：把帧拼成横排精灵图集。
        ///
        /// **落点是帧目录旁边的 sheet/，不进 UnityProject/Assets/**：
        /// 进正式资产目录要走 asset.import 那条规范化通道（改名、落点、门禁），
        /// 而这一步只是把帧摞成一张图，还没经过那一套。
        /// </summary>
        /// <param name="arguments">拼图集命令参数。</param>
        [EditorCommand("anim.assemble")]
        [Summary("帧动画第二步：人审过之后把帧拼成精灵图集（落帧目录旁边，不进 Assets）")]
        public static CommandResult Assemble(AnimationAssembleArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.DescriptionPath))
            {
                return CommandResult.Failure("必须给 --DescriptionPath（anim.frames 产的 frames.json）");
            }

            var repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            var descriptionPath = Path.GetFullPath(arguments.DescriptionPath);
            var sequence = FrameSequence.Load(descriptionPath, out var loadFailure);
            if (sequence == null)
            {
                return CommandResult.Failure(loadFailure);
            }

            var frameDirectory = Path.GetDirectoryName(descriptionPath) ?? "";
            var outputDirectory = string.IsNullOrWhiteSpace(arguments.OutputDirectory)
                ? Path.Combine(frameDirectory, "sheet")
                : Path.GetFullPath(arguments.OutputDirectory);
            var sheetName = string.IsNullOrWhiteSpace(arguments.SheetName)
                ? new DirectoryInfo(frameDirectory).Name
                : arguments.SheetName.Trim();

            var result = SpriteSheetComposer.Compose(sequence, outputDirectory, sheetName);
            var lines = new List<string> { sequence.Describe() };
            lines.AddRange(result.Notes);

            if (!result.Succeeded)
            {
                return CommandResult.Failure("拼图集失败：" + result.FailureReason, lines);
            }

            lines.Add("图集：" + Relative(repositoryRoot, result.SheetPath));
            lines.Add("图集描述：" + Relative(repositoryRoot, result.MetadataPath));
            lines.Add($"每格 {result.CellWidth}×{result.CellHeight}，横排 {sequence.FrameCount} 格");
            lines.Add("");
            lines.Add("这批还没进 Assets/：要进正式资产目录走 asset.import，那条路会改名、定落点并过门禁。");

            return CommandResult.Success(
                $"图集拼好了：{sequence.FrameCount} 帧 · 每格 {result.CellWidth}×{result.CellHeight}", lines);
        }

        /// <summary>
        /// 2D 两路：调 bridge.generate 出帧。
        /// **直接复用那条命令**而不是自己拼一遍载荷——配方路由、正式落点、种子、溯源边车
        /// 全在它里面，另写一份的话这两条路会慢慢长歪（同一个配方名在两处解析出不同结果）。
        /// </summary>
        private static bool TryRunGeneration(
            AnimationFramesArguments arguments,
            string kind,
            string repositoryRoot,
            List<string> lines,
            out string frameDirectory,
            out string source,
            out string failureReason)
        {
            frameDirectory = "";
            source = "";
            failureReason = "";

            if (string.IsNullOrWhiteSpace(arguments.RequestPath))
            {
                failureReason = $"{kind} 这一路要 --RequestPath（资产请求 JSON）";
                return false;
            }

            if (kind == CharacterKind && string.IsNullOrWhiteSpace(arguments.ReferenceImagePath))
            {
                // 人物那一路少了参考图就退化成文生图，批出来的每一帧各画一个人。
                // 这种退化**不许静默发生**：帧看着都挺好，拼起来才发现换了脸。
                failureReason
                    = $"{CharacterKind} 这一路必须给 --ReferenceImagePath：没有参考图就成了文生图，每帧会画成不同的人";
                return false;
            }

            var driverName = string.IsNullOrWhiteSpace(arguments.Driver) ? "comfyui" : arguments.Driver.Trim();
            var routeTable = AssetRecipeRouteTable.Load(repositoryRoot);
            var recipeName = ResolveRecipeName(routeTable, driverName, kind, arguments.ReferenceImagePath.Length > 0, out var routeReason);
            if (recipeName.Length == 0)
            {
                failureReason = routeReason;
                return false;
            }

            lines.Add($"配方：{recipeName}（{kind}，driver={driverName}）");

            var generateArguments = new BridgeGenerateArguments
            {
                Driver = driverName,
                RequestPath = arguments.RequestPath,
                RecipeName = recipeName,
                OutputDirectory = arguments.OutputDirectory,
                ReferenceImagePath = arguments.ReferenceImagePath,
                RepositoryRoot = arguments.RepositoryRoot,
                TimeoutSeconds = arguments.TimeoutSeconds
            };

            // 出帧之前先记下目录里已经有哪些 PNG。**重跑这条命令不会清目录**，
            // 于是上一轮的帧还躺在那儿，扫出来的帧数会是两轮之和——
            // 人拿到一份写着「8 帧」的描述，而这一轮只出了 4 帧，前 4 帧是上次的。
            // 不替人删（那是别人的产物），但必须**说出来**。
            var directoryBefore = ResolveVariantDirectory(arguments, repositoryRoot, out _);
            var existingBefore = SnapshotPngNames(directoryBefore);

            var result = BridgeCommands.Generate(generateArguments);
            lines.AddRange(result.OutputLines ?? Array.Empty<string>());
            if (!result.IsSuccess)
            {
                failureReason = result.Message;
                return false;
            }

            // 变体落在 <落点>/variants/ 下，帧就是那批变体。
            frameDirectory = ResolveVariantDirectory(arguments, repositoryRoot, out var directoryReason);
            if (frameDirectory.Length == 0)
            {
                failureReason = directoryReason;
                return false;
            }

            if (existingBefore.Count > 0)
            {
                lines.Add($"注意：出帧前 {Relative(repositoryRoot, frameDirectory)} 里已经有 {existingBefore.Count} 张 PNG，"
                    + "它们会一起被算进这一段帧序列。只要这一轮的，先把目录清空再跑。");
            }

            source = recipeName;
            return true;
        }

        /// <summary>数一个目录里现有的 PNG 文件名；目录不在给空集合。</summary>
        private static IReadOnlyList<string> SnapshotPngNames(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .ToList();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>3D 那一路：调模型加工 port 的 turntable 动作。</summary>
        private static bool TryRunTurntable(
            AnimationFramesArguments arguments,
            string repositoryRoot,
            List<string> lines,
            out string frameDirectory,
            out string source,
            out string failureReason)
        {
            frameDirectory = "";
            source = "";
            failureReason = "";

            if (string.IsNullOrWhiteSpace(arguments.ModelPath))
            {
                failureReason = $"{ModelKind} 这一路要 --ModelPath（模型文件）";
                return false;
            }

            if (string.IsNullOrWhiteSpace(arguments.OutputDirectory))
            {
                failureReason = $"{ModelKind} 这一路要 --OutputDirectory：转台的帧没有资产请求可以算落点";
                return false;
            }

            var modelPath = Path.GetFullPath(arguments.ModelPath);
            if (!File.Exists(modelPath))
            {
                failureReason = $"模型文件不存在：{modelPath}";
                return false;
            }

            var outputDirectory = Path.GetFullPath(arguments.OutputDirectory);
            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["输入模型"] = modelPath.Replace('\\', '/'),
                ["输出目录"] = outputDirectory.Replace('\\', '/'),
                ["边长"] = arguments.SideLength > 0 ? arguments.SideLength : 512,
                ["帧数"] = arguments.FrameCount > 0 ? arguments.FrameCount : 12,
                ["模式"] = string.IsNullOrWhiteSpace(arguments.TurntableMode) ? "环绕" : arguments.TurntableMode.Trim()
            });

            var call = BridgeInvoker.InvokeByPort(
                repositoryRoot, ModelProcessPortName, "turntable", payload, arguments.TimeoutSeconds);
            if (!call.Result.Succeeded)
            {
                failureReason = call.Result.ErrorCode + "：" + call.Result.HumanText;
                return false;
            }

            var mode = call.Result.Payload.ValueKind == JsonValueKind.Object
                && call.Result.Payload.TryGetProperty("模式", out var modeValue)
                && modeValue.ValueKind == JsonValueKind.String
                ? modeValue.GetString() ?? ""
                : "";
            lines.Add($"转台渲完：{Path.GetFileName(modelPath)}（模式 {mode}）");

            frameDirectory = outputDirectory;
            source = "blender 转台 · " + mode;
            return true;
        }

        /// <summary>按种类与有没有参考图从配方路由表里取配方名；取不到给空串与原因。</summary>
        private static string ResolveRecipeName(
            AssetRecipeRouteTable routeTable, string driverName, string kind, bool hasReferenceImage, out string failureReason)
        {
            if (routeTable.TryResolve(driverName, kind, hasReferenceImage, out var recipeName, out failureReason))
            {
                return recipeName;
            }

            {
                failureReason = $"配方路由表里查不到 {driverName} 的「{kind}」"
                    + (hasReferenceImage ? "的图生图那份" : "")
                    + $"（{failureReason}）。**不退回别的配方**——退回去参考图会被悄悄丢掉，图照出、钱照花。";
                return "";
            }
        }

        /// <summary>算 2D 那两路的变体目录。</summary>
        private static string ResolveVariantDirectory(
            AnimationFramesArguments arguments, string repositoryRoot, out string failureReason)
        {
            failureReason = "";
            if (!string.IsNullOrWhiteSpace(arguments.OutputDirectory))
            {
                // 桥自己会在给它的目录下再拼一层 variants，所以这里跟着往下走一层。
                return Path.Combine(Path.GetFullPath(arguments.OutputDirectory), "variants");
            }

            try
            {
                var requestNode = JsonNode.Parse(File.ReadAllText(Path.GetFullPath(arguments.RequestPath)));
                if (requestNode is not JsonObject requestObject)
                {
                    failureReason = "资产请求顶层不是对象，算不出变体目录";
                    return "";
                }

                var requirementIdentifier = requestObject["需求id"]?.GetValue<string>() ?? "";
                var assetIdentifier = requestObject["id"]?.GetValue<string>() ?? "";
                if (requirementIdentifier.Length == 0 || assetIdentifier.Length == 0)
                {
                    failureReason = "资产请求里缺「需求id」或「id」，算不出变体目录";
                    return "";
                }

                return AssetPaths.VariantDirectory(repositoryRoot, requirementIdentifier, assetIdentifier);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException || exception is InvalidOperationException)
            {
                failureReason = "资产请求读不了，算不出变体目录：" + exception.Message;
                return "";
            }
        }

        /// <summary>把绝对路径压成仓库相对路径，给人看。</summary>
        private static string Relative(string repositoryRoot, string filePath)
        {
            try
            {
                return Path.GetRelativePath(repositoryRoot, filePath).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return filePath;
            }
        }
    }
}
