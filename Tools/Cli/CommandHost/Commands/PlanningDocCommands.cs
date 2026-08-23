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
    /// <summary>渲染模块策划案的参数。</summary>
    public sealed class PlanRenderArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        [DefaultValue("")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录。</summary>
        [Summary("池子根目录；留空取 <仓库根>/Pools")]
        [DefaultValue("")]
        public string PoolRoot { get; set; }

        /// <summary>渲染哪个模块；留空表示全渲。</summary>
        [Summary("模块名，与 Scripts/Modules 下的目录同名；留空表示把已有的策划案全渲一遍")]
        [DefaultValue("")]
        public string Module { get; set; }

        /// <summary>干跑：算出全文但不写盘。</summary>
        [Summary("干跑：算出全文但不写盘")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }
    }

    /// <summary>模块策划案门禁的参数。</summary>
    public sealed class PlanGateArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        [DefaultValue("")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录。</summary>
        [Summary("池子根目录；留空取 <仓库根>/Pools")]
        [DefaultValue("")]
        public string PoolRoot { get; set; }
    }

    /// <summary>plan.draft 的参数。</summary>
    public sealed class PlanDraftArguments
    {
        /// <summary>仓库根目录。</summary>
        [Summary("仓库根目录")]
        [DefaultValue("")]
        public string RepositoryRoot { get; set; }

        /// <summary>池子根目录。</summary>
        [Summary("池子根目录；留空取 <仓库根>/Pools")]
        [DefaultValue("")]
        public string PoolRoot { get; set; }

        /// <summary>给哪个模块产草案。</summary>
        [Summary("模块名，与 Scripts/Modules 下的目录同名")]
        public string Module { get; set; }

        /// <summary>执行后端调用超时秒数。</summary>
        [Summary("执行后端调用超时秒数")]
        [DefaultValue(300)]
        public int TimeoutSeconds { get; set; }

        /// <summary>钉死这一次用哪个模型；留空走本机配置。</summary>
        [Summary("钉死这一次用哪个模型；留空走本机配置")]
        [DefaultValue("")]
        public string Model { get; set; }

        /// <summary>干跑：只把要发的提示词打出来，不调执行后端。</summary>
        [Summary("干跑：只把要发的提示词打出来，不调执行后端（不花钱）")]
        [DefaultValue(false)]
        public bool DryRun { get; set; }
    }

    /// <summary>
    /// 模块策划案命令族：渲染与校验。
    ///
    /// 与需求文档那一族（`doc.render` / `gate.reqdoc`）**形状一样但对象不同**：
    /// 那边一条需求一份，做完归档；这边一个模块一份，常驻，随需求验收更新。
    /// </summary>
    public static class PlanningDocCommands
    {
        /// <summary>
        /// 渲染模块策划案的生成区：需求 / 界面与交互 / 配置表结构 / 参考图 / 代码公开面。
        /// 人写区一个字都不碰。
        /// </summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("plan.render")]
        [Summary("渲染模块策划案的生成区（人写区不碰）；留空模块名表示全渲一遍")]
        public static CommandResult Render(PlanRenderArguments arguments)
        {
            if (arguments == null)
            {
                return CommandResult.Failure("参数为空");
            }

            var repositoryRoot = ResolveRepositoryRoot(arguments.RepositoryRoot);
            var poolRoot = ResolvePoolRoot(repositoryRoot, arguments.PoolRoot);

            PlanningDocumentSpec specification;
            try
            {
                specification = PlanningDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                return CommandResult.Failure(exception.Message);
            }

            var modules = ResolveModules(repositoryRoot, poolRoot, arguments.Module);
            if (modules.Count == 0)
            {
                return CommandResult.Failure(
                    "没有可渲的模块：给一个 Module，或者先在 Pools/Designs/Modules/ 下建一份策划案");
            }

            var lines = new List<string>();
            var changed = 0;
            foreach (var module in modules)
            {
                PlanningDocumentRenderOutcome outcome;
                try
                {
                    outcome = PlanningDocumentRenderer.Render(
                        repositoryRoot, poolRoot, module, specification, arguments.DryRun);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    lines.Add($"{module}：写不动——{exception.Message}");
                    continue;
                }

                lines.Add($"{module}：{(outcome.IsCreated ? "新建" : outcome.IsChanged ? "刷新" : "无变化")}"
                    + $"　{Relative(repositoryRoot, outcome.DocumentPath)}");
                foreach (var note in outcome.Notes)
                {
                    lines.Add("  " + note);
                }

                if (outcome.IsChanged)
                {
                    changed++;
                }
            }

            return CommandResult.Success(
                $"模块策划案渲了 {modules.Count} 份，动了 {changed} 份"
                    + (arguments.DryRun ? "（干跑，没写盘）" : ""),
                lines);
        }

        /// <summary>
        /// 模块策划案门禁：必备键、必填小节、生成区没被手改、配置表声明可解析。
        /// </summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("gate.plandoc")]
        [Summary("模块策划案门禁：必备键、必填小节、生成区未被手改、配置表声明可解析")]
        public static CommandResult Gate(PlanGateArguments arguments)
        {
            if (arguments == null)
            {
                return CommandResult.Failure("参数为空");
            }

            var repositoryRoot = ResolveRepositoryRoot(arguments.RepositoryRoot);
            var poolRoot = ResolvePoolRoot(repositoryRoot, arguments.PoolRoot);

            PlanningDocumentSpec specification;
            try
            {
                specification = PlanningDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                return CommandResult.Failure(exception.Message);
            }

            var findings = PlanningDocumentChecker.Check(repositoryRoot, poolRoot, specification);
            var lines = new List<string>();
            foreach (var finding in findings)
            {
                lines.Add(finding.ToDisplayText());
            }

            var count = ModulePlanDirectories(poolRoot).Count;
            return findings.Count == 0
                ? CommandResult.Success($"模块策划案门禁（策划案 {count} 份）通过，问题 0 条", lines)
                : CommandResult.Failure($"模块策划案门禁不通过，问题 {findings.Count} 条", lines);
        }

        /// <summary>
        /// 冷启动：照代码摘要、模块自述与已有需求，产一份模块策划案的**人写区**草案。
        ///
        /// 只产人写区（目标用途 / 玩法 / 边界与不做）。现状那一段是投影，
        /// 产完顺手渲一遍就有了，不问模型。
        ///
        /// **「往后要做成什么样」一律留占位符**——那是人的判断，代码里没有依据。
        /// 让模型编一段未来规划，它会被往后所有东西当成事实继承，
        /// 而那个方向没有任何人同意过。
        /// </summary>
        /// <param name="arguments">命令参数。</param>
        [EditorCommand("plan.draft")]
        [Summary("冷启动：照代码与已有需求产模块策划案的人写区草案（往后要做成什么样留给人写）")]
        public static CommandResult Draft(PlanDraftArguments arguments)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.Module))
            {
                return CommandResult.Failure("参数 Module 为必填项");
            }

            var repositoryRoot = ResolveRepositoryRoot(arguments.RepositoryRoot);
            var poolRoot = ResolvePoolRoot(repositoryRoot, arguments.PoolRoot);
            var moduleName = arguments.Module.Trim();

            PlanningDocumentSpec specification;
            try
            {
                specification = PlanningDocumentSpec.Load(repositoryRoot);
            }
            catch (Exception exception)
                when (exception is FileNotFoundException || exception is InvalidOperationException)
            {
                return CommandResult.Failure(exception.Message);
            }

            var documentPath = PoolPaths.ModulePlanDocument(poolRoot, moduleName);
            if (File.Exists(documentPath))
            {
                // **不覆盖已有的那份。** 人写区里可能已经有人写了半天的东西，
                // 而这条命令的定位是冷启动，不是重写。
                return CommandResult.Failure(
                    moduleName + " 已经有策划案了（" + Relative(repositoryRoot, documentPath) + "）。"
                        + "这条命令只管冷启动，不覆盖已有的；要重写就先把那份挪走");
            }

            var readmeFile = PlanningDocumentDraftPrompt.ReadmeFile(repositoryRoot, moduleName);
            var readmeText = File.Exists(readmeFile) ? File.ReadAllText(readmeFile) : "";

            var codeSurface = new List<string>();
            foreach (var module in ModuleInterfaceDigest.Collect(repositoryRoot))
            {
                if (string.Equals(module.ModuleName, moduleName, StringComparison.Ordinal))
                {
                    codeSurface.AddRange(module.Types);
                    break;
                }
            }

            var requirements = CollectRequirementSummaries(poolRoot, moduleName);
            var prompt = PlanningDocumentDraftPrompt.Build(moduleName, readmeText, codeSurface, requirements);

            var lines = new List<string>
            {
                "模块自述：" + (readmeText.Length > 0 ? "有" : "没有"),
                "代码公开面：" + codeSurface.Count + " 个类型",
                "已有需求：" + requirements.Count + " 条"
            };

            if (codeSurface.Count == 0 && requirements.Count == 0 && readmeText.Length == 0)
            {
                return CommandResult.Failure(
                    moduleName + " 手上什么材料都没有（没自述、没抽到公开类型、名下也没有需求）。"
                        + "**这时候产草案就是纯编**——先确认模块名对不对，或者干脆人来写第一版",
                    lines);
            }

            if (arguments.DryRun)
            {
                lines.Add("干跑：没有调执行后端，下面是要发的提示词");
                lines.Add(prompt);
                return CommandResult.Success("干跑完成，未发任何请求", lines);
            }

            var routeTable = BridgeRouteTable.Load(repositoryRoot);
            if (!routeTable.TryResolvePort("执行后端", out var backendDriver, out var driverReason))
            {
                return CommandResult.Failure("执行后端取不到：" + driverReason, lines);
            }

            var payload = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["提示"] = prompt,
                ["上下文"] = PlanningDocumentDraftPrompt.SystemContextText
            });

            var call = BridgeInvoker.Invoke(
                repositoryRoot, backendDriver, "complete", payload, arguments.TimeoutSeconds, arguments.Model ?? "");
            if (!call.Succeeded)
            {
                return CommandResult.Failure(
                    "执行后端调用失败（" + call.ErrorCode + "）：" + call.HumanText, lines);
            }

            var modelText = call.Payload.ValueKind == JsonValueKind.Object
                && call.Payload.TryGetProperty("文本", out var textElement)
                && textElement.ValueKind == JsonValueKind.String
                ? textElement.GetString() ?? ""
                : "";

            if (!PlanningDocumentDraftPrompt.TryParse(modelText, out var sections, out var parseReason))
            {
                return CommandResult.Failure("读不懂执行后端的回答：" + parseReason, lines);
            }

            try
            {
                PlanningDocumentDraftWriter.Write(poolRoot, moduleName, sections, specification);
            }
            catch (Exception exception)
                when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return CommandResult.Failure("草案写不下去：" + exception.Message, lines);
            }

            lines.Add("草案已落盘：" + Relative(repositoryRoot, documentPath));

            // 顺手渲一遍生成区：人写区刚落，现状那几节还空着。
            var rendered = PlanningDocumentRenderer.Render(
                repositoryRoot, poolRoot, moduleName, specification, isDryRun: false);
            foreach (var note in rendered.Notes)
            {
                lines.Add("  " + note);
            }

            return CommandResult.Success(
                moduleName + " 的策划案草案产出了——**「往后要做成什么样」留给你写**，写完再跑 gate.plandoc",
                lines);
        }

        /// <summary>挂在这个模块名下的需求，一行一条，按 id 排序。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="moduleName">模块名。</param>
        private static IReadOnlyList<string> CollectRequirementSummaries(string poolRoot, string moduleName)
        {
            var rows = new List<string>();
            var directory = PoolPaths.RequirementsDirectory(poolRoot);
            if (!Directory.Exists(directory))
            {
                return rows;
            }

            var identifiers = new List<string>(Directory.GetDirectories(directory));
            identifiers.Sort(StringComparer.Ordinal);

            foreach (var requirementDirectory in identifiers)
            {
                var identifier = Path.GetFileName(requirementDirectory);
                var file = PoolPaths.RequirementFile(poolRoot, identifier);
                if (!File.Exists(file))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("专项", out var epic)
                        || epic.ValueKind != JsonValueKind.String
                        || !string.Equals(epic.GetString(), moduleName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var title = root.TryGetProperty("标题", out var titleElement)
                        && titleElement.ValueKind == JsonValueKind.String ? titleElement.GetString() : "";
                    var description = root.TryGetProperty("描述", out var descriptionElement)
                        && descriptionElement.ValueKind == JsonValueKind.String
                            ? descriptionElement.GetString() : "";
                    rows.Add(identifier + " " + title + "：" + description);
                }
                catch (Exception exception) when (exception is IOException || exception is JsonException)
                {
                    // 一条读不动不该让整趟产不出草案；这条跳过就是了。
                }
            }

            return rows;
        }

        /// <summary>要渲哪些模块：给了名字就只渲那一个，没给就渲已经建过策划案的全部。</summary>
        private static IReadOnlyList<string> ResolveModules(string repositoryRoot, string poolRoot, string module)
        {
            if (!string.IsNullOrWhiteSpace(module))
            {
                return new[] { module.Trim() };
            }

            return ModulePlanDirectories(poolRoot);
        }

        /// <summary>池子里已经有策划案的模块名，按名字排序。</summary>
        private static IReadOnlyList<string> ModulePlanDirectories(string poolRoot)
        {
            var root = PoolPaths.ModulePlanRoot(poolRoot);
            if (!Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            foreach (var directory in Directory.GetDirectories(root))
            {
                names.Add(Path.GetFileName(directory));
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static string ResolveRepositoryRoot(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? Directory.GetCurrentDirectory() : value;
        }

        private static string ResolvePoolRoot(string repositoryRoot, string value)
        {
            return string.IsNullOrWhiteSpace(value) ? Path.Combine(repositoryRoot, "Pools") : value;
        }

        private static string Relative(string repositoryRoot, string path)
        {
            return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
        }
    }
}
