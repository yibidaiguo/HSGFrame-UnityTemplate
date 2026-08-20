using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        [Summary("需求 id，如「REQ-0042」")]
        public string Requirement { get; set; }

        /// <summary>工作项 id，如「WI-0042-03」。</summary>
        [Summary("工作项 id，如「WI-0042-03」")]
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

        /// <summary>业务模块名，用于取 规范/业务/&lt;模块&gt;/ 的就近覆盖。</summary>
        [Summary("业务模块名，用于取 规范/业务/<模块>/ 的就近覆盖")]
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
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Requirement))
            {
                return CommandResult.Failure("参数 Requirement 为必填项");
            }

            if (string.IsNullOrWhiteSpace(arguments.WorkItem))
            {
                return CommandResult.Failure("参数 WorkItem 为必填项");
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
                schema = PoolSchemaLoader.Load(poolRoot, "资产请求");
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
                requestSchema = PoolSchemaLoader.Load(poolRoot, "资产请求");
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

                    foreach (var sidecarFile in Directory.EnumerateFiles(variantDirectory, "*.溯源.json", SearchOption.TopDirectoryOnly))
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
