using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>下游供给命令的参数。</summary>
    public sealed class BridgeProvisionArguments
    {
        /// <summary>要供给的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要供给的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>只算不写，列出将要生成的文件。</summary>
        [Summary("只算不写，列出将要生成的文件")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }
    }

    /// <summary>下游供给产物检查命令的参数。</summary>
    public sealed class BridgePackageCheckArguments
    {
        /// <summary>要检查的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要检查的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }
    }

    /// <summary>下游能力探测命令 bridge.probe 的参数。</summary>
    public sealed class BridgeProbeArguments
    {
        /// <summary>要探测的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要探测的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>探测输出文件的路径（绝对或相对路径，跑完 CapabilityProbeResult 要能读它）；空串时用默认位置 _Generated/Bridges/&lt;driver&gt;/probe-result.json 并自动建目录。</summary>
        [Summary("探测输出文件的路径（绝对或相对路径，跑完 CapabilityProbeResult 要能读它）；空串用默认位置并自动建目录")]
        [DefaultValue("")]
        public string OutputPath { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(300)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>下游模型加工命令 bridge.process 的参数。</summary>
    public sealed class BridgeProcessArguments
    {
        /// <summary>要调用的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要调用的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>输入模型的路径（绝对或相对路径）。</summary>
        [Summary("输入模型的路径（绝对或相对路径）")]
        public string InputModelPath { get; set; }

        /// <summary>加工产物（模型 + 指标文件）的输出目录。</summary>
        [Summary("加工产物（模型 + 指标文件）的输出目录")]
        public string OutputDirectory { get; set; }

        /// <summary>加工计划 JSON 文件的路径（art.plan 产的）。</summary>
        [Summary("加工计划 JSON 文件的路径（art.plan 产的）")]
        public string PlanPath { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(900)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>下游生图命令 bridge.generate 的参数。</summary>
    public sealed class BridgeGenerateArguments
    {
        /// <summary>要调用的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要调用的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>资产请求 JSON 文件的路径（art.request 产的）。</summary>
        [Summary("资产请求 JSON 文件的路径（art.request 产的）")]
        public string RequestPath { get; set; }

        /// <summary>配方名，对应 Bridges/&lt;driver&gt;/配方/&lt;配方名&gt;/。</summary>
        [Summary("配方名，对应 Bridges/<driver>/recipes/<配方名>/")]
        public string RecipeName { get; set; }

        /// <summary>
        /// 变体与溯源边车的输出目录（绝对或相对路径，变体落其下「变体/」子目录）。
        /// **留空就按资产请求里的「需求id」与「id」算正式落点**
        /// （<c>_Tasks/&lt;需求id&gt;/30-outputs/&lt;资产id&gt;/</c>）——真跑业务流程时就该落那儿，
        /// 而不是每次由调用方现编一个目录（P8 批次 4 留的缺口）。
        /// </summary>
        [Summary("输出目录；留空按资产请求算正式落点 _Tasks/<需求id>/30-outputs/<资产id>/")]
        [DefaultValue("")]
        public string OutputDirectory { get; set; }

        /// <summary>参考图路径（本机文件）：给了就走图生图——桥会先把它传进下游的 input 目录，再填进配方的「参考图」锚点槽。</summary>
        [Summary("参考图路径（本机文件）：给了就走图生图；配方必须有「参考图」锚点槽")]
        [DefaultValue("")]
        public string ReferenceImagePath { get; set; }

        /// <summary>生成种子；空串让桥自己产随机种。种子是 64 位无符号量，用 string 接避免边界悄悄变号（决策 26 重生成的前提）。</summary>
        [Summary("生成种子；空串让桥自己产随机种")]
        [DefaultValue("")]
        public string Seed { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(900)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>下游模型生成命令 bridge.model 的参数。</summary>
    public sealed class BridgeModelArguments
    {
        /// <summary>要调用的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要调用的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>生成提示词。给了参考图地址时可以不填，所以它在框架层是可选的，两个都空由命令自己拦。</summary>
        [Summary("生成提示词；给了 --reference-image-url 时可以不填")]
        [DefaultValue("")]
        public string Prompt { get; set; }

        /// <summary>参考图地址：给了就走图生模型，下游要能直接取到这个地址。</summary>
        [Summary("参考图地址：给了就走图生模型；下游要能直接取到这个地址（本地文件不行）")]
        [DefaultValue("")]
        public string ReferenceImageUrl { get; set; }

        /// <summary>参考图类型，如 png / jpg；空串按 png。</summary>
        [Summary("参考图类型，如 png / jpg；空串按 png")]
        [DefaultValue("")]
        public string ReferenceImageType { get; set; }

        /// <summary>粗模的输出目录（绝对或相对路径）。</summary>
        [Summary("粗模的输出目录（绝对或相对路径）")]
        public string OutputDirectory { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>只打要发的请求不真发——真发花用户的积分，默认不花。</summary>
        [Summary("只打要发的请求不真发；默认 true，要真发显式传 --DryRun false（参数名按 CLR 属性名写）")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(600)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>执行后端直调命令 bridge.complete 的参数。</summary>
    public sealed class BridgeCompleteArguments
    {
        /// <summary>要调用的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要调用的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>提示词。缺省是一句最短的探路话——试跑要的是「通不通」，不是「答得好不好」。</summary>
        [Summary("提示词；缺省是一句最短的探路话")]
        [DefaultValue("回一个字：通")]
        public string Prompt { get; set; }

        /// <summary>系统上下文。</summary>
        [Summary("系统上下文")]
        [DefaultValue("你是连通性自检，用户让你回什么就回什么，不要多说。")]
        public string Context { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>只组装不发：打印将发的请求，不真调。默认 false——这条命令存在的意义就是真调一次。</summary>
        [Summary("只组装不发；默认 false，因为试跑就该是一次真调用（决策 91）")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>下游供给命令：bridge.provision，一次产出建表描述、专项表、校验错误文案、assistant-package与指纹。</summary>
    public static class BridgeCommands
    {
        /// <summary>
        /// 跑一次下游供给：读 driver 自述与合并 schema，产出全部供给产物；干跑时只列不写。
        /// </summary>
        /// <param name="arguments">供给命令参数。</param>
        [EditorCommand("bridge.provision")]
        [Summary("产出下游供给的全部产物：建表描述、专项表、校验错误文案、assistant-package与指纹")]
        public static CommandResult Provision(BridgeProvisionArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            string poolRoot;
            try
            {
                poolRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.PoolRoot) ? "Pools" : arguments.PoolRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 PoolRoot 无法解析为绝对路径：{exception.Message}");
            }

            var isDryRun = arguments.DryRun;

            ProvisionOutcome outcome;
            try
            {
                outcome = BridgeProvisioner.Run(repositoryRoot, poolRoot, arguments.Driver, isDryRun);
            }
            catch (InvalidOperationException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            var lines = new List<string>();
            var headLine = isDryRun ? "干跑完成" : "供给完成";
            lines.Add($"{headLine}：driver={outcome.DriverName} 干跑={(isDryRun ? "是" : "否")}");
            lines.Add($"schema 哈希={FirstTwelve(outcome.SchemaHash)}  设计池汇总哈希={FirstTwelve(outcome.DesignDigestHash)}");

            var filePrefix = isDryRun ? "将生成：" : "产物：";
            foreach (var file in outcome.ProducedFiles)
            {
                lines.Add($"{filePrefix}{RelativeTo(repositoryRoot, file)}");
            }

            lines.Add($"共 {outcome.ProducedFiles.Count} 个产物");
            return CommandResult.Success($"共 {outcome.ProducedFiles.Count} 个产物", lines);
        }

        /// <summary>
        /// 检查供给产物是否齐全并打印人工导入清单：逐份列出 10 份产物的存在性与字节数；
        /// 有缺失或空文件时返回失败，全部齐全返回成功；尚未供给时返回成功并提示先跑供给。
        /// </summary>
        /// <param name="arguments">供给产物检查命令参数。</param>
        [EditorCommand("bridge.package-check")]
        [Summary("检查供给产物是否齐全，并打印人工导入清单")]
        public static CommandResult PackageCheck(BridgePackageCheckArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            var inspection = AssistantPackageInspector.Inspect(repositoryRoot, arguments.Driver);
            var isNotProvisioned = !Directory.Exists(ProvisionPaths.GeneratedBridgeDirectory(repositoryRoot, arguments.Driver));

            var lines = new List<string>();
            lines.Add($"配置包检查：driver={inspection.DriverName}  缺失 {inspection.MissingCount} 份，空文件 {inspection.EmptyCount} 份");
            if (isNotProvisioned)
            {
                lines.Add("（尚未供给，先跑 bridge.provision）");
            }

            foreach (var artifact in inspection.Artifacts)
            {
                if (artifact.Exists && artifact.ByteCount > 0)
                {
                    lines.Add($"[有] {artifact.RelativePath}（{artifact.ByteCount} 字节）→ {artifact.ImportHint}");
                }
                else if (artifact.Exists)
                {
                    lines.Add($"[空] {artifact.RelativePath}（0 字节）→ {artifact.ImportHint}");
                }
                else
                {
                    lines.Add($"[缺] {artifact.RelativePath} → {artifact.ImportHint}");
                }
            }

            lines.Add("以上带「→」的说明就是人工导入清单；程序化导入至今未验证，见 Doc/Backlog.md 第 4 条");

            if (isNotProvisioned)
            {
                return CommandResult.Success("尚未供给，先跑 bridge.provision", lines);
            }

            if (inspection.MissingCount > 0 || inspection.EmptyCount > 0)
            {
                return CommandResult.Failure($"缺失 {inspection.MissingCount} 份，空文件 {inspection.EmptyCount} 份", lines);
            }

            return CommandResult.Success($"产物齐全，共 {inspection.Artifacts.Count} 份", lines);
        }

        /// <summary>
        /// 下游能力探测：跑 driver 的 caps 动作，把探测输出写到指定文件。
        /// 失败时把错误信封的「人话」原样摆出来。
        /// </summary>
        /// <param name="arguments">探测命令参数。</param>
        [EditorCommand("bridge.probe")]
        [Summary("下游能力探测：把 driver 探到的节点/模型/lora 写到探测输出文件")]
        public static CommandResult Probe(BridgeProbeArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            string outputPath;
            try
            {
                if (string.IsNullOrWhiteSpace(arguments.OutputPath))
                {
                    // 默认落到面板下游页找的位置：_Generated/Bridges/<driver>/probe-result.json，并自动建目录。
                    outputPath = ProvisionPaths.ProbeResultFile(repositoryRoot, arguments.Driver);
                    var directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }
                else
                {
                    outputPath = Path.GetFullPath(arguments.OutputPath);
                }
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 OutputPath 无法解析为绝对路径：{exception.Message}");
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["输出路径"] = outputPath
            });

            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "caps", payload, arguments.TimeoutSeconds);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            var nodeCount = ReadArrayLength(result.Payload, "节点");
            var modelCount = ReadArrayLength(result.Payload, "模型");
            var loraCount = ReadArrayLength(result.Payload, "lora");

            var lines = new List<string>
            {
                $"探测输出已写到：{RelativeTo(repositoryRoot, outputPath)}",
                $"节点 {nodeCount} 项、模型 {modelCount} 项、lora {loraCount} 项"
            };

            return CommandResult.Success($"探测输出已写到 {RelativeTo(repositoryRoot, outputPath)}", lines);
        }

        /// <summary>
        /// 下游模型加工：跑 driver 的 process 动作，把输入模型按加工计划加工成新模型 + 指标文件。
        /// 失败时把错误信封的「人话」原样摆出来。
        /// </summary>
        /// <param name="arguments">加工命令参数。</param>
        [EditorCommand("bridge.process")]
        [Summary("下游模型加工：按加工计划把输入模型加工成新模型 + 指标文件")]
        public static CommandResult Process(BridgeProcessArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            if (string.IsNullOrWhiteSpace(arguments.InputModelPath))
            {
                return CommandResult.Failure("必须指定 --input-model-path");
            }

            if (string.IsNullOrWhiteSpace(arguments.OutputDirectory))
            {
                return CommandResult.Failure("必须指定 --output-directory");
            }

            if (string.IsNullOrWhiteSpace(arguments.PlanPath))
            {
                return CommandResult.Failure("必须指定 --plan-path");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            string inputModelPath;
            string outputDirectory;
            string planPath;
            try
            {
                inputModelPath = Path.GetFullPath(arguments.InputModelPath);
                outputDirectory = Path.GetFullPath(arguments.OutputDirectory);
                planPath = Path.GetFullPath(arguments.PlanPath);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"路径参数无法解析为绝对路径：{exception.Message}");
            }

            if (!File.Exists(planPath))
            {
                return CommandResult.Failure($"加工计划文件不存在：{planPath}");
            }

            JsonNode planNode;
            try
            {
                planNode = JsonNode.Parse(File.ReadAllText(planPath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"加工计划文件不是合法 JSON：{planPath}：{exception.Message}");
            }

            if (planNode is not JsonObject)
            {
                return CommandResult.Failure($"加工计划文件顶层必须是对象：{planPath}");
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["输入模型"] = inputModelPath,
                ["输出目录"] = outputDirectory,
                ["加工计划"] = planNode
            });

            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "process", payload, arguments.TimeoutSeconds);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            var outputModel = ReadString(result.Payload, "输出模型");
            var metricsFile = ReadString(result.Payload, "指标文件");

            var lines = new List<string>
            {
                $"输出模型：{RelativeTo(repositoryRoot, outputModel)}",
                $"指标文件：{RelativeTo(repositoryRoot, metricsFile)}"
            };

            if (result.Payload.TryGetProperty("执行了的步骤", out var executedSteps) && executedSteps.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var item in executedSteps.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        names.Add(item.GetString() ?? "");
                    }
                }

                lines.Add($"执行了的步骤：{string.Join("、", names)}");
            }

            if (result.Payload.TryGetProperty("跳过的步骤", out var skippedSteps) && skippedSteps.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in skippedSteps.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var name = item.TryGetProperty("步骤", out var stepName) && stepName.ValueKind == JsonValueKind.String ? stepName.GetString() : "";
                    var reason = item.TryGetProperty("原因", out var skipReason) && skipReason.ValueKind == JsonValueKind.String ? skipReason.GetString() : "";
                    lines.Add($"跳过：{name}（{reason}）");
                }
            }

            return CommandResult.Success($"加工完成：{RelativeTo(repositoryRoot, outputModel)}", lines);
        }

        /// <summary>
        /// 下游生图：跑 driver 的 generate 动作，把资产请求按配方真出图，变体与溯源边车落输出目录。
        /// 成功时把「出了几张、落在哪」摆成明细行；失败时把错误信封的「人话」原样摆出来。
        /// </summary>
        /// <param name="arguments">生图命令参数。</param>
        [EditorCommand("bridge.generate")]
        [Summary("下游生图：按配方把资产请求真出图，变体与溯源边车落输出目录")]
        public static CommandResult Generate(BridgeGenerateArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            if (string.IsNullOrWhiteSpace(arguments.RequestPath))
            {
                return CommandResult.Failure("必须指定 --request-path");
            }

            if (string.IsNullOrWhiteSpace(arguments.RecipeName))
            {
                return CommandResult.Failure("必须指定 --recipe-name");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            string requestPath;
            try
            {
                requestPath = Path.GetFullPath(arguments.RequestPath);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"路径参数无法解析为绝对路径：{exception.Message}");
            }

            if (!File.Exists(requestPath))
            {
                return CommandResult.Failure($"资产请求文件不存在：{requestPath}");
            }

            JsonNode requestNode;
            try
            {
                requestNode = JsonNode.Parse(File.ReadAllText(requestPath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"资产请求文件不是合法 JSON：{requestPath}：{exception.Message}");
            }

            if (requestNode is not JsonObject requestObject)
            {
                return CommandResult.Failure($"资产请求文件顶层必须是对象：{requestPath}");
            }

            // 输出目录：给了就用给的（验证、试跑常这么干）；
            // **留空按资产请求算正式落点**——真跑业务流程时变体就该落
            // _Tasks/<需求id>/30-outputs/<资产id>/，不该每次现编一个目录（P8 批次 4 的缺口）。
            string outputDirectory;
            if (!string.IsNullOrWhiteSpace(arguments.OutputDirectory))
            {
                try
                {
                    outputDirectory = Path.GetFullPath(arguments.OutputDirectory);
                }
                catch (Exception exception)
                {
                    return CommandResult.Failure($"参数 OutputDirectory 无法解析为绝对路径：{exception.Message}");
                }
            }
            else
            {
                var requirementIdentifier = ReadNodeString(requestObject, "需求id");
                var assetIdentifier = ReadNodeString(requestObject, "id");
                if (requirementIdentifier.Length == 0 || assetIdentifier.Length == 0)
                {
                    return CommandResult.Failure(
                        "没给 --OutputDirectory，而资产请求里缺「需求id」或「id」，算不出正式落点");
                }

                // 变体目录是 <落点>/变体，桥自己会拼「变体」这一层，所以这里给它的是上一层。
                outputDirectory = Path.GetDirectoryName(
                    AssetPaths.VariantDirectory(repositoryRoot, requirementIdentifier, assetIdentifier));
            }

            var payloadObject = new JsonObject
            {
                ["asset-requests"] = requestNode,
                ["配方名"] = arguments.RecipeName,
                ["输出目录"] = outputDirectory
            };

            // 给了种子就原样透传进载荷「种子」字段；空串 = 桥自己产随机种（保持原行为）。
            // 用 string 不用 long：种子是 64 位无符号量，有符号整数会在边界悄悄变号（决策 26）。
            if (!string.IsNullOrEmpty(arguments.Seed))
            {
                payloadObject["种子"] = arguments.Seed;
            }

            // 参考图给了就走图生图。路径**先在这里查存在性**：桥那边也会查，
            // 但在命令层查能给出更早、更贴近调用方的报错（少起一次子进程）。
            var referenceImagePath = (arguments.ReferenceImagePath ?? "").Trim();
            if (referenceImagePath.Length > 0)
            {
                string fullReferencePath;
                try
                {
                    fullReferencePath = Path.GetFullPath(referenceImagePath);
                }
                catch (Exception exception)
                {
                    return CommandResult.Failure($"参数 ReferenceImagePath 无法解析为绝对路径：{exception.Message}");
                }

                if (!File.Exists(fullReferencePath))
                {
                    return CommandResult.Failure($"参考图不存在：{fullReferencePath}");
                }

                payloadObject["参考图路径"] = fullReferencePath;
            }

            var payload = JsonSerializer.SerializeToElement(payloadObject);

            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "generate", payload, arguments.TimeoutSeconds);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            var lines = new List<string>();
            var variantCount = ReadArrayLength(result.Payload, "variants");
            lines.Add($"共出 {variantCount} 张图");
            lines.Add($"prompt id：{ReadString(result.Payload, "prompt_id")}");

            if (result.Payload.TryGetProperty("variants", out var variants) && variants.ValueKind == JsonValueKind.Array)
            {
                foreach (var variant in variants.EnumerateArray())
                {
                    if (variant.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var file = ReadString(variant, "文件");
                    var byteCount = ReadInt(variant, "字节数");
                    var width = ReadInt(variant, "宽");
                    var height = ReadInt(variant, "高");
                    lines.Add($"{RelativeTo(repositoryRoot, file)}（{byteCount} 字节，{width}×{height}）");
                }
            }

            return CommandResult.Success($"共出 {variantCount} 张图", lines);
        }

        /// <summary>
        /// 下游模型生成：真出粗模（花积分）。默认干跑只打要发的请求不真发——真发要花用户的积分，
        /// 默认值就该是不花。干跑时读 driver 自述与本机配置，把「要发给桥的请求」打出来，
        /// 密钥只报配没配、绝不显示值（决策 5、78）。
        /// </summary>
        /// <param name="arguments">模型生成命令参数。</param>
        [EditorCommand("bridge.model")]
        [Summary("下游模型生成：真出粗模；默认干跑，--DryRun false 才真发（花积分）")]
        public static CommandResult Model(BridgeModelArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            var referenceImageUrl = (arguments.ReferenceImageUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(arguments.Prompt) && referenceImageUrl.Length == 0)
            {
                return CommandResult.Failure("必须指定 --prompt 或 --reference-image-url，两个都空就没有输入了");
            }

            if (string.IsNullOrWhiteSpace(arguments.OutputDirectory))
            {
                return CommandResult.Failure("必须指定 --output-directory");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            string outputDirectory;
            try
            {
                outputDirectory = Path.GetFullPath(arguments.OutputDirectory);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 OutputDirectory 无法解析为绝对路径：{exception.Message}");
            }

            if (arguments.DryRun)
            {
                return DryRunModel(repositoryRoot, arguments.Driver, arguments.Prompt ?? "", referenceImageUrl, outputDirectory);
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["提示词"] = arguments.Prompt ?? "",
                ["参考图地址"] = referenceImageUrl,
                ["参考图类型"] = (arguments.ReferenceImageType ?? "").Trim(),
                ["输出目录"] = outputDirectory
            });

            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "generate", payload, arguments.TimeoutSeconds);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            var modelFile = ReadString(result.Payload, "模型文件");
            var taskId = ReadString(result.Payload, "task_id");
            var statusText = ReadString(result.Payload, "状态");
            var submitMode = ReadString(result.Payload, "提交方式");

            var lines = new List<string>
            {
                $"模型文件：{RelativeTo(repositoryRoot, modelFile)}",
                $"字节数：{ByteCountOf(modelFile)}",
                $"task_id：{taskId}",
                $"状态：{statusText}",
                $"提交方式：{(submitMode.Length == 0 ? "（桥没报）" : submitMode)}"
            };

            return CommandResult.Success($"模型生成完成：{RelativeTo(repositoryRoot, modelFile)}", lines);
        }

        /// <summary>
        /// 执行后端直调：发一句最短的提示，看下游通不通。
        /// **这就是执行后端 driver 的试跑**——决策 91 说得很死：能不能用只有真调一次才算数，
        /// 密钥非空、HTTP 200 都不是判据。一次调用的开销是可以忽略的几个 token，
        /// 但它能一次分清「密钥错 / 地址错 / 模型名错 / 余额不够」四种完全不同的毛病。
        /// </summary>
        /// <param name="arguments">执行后端直调命令参数。</param>
        [EditorCommand("bridge.complete")]
        [Summary("执行后端直调：发一句最短的提示看通不通，这就是它的试跑（真调一次）")]
        public static CommandResult Complete(BridgeCompleteArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --Driver，值取 Bridges/ 下的目录名");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            var prompt = arguments.Prompt ?? "";
            var context = arguments.Context ?? "";
            if (arguments.DryRun)
            {
                return CommandResult.Success($"干跑完成：driver={arguments.Driver}，未发任何请求", new[]
                {
                    "动作：complete",
                    $"提示：{prompt}",
                    $"上下文：{context}"
                });
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["提示"] = prompt,
                ["上下文"] = context
            });

            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "complete", payload, arguments.TimeoutSeconds);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            var text = ReadString(result.Payload, "文本");
            var model = ReadString(result.Payload, "模型");
            return CommandResult.Success($"执行后端通了：driver={arguments.Driver}", new[]
            {
                $"服务端报的模型：{(model.Length == 0 ? "（没报）" : model)}",
                $"回答字数：{text.Length}",
                $"回答（截断到 200 字）：{(text.Length <= 200 ? text : text.Substring(0, 200) + "…")}"
            });
        }

        /// <summary>
        /// 下游账号余额：真发一次查询（不花积分）。**这不是就绪判据**——决策 91 定死了
        /// 「能不能用只有真提交一次任务才算数」，余额只用来诊断「额度不足是不是真没钱」。
        /// 桥不支持 balance 动作时会回「未知动作」，那是准确的，不是坏了。
        /// </summary>
        /// <param name="arguments">余额查询命令参数。</param>
        [EditorCommand("bridge.balance")]
        [Summary("查下游账号余额（不花积分）；余额不是就绪判据，只用来诊断额度不足")]
        public static CommandResult Balance(BridgePackageCheckArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject());
            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "balance", payload, timeoutSeconds: 120);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            var available = ReadNumberText(result.Payload, "可用积分");
            var frozen = ReadNumberText(result.Payload, "冻结积分");
            var lines = new List<string>
            {
                $"可用积分：{available}",
                $"冻结积分：{frozen}",
                "提醒：余额不是就绪判据（决策 91）——能不能用只有真提交一次任务才算数"
            };

            return CommandResult.Success($"余额查询完成：driver={arguments.Driver}，可用 {available}", lines);
        }

        /// <summary>读响应载荷里数字键的文本形式；缺失或类型不对给「（桥没报）」。</summary>
        private static string ReadNumberText(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetRawText();
            }

            return "（桥没报）";
        }

        /// <summary>干跑：读 driver 自述与本机配置，把将发给桥的请求打出来，不发任何请求。</summary>
        private static CommandResult DryRunModel(string repositoryRoot, string driverName, string prompt, string referenceImageUrl, string outputDirectory)
        {
            BridgeDriverDescriptor descriptor;
            try
            {
                descriptor = BridgeDriverDescriptor.Load(repositoryRoot, driverName);
            }
            catch (InvalidOperationException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            var localSettings = LocalBridgeSettings.Load(repositoryRoot);
            if (!localSettings.Loaded)
            {
                return CommandResult.Failure("本机配置错误", new[] { localSettings.LoadFailureReason });
            }

            var usesImage = !string.IsNullOrWhiteSpace(referenceImageUrl);
            var lines = new List<string>
            {
                "干跑：以下是将发给桥的请求，没有真发（真发花积分）",
                $"动作：generate",
                $"提交方式：{(usesImage ? "image-to-model（给了参考图地址）" : "text-to-model")}",
                $"载荷：{{\"提示词\":\"{prompt}\",\"参考图地址\":\"{referenceImageUrl}\",\"输出目录\":\"{outputDirectory}\"}}"
            };

            if (localSettings.TryGetDriverConfiguration(driverName, out var driverConfiguration)
                && driverConfiguration.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in driverConfiguration.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        lines.Add($"配置.{property.Name}：{property.Value.GetString()}");
                    }
                    else
                    {
                        lines.Add($"配置.{property.Name}：{property.Value.GetRawText()}");
                    }
                }
            }

            foreach (var secretFieldName in descriptor.SecretFieldNames)
            {
                var configured = localSettings.TryGetSecret(secretFieldName, out _);
                lines.Add($"密钥「{secretFieldName}」：{(configured ? "已配置" : "未配置")}（值绝不显示）");
            }

            lines.Add("说明：桥将向下游提交生成任务、轮询到终态、下载模型落盘；密钥只进 Authorization 头");
            return CommandResult.Success($"干跑完成：driver={descriptor.Name}，未发任何请求", lines);
        }

        /// <summary>读文件字节数；文件不存在给 -1。</summary>
        private static long ByteCountOf(string filePath)
        {
            try
            {
                return new FileInfo(filePath).Length;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            {
                return -1;
            }
        }

        /// <summary>读 JSON 对象里字符串键的值；缺失或类型不对给空串。</summary>
        private static string ReadNodeString(JsonObject node, string propertyName)
        {
            if (node != null
                && node.TryGetPropertyValue(propertyName, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var text))
            {
                return text ?? "";
            }

            return "";
        }

        /// <summary>读响应载荷里字符串键的值；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读响应载荷里数组键的长度；缺失或类型不对给 0。</summary>
        private static int ReadArrayLength(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Array)
            {
                return value.GetArrayLength();
            }

            return 0;
        }

        /// <summary>读响应载荷里整数键的值；缺失或类型不对给 0。</summary>
        private static int ReadInt(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return value.GetInt32();
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                }
            }

            return 0;
        }

        /// <summary>取哈希的前 12 位；文本不足 12 位时原样返回。</summary>
        private static string FirstTwelve(string text)
        {
            return text.Length <= 12 ? text : text.Substring(0, 12);
        }

        /// <summary>把绝对路径转成相对仓库根的路径；无法相对化时原样返回。</summary>
        private static string RelativeTo(string basePath, string fullPath)
        {
            var relative = Path.GetRelativePath(basePath, fullPath);
            return relative.StartsWith("..", StringComparison.Ordinal) ? fullPath : relative;
        }
    }

    /// <summary>下游建表命令 bridge.apply 的参数。</summary>
    public sealed class BridgeApplyArguments
    {
        /// <summary>要调用的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要调用的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>只算不写：列出要建的表与字段，不发任何写请求。默认 true——真建表是写下游的工作区，默认不写。</summary>
        [Summary("只算不写：列出要建的表与字段，不发任何写请求。默认 true，要真建显式传 false")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>下游发卡片命令 bridge.card 的参数。</summary>
    public sealed class BridgeCardArguments
    {
        /// <summary>要调用的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要调用的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>只算不写：把要发的卡片 JSON 打出来，不真发。默认 true——真发消息也是写下游的工作区，默认不写。</summary>
        [Summary("只算不写：把要发的卡片 JSON 打出来，不真发。默认 true，要真发显式传 false")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }

        /// <summary>需求 id，如 REQ-0042，选片卡的数据来源。</summary>
        [Summary("需求 id，如 REQ-0042，选片卡的数据来源")]
        public string RequirementIdentifier { get; set; }

        /// <summary>资产 id，如 ASSET-0042-01。</summary>
        [Summary("资产 id，如 ASSET-0042-01")]
        public string AssetIdentifier { get; set; }

        /// <summary>选片轮次，从 1 起。</summary>
        [Summary("选片轮次，从 1 起")]
        [DefaultValue(1)]
        public int Round { get; set; }
    }

    /// <summary>下游写记录命令 bridge.push 的参数。</summary>
    public sealed class BridgePushArguments
    {
        /// <summary>要调用的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要调用的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>只算不写：列出每条记录将新建还是更新，不发任何写请求。默认 true——真写是写下游的工作区，默认不写。</summary>
        [Summary("只算不写：列出每条记录将新建还是更新，不发任何写请求。默认 true，要真写显式传 false")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }

        /// <summary>要写的记录：JSON 数组字符串，形如 [{"id":"REQ-TEST-0001","标题":"…","锁定":true}, …]。</summary>
        [Summary("要写的记录：JSON 数组字符串，形如 [{\"id\":\"REQ-TEST-0001\",\"锁定\":true}]")]
        public string RecordsJson { get; set; }

        /// <summary>幂等键字段名，按它先查后写（已存在更新、不存在新建）。</summary>
        [Summary("幂等键字段名，按它先查后写（已存在更新、不存在新建）")]
        [DefaultValue("id")]
        public string IdempotencyKeyField { get; set; }
    }

    /// <summary>下游读记录命令 bridge.pull 的参数。</summary>
    public sealed class BridgePullArguments
    {
        /// <summary>要调用的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。</summary>
        [Summary("要调用的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>只算不写：打印将拉取的范围，不发任何请求之外的读操作。默认 true。</summary>
        [Summary("只算不写：打印将拉取的范围。默认 true")]
        [DefaultValue(true)]
        public bool DryRun { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(120)]
        public int TimeoutSeconds { get; set; }

        /// <summary>上次的水位串（ISO 8601 时间），空串 = 全量拉（决策 65）。</summary>
        [Summary("上次的水位串（ISO 8601 时间），空串 = 全量拉")]
        [DefaultValue("")]
        public string Watermark { get; set; }

        /// <summary>入站信封落盘目录；空串时用 {RepositoryRoot}/Pools/Inbox。</summary>
        [Summary("入站信封落盘目录；空串用 {RepositoryRoot}/Pools/Inbox")]
        [DefaultValue("")]
        public string OutputDirectory { get; set; }
    }

    /// <summary>下游供给命令族：bridge.apply（真建表）、bridge.card（真发卡）等。</summary>
    public static class BridgeWriteCommands
    {
        /// <summary>
        /// 下游建表：按本地产物里的建表描述真建表（幂等，同名表跳过）；干跑只列计划、不发写请求。
        /// 真建表是写下游的工作区，所以 DryRun 默认 true，要真建必须显式传 --dry-run false。
        /// </summary>
        /// <param name="arguments">建表命令参数。</param>
        [EditorCommand("bridge.apply")]
        [Summary("按建表描述在下游真建表（幂等，同名跳过）；默认干跑，--dry-run false 才真写")]
        public static CommandResult Apply(BridgeApplyArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject { ["干跑"] = arguments.DryRun });

            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "apply", payload, arguments.TimeoutSeconds);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            var lines = new List<string>();
            if (arguments.DryRun)
            {
                lines.Add("干跑：以下是建表计划，没有发任何写请求");
                var plans = ReadArray(result.Payload, "计划");
                foreach (var plan in plans)
                {
                    if (plan.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var tableName = ReadString(plan, "表名");
                    var fieldCount = ReadInt(plan, "字段数");
                    lines.Add($"表「{tableName}」：{fieldCount} 个字段");
                    foreach (var field in ReadArray(plan, "字段"))
                    {
                        if (field.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        lines.Add($"  {ReadString(field, "名称")}：{ReadString(field, "下游类型")}（类型码 {ReadInt(field, "类型码")}）");
                    }
                }

                return CommandResult.Success($"干跑完成，计划 {plans.Count} 张表", lines);
            }

            var created = ReadArray(result.Payload, "建了");
            var skipped = ReadArray(result.Payload, "跳过的");
            foreach (var item in created)
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                lines.Add($"建了：{ReadString(item, "表名")}（table_id={ReadString(item, "table_id")}）");
            }

            foreach (var item in skipped)
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                lines.Add($"跳过：{ReadString(item, "表名")}（{ReadString(item, "原因")}）");
            }

            var addedColumns = ReadArray(result.Payload, "补的列");
            foreach (var item in addedColumns)
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    lines.Add($"补列：{ReadString(item, "字段")}（{ReadString(item, "下游类型")}）");
                }
            }

            lines.Add($"共建 {created.Count} 张、跳过 {skipped.Count} 张、补列 {addedColumns.Count} 个");
            return CommandResult.Success(
                $"建表完成：建了 {created.Count} 张、跳过 {skipped.Count} 张、补列 {addedColumns.Count} 个",
                lines);
        }

        /// <summary>
        /// 下游发卡片：装配一张选片卡真发一条；干跑只打印要发的卡片 JSON、不真发。
        /// 真发是写下游的工作区，所以 DryRun 默认 true，要真发必须显式传 --dry-run false。
        /// </summary>
        /// <param name="arguments">发卡片命令参数。</param>
        [EditorCommand("bridge.card")]
        [Summary("装配一张选片卡并发一条消息；默认干跑，--dry-run false 才真发")]
        public static CommandResult Card(BridgeCardArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            if (string.IsNullOrWhiteSpace(arguments.RequirementIdentifier) || string.IsNullOrWhiteSpace(arguments.AssetIdentifier))
            {
                return CommandResult.Failure("必须指定 --requirement-identifier 与 --asset-identifier");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            var buildResult = SelectionCardBuilder.Build(repositoryRoot, arguments.RequirementIdentifier, arguments.AssetIdentifier, arguments.Round);
            if (buildResult.Card == null)
            {
                var lines = new List<string>();
                foreach (var finding in buildResult.Findings)
                {
                    lines.Add(finding.ToDisplayText());
                }

                return CommandResult.Failure("选片卡装配失败，没有可发的卡片", lines);
            }

            var variants = new JsonArray();
            foreach (var variant in buildResult.Card.QualifiedVariants)
            {
                variants.Add(variant);
            }

            var buttons = new JsonArray();
            foreach (var button in buildResult.Card.Buttons)
            {
                buttons.Add(button);
            }

            // 组九宫格拼图：图片变体一格，模型变体三格（F/S/I 三视图）。
            var cells = new List<ContactSheetCell>();
            var hasModelVariant = false;
            for (var index = 0; index < buildResult.Card.QualifiedVariants.Count; index++)
            {
                var variantName = buildResult.Card.QualifiedVariants[index];
                var sequence = (index + 1).ToString();
                if (SelectionCardBuilder.IsImageFile(variantName))
                {
                    var imagePath = Path.Combine(AssetPaths.VariantDirectory(repositoryRoot, arguments.RequirementIdentifier, arguments.AssetIdentifier), variantName);
                    cells.Add(new ContactSheetCell(sequence, imagePath));
                }
                else if (SelectionCardBuilder.IsModelFile(variantName))
                {
                    hasModelVariant = true;
                    var variantPath = Path.Combine(AssetPaths.VariantDirectory(repositoryRoot, arguments.RequirementIdentifier, arguments.AssetIdentifier), variantName);
                    var viewDirectory = AssetPaths.VariantViewDirectory(repositoryRoot, arguments.RequirementIdentifier, arguments.AssetIdentifier);
                    var views = new[] { ("front", "F"), ("side", "S"), ("iso", "I") };
                    foreach (var (viewName, viewLabel) in views)
                    {
                        var viewPath = AssetPaths.VariantViewFile(repositoryRoot, arguments.RequirementIdentifier, arguments.AssetIdentifier, variantName, viewName);
                        if (!File.Exists(viewPath))
                        {
                            return CommandResult.Failure(
                                $"变体「{variantName}」缺「{viewName}」视图，拼不出三视图",
                                // driver 名不写死在这句提示里——加工站换一家，这句话不该跟着骗人（子文档 05）。
                                new[] { $"art.views --Driver <加工站driver名> --InputModelPath {variantPath} --OutputDirectory {viewDirectory}" });
                        }

                        cells.Add(new ContactSheetCell(sequence + " " + viewLabel, viewPath));
                    }
                }
            }

            var columnCount = hasModelVariant ? 3 : ContactSheetComposer.ColumnCountFor(cells.Count);
            var sheetPath = AssetPaths.ContactSheetFile(repositoryRoot, arguments.RequirementIdentifier, arguments.AssetIdentifier, buildResult.Card.Round);

            var composeResult = ContactSheetComposer.Compose(cells, columnCount, sheetPath);
            if (!composeResult.Succeeded)
            {
                var composeFailureLines = new List<string>();
                foreach (var finding in composeResult.Findings)
                {
                    composeFailureLines.Add(finding.ToDisplayText());
                }

                return CommandResult.Failure("九宫格拼不出来，没有可发的卡片", composeFailureLines);
            }

            var cardNode = new JsonObject
            {
                ["需求id"] = buildResult.Card.RequirementIdentifier,
                ["资产id"] = buildResult.Card.AssetIdentifier,
                ["轮次"] = buildResult.Card.Round,
                ["合格变体"] = variants,
                ["弃置数"] = buildResult.Card.RejectedCount,
                ["按钮"] = buttons,
                ["提示"] = buildResult.Card.Hint,
                ["拼图路径"] = sheetPath
            };

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = arguments.DryRun,
                ["卡片"] = cardNode
            });

            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "card", payload, arguments.TimeoutSeconds);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            if (arguments.DryRun)
            {
                var cardJson = ReadString(result.Payload, "要发的卡片JSON");
                var lines = new List<string>
                {
                    cardJson,
                    $"九宫格：{sheetPath}"
                };
                foreach (var finding in composeResult.Findings)
                {
                    lines.Add(finding.ToDisplayText());
                }

                return CommandResult.Success("干跑：以下是卡片 JSON，没有真发", lines);
            }

            var messageId = ReadString(result.Payload, "message_id");
            var sendLines = new List<string> { $"message_id={messageId}" };
            foreach (var finding in composeResult.Findings)
            {
                sendLines.Add(finding.ToDisplayText());
            }

            return CommandResult.Success($"已发送一条选片卡，message_id={messageId}", sendLines);
        }

        /// <summary>
        /// 下游写记录：按幂等键先查后写（已存在更新、不存在新建），真写「需求」表；
        /// 干跑只列出每条将新建还是更新、不发任何写请求。
        /// 真写是写下游的工作区，所以 DryRun 默认 true，要真写必须显式传 --dry-run false。
        /// </summary>
        /// <param name="arguments">写记录命令参数。</param>
        [EditorCommand("bridge.push")]
        [Summary("按幂等键把记录写进下游表（已存在更新、不存在新建）；默认干跑，--dry-run false 才真写")]
        public static CommandResult Push(BridgePushArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            if (string.IsNullOrWhiteSpace(arguments.RecordsJson))
            {
                return CommandResult.Failure("必须指定 --records-json：要写的记录（JSON 数组字符串）");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            JsonElement recordsArray;
            try
            {
                using var document = JsonDocument.Parse(arguments.RecordsJson);
                recordsArray = document.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                return CommandResult.Failure($"参数 RecordsJson 不是合法 JSON：{exception.Message}");
            }

            if (recordsArray.ValueKind != JsonValueKind.Array)
            {
                return CommandResult.Failure("参数 RecordsJson 必须是 JSON 数组，如 [{\"id\":\"REQ-TEST-0001\",\"锁定\":true}]");
            }

            var idempotencyKeyField = string.IsNullOrWhiteSpace(arguments.IdempotencyKeyField) ? "id" : arguments.IdempotencyKeyField;
            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = arguments.DryRun,
                ["记录"] = JsonNode.Parse(recordsArray.GetRawText()),
                ["幂等键字段"] = idempotencyKeyField
            });

            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "push", payload, arguments.TimeoutSeconds);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            var lines = new List<string>();
            if (arguments.DryRun)
            {
                lines.Add("干跑：以下是每条记录的写入计划，没有发任何写请求");
                foreach (var plan in ReadArray(result.Payload, "计划"))
                {
                    if (plan.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var fieldsJson = plan.TryGetProperty("要写的字段", out var fieldsNode) ? fieldsNode.GetRawText() : "{}";
                    lines.Add($"{ReadString(plan, "id")} → {ReadString(plan, "动作")}（要写的字段：{fieldsJson}）");
                }

                return CommandResult.Success($"干跑完成，计划 {ReadArray(result.Payload, "计划").Count} 条", lines);
            }

            foreach (var item in ReadArray(result.Payload, "新建"))
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    lines.Add($"新建：{ReadString(item, "id")}（record_id={ReadString(item, "record_id")}）");
                }
            }

            foreach (var item in ReadArray(result.Payload, "更新"))
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    lines.Add($"更新：{ReadString(item, "id")}（record_id={ReadString(item, "record_id")}）");
                }
            }

            foreach (var item in ReadArray(result.Payload, "跳过"))
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    lines.Add($"跳过：{ReadString(item, "id")}（{ReadString(item, "原因")}）");
                }
            }

            return CommandResult.Success(
                $"push 完成：新建 {ReadArray(result.Payload, "新建").Count} 条、更新 {ReadArray(result.Payload, "更新").Count} 条、跳过 {ReadArray(result.Payload, "跳过").Count} 条",
                lines);
        }

        /// <summary>
        /// 下游读记录：把「需求」表记录读成入站信封落盘（只读，一个写请求都不发）；
        /// 干跑只打印将拉取的范围。水位为空 = 全量拉（决策 65）。
        /// </summary>
        /// <param name="arguments">读记录命令参数。</param>
        [EditorCommand("bridge.pull")]
        [Summary("把下游表记录读成入站信封落盘（只读）；默认干跑打印拉取范围")]
        public static CommandResult Pull(BridgePullArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("必须指定 --driver，值取 Bridges/ 下的目录名");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(arguments.RepositoryRoot) ? "." : arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            var outputDirectory = string.IsNullOrWhiteSpace(arguments.OutputDirectory)
                ? Path.Combine(repositoryRoot, "Pools", "Inbox")
                : Path.GetFullPath(arguments.OutputDirectory);

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["干跑"] = arguments.DryRun,
                ["水位"] = arguments.Watermark ?? "",
                ["输出目录"] = outputDirectory
            });

            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "pull", payload, arguments.TimeoutSeconds);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            if (arguments.DryRun)
            {
                var lines = new List<string>
                {
                    "干跑：以下是将拉取的范围，没有落盘任何文件",
                    $"表名：{ReadString(result.Payload, "表名")}",
                    $"水位：{ReadString(result.Payload, "水位")}",
                    $"将拉到：{ReadInt(result.Payload, "将拉到")} 条",
                    $"表字段：{string.Join("、", ReadStringArray(result.Payload, "表字段"))}"
                };
                return CommandResult.Success("干跑完成，未落盘任何文件", lines);
            }

            var landed = ReadArray(result.Payload, "落盘");
            var lines2 = new List<string>
            {
                $"拉到 {ReadInt(result.Payload, "拉到")} 条，落盘 {landed.Count} 个文件"
            };
            foreach (var path in landed)
            {
                if (path.ValueKind == JsonValueKind.String)
                {
                    lines2.Add("  " + path.GetString());
                }
            }

            var newWatermark = ReadString(result.Payload, "新水位");
            if (newWatermark.Length > 0)
            {
                lines2.Add($"新水位：{newWatermark}");
            }

            foreach (var skipped in ReadArray(result.Payload, "跳过的"))
            {
                if (skipped.ValueKind == JsonValueKind.String)
                {
                    lines2.Add("跳过：" + skipped.GetString());
                }
            }

            return CommandResult.Success($"pull 完成：拉到 {ReadInt(result.Payload, "拉到")} 条", lines2);
        }

        /// <summary>读响应载荷里数组键的值；缺失或类型不对给空列表。</summary>
        private static List<JsonElement> ReadArray(JsonElement element, string propertyName)
        {
            var values = new List<JsonElement>();
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    values.Add(item.Clone());
                }
            }

            return values;
        }

        /// <summary>读响应载荷里字符串数组键的值；缺失或类型不对给空列表。</summary>
        private static List<string> ReadStringArray(JsonElement element, string propertyName)
        {
            var values = new List<string>();
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        values.Add(item.GetString() ?? "");
                    }
                }
            }

            return values;
        }

        /// <summary>读响应载荷里字符串键的值；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读响应载荷里整数键的值；缺失或类型不对给 0。</summary>
        private static int ReadInt(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return value.GetInt32();
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                }
            }

            return 0;
        }
    }
}
