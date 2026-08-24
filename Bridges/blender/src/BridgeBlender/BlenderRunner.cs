using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Blender
{
    /// <summary>
    /// 起 Blender 子进程执行加工站脚本，并把脚本回传的结果解析成协议响应。
    /// Blender 自己会往 stdout 狂打东西，所以它的 stdout 必须当数据读进来解析，
    /// 绝不能直接流到本进程的 stdout——本进程的 stdout 只许有协议 JSON。
    /// 脚本靠约定前缀行 <c>BRIDGE_RESULT &lt;json&gt;</c> 回传结果；找不到那一行就是失败，
    /// 错误码「加工站没回结果」，人话带 stderr 末尾几行——绝不在没结果时编一份空结果。
    /// </summary>
    public static class BlenderRunner
    {
        /// <summary>Blender 可执行文件的缺省路径。</summary>
        private const string DefaultBlenderExecutable = "D:/Tools/Blender/blender.exe";

        /// <summary>起 Blender 的缺省超时秒数。</summary>
        private const int DefaultTimeoutSeconds = 900;

        /// <summary>脚本回传结果的前缀行。</summary>
        private const string ResultMarker = "BRIDGE_RESULT ";

        /// <summary>stderr 末尾最多带几行进错误人话。</summary>
        private const int StderrTailLineCount = 8;

        /// <summary>三视图的缺省边长，像素。</summary>
        private const int DefaultSideLength = 512;

        /// <summary>三视图边长的下限，像素。再小就看不出形了。</summary>
        private const int MinimumSideLength = 64;

        /// <summary>三视图边长的上限，像素。再大对着卡片看没有意义，只是让上传变慢。</summary>
        private const int MaximumSideLength = 2048;

        /// <summary>
        /// 能力探测：跑 probe.py，把脚本回传的探测 JSON 写到载荷「输出路径」。
        /// Blender 起不来 → 错误码「下游不可达」，不许写出空探测文件。
        /// </summary>
        /// <param name="request">请求信封，载荷形如 {"输出路径":"&lt;绝对路径&gt;"}。</param>
        public static BridgeResponse RunCaps(BridgeRequest request)
        {
            if (!TryGetPayloadString(request, "输出路径", out var outputPath, out var reason))
            {
                return FailureResponse("载荷缺「输出路径」或它不是字符串：" + reason);
            }

            var argumentsFile = WriteTemporaryArgumentsFile(request.Payload.GetRawText());
            try
            {
                var run = RunBlender(request, "probe.py", argumentsFile);
                if (!run.Succeeded)
                {
                    return run.Response;
                }

                if (!TryParseJson(run.ResultJson, out var document, out var parseReason))
                {
                    return FailureResponse($"加工站回传的不是合法 JSON：{parseReason}");
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!IsProbeShape(root))
                    {
                        return FailureResponse("加工站回传的探测结果不是 节点/模型/lora 三个数组的形状");
                    }

                    try
                    {
                        var directory = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.WriteAllText(outputPath, root.GetRawText(), new UTF8Encoding(false));
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        return FailureResponse($"探测输出写盘失败：{exception.Message}");
                    }

                    return BridgeResponse.Success("1.0.0", root.Clone());
                }
            }
            finally
            {
                TryDelete(argumentsFile);
            }
        }

        /// <summary>
        /// 模型加工：跑 process.py，返回
        /// {"输出模型":…,"指标文件":…,"执行了的步骤":[…],"跳过的步骤":[…]}
        /// </summary>
        /// <param name="request">请求信封，载荷含 输入模型 / 输出目录 / 加工计划。</param>
        public static BridgeResponse RunProcess(BridgeRequest request)
        {
            if (!TryGetPayloadString(request, "输入模型", out var inputModel, out var reason))
            {
                return FailureResponse("载荷缺「输入模型」或它不是字符串：" + reason);
            }

            if (!TryGetPayloadString(request, "输出目录", out var outputDirectory, out reason))
            {
                return FailureResponse("载荷缺「输出目录」或它不是字符串：" + reason);
            }

            if (!TryGetPayloadObject(request, "加工计划", out _, out reason))
            {
                return FailureResponse("载荷缺「加工计划」或它不是对象：" + reason);
            }

            if (!File.Exists(inputModel))
            {
                return FailureResponse($"输入模型不存在：{inputModel}");
            }

            var argumentsFile = WriteTemporaryArgumentsFile(request.Payload.GetRawText());
            try
            {
                var run = RunBlender(request, "process.py", argumentsFile);
                if (!run.Succeeded)
                {
                    return run.Response;
                }

                if (!TryParseJson(run.ResultJson, out var document, out var parseReason))
                {
                    return FailureResponse($"加工站回传的不是合法 JSON：{parseReason}");
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!IsProcessShape(root))
                    {
                        return FailureResponse("加工站回传的结果缺 输出模型 / 指标文件 / 执行了的步骤 / 跳过的步骤");
                    }

                    return BridgeResponse.Success("1.0.0", root.Clone());
                }
            }
            finally
            {
                TryDelete(argumentsFile);
            }
        }

        /// <summary>
        /// 三视图批渲：跑 render_views.py，返回
        /// {"输出图":[{"视角":"front","路径":…},{"视角":"side",…},{"视角":"iso",…}]}
        /// 出图的文件名是跨环硬约定「&lt;模型文件名（带后缀）&gt;.&lt;视角&gt;.png」，
        /// 下一环的九宫格按 AssetPaths.VariantViewFile 去找，名字对不上就等于没渲。
        /// </summary>
        /// <param name="request">请求信封，载荷含 输入模型 / 输出目录 / 边长（可选）。</param>
        public static BridgeResponse RunRender(BridgeRequest request)
        {
            if (!TryGetPayloadString(request, "输入模型", out var inputModel, out var reason))
            {
                return FailureResponse("载荷缺「输入模型」或它不是字符串：" + reason);
            }

            if (!TryGetPayloadString(request, "输出目录", out var outputDirectory, out reason))
            {
                return FailureResponse("载荷缺「输出目录」或它不是字符串：" + reason);
            }

            if (!File.Exists(inputModel))
            {
                return FailureResponse($"输入模型不存在：{inputModel}");
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            {
                return FailureResponse($"输出目录建不出来：{exception.Message}");
            }

            // 边长的缺省与钳制在这里定死后写进参数文件——脚本侧另有一道同样的兜底，
            // 两边同源，谁被单独调用都不会渲出一张尺寸离谱的图。
            var sideLength = ReadPayloadInt(request, "边长", DefaultSideLength);
            if (sideLength < MinimumSideLength)
            {
                sideLength = MinimumSideLength;
            }

            if (sideLength > MaximumSideLength)
            {
                sideLength = MaximumSideLength;
            }

            var argumentsObject = new JsonObject
            {
                ["输入模型"] = inputModel,
                ["输出目录"] = outputDirectory,
                ["边长"] = sideLength
            };

            var argumentsFile = WriteTemporaryArgumentsFile(argumentsObject.ToJsonString());
            try
            {
                var run = RunBlender(request, "render_views.py", argumentsFile);
                if (!run.Succeeded)
                {
                    return run.Response;
                }

                if (!TryParseJson(run.ResultJson, out var document, out var parseReason))
                {
                    return FailureResponse($"加工站回传的不是合法 JSON：{parseReason}");
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!IsRenderShape(root))
                    {
                        return FailureResponse("加工站回传的结果不是「输出图」数组，或数组里有项缺 视角 / 路径");
                    }

                    return BridgeResponse.Success("1.0.0", root.Clone());
                }
            }
            finally
            {
                TryDelete(argumentsFile);
            }
        }

        /// <summary>帧数的合法区间，与 render_turntable.py 里的钳制同源。</summary>
        private const int MinimumFrameCount = 2;

        /// <summary>帧数上限。一帧一次渲，几百帧要跑很久，而这条链的第一步是给人看方向的。</summary>
        private const int MaximumFrameCount = 240;

        /// <summary>缺省帧数。</summary>
        private const int DefaultFrameCount = 12;

        /// <summary>
        /// 转台帧渲：跑 render_turntable.py，返回
        /// <c>{"模式":"环绕","边长":512,"输出帧":[{"序号":0,"路径":…},…]}</c>。
        ///
        /// 这是 3D 动画那一支的第一步。出的帧是**真透明底**（Blender 的 film_transparent），
        /// 不像 2D 那两支要先铺纯色再回本地抠——本机 ComfyUI 的 background_removal 里
        /// 一个模型都没有，而 Blender 本来就能出 alpha。
        ///
        /// 文件名是跨环硬约定 <c>&lt;模型文件名（带后缀）&gt;.frame_&lt;四位序号&gt;.png</c>，
        /// 帧序列描述按这个名字找帧。
        /// </summary>
        /// <param name="request">请求信封，载荷含 输入模型 / 输出目录，可选 边长 / 帧数 / 模式。</param>
        public static BridgeResponse RunTurntable(BridgeRequest request)
        {
            if (!TryGetPayloadString(request, "输入模型", out var inputModel, out var reason))
            {
                return FailureResponse("载荷缺「输入模型」或它不是字符串：" + reason);
            }

            if (!TryGetPayloadString(request, "输出目录", out var outputDirectory, out reason))
            {
                return FailureResponse("载荷缺「输出目录」或它不是字符串：" + reason);
            }

            if (!File.Exists(inputModel))
            {
                return FailureResponse($"输入模型不存在：{inputModel}");
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            {
                return FailureResponse($"输出目录建不出来：{exception.Message}");
            }

            var sideLength = Clamp(ReadPayloadInt(request, "边长", DefaultSideLength), MinimumSideLength, MaximumSideLength);
            var frameCount = Clamp(ReadPayloadInt(request, "帧数", DefaultFrameCount), MinimumFrameCount, MaximumFrameCount);

            // 模式不认识时**在这里就报**，不要让它跑到脚本里才炸：报错要指到「你传的模式不对」，
            // 而不是一段 Python traceback。脚本侧另有同一道判断，谁被单独调用都拦得住。
            var mode = ReadOptionalPayloadString(request, "模式", "环绕");
            if (mode != "环绕" && mode != "自带动画")
            {
                return FailureResponse($"不认识的模式「{mode}」，只有：环绕、自带动画");
            }

            var argumentsObject = new JsonObject
            {
                ["输入模型"] = inputModel,
                ["输出目录"] = outputDirectory,
                ["边长"] = sideLength,
                ["帧数"] = frameCount,
                ["模式"] = mode
            };

            var argumentsFile = WriteTemporaryArgumentsFile(argumentsObject.ToJsonString());
            try
            {
                var run = RunBlender(request, "render_turntable.py", argumentsFile);
                if (!run.Succeeded)
                {
                    return run.Response;
                }

                if (!TryParseJson(run.ResultJson, out var document, out var parseReason))
                {
                    return FailureResponse($"加工站回传的不是合法 JSON：{parseReason}");
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!IsTurntableShape(root))
                    {
                        return FailureResponse("加工站回传的结果不是「输出帧」数组，或数组里有项缺 序号 / 路径");
                    }

                    return BridgeResponse.Success("1.0.0", root.Clone());
                }
            }
            finally
            {
                TryDelete(argumentsFile);
            }
        }

        /// <summary>把一个整数钳回区间。</summary>
        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            return value > maximum ? maximum : value;
        }

        /// <summary>转台结果的形状检查：顶层「输出帧」是数组，每项有 序号（数字）与 路径（非空字符串）。</summary>
        private static bool IsTurntableShape(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object || !IsArray(root, "输出帧"))
            {
                return false;
            }

            foreach (var item in root.GetProperty("输出帧").EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("序号", out var index) || index.ValueKind != JsonValueKind.Number
                    || !item.TryGetProperty("路径", out var path) || path.ValueKind != JsonValueKind.String
                    || string.IsNullOrEmpty(path.GetString()))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>一次 Blender 子进程执行的结果：成功时带回传的 JSON 文本，失败时带协议响应。</summary>
        private sealed class BlenderRun
        {
            public bool Succeeded;
            public BridgeResponse Response;
            public string ResultJson;
        }

        /// <summary>起 Blender 跑指定脚本，异步读 stdout/stderr，超时必杀，解析 BRIDGE_RESULT 行。</summary>
        private static BlenderRun RunBlender(BridgeRequest request, string scriptName, string argumentsFile)
        {
            var blenderExecutable = ReadConfigurationString(request, "可执行文件", DefaultBlenderExecutable);
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var scriptPath = Path.Combine(AppContext.BaseDirectory, scriptName);
            if (!File.Exists(scriptPath))
            {
                return FailedRun("加工站没回结果", $"加工站脚本不存在：{scriptPath}", retryable: false);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = blenderExecutable,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,

                // 脚本回传的 BRIDGE_RESULT 里全是中文键，而重定向流不钉编码就跟着控制台走
                // （本机是 GBK）——那样收回来的 JSON 是乱码，报错还会指到「JSON 不合法」上，
                // 根本看不出是编码问题。脚本侧是 UTF-8，这里必须对上。
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            startInfo.ArgumentList.Add("--background");
            startInfo.ArgumentList.Add("--factory-startup");
            startInfo.ArgumentList.Add("--python");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(argumentsFile);

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            Process process;
            try
            {
                process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data != null) { stdout.AppendLine(eventArgs.Data); } };
                process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data != null) { stderr.AppendLine(eventArgs.Data); } };

                if (!process.Start())
                {
                    return FailedRun("下游不可达", $"起 Blender 失败（进程未能启动）：{blenderExecutable}", retryable: true);
                }
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException || exception is IOException)
            {
                return FailedRun("下游不可达", $"起 Blender 失败：{exception.Message}（可执行文件：{blenderExecutable}）", retryable: true);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Blender 不读 stdin，直接关掉——不关的话管道读侧可能等不到 EOF。
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            if (!process.WaitForExit(checked(timeoutSeconds * 1000)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception || exception is NotSupportedException)
                {
                }

                try
                {
                    process.WaitForExit();
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
                {
                }

                return FailedRun("超时", $"Blender 超过 {timeoutSeconds} 秒未退出，已强制终止整棵进程树", retryable: true);
            }

            process.WaitForExit();

            var resultLine = FindResultLine(stdout.ToString());
            if (resultLine == null)
            {
                return FailedRun(
                    "加工站没回结果",
                    $"Blender 跑完但 stdout 里没有「{ResultMarker.Trim()}」行（stderr 末尾：\n{Tail(stderr.ToString())}）",
                    retryable: false);
            }

            return new BlenderRun { Succeeded = true, ResultJson = resultLine };
        }

        /// <summary>在 stdout 里找 BRIDGE_RESULT 行的后半段；找不到返回 null。</summary>
        private static string FindResultLine(string stdoutText)
        {
            if (string.IsNullOrEmpty(stdoutText))
            {
                return null;
            }

            foreach (var rawLine in stdoutText.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var markerIndex = line.IndexOf(ResultMarker, StringComparison.Ordinal);
                if (markerIndex >= 0)
                {
                    return line.Substring(markerIndex + ResultMarker.Length);
                }
            }

            return null;
        }

        /// <summary>构造失败运行结果。</summary>
        private static BlenderRun FailedRun(string errorCode, string humanText, bool retryable)
        {
            return new BlenderRun { Succeeded = false, Response = BridgeResponse.Failure("1.0.0", errorCode, humanText, retryable) };
        }

        /// <summary>失败响应（不带子进程信息）：载荷/形状校验失败属于调用方参数问题，归「请求不合协议」。</summary>
        private static BridgeResponse FailureResponse(string humanText)
        {
            return BridgeResponse.Failure("1.0.0", "请求不合协议", humanText, retryable: false);
        }

        /// <summary>读请求配置里的字符串键；缺失给缺省值。</summary>
        private static string ReadConfigurationString(BridgeRequest request, string key, string fallback)
        {
            if (request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? fallback;
            }

            return fallback;
        }

        /// <summary>读请求配置里的整数键；缺失、类型不对给缺省值。</summary>
        private static int ReadConfigurationInt(BridgeRequest request, string key, int fallback)
        {
            if (request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return element.GetInt32();
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                }
            }

            return fallback;
        }

        /// <summary>读载荷里的字符串键。</summary>
        private static bool TryGetPayloadString(BridgeRequest request, string key, out string value, out string reason)
        {
            value = "";
            reason = "";
            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty(key, out var element)
                || element.ValueKind != JsonValueKind.String)
            {
                reason = "缺「" + key + "」或它不是字符串";
                return false;
            }

            value = element.GetString() ?? "";
            return true;
        }

        /// <summary>读载荷里的对象键。</summary>
        private static bool TryGetPayloadObject(BridgeRequest request, string key, out JsonElement value, out string reason)
        {
            value = default;
            reason = "";
            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty(key, out var element)
                || element.ValueKind != JsonValueKind.Object)
            {
                reason = "缺「" + key + "」或它不是对象";
                return false;
            }

            value = element;
            return true;
        }

        /// <summary>探测结果形状：顶层 节点/模型/lora 三个数组。</summary>
        private static bool IsProbeShape(JsonElement root)
        {
            return root.ValueKind == JsonValueKind.Object
                && IsArray(root, "节点")
                && IsArray(root, "模型")
                && IsArray(root, "lora");
        }

        /// <summary>三视图结果形状：顶层有「输出图」数组，且每一项都是含 视角 / 路径 两个字符串键的对象。</summary>
        private static bool IsRenderShape(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object || !IsArray(root, "输出图"))
            {
                return false;
            }

            foreach (var item in root.GetProperty("输出图").EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !IsString(item, "视角") || !IsString(item, "路径"))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>读载荷里的可选字符串键；缺失、空串、类型不对都给缺省值。</summary>
        /// <param name="request">请求信封。</param>
        /// <param name="key">键名。</param>
        /// <param name="fallback">缺省值。</param>
        private static string ReadOptionalPayloadString(BridgeRequest request, string key, string fallback)
        {
            if (request.Payload.ValueKind != JsonValueKind.Object
                || !request.Payload.TryGetProperty(key, out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return fallback;
            }

            var text = value.GetString() ?? "";
            return text.Length == 0 ? fallback : text;
        }

        /// <summary>读载荷里的整数键；缺失、类型不对给缺省值。</summary>
        private static int ReadPayloadInt(BridgeRequest request, string key, int fallback)
        {
            if (request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return element.GetInt32();
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                }
            }

            return fallback;
        }

        /// <summary>加工结果形状：四个必填键。</summary>
        private static bool IsProcessShape(JsonElement root)
        {
            return root.ValueKind == JsonValueKind.Object
                && IsString(root, "输出模型")
                && IsString(root, "指标文件")
                && IsArray(root, "执行了的步骤")
                && IsArray(root, "跳过的步骤");
        }

        private static bool IsString(JsonElement root, string key)
        {
            return root.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.String;
        }

        private static bool IsArray(JsonElement root, string key)
        {
            return root.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.Array;
        }

        /// <summary>解析 JSON 文本；失败给可读原因。</summary>
        private static bool TryParseJson(string text, out JsonDocument document, out string reason)
        {
            document = null;
            reason = "";
            try
            {
                document = JsonDocument.Parse(text);
                return true;
            }
            catch (JsonException exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        /// <summary>写临时参数文件到系统临时目录；返回路径。</summary>
        private static string WriteTemporaryArgumentsFile(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "bridge-blender-args-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        /// <summary>删临时文件；删不掉就放着，不影响结果。</summary>
        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>取 stderr 的最后几行；空输出给「（无输出）」。</summary>
        private static string Tail(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "（无输出）";
            }

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var start = Math.Max(0, lines.Length - StderrTailLineCount);
            var builder = new StringBuilder();
            for (var index = start; index < lines.Length; index++)
            {
                builder.AppendLine(lines[index]);
            }

            return builder.ToString().TrimEnd();
        }
    }
}
