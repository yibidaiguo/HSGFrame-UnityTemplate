using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Tripocli
{
    /// <summary>
    /// 起一次 Tripo 命令行，把它的 <c>--json</c> 输出翻成桥协议响应。
    ///
    /// 两种输出形状都是**看准了才写的**，不是猜的：
    /// - **失败**：<c>{"error":"Insufficient credits","exit_code":4,"api_code":2010,"suggestion":"…"}</c>
    ///   ——2026-08-24 真跑过一次（余额 0 的账号），一字不差。
    /// - **成功**：<c>{task_id,type,status,credits_consumed,output_dir,files[],model_file,preview}</c>
    ///   ——读 CLI 自己的 <c>dist/core/task-service.js</c> 与 <c>dist/core/download.js</c> 得来。
    ///   <c>files</c> 里是**文件名不是路径**（CLI 那边 map 了 basename），要拼 <c>output_dir</c> 才是全路径。
    ///   **这一支还没被真回包验证过**（账号余额 0，跑不到成功那一步），所以
    ///   <see cref="ReadModelFile"/> 认不出模型文件时会退回扫目录，并且**如实说是扫出来的**。
    ///
    /// 与线上那条（<c>Bridges/tripo</c>）说同一套错误码：额度不足 / 凭据无效 / 下游不可达 / 超时 / 下游报错。
    /// 错误码对齐是这两条路能互为候选的前提——码不一样的话，「失败转移」那一档没法判断该不该换人。
    /// </summary>
    public static class TripoCliRunner
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数。生成一个模型常常要几分钟，给足。</summary>
        private const int DefaultTimeoutSeconds = 900;

        /// <summary>模型文件的扩展名，扫目录认产物时用。</summary>
        private static readonly string[] ModelExtensions = { ".glb", ".gltf", ".fbx", ".obj", ".usdz" };

        /// <summary>
        /// generate 动作：起 <c>tripo make</c>。
        /// 载荷 <c>{"提示词":"…","输出目录":"…"}</c>，可选 <c>参考图地址</c>（本地文件路径或 URL）。
        /// 返回 <c>{"模型文件":"…","task_id":"…","状态":"…","消耗":N,"产物目录":"…","模型文件怎么认出来的":"…"}</c>。
        /// </summary>
        /// <param name="request">请求信封。</param>
        public static BridgeResponse RunGenerate(BridgeRequest request)
        {
            var referenceImage = ReadPayloadString(request, "参考图地址");
            var prompt = ReadPayloadString(request, "提示词");
            var outputDirectory = ReadPayloadString(request, "输出目录");

            if (outputDirectory.Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「输出目录」", retryable: false);
            }

            // 提示词与参考图至少要有一样。两样都空时报出来而不是让 CLI 去报——
            // CLI 那句话是英文的用法说明，指不到「你这次请求里什么都没带」。
            if (prompt.Length == 0 && referenceImage.Length == 0)
            {
                return Failure("请求不合协议", "载荷里「提示词」与「参考图地址」都是空的，没有可生成的输入", retryable: false);
            }

            var executable = ReadConfigurationString(request, "可执行文件", "");
            if (executable.Length == 0)
            {
                return Failure(
                    "本机配置错误",
                    "没配「可执行文件」：Tripo CLI 不在 PATH 里，这一格要填绝对路径（Windows 上通常是 …\\Tripo\\tripo.cmd）",
                    retryable: false);
            }

            if (!File.Exists(executable))
            {
                return Failure("本机配置错误", $"配的「可执行文件」不存在：{executable}", retryable: false);
            }

            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var scenario = ReadConfigurationString(request, "场景预设", "");
            var chain = ReadConfigurationString(request, "加工链", "");

            var arguments = new List<string> { "make" };

            // 参考图优先当输入：CLI 的 make 认「文字 / 图片文件 / URL」同一个位置参数。
            // 两样都给时把图片放前面、提示词也带上——CLI 会当成带文字引导的图生模型。
            if (referenceImage.Length > 0)
            {
                arguments.Add(referenceImage);
            }

            if (prompt.Length > 0)
            {
                arguments.Add(prompt);
            }

            if (scenario.Trim().Length > 0)
            {
                arguments.Add("--for");
                arguments.Add(scenario.Trim());
            }

            if (chain.Trim().Length > 0)
            {
                arguments.Add("--then");
                arguments.Add(chain.Trim());
            }

            arguments.Add("--out");
            arguments.Add(outputDirectory);

            // --json 机器可读；--yes 免交互（不加的话它会等人按回车，而这里没有人）；
            // --no-open 别弹浏览器；--quiet 把进度日志压到 stderr 之外。
            // --timeout 是 CLI 自己的等待上限，比我们杀进程的上限短一点，
            // 好让它有机会自己吐一句人话，而不是被我们从背后打死。
            arguments.Add("--json");
            arguments.Add("--yes");
            arguments.Add("--no-open");
            arguments.Add("--quiet");
            arguments.Add("--timeout");
            arguments.Add(Math.Max(30, timeoutSeconds - 30).ToString(CultureInfo.InvariantCulture));

            var run = Execute(executable, arguments, timeoutSeconds, outputDirectory);
            if (run.Failure != null)
            {
                return run.Failure;
            }

            var payload = ParseJsonObject(run.StandardOutput);

            if (run.ExitCode != 0)
            {
                return MapCliFailure(run, payload);
            }

            if (payload == null)
            {
                return Failure(
                    "下游报错",
                    "CLI 退出码是 0，但 stdout 上没有可解析的 JSON——不敢当成成功。"
                        + "\nstdout 末尾：\n" + Tail(run.StandardOutput)
                        + "\nstderr 末尾：\n" + Tail(run.StandardError),
                    retryable: false);
            }

            var producedDirectory = ReadString(payload, "output_dir");
            var modelFile = ReadModelFile(payload, producedDirectory, outputDirectory, out var howFound);
            if (modelFile.Length == 0)
            {
                return Failure(
                    "下游报错",
                    "CLI 报成功，但既没在 JSON 里给出模型文件，落点目录里也扫不到模型文件。"
                        + "\n产物目录：" + (producedDirectory.Length > 0 ? producedDirectory : outputDirectory)
                        + "\nstdout 末尾：\n" + Tail(run.StandardOutput),
                    retryable: false);
            }

            var result = new JsonObject
            {
                ["模型文件"] = modelFile,
                ["task_id"] = ReadString(payload, "task_id"),
                ["状态"] = ReadString(payload, "status"),
                ["产物目录"] = producedDirectory.Length > 0 ? producedDirectory : outputDirectory,
                ["模型文件怎么认出来的"] = howFound
            };

            if (payload.TryGetPropertyValue("credits_consumed", out var credits) && credits != null)
            {
                result["消耗"] = credits.DeepClone();
            }

            var preview = ReadString(payload, "preview");
            if (preview.Length > 0)
            {
                result["预览图"] = preview;
            }

            return Success(JsonSerializer.SerializeToElement(result));
        }

        /// <summary>
        /// balance 动作：起 <c>tripo balance --json</c>，回 <c>{"余额":N,"冻结":N}</c>。
        /// 与线上那条同名同形，面板与 art.plan 那边不必分辨自己连的是哪一条。
        /// </summary>
        /// <param name="request">请求信封。</param>
        public static BridgeResponse RunBalance(BridgeRequest request)
        {
            var executable = ReadConfigurationString(request, "可执行文件", "");
            if (executable.Length == 0 || !File.Exists(executable))
            {
                return Failure("本机配置错误", $"「可执行文件」没配或不存在：{executable}", retryable: false);
            }

            var run = ExecuteInScratch(executable, new List<string> { "balance", "--json", "--quiet" },
                ReadConfigurationInt(request, "超时秒", 60));
            if (run.Failure != null)
            {
                return run.Failure;
            }

            var payload = ParseJsonObject(run.StandardOutput);
            if (run.ExitCode != 0)
            {
                return MapCliFailure(run, payload);
            }

            if (payload == null)
            {
                return Failure("下游报错", "balance 回的不是 JSON：\n" + Tail(run.StandardOutput), retryable: false);
            }

            var result = new JsonObject();
            result["余额"] = payload.TryGetPropertyValue("balance", out var balance) && balance != null
                ? balance.DeepClone()
                : 0;
            result["冻结"] = payload.TryGetPropertyValue("frozen", out var frozen) && frozen != null
                ? frozen.DeepClone()
                : 0;
            return Success(JsonSerializer.SerializeToElement(result));
        }

        /// <summary>
        /// caps 动作：这条路能干什么，**并把清单写到载荷给的「输出路径」**。
        ///
        /// 写盘不是可选项：<c>bridge.probe</c> 的契约就是「桥往那个路径写一份
        /// <c>{节点,模型,lora}</c>」，面板下游页读的正是那份文件。只回响应不写盘的话，
        /// 命令会报「探测输出已写到 …」而那个文件根本不存在——假成功。
        ///
        /// **不去问 CLI 要模型清单**（它没有这么个子命令），所以「模型」那一栏放的是
        /// CLI 自己 <c>--model</c> 认的几个版本名。真正有价值的是后面几格：
        /// 装没装、登没登、余额多少——能力探测报「装了但余额 0」远比报一串模型名有用。
        /// </summary>
        /// <param name="request">请求信封，载荷含「输出路径」。</param>
        public static BridgeResponse RunCaps(BridgeRequest request)
        {
            var outputPath = ReadPayloadString(request, "输出路径");
            if (outputPath.Length == 0)
            {
                return Failure("请求不合协议", "载荷缺「输出路径」：探测清单要落盘，面板读的是那份文件", retryable: false);
            }

            var executable = ReadConfigurationString(request, "可执行文件", "");
            var result = new JsonObject
            {
                // 这三栏是 bridge.probe 的契约形状，一格都不许少——少了它数出来是 0，
                // 而 0 与「这个 driver 没有节点这个概念」长得一样。
                ["节点"] = new JsonArray(),
                ["模型"] = new JsonArray(),
                ["lora"] = new JsonArray(),
                ["形态"] = "本地",
                ["可执行文件"] = executable,
                ["装了"] = executable.Length > 0 && File.Exists(executable)
            };

            if (executable.Length == 0 || !File.Exists(executable))
            {
                result["可用"] = false;
                result["为什么"] = "「可执行文件」没配或不存在";
                return WriteAndReturn(result, outputPath);
            }

            var version = ExecuteInScratch(executable, new List<string> { "--version" }, 30);
            var versionText = version.Failure == null ? version.StandardOutput.Trim() : "";
            result["版本"] = versionText;

            // 把 CLI 自己报成一条「节点」。**能力对账查的就是这一栏**：
            // dependencies.json 里那条依赖叫 tripo-cli、类别是「节点」，
            // 对账拿名字去探测结果的「节点」里找，找不到就判 0/1。
            // blender 那条同理（它的 probe.py 也把自己报成节点「blender」）。
            //
            // 名字必须与 dependencies.json 里那条**逐字一致**，否则对账永远对不上——
            // 而那种红看起来像「CLI 没装」，人会去重装一个本来就在的东西。
            result["节点"] = new JsonArray
            {
                new JsonObject { ["名"] = "tripo-cli", ["版本"] = versionText, ["hash"] = "" }
            };

            var who = ExecuteInScratch(executable, new List<string> { "whoami", "--json", "--quiet" }, 60);
            var whoPayload = who.Failure == null ? ParseJsonObject(who.StandardOutput) : null;
            if (whoPayload == null)
            {
                result["可用"] = false;
                result["为什么"] = "问不出登录状态（还没跑过 tripo login？）";
                return WriteAndReturn(result, outputPath);
            }

            var balanceValue = whoPayload.TryGetPropertyValue("balance", out var node) && node != null
                ? node.ToString()
                : "";
            result["登录了"] = true;
            result["区域"] = ReadString(whoPayload, "region");
            result["余额"] = balanceValue;

            // 「模型」那一栏填 CLI 的 --model 认的版本名。写死在这里是实话：
            // 它们来自 CLI 的 --help，不是下游报的——所以旁边挂一句说明，
            // 别让人以为这是探回来的。
            var models = new JsonArray();
            foreach (var name in new[] { "tripo-v3.1", "tripo-p1" })
            {
                models.Add(new JsonObject { ["名"] = name, ["版本"] = name, ["hash"] = "" });
            }

            result["模型"] = models;
            result["模型清单来源"] = "CLI 的 --model 帮助文本，不是下游探回来的";

            // 余额 0 时**明说不可用**，别让上层拿着「装了、登了」就去派活——
            // 那会在真提交那一刻才炸，而那时人已经等了几分钟。
            var hasCredits = double.TryParse(balanceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) && amount > 0;
            result["可用"] = hasCredits;
            if (!hasCredits)
            {
                result["为什么"] = "登录了但余额是 " + (balanceValue.Length == 0 ? "未知" : balanceValue) + "，提交必然被判额度不足";
            }

            return WriteAndReturn(result, outputPath);
        }

        /// <summary>把探测清单写到指定路径再返回；写不动就报失败——写不动时报成功就是假成功。</summary>
        /// <param name="result">探测清单。</param>
        /// <param name="outputPath">落盘路径。</param>
        private static BridgeResponse WriteAndReturn(JsonObject result, string outputPath)
        {
            try
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outputPath, result.ToJsonString(), new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Failure("请求不合协议", $"探测输出写盘失败：{exception.Message}", retryable: false);
            }

            return Success(JsonSerializer.SerializeToElement(result));
        }

        /// <summary>
        /// 认模型文件：先信 JSON 里的 <c>model_file</c>，再拿 <c>files</c> 拼 <c>output_dir</c>，
        /// 最后才扫目录。**每一档都把「怎么认出来的」交出去**——
        /// 扫目录那一档有可能捡到上一次的产物，调用方有权知道这一件是猜的还是下游给的。
        /// </summary>
        /// <param name="payload">CLI 的 JSON 输出。</param>
        /// <param name="producedDirectory">CLI 报的产物目录。</param>
        /// <param name="requestedDirectory">我们要求的落点目录。</param>
        /// <param name="howFound">怎么认出来的，给人看。</param>
        private static string ReadModelFile(JsonObject payload, string producedDirectory, string requestedDirectory, out string howFound)
        {
            var direct = ReadString(payload, "model_file");
            if (direct.Length > 0)
            {
                howFound = "下游 JSON 的 model_file";
                return direct;
            }

            var baseDirectory = producedDirectory.Length > 0 ? producedDirectory : requestedDirectory;
            if (payload.TryGetPropertyValue("files", out var files) && files is JsonArray array)
            {
                foreach (var item in array)
                {
                    var name = item?.ToString() ?? "";
                    if (name.Length > 0 && ModelExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
                    {
                        howFound = "下游 JSON 的 files 里那个模型文件名，拼上 output_dir";
                        return Path.Combine(baseDirectory, name);
                    }
                }
            }

            howFound = "JSON 里没有，扫落点目录扫出来的（有可能捡到上一次的产物）";
            return ScanForModel(baseDirectory);
        }

        /// <summary>在目录树里找最近改动的那个模型文件；找不到给空串。</summary>
        private static string ScanForModel(string directory)
        {
            if (directory.Length == 0 || !Directory.Exists(directory))
            {
                return "";
            }

            try
            {
                return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(path => ModelExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(path => new FileInfo(path).LastWriteTimeUtc)
                    .FirstOrDefault() ?? "";
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return "";
            }
        }

        /// <summary>
        /// 把 CLI 的失败翻成桥协议错误码。判据优先用 <c>api_code</c> / <c>exit_code</c>
        /// 这些结构化的东西，**文案匹配只当兜底**——文案会随版本改，改了之后
        /// 「额度不足」会悄悄降级成「下游报错」，而那两个码的重试语义完全不同。
        /// </summary>
        private static BridgeResponse MapCliFailure(CliRun run, JsonObject payload)
        {
            var message = payload != null ? ReadString(payload, "error") : "";
            var suggestion = payload != null ? ReadString(payload, "suggestion") : "";
            var apiCode = payload != null && payload.TryGetPropertyValue("api_code", out var code) && code != null
                ? code.ToString()
                : "";

            var humanText = (message.Length > 0 ? message : "CLI 退出码 " + run.ExitCode)
                + (suggestion.Length > 0 ? "（" + suggestion + "）" : "")
                + (apiCode.Length > 0 ? " [api_code " + apiCode + "]" : "");

            // exit_code 4 与 api_code 2010 是「额度不足」——2026-08-24 实证。
            if (run.ExitCode == 4 || apiCode == "2010"
                || message.IndexOf("Insufficient credits", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Failure("额度不足", humanText, retryable: false);
            }

            if (message.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0
                && message.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Failure("凭据无效", humanText + "；跑一次 tripo login 重新登录", retryable: false);
            }

            if (message.IndexOf("not logged in", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("no api key", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Failure("凭据无效", humanText + "；这条路的钥匙在 ~/.tripo/config.json，跑 tripo login 就有", retryable: false);
            }

            if (message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Failure("超时", humanText, retryable: true);
            }

            if (message.IndexOf("ENOTFOUND", StringComparison.Ordinal) >= 0
                || message.IndexOf("ECONNREFUSED", StringComparison.Ordinal) >= 0
                || message.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Failure("下游不可达", humanText, retryable: true);
            }

            var tail = run.StandardError.Trim().Length > 0 ? "\nstderr 末尾：\n" + Tail(run.StandardError) : "";
            return Failure("下游报错", humanText + tail, retryable: false);
        }

        /// <summary>一次子进程执行的结果。</summary>
        private sealed class CliRun
        {
            /// <summary>退出码。</summary>
            public int ExitCode { get; set; }

            /// <summary>stdout 全文。</summary>
            public string StandardOutput { get; set; } = "";

            /// <summary>stderr 全文。</summary>
            public string StandardError { get; set; } = "";

            /// <summary>起不来或超时时的协议响应；正常为 null。</summary>
            public BridgeResponse Failure { get; set; }
        }

        /// <summary>
        /// 起一次子进程，异步读两条流，超时杀整棵进程树。
        ///
        /// 两条流都钉成 UTF-8：CLI 会吐中文（它自己带 i18n），
        /// 不钉就跟着控制台走（本机 GBK），收回来是乱码，而报错会指到「JSON 不合法」上。
        /// </summary>
        /// <summary>
        /// 起一次只读的 CLI（探测那几支：--version / whoami / balance）。
        /// 工作目录一律给临时目录——这几支不产出任何东西，也就不该在仓库里留下任何东西。
        /// </summary>
        /// <param name="executable">CLI 可执行文件。</param>
        /// <param name="arguments">命令行参数。</param>
        /// <param name="timeoutSeconds">超时秒数。</param>
        private static CliRun ExecuteInScratch(string executable, List<string> arguments, int timeoutSeconds)
        {
            return Execute(executable, arguments, timeoutSeconds, Path.GetTempPath());
        }

        /// <summary>
        /// 起一次 CLI。
        /// </summary>
        /// <param name="executable">CLI 可执行文件。</param>
        /// <param name="arguments">命令行参数。</param>
        /// <param name="timeoutSeconds">超时秒数。</param>
        /// <param name="workingDirectory">
        /// 工作目录。**必须显式给**：不给的话进程继承调用方的当前目录，
        /// 而调用方是从仓库根跑的——Tripo CLI 会在那儿拉一个 <c>.tripo/</c>
        /// 放自己的 <c>last_task_id</c>，于是仓库根多出一个没人认识的目录，
        /// 改动文件白名单那道门禁当场变红，而红的原因跟这次生成毫无关系（真踩过）。
        /// 生成时给输出目录（状态跟着产物走），探测时给临时目录（什么都不该留下）。
        /// </param>
        private static CliRun Execute(
            string executable, List<string> arguments, int timeoutSeconds, string workingDirectory)
        {
            var effectiveWorkingDirectory = workingDirectory;
            if (string.IsNullOrWhiteSpace(effectiveWorkingDirectory) || !Directory.Exists(effectiveWorkingDirectory))
            {
                // 给不出一个存在的目录时退到临时目录——**绝不退回「继承调用方」**，
                // 那正是要防的那种落法。
                effectiveWorkingDirectory = Path.GetTempPath();
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = effectiveWorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

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
                    return new CliRun { Failure = Failure("下游不可达", "起 Tripo CLI 失败（进程未能启动）：" + executable, retryable: true) };
                }
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException || exception is IOException)
            {
                return new CliRun { Failure = Failure("下游不可达", $"起 Tripo CLI 失败：{exception.Message}（可执行文件：{executable}）", retryable: true) };
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // CLI 不读 stdin（--yes 已经免掉交互），关掉；不关的话它可能等在读侧。
            try
            {
                process.StandardInput.Close();
            }
            catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException)
            {
            }

            if (!process.WaitForExit(checked(Math.Max(1, timeoutSeconds) * 1000)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception || exception is NotSupportedException)
                {
                }

                return new CliRun
                {
                    Failure = Failure("超时", $"Tripo CLI 超过 {timeoutSeconds} 秒未退出，已强制终止整棵进程树", retryable: true)
                };
            }

            process.WaitForExit();
            return new CliRun
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString()
            };
        }

        /// <summary>
        /// 从一段输出里挑出那份 JSON 对象。
        /// **逐行倒着找**，不是整段解析：<c>--quiet</c> 压掉的只是进度条，
        /// 版本提醒之类的东西照样会跑到 stdout 上，整段解析会被它们一句话搞挂。
        /// </summary>
        private static JsonObject ParseJsonObject(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (var index = lines.Length - 1; index >= 0; index--)
            {
                var line = lines[index].Trim();
                if (line.Length == 0 || line[0] != '{')
                {
                    continue;
                }

                try
                {
                    if (JsonNode.Parse(line) is JsonObject parsed)
                    {
                        return parsed;
                    }
                }
                catch (JsonException)
                {
                }
            }

            return null;
        }

        /// <summary>读一个字符串键；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonObject payload, string key)
        {
            if (payload == null || !payload.TryGetPropertyValue(key, out var node) || node == null)
            {
                return "";
            }

            return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToString();
        }

        /// <summary>取一段输出的末尾若干行，报错时贴给人看。</summary>
        private static string Tail(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            var take = Math.Min(8, lines.Length);
            return string.Join("\n", lines.Skip(lines.Length - take));
        }

        /// <summary>读请求配置里的字符串键；缺失给缺省值。</summary>
        private static string ReadConfigurationString(BridgeRequest request, string key, string fallback)
        {
            return request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        /// <summary>读请求配置里的整数键；缺失给缺省值。</summary>
        private static int ReadConfigurationInt(BridgeRequest request, string key, int fallback)
        {
            return request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number)
                ? number
                : fallback;
        }

        /// <summary>读载荷里的字符串键；缺失给空串。</summary>
        private static string ReadPayloadString(BridgeRequest request, string key)
        {
            return request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        /// <summary>成功响应。</summary>
        private static BridgeResponse Success(JsonElement payload)
        {
            return BridgeResponse.Success(ContractVersion, payload);
        }

        /// <summary>失败响应。</summary>
        private static BridgeResponse Failure(string code, string humanText, bool retryable)
        {
            return BridgeResponse.Failure(ContractVersion, code, humanText, retryable);
        }
    }
}
