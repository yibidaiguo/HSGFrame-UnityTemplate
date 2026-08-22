using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>建资产请求命令的参数。</summary>
    public sealed class ArtRequestArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>需求 id，如「REQ-0042」。</summary>
        [Summary("需求 id，如 REQ-0042；留空表示这张图还没有主，落进 REQ-0000")]
        [DefaultValue("")]
        public string Requirement { get; set; }

        /// <summary>工作项 id，如「WI-0042-03」。</summary>
        [Summary("工作项 id，如 WI-0042-03；留空跟着需求一起落进无主那一档")]
        [DefaultValue("")]
        public string WorkItem { get; set; }

        /// <summary>域，默认取资产规格数据的域。</summary>
        [Summary("域，默认取资产规格数据的域")]
        [DefaultValue("")]
        public string Domain { get; set; }

        /// <summary>资产类型，如「图标」。</summary>
        [Summary("资产类型，如「图标」")]
        public string AssetType { get; set; }

        /// <summary>落点目录，默认取资产规格数据的落点。</summary>
        [Summary("落点目录，默认取资产规格数据的落点")]
        [DefaultValue("")]
        public string Destination { get; set; }

        /// <summary>业务模块名，用于取 Specifications/Business/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 Specifications/Business/<模块>/ 的就近覆盖")]
        [DefaultValue("")]
        public string Module { get; set; }

        /// <summary>命名文本。</summary>
        [Summary("命名文本")]
        public string NamingText { get; set; }

        /// <summary>描述。</summary>
        [Summary("描述")]
        public string Description { get; set; }

        /// <summary>变体数，默认 6。</summary>
        [Summary("变体数，默认 6")]
        [DefaultValue(6)]
        public int VariantCount { get; set; }
    }

    /// <summary>能力对账命令的参数。</summary>
    public sealed class ArtCapabilityArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>driver 名称。</summary>
        [Summary("driver 名称")]
        public string DriverName { get; set; }

        /// <summary>能力探测输出文件的路径。</summary>
        [Summary("能力探测输出文件的路径")]
        public string ProbeResultPath { get; set; }
    }

    /// <summary>校验资产请求与边车命令的参数。</summary>
    public sealed class ArtValidateArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>需求 id，如「REQ-0042」。</summary>
        [Summary("需求 id，如「REQ-0042」")]
        public string Requirement { get; set; }
    }

    /// <summary>选片命令 art.select 的参数。</summary>
    public sealed class ArtSelectArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录，相对当前工作目录。</summary>
        [Summary("池子根目录，相对当前工作目录")]
        public string PoolRoot { get; set; }

        /// <summary>需求 id，如「REQ-0042」。</summary>
        [Summary("需求 id，如「REQ-0042」")]
        public string RequirementIdentifier { get; set; }

        /// <summary>资产 id，如「ASSET-0042-01」。</summary>
        [Summary("资产 id，如「ASSET-0042-01」")]
        public string AssetIdentifier { get; set; }

        /// <summary>需求所属专项 id，可空；空串按无专项路由。</summary>
        [Summary("需求所属专项 id，可空；空串按无专项路由")]
        [DefaultValue("")]
        public string EpicIdentifier { get; set; }

        /// <summary>选片轮次，缺省 1。</summary>
        [Summary("选片轮次，缺省 1")]
        [DefaultValue(1)]
        public int Round { get; set; }

        /// <summary>出站意图信封的落盘路径；缺省落 _Tasks/&lt;需求id&gt;/40-出站/。</summary>
        [Summary("出站意图信封的落盘路径；缺省落 _Tasks/<需求id>/40-出站/")]
        [DefaultValue("")]
        public string OutputPath { get; set; }
    }

    /// <summary>三视图命令 art.views 的参数。</summary>
    public sealed class ArtViewsArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        [DefaultValue(".")]
        public string RepositoryRoot { get; set; }

        /// <summary>要调用的下游 driver 名，对应 Bridges/&lt;名&gt;/ 目录。必填，不给缺省值——
        /// driver 名只能是运行时参数，写进引擎代码就把加工站钉死在某一家上了（子文档 05）。</summary>
        [Summary("要调用的下游 driver 名，对应 Bridges/<名>/ 目录")]
        public string Driver { get; set; }

        /// <summary>要渲的模型路径（绝对或相对路径），支持 .glb / .gltf / .fbx。</summary>
        [Summary("要渲的模型路径（绝对或相对路径），支持 .glb / .gltf / .fbx")]
        public string InputModelPath { get; set; }

        /// <summary>三张视图的输出目录；一般给该资产的 variants/views/。</summary>
        [Summary("三张视图的输出目录；一般给该资产的 variants/views/")]
        public string OutputDirectory { get; set; }

        /// <summary>单张视图的边长，像素；小于 64 或大于 2048 会被钳回区间。</summary>
        [Summary("单张视图的边长，像素；小于 64 或大于 2048 会被钳回区间")]
        [DefaultValue(512)]
        public int SideLength { get; set; }

        /// <summary>子进程超时秒数。</summary>
        [Summary("子进程超时秒数")]
        [DefaultValue(900)]
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>加工计划命令 art.plan 的参数。</summary>
    public sealed class ArtPlanArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>需求 id，如「REQ-0042」。</summary>
        [Summary("需求 id，如「REQ-0042」")]
        public string RequirementIdentifier { get; set; }

        /// <summary>资产 id，如「ASSET-0042-01」。</summary>
        [Summary("资产 id，如「ASSET-0042-01」")]
        public string AssetIdentifier { get; set; }

        /// <summary>业务模块名，用于取 Specifications/Business/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 Specifications/Business/<模块>/ 的就近覆盖")]
        [DefaultValue("")]
        public string ModuleName { get; set; }

        /// <summary>加工计划落盘路径；缺省落 _Tasks/&lt;需求id&gt;/30-outputs/&lt;资产id&gt;/加工计划.json。</summary>
        [Summary("加工计划落盘路径；缺省落 _Tasks/<需求id>/30-outputs/<资产id>/加工计划.json")]
        [DefaultValue("")]
        public string OutputPath { get; set; }
    }

    /// <summary>模型机检命令 art.modelcheck 的参数。</summary>
    public sealed class ArtModelCheckArguments
    {
        /// <summary>仓库根目录，相对当前工作目录。</summary>
        [Summary("仓库根目录，相对当前工作目录")]
        public string RepositoryRoot { get; set; }

        /// <summary>需求 id，如「REQ-0042」。</summary>
        [Summary("需求 id，如「REQ-0042」")]
        public string RequirementIdentifier { get; set; }

        /// <summary>资产 id，如「ASSET-0042-01」。</summary>
        [Summary("资产 id，如「ASSET-0042-01」")]
        public string AssetIdentifier { get; set; }

        /// <summary>模型度量文件的路径，由加工站产出。</summary>
        [Summary("模型度量文件的路径，由加工站产出")]
        public string MetricsPath { get; set; }

        /// <summary>业务模块名，用于取 Specifications/Business/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 Specifications/Business/<模块>/ 的就近覆盖")]
        [DefaultValue("")]
        public string ModuleName { get; set; }
    }

    /// <summary>主色板命令 art.palette 的参数。</summary>
    public sealed class ArtPaletteArguments
    {
        /// <summary>PNG 图片路径，必填。</summary>
        [Summary("PNG 图片路径，必填")]
        public string ImagePath { get; set; }

        /// <summary>聚类色数，默认 8。</summary>
        [Summary("聚类色数，默认 8")]
        [DefaultValue(8)]
        public int ClusterCount { get; set; }
    }

    /// <summary>离风格报告命令 art.deviation 的参数。</summary>
    public sealed class ArtDeviationArguments
    {
        /// <summary>池子根目录，必填。</summary>
        [Summary("池子根目录，必填")]
        public string PoolRoot { get; set; }

        /// <summary>定稿名，必填，对应 Pools/Designs/Final/&lt;名&gt;/final.json。</summary>
        [Summary("定稿名，必填，对应 Pools/Designs/Final/<名>/final.json")]
        public string FinalName { get; set; }

        /// <summary>图片根目录，必填，递归扫 *.png。</summary>
        [Summary("图片根目录，必填，递归扫 *.png")]
        public string ImageRoot { get; set; }

        /// <summary>列出条数上限，默认 20；0 或负数表示全列。</summary>
        [Summary("列出条数上限，默认 20；0 或负数表示全列")]
        [DefaultValue(20)]
        public int TopCount { get; set; }

        /// <summary>聚类色数，默认 8。</summary>
        [Summary("聚类色数，默认 8")]
        [DefaultValue(8)]
        public int ClusterCount { get; set; }
    }

    /// <summary>美术资产命令：art.request 建资产请求、art.validate 校验资产请求与溯源边车。</summary>
    public static class ArtCommands
    {
        /// <summary>
        /// 建一份资产请求：取号、组装、写盘，随即用通用校验器校验刚写的文件，校验不过就删除并返回失败。
        /// </summary>
        /// <param name="arguments">建资产请求命令参数。</param>
        [EditorCommand("art.request")]
        [Summary("建一份资产请求：取号、落盘并立刻自校验")]
        public static CommandResult Request(ArtRequestArguments arguments)
        {
            if (arguments == null)
            {
                return CommandResult.Failure("参数为空");
            }

            // 需求与工作项留空表示**这张图还没有主**：人在聊天里说「先出张图看看」，
            // 那时往往连需求都还没有。硬要一条需求才让出图，等于把「试一张」这件事挡在门外。
            // 落进 REQ-0000 这个收容所——它符合 id 模式，schema 与校验器一个字都不用改，
            // 而且事后要认领给某条需求时，人一眼就知道哪些是还没主的。
            if (string.IsNullOrWhiteSpace(arguments.Requirement))
            {
                arguments.Requirement = AssetRequest.UnownedRequirementIdentifier;
            }

            if (string.IsNullOrWhiteSpace(arguments.WorkItem))
            {
                arguments.WorkItem = AssetRequest.UnownedWorkItemIdentifier;
            }

            if (string.IsNullOrWhiteSpace(arguments.AssetType))
            {
                return CommandResult.Failure("参数 AssetType 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.NamingText))
            {
                return CommandResult.Failure("参数 NamingText 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.Description))
            {
                return CommandResult.Failure("参数 Description 为必填项");
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

            PoolSchema schema;
            try
            {
                schema = PoolSchemaLoader.Load(poolRoot, "asset-requests");
            }
            catch (FileNotFoundException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            var catalog = AssetSpecCatalog.Load(repositoryRoot, arguments.Module);
            var assetSpec = catalog.Find(arguments.AssetType);
            if (assetSpec == null)
            {
                var availableTypes = catalog.Types.Count == 0
                    ? "（无）"
                    : string.Join("、", catalog.Types.Keys);
                return CommandResult.Failure($"资产类型「{arguments.AssetType}」不在资产规格数据里；可用类型：{availableTypes}");
            }

            var specification = new Dictionary<string, string>();
            foreach (var pair in assetSpec.Values)
            {
                if (pair.Key.StartsWith("规格.", StringComparison.Ordinal))
                {
                    specification[pair.Key.Substring("规格.".Length)] = pair.Value;
                }
            }

            var destination = string.IsNullOrWhiteSpace(arguments.Destination) ? assetSpec.Destination : arguments.Destination;
            var domain = string.IsNullOrWhiteSpace(arguments.Domain) ? assetSpec.Domain : arguments.Domain;

            var identifier = AssetRequest.AllocateIdentifier(repositoryRoot, arguments.Requirement);
            var request = new AssetRequest(
                identifier,
                arguments.Requirement,
                arguments.WorkItem,
                domain,
                arguments.AssetType,
                specification,
                destination,
                arguments.NamingText,
                arguments.Description,
                new Dictionary<string, string>(),
                arguments.VariantCount,
                0,
                Array.Empty<string>(),
                false,
                "1.0.0");

            var filePath = AssetPaths.AssetRequestFile(repositoryRoot, arguments.Requirement, identifier);
            request.WriteTo(filePath);

            var findings = EntityDocumentValidator.Validate(filePath, schema);
            if (findings.Count > 0)
            {
                File.Delete(filePath);
                return CommandResult.Failure(
                    $"资产请求校验未通过，已删除文件：{identifier}",
                    findings.Select(finding => finding.ToDisplayText()).ToList());
            }

            var specFindings = AssetSpecInspector.Inspect(repositoryRoot, arguments.Requirement, arguments.Module);
            if (specFindings.Count > 0)
            {
                File.Delete(filePath);
                return CommandResult.Failure(
                    $"资产规格门禁未通过，已删除文件：{identifier}",
                    specFindings.Select(finding => finding.ToDisplayText()).ToList());
            }

            var lines = new List<string>
            {
                $"已建资产请求：{identifier}",
                $"落盘：{RelativeTo(repositoryRoot, filePath)}",
                $"规格来自：{assetSpec.SourceLayer} 层",
                "校验：通过"
            };

            return CommandResult.Success($"变体数 {arguments.VariantCount}，域 {domain}", lines);
        }

        /// <summary>
        /// 校验一个需求下全部资产请求与全部溯源边车：请求过「资产请求」schema、边车过「溯源」schema；
        /// 一份都没有时算通过，标题里如实写 0。
        /// </summary>
        /// <param name="arguments">校验命令参数。</param>
        [EditorCommand("art.validate")]
        [Summary("校验一个需求下全部资产请求与全部溯源边车")]
        public static CommandResult Validate(ArtValidateArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Requirement))
            {
                return CommandResult.Failure("参数 Requirement 为必填项");
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

            PoolSchema requestSchema;
            PoolSchema sidecarSchema;
            try
            {
                requestSchema = PoolSchemaLoader.Load(poolRoot, "asset-requests");
                sidecarSchema = PoolSchemaLoader.Load(poolRoot, "溯源");
            }
            catch (FileNotFoundException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            var findings = new List<PoolFinding>();
            var requestDirectory = AssetPaths.AssetRequestDirectory(repositoryRoot, arguments.Requirement);
            var requestCount = 0;
            var sidecarCount = 0;

            if (Directory.Exists(requestDirectory))
            {
                foreach (var requestFile in Directory.EnumerateFiles(requestDirectory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    requestCount++;
                    findings.AddRange(EntityDocumentValidator.Validate(requestFile, requestSchema));

                    var assetIdentifier = Path.GetFileNameWithoutExtension(requestFile);
                    var variantDirectory = AssetPaths.VariantDirectory(repositoryRoot, arguments.Requirement, assetIdentifier);
                    if (!Directory.Exists(variantDirectory))
                    {
                        continue;
                    }

                    foreach (var sidecarFile in Directory.EnumerateFiles(variantDirectory, "*.provenance.json", SearchOption.TopDirectoryOnly))
                    {
                        sidecarCount++;
                        findings.AddRange(EntityDocumentValidator.Validate(sidecarFile, sidecarSchema));
                    }
                }
            }

            var title = $"资产校验（资产请求 {requestCount} 份，边车 {sidecarCount} 份）";
            if (findings.Count == 0)
            {
                return CommandResult.Success($"{title}通过，问题 0 条");
            }

            return CommandResult.Failure(
                $"{title}失败，问题 {findings.Count} 条",
                findings.Select(finding => finding.ToDisplayText()).ToList());
        }

        /// <summary>
        /// 选片：装配选片卡片并产出站意图信封。
        /// 卡片装配不出来（变体目录缺失、没有合格变体等）判失败并逐条列问题；
        /// 卡片建出来了但带 findings 仍算成功，问题附在文案里。
        /// </summary>
        /// <param name="arguments">选片命令参数。</param>
        [EditorCommand("art.select")]
        [Summary("选片：装配选片卡片并产出站意图信封")]
        public static CommandResult Select(ArtSelectArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RequirementIdentifier))
            {
                return CommandResult.Failure("参数 RequirementIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.AssetIdentifier))
            {
                return CommandResult.Failure("参数 AssetIdentifier 为必填项");
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

            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"仓库根目录不存在：{repositoryRoot}");
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

            if (!Directory.Exists(poolRoot))
            {
                return CommandResult.Failure($"池子根目录不存在：{poolRoot}");
            }

            var result = SelectionOutboundPlanner.Plan(
                repositoryRoot,
                poolRoot,
                arguments.RequirementIdentifier,
                arguments.AssetIdentifier,
                arguments.EpicIdentifier ?? "",
                arguments.Round,
                DateTimeOffset.Now);

            if (result.Envelope == null)
            {
                return CommandResult.Failure(
                    $"选片卡片没装配出来，问题 {result.Findings.Count} 条",
                    result.Findings.Select(finding => finding.ToDisplayText()).ToList());
            }

            var outputPath = ResolveSelectionOutputPath(repositoryRoot, arguments, result.Card);
            try
            {
                WriteSelectionEnvelope(outputPath, result.Envelope);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return CommandResult.Failure($"出站意图信封写盘失败：{exception.Message}");
            }

            var recipients = result.Envelope.Routing.Recipients.Count == 0
                ? "无"
                : string.Join(",", result.Envelope.Routing.Recipients);
            var buttons = string.Join(" ", result.Card.Buttons);

            var lines = new List<string>
            {
                $"需求：{result.Envelope.RequirementIdentifier}",
                $"资产：{result.Card.AssetIdentifier}",
                $"第 {result.Card.Round} 轮",
                $"合格变体 {result.Card.QualifiedVariants.Count} 张、弃置 {result.Card.RejectedCount} 张",
                $"按钮：{buttons}",
                $"推送对象：{recipients}",
                $"命中步骤：{result.Envelope.Routing.Step.ToString()}",
                $"落盘：{RelativeTo(repositoryRoot, outputPath)}"
            };

            if (result.Findings.Count > 0)
            {
                foreach (var finding in result.Findings)
                {
                    lines.Add($"注意：{finding.ToDisplayText()}");
                }
            }

            return CommandResult.Success(
                $"选片卡片已装配，出站意图信封已生成（推送对象：{recipients}）",
                lines);
        }

        /// <summary>选片信封的落盘路径：给了 OutputPath 用它，否则落 _Tasks/&lt;需求id&gt;/40-出站/&lt;资产id&gt;-选片-第&lt;轮次&gt;轮.json。</summary>
        private static string ResolveSelectionOutputPath(string repositoryRoot, ArtSelectArguments arguments, SelectionCard card)
        {
            if (!string.IsNullOrWhiteSpace(arguments.OutputPath))
            {
                return Path.GetFullPath(arguments.OutputPath);
            }

            return Path.Combine(
                repositoryRoot,
                "_Tasks",
                card.RequirementIdentifier,
                "40-出站",
                $"{card.AssetIdentifier}-选片-第{card.Round}轮.json");
        }

        /// <summary>把选片出站意图信封写成与 OutboundEnvelope 同构的 JSON 文件。</summary>
        private static void WriteSelectionEnvelope(string filePath, OutboundEnvelope envelope)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var writeBack = new JsonObject();
            foreach (var pair in envelope.WriteBackFields)
            {
                writeBack[pair.Key] = pair.Value;
            }

            var recipients = new JsonArray();
            foreach (var recipient in envelope.Routing.Recipients)
            {
                recipients.Add(recipient);
            }

            var content = new JsonObject
            {
                ["需求id"] = envelope.RequirementIdentifier,
                ["事件"] = envelope.Event,
                ["时间"] = envelope.Moment.ToString("o"),
                ["回写"] = writeBack,
                ["卡片"] = new JsonObject
                {
                    ["类型"] = envelope.Routing.CardType,
                    ["职责"] = envelope.Routing.Duty,
                    ["收件人"] = recipients,
                    ["命中步骤"] = envelope.Routing.Step.ToString(),
                    ["理由"] = envelope.Routing.Reason
                },
                ["摘要"] = envelope.Summary
            };

            File.WriteAllText(filePath, content.ToJsonString(CreateWriteOptions()), new UTF8Encoding(false));
        }

        /// <summary>
        /// 写盘选项：以 JsonSerializerOptions.Default 为基类带上默认 TypeInfoResolver；
        /// 信封里的 JsonArray 含字符串元素，.NET 10 下无 resolver 的 options 序列化它们会抛异常。
        /// </summary>
        private static JsonSerializerOptions CreateWriteOptions()
        {
            return new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        /// <summary>
        /// 跑能力对账：本地形态 driver 的依赖清单逐条对着探测输出查「在不在」。
        /// 探测输出文件不存在时，报错文案点出自述里的「能力探测」值，让调用方知道先跑什么生成探测输出。
        /// </summary>
        /// <param name="arguments">能力对账命令参数。</param>
        [EditorCommand("art.caps")]
        [Summary("能力对账：本地形态 driver 的依赖清单与探测输出对账")]
        public static CommandResult Capability(ArtCapabilityArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.DriverName))
            {
                return CommandResult.Failure("参数 DriverName 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.ProbeResultPath))
            {
                return CommandResult.Failure("参数 ProbeResultPath 为必填项");
            }

            var repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            if (!Directory.Exists(repositoryRoot))
            {
                return CommandResult.Failure($"位置：{repositoryRoot}；原因：仓库根目录不存在；修复：把 RepositoryRoot 指向仓库根");
            }

            BridgeDriverDescriptor descriptor;
            try
            {
                descriptor = BridgeDriverDescriptor.Load(repositoryRoot, arguments.DriverName);
            }
            catch (InvalidOperationException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            if (descriptor.Form != "本地")
            {
                return CommandResult.Failure("能力对账只对本地形态 driver 有意义");
            }

            DependencyManifest manifest;
            try
            {
                manifest = DependencyManifest.Load(repositoryRoot, arguments.DriverName);
            }
            catch (InvalidOperationException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            var probeResultPath = Path.GetFullPath(arguments.ProbeResultPath);
            CapabilityProbeResult probeResult;
            try
            {
                probeResult = CapabilityProbeResult.LoadFromFile(probeResultPath);
            }
            catch (InvalidOperationException exception)
            {
                if (!File.Exists(probeResultPath))
                {
                    var probeCommand = ReadProbeCommand(repositoryRoot, arguments.DriverName);
                    return CommandResult.Failure(
                        $"找不到能力探测输出：{probeResultPath}；跑「{probeCommand}」生成探测输出后再对账");
                }

                return CommandResult.Failure(exception.Message);
            }

            var report = CapabilityReconciler.Reconcile(arguments.DriverName, manifest, probeResult);
            if (report.Findings.Count == 0)
            {
                return CommandResult.Success($"能力对账通过（依赖 {report.DependencyCount} 项，全部满足）");
            }

            return CommandResult.Failure(
                $"能力对账未通过（依赖 {report.DependencyCount} 项，缺 {report.DependencyCount - report.SatisfiedCount} 项）",
                report.Findings.Select(finding => finding.ToDisplayText()).ToList());
        }

        /// <summary>
        /// 渲模型三视图：调加工站的 render 动作，出前 / 侧 / 45° 三张 PNG。
        /// 出图落在指定目录，文件名是「&lt;模型文件名（带后缀）&gt;.&lt;视角&gt;.png」——
        /// 选片卡的九宫格按 AssetPaths.VariantViewFile 到那儿去找，所以输出目录一般直接给
        /// 该资产的 variants/views/，别随手指到别处。
        /// </summary>
        /// <param name="arguments">三视图命令参数。</param>
        [EditorCommand("art.views")]
        [Summary("渲模型三视图：前 / 侧 / 45° 各一张 PNG，落到指定目录")]
        public static CommandResult Views(ArtViewsArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.Driver))
            {
                return CommandResult.Failure("参数 Driver 为必填项，值取 Bridges/ 下的目录名");
            }

            if (string.IsNullOrWhiteSpace(arguments.InputModelPath))
            {
                return CommandResult.Failure("参数 InputModelPath 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.OutputDirectory))
            {
                return CommandResult.Failure("参数 OutputDirectory 为必填项");
            }

            string repositoryRoot;
            string inputModelPath;
            string outputDirectory;
            try
            {
                repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
                inputModelPath = Path.GetFullPath(arguments.InputModelPath);
                outputDirectory = Path.GetFullPath(arguments.OutputDirectory);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"路径参数无法解析为绝对路径：{exception.Message}");
            }

            if (!File.Exists(inputModelPath))
            {
                return CommandResult.Failure($"输入模型不存在：{inputModelPath}");
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["输入模型"] = inputModelPath,
                ["输出目录"] = outputDirectory,
                ["边长"] = arguments.SideLength
            });

            var result = BridgeInvoker.Invoke(repositoryRoot, arguments.Driver, "render", payload, arguments.TimeoutSeconds);
            if (!result.Succeeded)
            {
                return CommandResult.Failure(result.HumanText, new[] { $"错误码：{result.ErrorCode}" });
            }

            var lines = new List<string>();
            if (result.Payload.ValueKind == JsonValueKind.Object
                && result.Payload.TryGetProperty("输出图", out var views)
                && views.ValueKind == JsonValueKind.Array)
            {
                foreach (var view in views.EnumerateArray())
                {
                    if (view.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var viewName = view.TryGetProperty("视角", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : "";
                    var viewPath = view.TryGetProperty("路径", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() : "";
                    lines.Add($"{viewName}：{RelativeTo(repositoryRoot, viewPath)}");
                }
            }

            return CommandResult.Success($"已渲出 {lines.Count} 张视图", lines);
        }

        /// <summary>
        /// 生成加工计划：读资产请求，从规格数据算出八步加工参数并落盘。
        /// findings 非空不算失败——缺一个可选规格键该让人看见，不该让门禁红。
        /// </summary>
        /// <param name="arguments">加工计划命令参数。</param>
        [EditorCommand("art.plan")]
        [Summary("加工计划：从资产请求与规格数据算出八步加工参数")]
        public static CommandResult Plan(ArtPlanArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.RequirementIdentifier))
            {
                return CommandResult.Failure("参数 RequirementIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.AssetIdentifier))
            {
                return CommandResult.Failure("参数 AssetIdentifier 为必填项");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            var requestFile = AssetPaths.AssetRequestFile(repositoryRoot, arguments.RequirementIdentifier, arguments.AssetIdentifier);
            if (!File.Exists(requestFile))
            {
                return CommandResult.Failure($"资产请求文件不存在：{requestFile}");
            }

            var request = AssetRequest.Read(requestFile);
            var plan = ProcessingPlanBuilder.Build(repositoryRoot, request, arguments.ModuleName ?? "");

            var outputPath = ResolvePlanOutputPath(repositoryRoot, arguments);
            try
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outputPath, plan.ToJsonText(), new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommandResult.Failure($"加工计划写盘失败：{exception.Message}");
            }

            var enabledCount = plan.Steps.Count(step => step.IsEnabled);
            var disabledCount = plan.Steps.Count - enabledCount;
            var lines = new List<string>
            {
                $"资产：{plan.AssetIdentifier}（{plan.AssetType}）",
                $"八步：启用 {enabledCount} 步、禁用 {disabledCount} 步",
                $"落盘：{RelativeTo(repositoryRoot, outputPath)}"
            };

            foreach (var finding in plan.Findings)
            {
                lines.Add($"注意：{finding.ToDisplayText()}");
            }

            return CommandResult.Success(
                $"加工计划已生成（启用 {enabledCount} 步、禁用 {disabledCount} 步）",
                lines);
        }

        /// <summary>
        /// 模型机检：读资产请求与模型度量，跑面数 / 材质 / 贴图 / 包围盒 / 骨骼五项。
        /// 有 finding 判失败并逐条列出；度量文件不存在时点出「先跑加工站」。
        /// </summary>
        /// <param name="arguments">模型机检命令参数。</param>
        [EditorCommand("art.modelcheck")]
        [Summary("模型机检：面数 / 材质 / 贴图 / 包围盒 / 骨骼五项")]
        public static CommandResult ModelCheck(ArtModelCheckArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.RepositoryRoot))
            {
                return CommandResult.Failure("参数 RepositoryRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.RequirementIdentifier))
            {
                return CommandResult.Failure("参数 RequirementIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.AssetIdentifier))
            {
                return CommandResult.Failure("参数 AssetIdentifier 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.MetricsPath))
            {
                return CommandResult.Failure("参数 MetricsPath 为必填项");
            }

            string repositoryRoot;
            try
            {
                repositoryRoot = Path.GetFullPath(arguments.RepositoryRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 RepositoryRoot 无法解析为绝对路径：{exception.Message}");
            }

            var requestFile = AssetPaths.AssetRequestFile(repositoryRoot, arguments.RequirementIdentifier, arguments.AssetIdentifier);
            if (!File.Exists(requestFile))
            {
                return CommandResult.Failure($"资产请求文件不存在：{requestFile}");
            }

            var request = AssetRequest.Read(requestFile);

            string metricsPath;
            try
            {
                metricsPath = Path.GetFullPath(arguments.MetricsPath);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 MetricsPath 无法解析为绝对路径：{exception.Message}");
            }

            if (!File.Exists(metricsPath))
            {
                return CommandResult.Failure($"模型度量由加工站产出，先跑加工站再机检：{metricsPath}");
            }

            ModelMetrics metrics;
            try
            {
                metrics = ModelMetrics.LoadFromFile(metricsPath);
            }
            catch (InvalidOperationException exception)
            {
                return CommandResult.Failure(exception.Message);
            }

            var findings = ModelInspector.Inspect(repositoryRoot, request, metrics, arguments.ModuleName ?? "");
            if (findings.Count == 0)
            {
                return CommandResult.Success("模型机检通过（五项全过）");
            }

            return CommandResult.Failure(
                $"模型机检未通过，问题 {findings.Count} 条",
                findings.Select(finding => finding.ToDisplayText()).ToList());
        }

        /// <summary>
        /// 主色板：对一张 PNG 做确定性 k-means 聚类，输出主色、权重与样本数。
        /// 解码失败判失败；聚类没跑成（全透明等）算成功但结论行明说没算成，绝不输出空色板了事（决策 42）。
        /// </summary>
        /// <param name="arguments">主色板命令参数。</param>
        [EditorCommand("art.palette")]
        [Summary("对一张 PNG 出主色板：确定性 k-means 聚类")]
        public static CommandResult Palette(ArtPaletteArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.ImagePath))
            {
                return CommandResult.Failure("参数 ImagePath 为必填项");
            }

            string imagePath;
            try
            {
                imagePath = Path.GetFullPath(arguments.ImagePath);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 ImagePath 无法解析为绝对路径：{exception.Message}");
            }

            var decode = PngDecoder.DecodeFile(imagePath);
            if (!decode.Succeeded)
            {
                return CommandResult.Failure($"图片解码失败：{decode.FailureReason}");
            }

            var clusterCount = arguments.ClusterCount > 0 ? arguments.ClusterCount : ColorPalette.DefaultClusterCount;
            var result = ColorPalette.Cluster(decode.Image, clusterCount);
            if (!result.Clustered)
            {
                return CommandResult.Success($"主色板没算成：{result.FailureReason}");
            }

            var lines = new List<string>();
            foreach (var swatch in result.Swatches)
            {
                lines.Add($"{swatch.Color.ToHex()}  权重 {FormatFixedFour(swatch.Weight)}  样本 {swatch.SampleCount}");
            }

            return CommandResult.Success(
                $"主色板（采样 {result.SampledPixelCount} 像素，跳过透明 {result.SkippedTransparentCount} 像素，共 {result.Swatches.Count} 色）",
                lines);
        }

        /// <summary>
        /// 离风格报告：扫图片根目录下全部 PNG，对每张算「主色聚类 vs 定稿色板最小距离和」，
        /// 按距离降序列出 top-N。只报告不自动行动——这条命令一个字都不写盘。
        /// 定稿色板没读成时明确说「报告没出」并列出全部跳过项，绝不说成「全部资产都符合风格」（决策 42）；
        /// 被 TopCount 截断时加一行说明（决策 46 要求跳过项也必须逐条贴出来）。
        /// </summary>
        /// <param name="arguments">离风格报告命令参数。</param>
        [EditorCommand("art.deviation")]
        [Summary("离风格报告：资产主色 vs 定稿色板距离和排序，只报告不行动")]
        public static CommandResult Deviation(ArtDeviationArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.PoolRoot))
            {
                return CommandResult.Failure("参数 PoolRoot 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.FinalName))
            {
                return CommandResult.Failure("参数 FinalName 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.ImageRoot))
            {
                return CommandResult.Failure("参数 ImageRoot 为必填项");
            }

            string poolRoot;
            try
            {
                poolRoot = Path.GetFullPath(arguments.PoolRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 PoolRoot 无法解析为绝对路径：{exception.Message}");
            }

            string imageRoot;
            try
            {
                imageRoot = Path.GetFullPath(arguments.ImageRoot);
            }
            catch (Exception exception)
            {
                return CommandResult.Failure($"参数 ImageRoot 无法解析为绝对路径：{exception.Message}");
            }

            if (!Directory.Exists(imageRoot))
            {
                return CommandResult.Failure($"图片根目录不存在：{imageRoot}");
            }

            // 递归取全部 .png（大小写不敏感），按路径序数序排（确定性）。
            var imagePaths = new List<string>();
            foreach (var file in Directory.EnumerateFiles(imageRoot, "*.png", SearchOption.AllDirectories))
            {
                imagePaths.Add(file);
            }

            imagePaths.Sort(StringComparer.Ordinal);

            var palette = FinalPalette.Load(poolRoot, arguments.FinalName);
            var clusterCount = arguments.ClusterCount > 0 ? arguments.ClusterCount : ColorPalette.DefaultClusterCount;
            var result = StyleDeviationAnalyzer.Measure(imagePaths, palette, clusterCount, arguments.TopCount);

            if (!result.PaletteLoaded)
            {
                var lines = new List<string>();
                if (result.Skipped.Count == 0)
                {
                    lines.Add("跳过：无");
                }
                else
                {
                    foreach (var entry in result.Skipped)
                    {
                        lines.Add($"跳过  {RelativeTo(imageRoot, entry.AssetPath)}  {entry.SkipReason}");
                    }
                }

                return CommandResult.Failure($"离风格报告没出：{result.PaletteFailureReason}", lines);
            }

            var scannedCount = imagePaths.Count;
            var skippedCount = result.Skipped.Count;
            var measuredCount = scannedCount - skippedCount;
            var listedCount = result.Ranked.Count;

            var outputLines = new List<string>();
            foreach (var entry in result.Ranked)
            {
                var relativePath = RelativeTo(imageRoot, entry.AssetPath);
                var topColors = entry.Swatches.Take(3).Select(swatch => swatch.Color.ToHex()).ToList();
                var colorsText = topColors.Count == 0 ? "无" : string.Join(",", topColors);
                outputLines.Add($"距离 {FormatFixedFour(entry.Deviation)}  {relativePath}  主色 {colorsText}");
            }

            if (listedCount < measuredCount)
            {
                outputLines.Add($"（共 {measuredCount} 张算成，按 TopCount 只列了前 {listedCount} 张）");
            }

            if (skippedCount == 0)
            {
                outputLines.Add("跳过：无");
            }
            else
            {
                foreach (var entry in result.Skipped)
                {
                    outputLines.Add($"跳过  {RelativeTo(imageRoot, entry.AssetPath)}  {entry.SkipReason}");
                }
            }

            return CommandResult.Success(
                $"离风格报告（定稿 {palette.Name}@v{palette.Version}，扫描 {scannedCount} 张，算成 {measuredCount} 张，跳过 {skippedCount} 张，列出前 {listedCount} 张）",
                outputLines);
        }

        /// <summary>把 double 渲染成固定四位小数。</summary>
        private static string FormatFixedFour(double value)
        {
            return value.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        /// <summary>加工计划落盘路径：给了 OutputPath 用它，否则落 _Tasks/&lt;需求id&gt;/30-outputs/&lt;资产id&gt;/加工计划.json。</summary>
        private static string ResolvePlanOutputPath(string repositoryRoot, ArtPlanArguments arguments)
        {
            if (!string.IsNullOrWhiteSpace(arguments.OutputPath))
            {
                return Path.GetFullPath(arguments.OutputPath);
            }

            return Path.Combine(
                repositoryRoot,
                "_Tasks",
                arguments.RequirementIdentifier,
                "30-outputs",
                arguments.AssetIdentifier,
                "加工计划.json");
        }

        /// <summary>把绝对路径转成相对仓库根的路径；无法相对化时原样返回。</summary>
        private static string RelativeTo(string basePath, string fullPath)
        {
            var relative = Path.GetRelativePath(basePath, fullPath);
            return relative.StartsWith("..", StringComparison.Ordinal) ? fullPath : relative;
        }

        /// <summary>从 driver 自述文件读「能力探测」字段的值；缺失或不是字符串给空串。</summary>
        private static string ReadProbeCommand(string repositoryRoot, string driverName)
        {
            var driverFilePath = BridgeDriverDescriptor.DriverFile(repositoryRoot, driverName);
            if (!File.Exists(driverFilePath))
            {
                return "";
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(driverFilePath));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("能力探测", out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? "";
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 自述文件已在上面被 BridgeDriverDescriptor.Load 校验过，这里读不到就退回空串。
            }

            return "";
        }
    }
}
