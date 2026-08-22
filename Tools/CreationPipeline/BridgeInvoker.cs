using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一次下游调用的结果：成功时带载荷，失败时带错误码 / 人话 / 可重试与是否超时。
    /// 失败时的错误信封来自子进程原样带回，或由本进程按协议错误码构造。
    /// </summary>
    public sealed class BridgeCallResult
    {
        /// <summary>
        /// 构造一次调用的结果。
        /// </summary>
        /// <param name="succeeded">是否成功。</param>
        /// <param name="timedOut">是否因超时被强制终止。</param>
        /// <param name="errorCode">失败时的错误码；成功时为 ""。</param>
        /// <param name="humanText">失败时给人看的人话；成功时为 ""。</param>
        /// <param name="retryable">失败时是否值得重试；成功时为 false。</param>
        /// <param name="payload">成功时的载荷。</param>
        /// <param name="modelNote">这次用了哪个模型、依据是什么；没什么可说时为空串。</param>
        public BridgeCallResult(bool succeeded, bool timedOut, string errorCode, string humanText, bool retryable, JsonElement payload, string modelNote = "")
        {
            Succeeded = succeeded;
            TimedOut = timedOut;
            ErrorCode = errorCode ?? "";
            HumanText = humanText ?? "";
            Retryable = retryable;
            Payload = payload;
            ModelNote = modelNote ?? "";
        }

        /// <summary>是否成功。</summary>
        public bool Succeeded { get; }

        /// <summary>是否因超时被强制终止。</summary>
        public bool TimedOut { get; }

        /// <summary>失败时的错误码；成功时为 ""。</summary>
        public string ErrorCode { get; }

        /// <summary>失败时给人看的人话；成功时为 ""。</summary>
        public string HumanText { get; }

        /// <summary>失败时是否值得重试；成功时为 false。</summary>
        public bool Retryable { get; }

        /// <summary>成功时的载荷。</summary>
        public JsonElement Payload { get; }

        /// <summary>
        /// 这次用了哪个模型、依据是什么。空串 = 没什么可说的（配置里钉死了一个具体模型）。
        /// 「自动」那一档挑了谁**必须摆给人看**——不摆，那一档就成了黑箱。
        /// </summary>
        public string ModelNote { get; }

        /// <summary>复制一份，换上模型账。</summary>
        /// <param name="modelNote">模型账。</param>
        public BridgeCallResult WithModelNote(string modelNote)
        {
            return string.IsNullOrEmpty(modelNote)
                ? this
                : new BridgeCallResult(Succeeded, TimedOut, ErrorCode, HumanText, Retryable, Payload, modelNote);
        }

        /// <summary>构造成功结果。</summary>
        public static BridgeCallResult Success(JsonElement payload)
        {
            return new BridgeCallResult(true, false, "", "", false, payload);
        }

        /// <summary>构造失败结果。</summary>
        public static BridgeCallResult Failure(string errorCode, string humanText, bool retryable)
        {
            return new BridgeCallResult(false, false, errorCode, humanText, retryable, default);
        }

        /// <summary>构造超时结果。</summary>
        public static BridgeCallResult AsTimedOut(string errorCode, string humanText)
        {
            return new BridgeCallResult(false, true, errorCode, humanText, true, default);
        }
    }

    /// <summary>
    /// 按 port 调用一次的结果：除了调用本身的结果，还带上**实际用的是哪个 driver**
    /// 与**逐次尝试的账**。失败转移最容易骗人的地方是「报了个错，但你不知道它试过谁」——
    /// 所以尝试账是这个类型存在的理由，不是附赠品。
    /// </summary>
    public sealed class BridgePortCallResult
    {
        /// <summary>
        /// 构造一次按 port 调用的结果。
        /// </summary>
        /// <param name="result">调用结果（成功时是成功那一次的，失败时是最后一次的）。</param>
        /// <param name="driverName">实际用的 driver 名；一个都没试成时是最后试的那个。</param>
        /// <param name="attempts">逐次尝试的账，每条形如「driver → 错误码：人话」。</param>
        public BridgePortCallResult(BridgeCallResult result, string driverName, IReadOnlyList<string> attempts)
        {
            Result = result;
            DriverName = driverName ?? "";
            Attempts = attempts ?? Array.Empty<string>();
        }

        /// <summary>调用结果。</summary>
        public BridgeCallResult Result { get; }

        /// <summary>实际用的 driver 名。</summary>
        public string DriverName { get; }

        /// <summary>逐次尝试的账，每条形如「driver → 错误码：人话」。成功且一次就中时只有零条。</summary>
        public IReadOnlyList<string> Attempts { get; }
    }

    /// <summary>
    /// 下游 driver 调用器：起子进程、喂 stdin 收 stdout、超时必杀。
    /// 协议是子进程 stdin 收一份 JSON、stdout 出一份 JSON、退出码 0/非 0。
    /// stdout 上只许有那一份 JSON；子进程自己的日志都在 stderr，解析失败时从 stderr 末尾找原因。
    ///
    /// 本文件住在下游边界门禁的扫描根里：任何下游 driver 名都不许出现在这里，
    /// driver 名一律走参数（决策 17）。
    /// </summary>
    public static class BridgeInvoker
    {
        /// <summary>stderr 末尾最多带几行进错误人话。</summary>
        private const int StderrTailLineCount = 5;

        /// <summary>
        /// **不许失败转移**的错误码。除这几个之外一律可以换下一个候选。
        ///
        /// 判据不是「谁的错」，是「这个下游有没有可能已经干了活」——
        /// 换人重跑一次生图是要再花一次钱的，所以只要**说不清上一次干到哪了**，就不许换人：
        ///
        /// - 内部错误：桥自己崩在半路。图可能已经出了、钱已经花了，只是落盘那步炸的。
        /// - 响应不合协议：stdout 不是协议 JSON，我们根本不知道对面做了什么。
        /// - 本机配置错误 / 路由表错误：跟具体哪个 driver 无关，换谁都一样炸，白等一轮。
        ///
        /// 「超时」**在转移之列**——它才是失败转移最常见的触发场景（下游挂死）。
        /// 代价是：万一那次调用其实在下游跑完了，就会重复计费一次。这条写在
        /// 《创作管线 · 要你亲手填的东西》里，选「失败转移」的人得知道自己买的是什么。
        /// </summary>
        private static readonly string[] NonFailoverErrorCodes =
        {
            "内部错误",
            "响应不合协议",
            "本机配置错误",
            "路由表错误"
        };

        /// <summary>
        /// 按 port 调用：从域路由表拿到候选清单与策略，按策略逐个试。
        ///
        /// 「首选固定」只试第一个；「失败转移」顺着往下试，直到有一个成功或全部试完。
        /// 全部失败时，返回的人话把**每一次尝试的错都摆出来**——只报最后一个的话，
        /// 看的人会以为首选压根没被试过。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="portName">port 名，如「生图」「模型生成」；driver 名一律不进代码。</param>
        /// <param name="action">动作，如「caps」「generate」。</param>
        /// <param name="payload">业务载荷。</param>
        /// <param name="timeoutSeconds">单个候选的超时秒数；失败转移时**每个候选各算各的**。</param>
        /// <param name="modelOverride">本次调用指定的模型；空串表示按本机配置来（配「自动」时现挑）。</param>
        public static BridgePortCallResult InvokeByPort(
            string repositoryRoot,
            string portName,
            string action,
            JsonElement payload,
            int timeoutSeconds,
            string modelOverride = "")
        {
            var routeTable = BridgeRouteTable.Load(repositoryRoot);
            if (!routeTable.Loaded)
            {
                return new BridgePortCallResult(
                    BridgeCallResult.Failure("路由表错误", routeTable.LoadFailureReason, retryable: false),
                    "",
                    Array.Empty<string>());
            }

            if (!routeTable.TryResolveRoute(portName, out var route, out var routeReason))
            {
                return new BridgePortCallResult(
                    BridgeCallResult.Failure("域未路由", $"「{portName}」域没有可用的 driver：{routeReason}", retryable: false),
                    "",
                    Array.Empty<string>());
            }

            var candidates = route.AllowsFailover
                ? route.Candidates
                : new[] { route.PreferredDriverName };

            var attempts = new List<string>();
            BridgeCallResult lastResult = null;
            var lastDriverName = "";

            foreach (var candidate in candidates)
            {
                lastDriverName = candidate;
                var result = Invoke(repositoryRoot, candidate, action, payload, timeoutSeconds, modelOverride);
                if (result.Succeeded)
                {
                    return new BridgePortCallResult(result, candidate, attempts);
                }

                lastResult = result;
                attempts.Add($"{candidate} → {result.ErrorCode}：{result.HumanText}");

                if (Array.IndexOf(NonFailoverErrorCodes, result.ErrorCode) >= 0)
                {
                    // 说不清上一次干到哪了，不许换人重跑。
                    attempts.Add($"（就此打住：错误码「{result.ErrorCode}」说不清这次调用干到哪了，换人重跑有重复计费的风险）");
                    break;
                }
            }

            var humanText = BuildFailoverFailureText(portName, route, attempts);
            return new BridgePortCallResult(
                new BridgeCallResult(false, lastResult?.TimedOut ?? false, lastResult?.ErrorCode ?? "域未路由", humanText, lastResult?.Retryable ?? false, default),
                lastDriverName,
                attempts);
        }

        /// <summary>把逐次尝试的账拼成一段人能读的失败说明：试了几个、策略是什么、每个错在哪。</summary>
        /// <param name="portName">port 名。</param>
        /// <param name="route">这个 port 的路由。</param>
        /// <param name="attempts">逐次尝试的账。</param>
        private static string BuildFailoverFailureText(string portName, PortRoute route, IReadOnlyList<string> attempts)
        {
            var builder = new StringBuilder();
            builder.Append('「').Append(portName).Append("」域调用失败（策略：").Append(route.Strategy);
            if (route.Candidates.Count > 1)
            {
                builder.Append("，候选 ").Append(route.Candidates.Count).Append(" 个：").Append(string.Join("、", route.Candidates));
            }

            builder.AppendLine("）：");
            for (var index = 0; index < attempts.Count; index++)
            {
                builder.Append("  ").Append(index + 1).Append(". ").AppendLine(attempts[index]);
            }

            if (!route.AllowsFailover && route.Candidates.Count > 1)
            {
                builder.AppendLine($"  （策略是「{PortRoute.FixedPreferredStrategy}」，所以只试了首选；要让它自动换人把策略改成「{PortRoute.FailoverStrategy}」）");
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// 调用一次下游 driver：按 driver 自述、路由表与本机配置拼请求信封，起子进程执行，
        /// 收 stdout 解析响应。超时必杀；stdout 不是协议 JSON 时给出带 stderr 末尾的错误。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称，一律走参数。</param>
        /// <param name="action">动作，如「caps」「process」。</param>
        /// <param name="payload">业务载荷，拼进请求信封的「载荷」。</param>
        /// <param name="timeoutSeconds">超时秒数；超过即强制终止整棵进程树。</param>
        /// <param name="modelOverride">本次调用指定的模型；空串表示按本机配置来（配「自动」时现挑）。</param>
        public static BridgeCallResult Invoke(string repositoryRoot, string driverName, string action, JsonElement payload, int timeoutSeconds, string modelOverride = "")
        {
            BridgeDriverDescriptor descriptor;
            try
            {
                descriptor = BridgeDriverDescriptor.Load(repositoryRoot, driverName);
            }
            catch (InvalidOperationException exception)
            {
                return BridgeCallResult.Failure("驱动自述缺失", exception.Message, retryable: false);
            }

            if (descriptor.Ports.Count == 0)
            {
                return BridgeCallResult.Failure("域未路由", $"driver「{driverName}」的自述没有声明任何 port", retryable: false);
            }

            var routeTable = BridgeRouteTable.Load(repositoryRoot);
            if (!routeTable.Loaded)
            {
                return BridgeCallResult.Failure("路由表错误", routeTable.LoadFailureReason, retryable: false);
            }

            if (!routeTable.TryResolveImplementation(descriptor.ImplementationName, out var executable, out var arguments, out var implementationReason))
            {
                return BridgeCallResult.Failure("实现未配置", $"把实现名「{descriptor.ImplementationName}」解析成可执行时失败：{implementationReason}", retryable: false);
            }

            var localSettings = LocalBridgeSettings.Load(repositoryRoot);
            if (!localSettings.Loaded)
            {
                return BridgeCallResult.Failure("本机配置错误", localSettings.LoadFailureReason, retryable: false);
            }

            var configuration = BuildConfiguration(localSettings, descriptor);

            // 「自动」这一档在这里落地：整条链路只有这一处把配置值换成真模型名，
            // 桥永远收不到哨兵。哪个字段是模型字段由 driver 自述声明，不按 driver 名猜（决策 17）。
            if (!ApplyModelSelection(repositoryRoot, driverName, descriptor, configuration, modelOverride, out var modelNote, out var selectionFailure))
            {
                return selectionFailure;
            }

            var request = new BridgeRequest("1.0.0", descriptor.Ports[0], action, JsonSerializer.SerializeToElement(configuration), payload);

            return RunSubprocess(repositoryRoot, executable, arguments, request, timeoutSeconds).WithModelNote(modelNote);
        }

        /// <summary>把 driver 的本机配置与密钥字段拼成请求信封的「配置」对象（还没定稿，模型那一格随后还要解析）。</summary>
        private static JsonObject BuildConfiguration(LocalBridgeSettings localSettings, BridgeDriverDescriptor descriptor)
        {
            var configuration = new JsonObject();
            if (localSettings.TryGetDriverConfiguration(descriptor.Name, out var driverConfiguration)
                && driverConfiguration.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in driverConfiguration.EnumerateObject())
                {
                    configuration[property.Name] = JsonNode.Parse(property.Value.GetRawText());
                }
            }

            // 密钥字段的值只进请求信封的「配置」，这是它唯一被允许出现的地方。
            foreach (var secretFieldName in descriptor.SecretFieldNames)
            {
                if (localSettings.TryGetSecret(secretFieldName, out var secretValue))
                {
                    configuration[secretFieldName] = secretValue;
                }
            }

            return configuration;
        }

        /// <summary>
        /// 把配置里的模型那一格定稿：本次调用指定的盖过配置，配「自动」时从上次探测的清单里现挑。
        /// 解析结果为空串时**把这个键从配置里删掉**——留一个空串会被桥读成「配了个空模型」。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名。</param>
        /// <param name="descriptor">driver 自述。</param>
        /// <param name="configuration">正在拼的配置对象，就地改。</param>
        /// <param name="modelOverride">本次调用指定的模型。</param>
        /// <param name="modelNote">给人看的账。</param>
        /// <param name="failure">判定失败时的结果；返回 true 时不要看它。</param>
        private static bool ApplyModelSelection(
            string repositoryRoot,
            string driverName,
            BridgeDriverDescriptor descriptor,
            JsonObject configuration,
            string modelOverride,
            out string modelNote,
            out BridgeCallResult failure)
        {
            modelNote = "";
            failure = null;

            var fieldName = descriptor.ModelFieldName;
            if (fieldName.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(modelOverride))
                {
                    failure = BridgeCallResult.Failure(
                        "本机配置错误",
                        $"driver「{driverName}」的自述里没有哪个字段声明「选项来源: 探测.模型」，这次调用给的模型「{modelOverride.Trim()}」无处可放",
                        retryable: false);
                    return false;
                }

                return true;
            }

            var configuredValue = configuration[fieldName] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
            var resolved = ModelSelection.Resolve(repositoryRoot, driverName, configuredValue, modelOverride, out modelNote);
            if (resolved.Length == 0)
            {
                configuration.Remove(fieldName);
            }
            else
            {
                configuration[fieldName] = resolved;
            }

            return true;
        }

        /// <summary>桥协议的流编码：UTF-8 无 BOM。三条流共用一份，别各写各的。</summary>
        private static readonly Encoding ProtocolEncoding = new UTF8Encoding(false);

        /// <summary>起子进程、写 stdin、异步读 stdout/stderr、超时必杀，返回协议结果。</summary>
        private static BridgeCallResult RunSubprocess(
            string repositoryRoot,
            string executable,
            IReadOnlyList<string> arguments,
            BridgeRequest request,
            int timeoutSeconds)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = repositoryRoot,

                // 三条流的编码**必须钉死 UTF-8**，不许跟着宿主控制台的当前代码页走。
                // 协议 JSON 的键全是中文（「契约版本」「成功」「错误」），代码页一旦不是 UTF-8，
                // 收回来的就是乱码，整次调用被判「响应不合协议」——回话直接发不出去。
                // 这个坑最阴的地方是它跟**谁启动的宿主**有关：交互式 pwsh 里跑得好好的，
                // 换成后台常驻/计划任务起来就必挂，而两边跑的是同一份代码。
                // 不带 BOM：BOM 会变成 JSON 正文第一个字符，一样解析不了。
                StandardOutputEncoding = ProtocolEncoding,
                StandardErrorEncoding = ProtocolEncoding,
                StandardInputEncoding = ProtocolEncoding
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
                    return BridgeCallResult.Failure("起进程失败", $"子进程没能启动：{executable}", retryable: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception || exception is IOException)
            {
                return BridgeCallResult.Failure("起进程失败", $"起子进程失败：{exception.Message}", retryable: true);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                var requestJson = request.ToJson();
                try
                {
                    process.StandardInput.Write(requestJson);
                }
                catch (IOException)
                {
                    // 子进程不读 stdin 就退出了：写不进去没关系，退出码与 stdout 才是依据。
                }

                // 不关 stdin 子进程会一直等输入，两边互相等 = 死锁。
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // 管道已经断了，继续按退出码与 stdout 判定。
            }
            catch (ObjectDisposedException)
            {
                // 同上。
            }

            if (!process.WaitForExit(checked(timeoutSeconds * 1000)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception || exception is NotSupportedException)
                {
                    // 杀不掉也照报超时——结果已经注定。
                }

                // 等杀掉的进程把管道关干净，异步读完成。
                try
                {
                    process.WaitForExit();
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
                {
                }

                return BridgeCallResult.AsTimedOut("超时", $"子进程超过 {timeoutSeconds} 秒未退出，已强制终止整棵进程树");
            }

            // 正常退出后等异步读把 stdout/stderr 收干净，ExitCode 才可靠。
            process.WaitForExit();

            var stdoutText = stdout.ToString();
            if (!BridgeResponse.TryParse(stdoutText, out var response, out var reason))
            {
                return BridgeCallResult.Failure(
                    "响应不合协议",
                    $"子进程的 stdout 不是协议 JSON（{reason}）。stderr 末尾：\n{Tail(stderr.ToString())}",
                    retryable: false);
            }

            if (process.ExitCode != 0 || !response.Succeeded)
            {
                var error = response.Error;
                if (error != null)
                {
                    // 错误信封原样带回：错误码、人话、可重试都是子进程给的。
                    return new BridgeCallResult(false, false, error.Code, error.HumanText, error.Retryable, default);
                }

                return BridgeCallResult.Failure(
                    "响应不合协议",
                    $"子进程退出码非 0 且 stdout 里没有错误信封（stderr 末尾：\n{Tail(stderr.ToString())}）",
                    retryable: false);
            }

            return BridgeCallResult.Success(response.Payload.Clone());
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
