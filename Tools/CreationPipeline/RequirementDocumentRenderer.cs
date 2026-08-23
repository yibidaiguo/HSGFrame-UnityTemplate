using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次 doc.render 的结果：落点、是新建还是刷新、动了什么、最终全文。</summary>
    public sealed class RequirementDocumentRenderOutcome
    {
        /// <summary>
        /// 构造一次渲染结果。
        /// </summary>
        /// <param name="documentPath">文档落点绝对路径。</param>
        /// <param name="isCreated">这次是不是新建（原来没有 index.md）。</param>
        /// <param name="isChanged">最终全文与原文有没有差别。</param>
        /// <param name="addedSections">这次补上的小节标题列表。</param>
        /// <param name="documentText">渲染后的全文。</param>
        public RequirementDocumentRenderOutcome(
            string documentPath,
            bool isCreated,
            bool isChanged,
            IReadOnlyList<string> addedSections,
            string documentText)
        {
            DocumentPath = documentPath;
            IsCreated = isCreated;
            IsChanged = isChanged;
            AddedSections = addedSections;
            DocumentText = documentText;
        }

        /// <summary>文档落点绝对路径。</summary>
        public string DocumentPath { get; }

        /// <summary>这次是不是新建。</summary>
        public bool IsCreated { get; }

        /// <summary>最终全文与原文有没有差别。</summary>
        public bool IsChanged { get; }

        /// <summary>这次补上的小节标题列表。</summary>
        public IReadOnlyList<string> AddedSections { get; }

        /// <summary>渲染后的全文。</summary>
        public string DocumentText { get; }
    }

    /// <summary>
    /// `doc.render`：按需求骨架 JSON 生成或刷新 `index.md`。
    ///
    /// **刷新时只加不改**：人写下的每一个字都原样留着，工程只补三样东西——
    /// 工程所有权的 frontmatter 字段、缺掉的必填小节、生成区。
    /// 一个会重写人写的段落的渲染器，第二次就没人敢跑了；而没人敢跑的生成器等于没有。
    /// </summary>
    public static class RequirementDocumentRenderer
    {
        /// <summary>没有对应字段可填的小节，正文先摆这一行。</summary>
        private const string PlaceholderLine = "（待补）";

        /// <summary>
        /// 渲染一条需求的文档。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录，用来读工作项与规范基线。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="specification">需求文档规范。</param>
        /// <param name="isDryRun">干跑：算出全文但不写盘。</param>
        /// <exception cref="InvalidOperationException">需求骨架不存在，或已有文档解析不了时抛出。</exception>
        public static RequirementDocumentRenderOutcome Render(
            string repositoryRoot,
            string poolRoot,
            string requirementIdentifier,
            RequirementDocumentSpec specification,
            bool isDryRun)
        {
            var requirementFile = PoolPaths.RequirementFile(poolRoot, requirementIdentifier);
            if (!File.Exists(requirementFile))
            {
                throw new InvalidOperationException($"需求骨架不存在：{requirementFile}");
            }

            var documentPath = PoolPaths.RequirementDocument(poolRoot, requirementIdentifier);
            var originalText = File.Exists(documentPath) ? File.ReadAllText(documentPath) : "";
            var isCreated = originalText.Length == 0;

            using (var requirement = ParseRequirement(requirementFile))
            {
                var root = requirement.RootElement;
                var title = ReadString(root, "标题");
                var requirementType = ReadString(root, "类型");
                var status = ReadString(root, "状态");

                if (!RequirementDocument.TryParse(originalText, specification, out var parsed, out var parseReason)
                    && !isCreated)
                {
                    throw new InvalidOperationException($"已有的 index.md 解析不了：{parseReason}");
                }

                var generatedRegionLines = BuildGeneratedRegion(repositoryRoot, poolRoot, requirementIdentifier, root, specification);
                var generatedHash = RequirementDocument.HashGeneratedRegion(generatedRegionLines);

                var frontMatterLines = BuildFrontMatter(
                    originalText,
                    specification,
                    requirementIdentifier,
                    title,
                    requirementType,
                    status,
                    generatedHash);

                var bodyLines = BuildBody(
                    parsed,
                    isCreated,
                    specification,
                    root,
                    title,
                    requirementType,
                    generatedRegionLines,
                    out var addedSections);

                var builder = new StringBuilder();
                builder.Append("---\n");
                foreach (var line in frontMatterLines)
                {
                    builder.Append(line).Append('\n');
                }

                builder.Append("---\n\n");
                foreach (var line in bodyLines)
                {
                    builder.Append(line).Append('\n');
                }

                var documentText = builder.ToString();
                var isChanged = !string.Equals(Normalize(documentText), Normalize(originalText), StringComparison.Ordinal);

                if (!isDryRun && isChanged)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(documentPath));
                    File.WriteAllText(documentPath, documentText, new UTF8Encoding(false));
                }

                return new RequirementDocumentRenderOutcome(documentPath, isCreated, isChanged, addedSections, documentText);
            }
        }

        // frontmatter 逐行改写而不是整段重建：注释、项目层自己加的键、同步那一坨嵌套全得原样留着。
        // 整段重建的写法看着干净，代价是每跑一次 doc.render 就悄悄吃掉一批它不认识的键。
        private static List<string> BuildFrontMatter(
            string originalText,
            RequirementDocumentSpec specification,
            string requirementIdentifier,
            string title,
            string requirementType,
            string status,
            string generatedHash)
        {
            // 补键的顺序就是基线第一节那份样例的顺序——工具产出的东西与规范里的例子长得不一样，
            // 人只会以为是自己抄错了。
            var desiredOrder = new List<string>
            {
                "需求id", "标题", "类型", "状态", "文档版本", "权威侧", RequirementDocumentSpec.GeneratedHashKey
            };

            var engineOwned = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("需求id", requirementIdentifier),
                new KeyValuePair<string, string>("标题", title),
                new KeyValuePair<string, string>("类型", requirementType),
                new KeyValuePair<string, string>("状态", status),
                new KeyValuePair<string, string>(RequirementDocumentSpec.GeneratedHashKey, generatedHash)
            };

            // 这两样人可以改，工程只在缺的时候给个初值：文档版本从 1 起，
            // 权威侧默认「项目」——doc.render 是往仓库里渲染，仓库这一侧当然说了算。
            var defaults = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("文档版本", "1"),
                new KeyValuePair<string, string>("权威侧", DefaultAuthority(specification))
            };

            var existing = ExtractFrontMatterLines(originalText);
            var result = new List<string>();
            var written = new HashSet<string>(StringComparer.Ordinal);

            foreach (var line in existing)
            {
                var key = TopLevelKeyOf(line);
                if (key.Length == 0)
                {
                    result.Add(line);
                    continue;
                }

                var replacement = FindValue(engineOwned, key);
                if (replacement != null)
                {
                    result.Add(key + ": " + replacement);
                    written.Add(key);
                    continue;
                }

                result.Add(line);
                written.Add(key);
            }

            foreach (var key in desiredOrder)
            {
                if (written.Contains(key))
                {
                    continue;
                }

                var value = FindValue(engineOwned, key) ?? FindValue(defaults, key);
                if (value == null)
                {
                    continue;
                }

                InsertByDesiredOrder(result, desiredOrder, key, key + ": " + value);
                written.Add(key);
            }

            return result;
        }

        /// <summary>
        /// 把一个缺掉的 frontmatter 键补回它该在的位置：紧跟在「按规范排在它前面、且已经在位」的那个键后面。
        ///
        /// 一律补在最前面是不行的——那样「权威侧」会跑到「需求id」上面去，
        /// 产出与基线里那份样例长得不一样。一律补在最后也不行：frontmatter 底下是
        /// 「同步」与「媒体」两坨嵌套，补在末尾就成了那一坨的子键，解析出来直接是另一个东西。
        /// </summary>
        private static void InsertByDesiredOrder(
            List<string> lines,
            List<string> desiredOrder,
            string key,
            string newLine)
        {
            var selfIndex = desiredOrder.IndexOf(key);

            for (var index = selfIndex - 1; index >= 0; index--)
            {
                var anchor = IndexOfTopLevelKey(lines, desiredOrder[index]);
                if (anchor >= 0)
                {
                    lines.Insert(anchor + 1, newLine);
                    return;
                }
            }

            for (var index = selfIndex + 1; index < desiredOrder.Count; index++)
            {
                var anchor = IndexOfTopLevelKey(lines, desiredOrder[index]);
                if (anchor >= 0)
                {
                    lines.Insert(anchor, newLine);
                    return;
                }
            }

            lines.Insert(0, newLine);
        }

        private static int IndexOfTopLevelKey(IReadOnlyList<string> lines, string key)
        {
            for (var index = 0; index < lines.Count; index++)
            {
                if (string.Equals(TopLevelKeyOf(lines[index]), key, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private static List<string> BuildBody(
            RequirementDocument parsed,
            bool isCreated,
            RequirementDocumentSpec specification,
            JsonElement requirement,
            string title,
            string requirementType,
            IReadOnlyList<string> generatedRegionLines,
            out IReadOnlyList<string> addedSections)
        {
            var lines = isCreated || parsed == null
                ? new List<string>()
                : new List<string>(parsed.BodyLines);

            StripGeneratedRegion(lines, specification);

            // 首尾的空行都由这里统一补：frontmatter 之后固定空一行、末尾不留空行。
            // 不先剪掉原有的，每跑一次 doc.render 文档就会多长出一个空行——
            // 「跑两次结果不一样」的生成器等于没有幂等，幂等门禁也就白设了。
            TrimLeadingBlankLines(lines);
            TrimTrailingBlankLines(lines);

            if (!HasTopHeading(lines))
            {
                lines.InsertRange(0, new[] { "# " + title, "" });
            }

            var added = new List<string>();
            foreach (var section in specification.RequiredSectionsFor(requirementType))
            {
                if (IndexOfSection(lines, section) >= 0)
                {
                    continue;
                }

                InsertSection(lines, specification, requirementType, section, BuildSectionBody(requirement, section));
                added.Add(section);
            }

            TrimTrailingBlankLines(lines);
            lines.Add("");
            lines.Add(specification.GeneratedRegionBegin);
            lines.AddRange(generatedRegionLines);
            lines.Add(specification.GeneratedRegionEnd);

            addedSections = added;
            return lines;
        }

        // 生成区正文：设计记录与工作项两行，都是从别处算出来的，所以人不许手改（决策 99 那一族的老规矩）。
        private static List<string> BuildGeneratedRegion(
            string repositoryRoot,
            string poolRoot,
            string requirementIdentifier,
            JsonElement requirement,
            RequirementDocumentSpec specification)
        {
            var designRecords = ReadStringArray(requirement, "关联设计记录");
            var graph = WorkItemGraph.Load(repositoryRoot, requirementIdentifier);

            var completed = 0;
            foreach (var node in graph.Nodes)
            {
                if (string.Equals(node.State, "完成", StringComparison.Ordinal))
                {
                    completed++;
                }
            }

            string workItemText;
            if (graph.Nodes.Count == 0)
            {
                workItemText = "尚未规划";
            }
            else if (graph.Nodes.Count == 1)
            {
                workItemText = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}（{1}/1 完成）",
                    graph.Nodes[0].Identifier,
                    completed);
            }
            else
            {
                workItemText = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} … {1}（{2}/{3} 完成）",
                    graph.Nodes[0].Identifier,
                    graph.Nodes[graph.Nodes.Count - 1].Identifier,
                    completed,
                    graph.Nodes.Count);
            }

            var lines = new List<string>
            {
                "## " + specification.GeneratedSection,
                "- 设计记录：" + (designRecords.Count == 0 ? "无" : string.Join("、", designRecords)),
                "- 工作项：" + workItemText
            };

            AppendInterfaceSpecs(lines, repositoryRoot, poolRoot, requirementIdentifier);
            return lines;
        }

        // 界面规格在需求案里只留**一行指针**，不铺元素行为表。
        //
        // 表在**模块策划案**里（一个模块一份，常驻）。需求案做完就归档，
        // 把整屏契约铺在它里面的结果是：同一个面板被 N 条需求各存一份快照，
        // 谁是正本说不清——而人真要查「这一屏现在长什么样」时，
        // 翻到的多半是某条半年前已归档需求里的旧版本。
        private static void AppendInterfaceSpecs(
            List<string> lines, string repositoryRoot, string poolRoot, string requirementIdentifier)
        {
            var specs = InterfaceSpec.FindByRequirement(repositoryRoot, requirementIdentifier, out var skipped);

            foreach (var reason in skipped)
            {
                lines.Add("- 界面规格读不动：" + reason);
            }

            if (specs.Count == 0)
            {
                lines.Add("- 界面规格：尚未出功能图");
                return;
            }

            foreach (var spec in specs)
            {
                lines.Add($"- 界面规格：{spec.Identifier}「{spec.Title}」"
                    + $"（元素 {spec.Elements.Count} 个）→ 详见模块策划案 {spec.ModuleName}");
            }
        }

        // 小节正文的来源就是需求骨架里的同名字段：字符串照抄，数组渲成有序列表
        // （验收标准必须是有序列表，基线第二节第 2 条），没有同名字段就摆一行占位。
        private static List<string> BuildSectionBody(JsonElement requirement, string sectionTitle)
        {
            if (requirement.ValueKind == JsonValueKind.Object
                && requirement.TryGetProperty(sectionTitle, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return new List<string>(text.Replace("\r\n", "\n").Split('\n'));
                    }
                }

                if (value.ValueKind == JsonValueKind.Array)
                {
                    var items = new List<string>();
                    var ordinal = 1;
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        items.Add(ordinal.ToString(CultureInfo.InvariantCulture) + ". " + item.GetString());
                        ordinal++;
                    }

                    if (items.Count > 0)
                    {
                        return items;
                    }
                }
            }

            return new List<string> { PlaceholderLine };
        }

        // 插在「按基线顺序排在它后面、且已经在位」的那一节之前；后面全没有就插到末尾。
        private static void InsertSection(
            List<string> lines,
            RequirementDocumentSpec specification,
            string requirementType,
            string sectionTitle,
            IReadOnlyList<string> body)
        {
            var order = new List<string>(specification.RequiredSectionsFor(requirementType));
            foreach (var optional in specification.OptionalSections)
            {
                if (!order.Contains(optional))
                {
                    order.Add(optional);
                }
            }

            var position = lines.Count;
            var selfIndex = order.IndexOf(sectionTitle);
            if (selfIndex >= 0)
            {
                for (var index = selfIndex + 1; index < order.Count; index++)
                {
                    var found = IndexOfSection(lines, order[index]);
                    if (found >= 0)
                    {
                        position = found;
                        break;
                    }
                }
            }

            var block = new List<string> { "## " + sectionTitle };
            block.AddRange(body);
            block.Add("");

            if (position >= lines.Count)
            {
                TrimTrailingBlankLines(lines);
                if (lines.Count > 0)
                {
                    lines.Add("");
                }

                lines.AddRange(block);
                return;
            }

            lines.InsertRange(position, block);
        }

        private static void StripGeneratedRegion(List<string> lines, RequirementDocumentSpec specification)
        {
            GeneratedRegion.Strip(lines, specification.GeneratedRegionBegin, specification.GeneratedRegionEnd);
        }

        private static int IndexOfSection(IReadOnlyList<string> lines, string sectionTitle)
        {
            for (var index = 0; index < lines.Count; index++)
            {
                if (lines[index].StartsWith("## ", StringComparison.Ordinal)
                    && string.Equals(lines[index].Substring(3).Trim(), sectionTitle, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool HasTopHeading(IReadOnlyList<string> lines)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TrimLeadingBlankLines(List<string> lines)
        {
            while (lines.Count > 0 && lines[0].Trim().Length == 0)
            {
                lines.RemoveAt(0);
            }
        }

        private static void TrimTrailingBlankLines(List<string> lines)
        {
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }
        }

        private static IReadOnlyList<string> ExtractFrontMatterLines(string originalText)
        {
            var result = new List<string>();
            if (originalText.Length == 0)
            {
                return result;
            }

            var lines = originalText.Replace("\r\n", "\n").TrimStart('﻿').Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != "---")
            {
                return result;
            }

            for (var index = 1; index < lines.Length; index++)
            {
                if (lines[index].Trim() == "---")
                {
                    break;
                }

                result.Add(lines[index]);
            }

            return result;
        }

        private static string TopLevelKeyOf(string line)
        {
            if (line.Length == 0 || line[0] == ' ' || line[0] == '\t' || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                return "";
            }

            var separator = line.IndexOf(':');
            return separator <= 0 ? "" : line.Substring(0, separator).Trim();
        }

        private static string FindValue(IReadOnlyList<KeyValuePair<string, string>> pairs, string key)
        {
            foreach (var pair in pairs)
            {
                if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                {
                    return pair.Value;
                }
            }

            return null;
        }

        private static string DefaultAuthority(RequirementDocumentSpec specification)
        {
            foreach (var value in specification.AuthorityValues)
            {
                if (string.Equals(value, "项目", StringComparison.Ordinal))
                {
                    return value;
                }
            }

            return specification.AuthorityValues.Count > 0 ? specification.AuthorityValues[0] : "项目";
        }

        private static JsonDocument ParseRequirement(string requirementFile)
        {
            try
            {
                return JsonDocument.Parse(File.ReadAllText(requirementFile));
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"需求骨架 JSON 语法不合法：{requirementFile}：{exception.Message}", exception);
            }
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return "";
        }

        private static List<string> ReadStringArray(JsonElement root, string propertyName)
        {
            var result = new List<string>();
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(propertyName, out var value)
                || value.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    result.Add(item.GetString());
                }
            }

            return result;
        }

        private static string Normalize(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").TrimStart('﻿');
        }
    }
}
