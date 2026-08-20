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

        /// <summary>探测输出文件的路径（绝对或相对路径，跑完 CapabilityProbeResult 要能读它）。</summary>
        [Summary("探测输出文件的路径（绝对或相对路径，跑完 CapabilityProbeResult 要能读它）")]
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

    /// <summary>下游供给命令：bridge.provision，一次产出建表描述、专项表、校验错误文案、助手配置包与指纹。</summary>
    public static class BridgeCommands
    {
        /// <summary>
        /// 跑一次下游供给：读 driver 自述与合并 schema，产出全部供给产物；干跑时只列不写。
        /// </summary>
        /// <param name="arguments">供给命令参数。</param>
        [EditorCommand("bridge.provision")]
        [Summary("产出下游供给的全部产物：建表描述、专项表、校验错误文案、助手配置包与指纹")]
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

            lines.Add("以上带「→」的说明就是人工导入清单；程序化导入未验证，见 Doc/创作管线批次日志/P1-批次6-Aily导入spike.md");

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

            if (string.IsNullOrWhiteSpace(arguments.OutputPath))
            {
                return CommandResult.Failure("必须指定 --output-path");
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
                outputPath = Path.GetFullPath(arguments.OutputPath);
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
}
