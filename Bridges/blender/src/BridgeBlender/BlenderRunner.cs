using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
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
                UseShellExecute = false
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
