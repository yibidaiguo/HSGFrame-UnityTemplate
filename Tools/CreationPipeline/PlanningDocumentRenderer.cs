using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次 plan.render 的结果。</summary>
    /// <param name="DocumentPath">文档落点绝对路径。</param>
    /// <param name="IsCreated">这次是不是新建（原来没有 index.md）。</param>
    /// <param name="IsChanged">最终全文与原文有没有差别。</param>
    /// <param name="DocumentText">渲染后的全文。</param>
    /// <param name="Notes">这一趟各节取到了什么、跳过了什么，一句一条。</param>
    public sealed record PlanningDocumentRenderOutcome(
        string DocumentPath,
        bool IsCreated,
        bool IsChanged,
        string DocumentText,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// 渲染模块策划案的**生成区**：需求 / 界面与交互 / 配置表结构 / 参考图 / 代码公开面。
    ///
    /// **人写区一个字都不碰。** 目标用途、玩法、边界与不做、往后要做成什么样是人的判断，
    /// 机器碰了这份文档就活不长——人一旦发现自己写的东西会被重渲染吃掉，
    /// 就再也不会往里写东西，剩下一份只有机器投影的空壳。
    ///
    /// 生成区是**投影不是正本**：需求正本在池子、界面正本在界面规格、
    /// 配置表正本在 Config/Schema、代码正本就是代码。所以它可以整段重算，
    /// 也必须整段重算——一条需求验收之后不重算，这份正本就开始骗人。
    /// </summary>
    public static class PlanningDocumentRenderer
    {
        /// <summary>缺料时写这个，而不是省略这一节。</summary>
        private const string EmptyLine = "暂无";

        /// <summary>
        /// 渲染一个模块的策划案。文档不存在时先按规范摆一副人写区骨架（各节留占位）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="moduleName">模块名。</param>
        /// <param name="specification">模块策划案规范。</param>
        /// <param name="isDryRun">干跑：算出全文但不写盘。</param>
        public static PlanningDocumentRenderOutcome Render(
            string repositoryRoot,
            string poolRoot,
            string moduleName,
            PlanningDocumentSpec specification,
            bool isDryRun)
        {
            var documentPath = PoolPaths.ModulePlanDocument(poolRoot, moduleName);
            var originalText = File.Exists(documentPath) ? File.ReadAllText(documentPath) : "";
            var isCreated = originalText.Length == 0;

            var lines = new List<string>(
                (isCreated ? BuildSkeleton(moduleName, specification) : originalText)
                    .Replace("\r\n", "\n").TrimStart('﻿').Split('\n'));

            var configTables = ReadFrontMatterList(lines, "配置表");
            var notes = new List<string>();
            var region = BuildGeneratedRegion(
                repositoryRoot, poolRoot, moduleName, specification, configTables, notes);

            GeneratedRegion.Strip(lines, specification.GeneratedRegionBegin, specification.GeneratedRegionEnd);
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            lines.Add("");
            lines.Add(specification.GeneratedRegionBegin);
            lines.AddRange(region);
            lines.Add(specification.GeneratedRegionEnd);

            SetFrontMatterScalar(lines, PlanningDocumentSpec.GeneratedHashKey, GeneratedRegion.Hash(region));

            var text = string.Join("\n", lines) + "\n";
            var isChanged = text != originalText.Replace("\r\n", "\n");

            if (!isDryRun && isChanged)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(documentPath));
                File.WriteAllText(documentPath, text, new UTF8Encoding(false));
            }

            return new PlanningDocumentRenderOutcome(documentPath, isCreated, isChanged, text, notes);
        }

        // 新建时的骨架：frontmatter + 标题 + 人写区各节留一行占位。
        // **占位写「（待补）」而不是留空**：空小节读起来像文档坏了，占位说的是「轮到你写」。
        private static string BuildSkeleton(string moduleName, PlanningDocumentSpec specification)
        {
            var builder = new StringBuilder();
            builder.Append("---\n");
            builder.Append("模块: ").Append(moduleName).Append('\n');
            builder.Append("标题: ").Append(moduleName).Append('\n');
            builder.Append("状态: ").Append(specification.StatusValues.Count > 0 ? specification.StatusValues[0] : "生效").Append('\n');
            builder.Append("文档版本: 1\n");
            builder.Append("权威侧: 项目\n");
            builder.Append("配置表: []\n");
            builder.Append("---\n\n");
            builder.Append("# ").Append(moduleName).Append("\n\n");

            foreach (var section in specification.RequiredSections)
            {
                builder.Append("## ").Append(section).Append("\n（待补）\n\n");
            }

            return builder.ToString();
        }

        private static List<string> BuildGeneratedRegion(
            string repositoryRoot,
            string poolRoot,
            string moduleName,
            PlanningDocumentSpec specification,
            IReadOnlyList<string> configTables,
            List<string> notes)
        {
            var lines = new List<string> { "## " + specification.GeneratedSection };

            AppendRequirements(lines, poolRoot, moduleName, notes);
            AppendInterfaces(lines, repositoryRoot, poolRoot, moduleName, notes);
            AppendConfigTables(lines, repositoryRoot, configTables, notes);
            AppendReferenceImages(lines, repositoryRoot, moduleName, notes);
            AppendCodeSurface(lines, repositoryRoot, moduleName, notes);

            return lines;
        }

        // 需求：池子里 `专项` 是这个模块的那些，按 id 排序（生成区要幂等）。
        private static void AppendRequirements(
            List<string> lines, string poolRoot, string moduleName, List<string> notes)
        {
            lines.Add("");
            lines.Add("### 需求");

            var directory = PoolPaths.RequirementsDirectory(poolRoot);
            var rows = new List<string>();
            if (Directory.Exists(directory))
            {
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
                        if (!string.Equals(ReadString(root, "专项"), moduleName, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        rows.Add($"- {identifier} {ReadString(root, "标题")}（{ReadString(root, "状态")}）");
                    }
                    catch (Exception exception) when (exception is IOException || exception is JsonException)
                    {
                        notes.Add($"需求 {identifier} 读不动，这一节里跳过了：{exception.Message}");
                    }
                }
            }

            lines.Add(rows.Count == 0 ? EmptyLine : "");
            lines.AddRange(rows);
            notes.Add($"需求：{rows.Count} 条");
        }

        // 界面：`面板` 与模块同名的那些界面规格。面板名本来就决定资产目录与 uidef 名，
        // 拿它当模块归属不是巧合——那是同一个名字的同一个用途。
        private static void AppendInterfaces(
            List<string> lines, string repositoryRoot, string poolRoot, string moduleName, List<string> notes)
        {
            lines.Add("");
            lines.Add("### 界面与交互");

            var found = new List<InterfaceSpec>();
            var directory = InterfaceSpec.Directory(repositoryRoot);
            if (Directory.Exists(directory))
            {
                var files = Directory.GetFiles(directory, "*.json");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (var file in files)
                {
                    if (!InterfaceSpec.TryRead(file, out var spec, out var reason))
                    {
                        notes.Add($"界面规格 {Path.GetFileName(file)} 读不动：{reason}");
                        continue;
                    }

                    if (string.Equals(spec.PanelName, moduleName, StringComparison.Ordinal))
                    {
                        found.Add(spec);
                    }
                }
            }

            if (found.Count == 0)
            {
                lines.Add(EmptyLine);
                notes.Add("界面：这个模块还没出过功能图");
                return;
            }

            foreach (var spec in found)
            {
                lines.Add("");
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "**{0}「{1}」** · 画布 {2}×{3} · 元素 {4} 个",
                    spec.Identifier, spec.Title, spec.CanvasWidth, spec.CanvasHeight, spec.Elements.Count));

                // 布局图只在真拷进 media/ 之后才引：引一个不存在的路径，
                // 在飞书上是一个碎图标，比不放更难查。
                var relative = InterfaceLayoutMedia.RelativePathFor(spec.Identifier);
                var absolute = Path.Combine(
                    PoolPaths.ModulePlanDirectory(poolRoot, moduleName),
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absolute))
                {
                    lines.Add("");
                    lines.Add("![" + spec.Identifier + " 白块布局图](" + relative + ")");
                }

                AppendElementTable(lines, spec);
            }

            notes.Add($"界面：{found.Count} 屏");
        }

        // 元素行为表：程序最常回头问策划的就是这几列。
        private static void AppendElementTable(List<string> lines, InterfaceSpec spec)
        {
            if (spec.Elements.Count == 0)
            {
                lines.Add("");
                lines.Add("（这份规格里一个元素都没有。）");
                return;
            }

            lines.Add("");
            lines.Add("| 元素 | 类型 | 交互 | 成功 | 失败 | 边界 |");
            lines.Add("|---|---|---|---|---|---|");

            foreach (var element in spec.Elements)
            {
                var cells = new[]
                {
                    Cell(element.DisplayName.Length == 0 ? element.Identifier : element.DisplayName),
                    Cell(element.ElementType),
                    Cell(element.ReadString("交互")),
                    Cell(element.ReadString("成功")),
                    Cell(element.ReadString("失败")),
                    Cell(element.ReadString("边界"))
                };

                lines.Add("| " + string.Join(" | ", cells) + " |");
            }
        }

        // 配置表结构：照 Config/Schema 渲，**不许人手抄**——手抄的那份一定会与表漂。
        private static void AppendConfigTables(
            List<string> lines, string repositoryRoot, IReadOnlyList<string> configTables, List<string> notes)
        {
            lines.Add("");
            lines.Add("### 配置表结构");

            if (configTables.Count == 0)
            {
                lines.Add(EmptyLine + "（frontmatter 的「配置表」是空的；这个模块用哪几张表要人来声明）");
                notes.Add("配置表：frontmatter 里没声明");
                return;
            }

            var rendered = 0;
            foreach (var table in configTables)
            {
                var structure = ConfigTableSchemaReader.Read(repositoryRoot, table, out var reason);
                if (structure == null)
                {
                    lines.Add("");
                    lines.Add($"- `{table}`：{reason}");
                    notes.Add($"配置表 {table}：{reason}");
                    continue;
                }

                lines.Add("");
                lines.Add($"**{structure.TableName}**（`{structure.IdentifierName}` · 页签 {structure.SheetName}）");
                lines.Add("");
                lines.Add("| 参数名 | 标识名 | 类型 | 主键 |");
                lines.Add("|---|---|---|---|");
                foreach (var field in structure.Fields)
                {
                    lines.Add($"| {Cell(field.DisplayName)} | {Cell(field.IdentifierName)} | "
                        + $"{Cell(field.TypeName)} | {(field.IsPrimaryKey ? "是" : "—")} |");
                }

                rendered++;
            }

            notes.Add($"配置表：渲了 {rendered}/{configTables.Count} 张");
        }

        // 参考图：设计库里挂在这个模块名下的效果图，加上生效的定稿与色板。
        private static void AppendReferenceImages(
            List<string> lines, string repositoryRoot, string moduleName, List<string> notes)
        {
            lines.Add("");
            lines.Add("### 参考图");

            var anchor = StyleAnchorResolver.Resolve(repositoryRoot, moduleName, "", referenceImageCount: 0);
            var index = DesignLibraryIndex.Read(repositoryRoot);

            var rows = new List<string>();
            foreach (var entry in index.FindSimilar(moduleName, "", int.MaxValue))
            {
                rows.Add($"- `{entry.Destination}`（{entry.Origin}）");
            }

            if (rows.Count == 0 && anchor.StyleFinalName.Length == 0)
            {
                lines.Add(EmptyLine);
                notes.Add("参考图：库里这个模块下什么都没有");
                return;
            }

            lines.Add("");
            lines.AddRange(rows);

            if (anchor.StyleFinalName.Length > 0)
            {
                lines.Add($"- 定稿：{anchor.StyleFinalName} · 色板 {anchor.Palette.Count} 色"
                    + (anchor.NegativeList.Count > 0 ? $" · 负面 {anchor.NegativeList.Count} 条" : ""));
            }

            notes.Add($"参考图：{rows.Count} 张"
                + (anchor.StyleFinalName.Length > 0 ? $"，定稿 {anchor.StyleFinalName}" : "，无定稿"));
        }

        // 代码公开面：模块接口摘要里这个模块那一段。
        private static void AppendCodeSurface(
            List<string> lines, string repositoryRoot, string moduleName, List<string> notes)
        {
            lines.Add("");
            lines.Add("### 代码公开面");

            ModuleInterface found = null;
            foreach (var module in ModuleInterfaceDigest.Collect(repositoryRoot))
            {
                if (string.Equals(module.ModuleName, moduleName, StringComparison.Ordinal))
                {
                    found = module;
                    break;
                }
            }

            if (found == null || found.Types.Count == 0)
            {
                lines.Add(EmptyLine + "（`Scripts/Modules/" + moduleName + "` 下没抽出公开类型）");
                notes.Add("代码公开面：没抽到");
                return;
            }

            lines.Add("");
            foreach (var type in found.Types)
            {
                lines.Add("- " + type);
            }

            notes.Add($"代码公开面：{found.Types.Count} 个公开类型");
        }

        // 表格单元格：竖线会把这一行拆成两列，换行会把表格整个断开——都得就地拍平。
        private static string Cell(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "—";
            }

            return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace("|", "\\|").Trim();
        }

        // frontmatter 里读一个列表键：`配置表: [Bag, Monster]` 或多行短横线两种写法都认。
        private static IReadOnlyList<string> ReadFrontMatterList(IReadOnlyList<string> lines, string key)
        {
            var values = new List<string>();
            if (lines.Count == 0 || lines[0].Trim() != "---")
            {
                return values;
            }

            for (var index = 1; index < lines.Count; index++)
            {
                var line = lines[index];
                if (line.Trim() == "---")
                {
                    break;
                }

                if (!line.StartsWith(key + ":", StringComparison.Ordinal))
                {
                    continue;
                }

                var inline = line.Substring(key.Length + 1).Trim();
                if (inline.StartsWith("[", StringComparison.Ordinal) && inline.EndsWith("]", StringComparison.Ordinal))
                {
                    foreach (var item in inline.Substring(1, inline.Length - 2).Split(','))
                    {
                        var value = item.Trim().Trim('"', '\'');
                        if (value.Length > 0)
                        {
                            values.Add(value);
                        }
                    }

                    return values;
                }

                // 多行写法：往下读缩进的「- 值」，遇到不缩进的行就停。
                for (var next = index + 1; next < lines.Count; next++)
                {
                    var item = lines[next];
                    if (item.Trim() == "---")
                    {
                        break;
                    }

                    if (!item.StartsWith(" ", StringComparison.Ordinal) && !item.StartsWith("-", StringComparison.Ordinal))
                    {
                        break;
                    }

                    var trimmed = item.Trim();
                    if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                    {
                        break;
                    }

                    var value = trimmed.Substring(2).Trim().Trim('"', '\'');
                    if (value.Length > 0)
                    {
                        values.Add(value);
                    }
                }

                return values;
            }

            return values;
        }

        // frontmatter 里写一个标量键；没有就补在结尾的 --- 之前。
        private static void SetFrontMatterScalar(List<string> lines, string key, string value)
        {
            if (lines.Count == 0 || lines[0].Trim() != "---")
            {
                return;
            }

            for (var index = 1; index < lines.Count; index++)
            {
                if (lines[index].Trim() == "---")
                {
                    lines.Insert(index, key + ": " + value);
                    return;
                }

                if (lines[index].StartsWith(key + ":", StringComparison.Ordinal))
                {
                    lines[index] = key + ": " + value;
                    return;
                }
            }
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(propertyName, out var element)
                && element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? ""
                : "";
        }
    }
}
