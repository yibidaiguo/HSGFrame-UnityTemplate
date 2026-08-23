using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.CommandHost.Commands
{
    /// <summary>界面规格命令的公共参数。</summary>
    public sealed class InterfaceSpecArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        [DefaultValue("")]
        public string RepositoryRoot { get; set; }

        /// <summary>界面 id，如 UI-0007；留空表示全扫。</summary>
        [Summary("界面 id，如 UI-0007；留空表示 Pools/Designs/Interfaces/ 下全扫")]
        [DefaultValue("")]
        public string Interface { get; set; }

        /// <summary>业务模块名，取就近的元素类型模板覆盖。</summary>
        [Summary("业务模块名，取 Specifications/Business/<模块>/ 的就近覆盖；留空只用基线与项目两层")]
        [DefaultValue("")]
        public string Module { get; set; }

        /// <summary>只校验不写文件。</summary>
        [Summary("为 true 时只比对不写文件（幂等门禁用）")]
        [DefaultValue(false)]
        public bool VerifyOnly { get; set; }
    }

    /// <summary>「从需求产界面规格草案」的参数。</summary>
    public sealed class InterfaceSpecDraftArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        [DefaultValue("")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录。</summary>
        [Summary("池子根目录")]
        [DefaultValue("Pools")]
        public string PoolRoot { get; set; }

        /// <summary>照哪条需求产。</summary>
        [Summary("需求 id，如 REQ-0042")]
        public string Requirement { get; set; }

        /// <summary>面板名，PascalCase。</summary>
        [Summary("面板名，PascalCase，如 Inventory；决定 uidef 名与资产模块目录")]
        public string Panel { get; set; }

        /// <summary>画布宽。</summary>
        [Summary("画布宽；缺省 1920")]
        [DefaultValue(1920)]
        public int CanvasWidth { get; set; }

        /// <summary>画布高。</summary>
        [Summary("画布高；缺省 1080")]
        [DefaultValue(1080)]
        public int CanvasHeight { get; set; }

        /// <summary>单次调用超时秒数。</summary>
        [Summary("单次调用超时秒数")]
        [DefaultValue(300)]
        public int TimeoutSeconds { get; set; }

        /// <summary>只打提示词不真调。</summary>
        [Summary("为 true 时只把要发的提示词打出来，不调执行后端（不花钱）")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }
    }

    /// <summary>
    /// 界面规格这一层的命令：校验、渲布局图、生成 uidef、算资产清单。
    ///
    /// 这四条都是**确定性**的——同一份规格跑多少遍结果一样，不调任何模型。
    /// 「从需求聊出一份规格草案」那一步要调执行后端，是另一条路，不混在这里：
    /// 混在一起的话，一条本该秒回的校验命令会变得又慢又花钱。
    /// </summary>
    public static class InterfaceCommands
    {
        /// <summary>
        /// 从一条需求产一份界面规格草案。**这是这一族里唯一花钱的一条**——
        /// 它要调执行后端，所以默认就带 --dry-run 那条出口，先看提示词再决定发不发。
        /// </summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("ui.spec.draft")]
        [Summary("从需求产界面规格草案：调执行后端，产出后自动校验并渲布局图")]
        public static CommandResult Draft(InterfaceSpecDraftArguments arguments)
        {
            if (arguments == null
                || string.IsNullOrWhiteSpace(arguments.Requirement)
                || string.IsNullOrWhiteSpace(arguments.Panel))
            {
                return CommandResult.Failure("参数 Requirement 与 Panel 均为必填项");
            }

            var repositoryRoot = string.IsNullOrWhiteSpace(arguments.RepositoryRoot)
                ? Directory.GetCurrentDirectory()
                : arguments.RepositoryRoot;
            var poolRoot = Path.IsPathRooted(arguments.PoolRoot ?? "")
                ? arguments.PoolRoot
                : Path.Combine(repositoryRoot, arguments.PoolRoot ?? "Pools");

            var requirementFile = PoolPaths.RequirementFile(poolRoot, arguments.Requirement);
            if (!File.Exists(requirementFile))
            {
                return CommandResult.Failure($"需求不存在：{requirementFile}");
            }

            string requirementText;
            try
            {
                requirementText = File.ReadAllText(requirementFile);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommandResult.Failure("需求读不动：" + exception.Message);
            }

            var catalog = UiElementTemplateCatalog.Load(repositoryRoot, arguments.Panel);

            // 按三档读取策略取锚点：默认档（总设计层 + 定稿那几行），**不取参考图**——
            // 这一步产的是功能契约，不谈外观，参考图在出图那一步才有用。
            var anchor = StyleAnchorResolver.Resolve(repositoryRoot, arguments.Panel, "", referenceImageCount: 0);

            var prompt = InterfaceSpecDraftPrompt.Build(
                requirementText, arguments.Panel, arguments.CanvasWidth, arguments.CanvasHeight, catalog, anchor);

            var lines = new List<string>(anchor.Notes);

            if (arguments.DryRun)
            {
                lines.Add("干跑：没有调执行后端，下面是要发的提示词");
                lines.Add(prompt);
                return CommandResult.Success("干跑完成，未发任何请求", lines);
            }

            var routeTable = BridgeRouteTable.Load(repositoryRoot);
            if (!routeTable.TryResolvePort("执行后端", out var backendDriver, out var driverReason))
            {
                return CommandResult.Failure("执行后端取不到：" + driverReason);
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["提示"] = prompt,
                ["上下文"] = InterfaceSpecDraftPrompt.SystemContextText
            });

            var call = BridgeInvoker.Invoke(repositoryRoot, backendDriver, "complete", payload, arguments.TimeoutSeconds);
            if (!call.Succeeded)
            {
                return CommandResult.Failure($"执行后端调用失败（{call.ErrorCode}）：{call.HumanText}", lines);
            }

            var modelText = ReadPayloadText(call.Payload);
            var identifier = InterfaceSpecDraftPrompt.AllocateIdentifier(repositoryRoot);

            if (!InterfaceSpecDraftPrompt.TryParse(modelText, identifier, arguments.Requirement, out var draft, out var parseReason))
            {
                return CommandResult.Failure("读不懂执行后端的回答：" + parseReason, lines);
            }

            var path = InterfaceSpec.FilePathFor(repositoryRoot, identifier);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(
                    path,
                    draft.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
                    new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommandResult.Failure("界面规格写不下去：" + exception.Message, lines);
            }

            lines.Add($"草案已落盘：{RelativeTo(repositoryRoot, path)}");

            // **产出后立刻校验并渲布局图**，不留给人手跑：
            // 草案是模型写的，不校验就发给人看，等于把「模型编了个不合规的东西」当成结论。
            var spec = new InterfaceSpec(draft, path);
            var findings = InterfaceSpecInspector.Inspect(spec, catalog);
            foreach (var finding in findings)
            {
                lines.Add("校验：" + finding.ToDisplayText());
            }

            var layout = LayoutImageRenderer.Write(repositoryRoot, spec, out _, out var layoutReason);
            lines.Add(layout.Length > 0
                ? $"布局图：{RelativeTo(repositoryRoot, layout)}"
                : $"布局图没渲成：{layoutReason}");

            var manifest = InterfaceAssetManifest.Build(repositoryRoot, spec, catalog);
            lines.Add($"资产清单：元素 {manifest.Count} 个，真要出 {InterfaceAssetManifest.CountToGenerate(manifest)} 张");

            return findings.Count == 0
                ? CommandResult.Success($"{identifier} 草案已产出（元素 {spec.Elements.Count} 个）", lines)
                : CommandResult.Failure(
                    $"{identifier} 草案产出了，但校验有 {findings.Count} 条问题——**草案已经落盘，改它再跑 ui.spec.validate**",
                    lines);
        }

        /// <summary>读桥回来的文本字段。</summary>
        /// <param name="payload">桥的响应载荷。</param>
        private static string ReadPayloadText(JsonElement payload)
        {
            return payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("文本", out var element)
                && element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? ""
                : "";
        }

        /// <summary>校验界面规格：面板级必填、元素 id 唯一与合规、父子无环、按类型模板查必填。</summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("ui.spec.validate")]
        [Summary("校验界面规格：id 唯一与合规、父子无环、按元素类型模板查必填、验收可测")]
        public static CommandResult Validate(InterfaceSpecArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments);
            var findings = new List<PoolFinding>();
            var count = 0;
            string reason;

            foreach (var spec in LoadSpecs(repositoryRoot, arguments, findings, out reason))
            {
                count++;
                findings.AddRange(InterfaceSpecInspector.Inspect(
                    spec, UiElementTemplateCatalog.Load(repositoryRoot, arguments.Module ?? "")));
            }

            if (reason.Length > 0)
            {
                return CommandResult.Failure(reason);
            }

            var lines = new List<string>();
            foreach (var finding in findings)
            {
                lines.Add(finding.ToDisplayText());
            }

            return findings.Count == 0
                ? CommandResult.Success($"界面规格校验通过（{count} 份）", lines)
                : CommandResult.Failure($"界面规格校验未通过（{count} 份，问题 {findings.Count} 条）", lines);
        }

        /// <summary>把界面规格渲成白块布局图（SVG）。</summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("ui.spec.layout")]
        [Summary("把界面规格渲成白块布局图 SVG（确定性，可进幂等门禁）")]
        public static CommandResult Layout(InterfaceSpecArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments);
            var lines = new List<string>();
            var problems = new List<string>();
            var findings = new List<PoolFinding>();
            string reason;

            foreach (var spec in LoadSpecs(repositoryRoot, arguments, findings, out reason))
            {
                if (reason.Length > 0)
                {
                    return CommandResult.Failure(reason);
                }

                var path = LayoutImageRenderer.OutputPath(repositoryRoot, spec.Identifier);

                if (arguments.VerifyOnly)
                {
                    var expected = LayoutImageRenderer.Render(spec);
                    if (!File.Exists(path))
                    {
                        problems.Add($"布局图尚未生成：{Path.GetFileName(path)}");
                    }
                    else if (!string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal))
                    {
                        problems.Add($"布局图与界面规格不一致：{Path.GetFileName(path)}——重跑 ui.spec.layout");
                    }

                    continue;
                }

                var written = LayoutImageRenderer.Write(repositoryRoot, spec, out var changed, out var writeReason);
                if (written.Length == 0)
                {
                    problems.Add($"{spec.Identifier} 的布局图写不出：{writeReason}");
                    continue;
                }

                lines.Add($"{spec.Identifier}　{(changed ? "已更新" : "无变化")}　{RelativeTo(repositoryRoot, written)}");
            }

            foreach (var finding in findings)
            {
                problems.Add(finding.ToDisplayText());
            }

            return problems.Count == 0
                ? CommandResult.Success(arguments.VerifyOnly ? "布局图与界面规格一致" : $"布局图已生成（{lines.Count} 份）", lines)
                : CommandResult.Failure($"布局图有问题，{problems.Count} 条", problems);
        }

        /// <summary>算这一屏的资产清单：哪些要出图、哪些复用、哪些根本不出。</summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("ui.spec.manifest")]
        [Summary("算资产清单：按元素类型、复用档、重复数收敛出真正要发的生图次数")]
        public static CommandResult Manifest(InterfaceSpecArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments);
            var findings = new List<PoolFinding>();
            var lines = new List<string>();
            var totalToGenerate = 0;
            var totalElements = 0;
            string reason;

            var catalog = UiElementTemplateCatalog.Load(repositoryRoot, arguments.Module ?? "");
            foreach (var spec in LoadSpecs(repositoryRoot, arguments, findings, out reason))
            {
                if (reason.Length > 0)
                {
                    return CommandResult.Failure(reason);
                }

                var manifest = InterfaceAssetManifest.Build(repositoryRoot, spec, catalog);
                var toGenerate = InterfaceAssetManifest.CountToGenerate(manifest);
                totalToGenerate += toGenerate;
                totalElements += manifest.Count;

                lines.Add($"{spec.Identifier}　{spec.PanelName}　元素 {manifest.Count} 个 → 要出 {toGenerate} 张");
                foreach (var entry in manifest)
                {
                    lines.Add($"  · {entry.ElementIdentifier}（{entry.ElementType}）　{entry.Action}"
                        + (entry.Naming.Length > 0 ? $"　{entry.Destination}{entry.Naming}.png　{entry.Width}×{entry.Height}" : "")
                        + (entry.Reason.Length > 0 ? $"　{entry.Reason}" : ""));
                }
            }

            if (findings.Count > 0)
            {
                var problems = new List<string>();
                foreach (var finding in findings)
                {
                    problems.Add(finding.ToDisplayText());
                }

                return CommandResult.Failure($"资产清单算不出来，{findings.Count} 条问题", problems);
            }

            return CommandResult.Success($"元素 {totalElements} 个，真要出 {totalToGenerate} 张", lines);
        }

        /// <summary>从界面规格生成 uidef（再跑 ui.scaffold 就出 UXML/USS/C#）。</summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("ui.spec.scaffold")]
        [Summary("从界面规格生成 uidef：依赖方向是规格 → uidef，不是拆图结果 → uidef")]
        public static CommandResult Scaffold(InterfaceSpecArguments arguments)
        {
            var repositoryRoot = ResolveRoot(arguments);
            var findings = new List<PoolFinding>();
            var lines = new List<string>();
            var catalog = UiElementTemplateCatalog.Load(repositoryRoot, arguments.Module ?? "");
            string reason;

            foreach (var spec in LoadSpecs(repositoryRoot, arguments, findings, out reason))
            {
                if (reason.Length > 0)
                {
                    return CommandResult.Failure(reason);
                }

                var manifest = InterfaceAssetManifest.Build(repositoryRoot, spec, catalog);
                var elements = InterfaceSpecProjection.ToPanelElements(spec, manifest);
                var panelIdentifier = InterfaceSpecProjection.PanelIdentifier(spec);

                var path = UiPanelDefinitionWriter.Write(
                    repositoryRoot, spec.Title.Length > 0 ? spec.Title : spec.PanelName, panelIdentifier, elements);

                if (path.Length == 0)
                {
                    return CommandResult.Failure($"{spec.Identifier} 的 uidef 写不出来");
                }

                lines.Add($"{spec.Identifier} → {RelativeTo(repositoryRoot, path)}（元素 {elements.Count} 个）");
            }

            if (findings.Count > 0)
            {
                var problems = new List<string>();
                foreach (var finding in findings)
                {
                    problems.Add(finding.ToDisplayText());
                }

                return CommandResult.Failure($"uidef 生成不了，{findings.Count} 条问题", problems);
            }

            lines.Add("接着跑 ui.scaffold 出 UXML/USS/C#");
            return CommandResult.Success($"uidef 已生成（{lines.Count - 1} 份）", lines);
        }

        /// <summary>取仓库根：参数给了用参数的，没给用当前目录。</summary>
        /// <param name="arguments">命令参数。</param>
        private static string ResolveRoot(InterfaceSpecArguments arguments)
        {
            return string.IsNullOrWhiteSpace(arguments.RepositoryRoot)
                ? Directory.GetCurrentDirectory()
                : arguments.RepositoryRoot;
        }

        /// <summary>
        /// 读要处理的界面规格：给了 id 就读那一份，没给就全扫。
        /// 读不动的那份**记进 findings 而不是抛异常**——一份坏文件不该让整批停下。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="arguments">命令参数。</param>
        /// <param name="findings">读不动的记这里。</param>
        /// <param name="fatalReason">致命错误（比如点名的那份不存在）；正常为空串。</param>
        private static IReadOnlyList<InterfaceSpec> LoadSpecs(
            string repositoryRoot, InterfaceSpecArguments arguments, List<PoolFinding> findings, out string fatalReason)
        {
            fatalReason = "";
            var specs = new List<InterfaceSpec>();
            var identifier = (arguments.Interface ?? "").Trim();

            if (identifier.Length > 0)
            {
                var path = InterfaceSpec.FilePathFor(repositoryRoot, identifier);
                if (!File.Exists(path))
                {
                    fatalReason = $"界面规格不存在：{path}";
                    return specs;
                }

                if (!InterfaceSpec.TryRead(path, out var one, out var reason))
                {
                    fatalReason = reason;
                    return specs;
                }

                specs.Add(one);
                return specs;
            }

            var directory = InterfaceSpec.Directory(repositoryRoot);
            if (!Directory.Exists(directory))
            {
                return specs;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (InterfaceSpec.TryRead(path, out var spec, out var reason))
                {
                    specs.Add(spec);
                }
                else
                {
                    findings.Add(new PoolFinding(
                        path, reason, "把文件修好", "Pools/Schema/Baseline/interface-spec.schema.json"));
                }
            }

            return specs;
        }

        /// <summary>把绝对路径缩成相对仓库根的路径，日志里好看。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="path">绝对路径。</param>
        private static string RelativeTo(string repositoryRoot, string path)
        {
            try
            {
                return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return path;
            }
        }
    }
}
