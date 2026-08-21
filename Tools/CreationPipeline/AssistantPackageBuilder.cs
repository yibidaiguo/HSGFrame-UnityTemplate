using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 生成assistant-package：系统提示、设计池摘要、冲突清单、术语表、正反例、模块清单与导入说明七个 markdown 文件。
    /// 知识素材（术语表 / 正反例 / 模块清单）是可选的——拿不到时降级成占位文案，不让供给整条失败。
    /// </summary>
    public static class AssistantPackageBuilder
    {
        /// <summary>
        /// 生成assistant-package的七个 markdown 文件，全部写 UTF-8、中文正文；
        /// 返回写出的文件绝对路径列表，顺序即写盘顺序。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录，模块清单降级扫描从这里展开。</param>
        /// <param name="poolRoot">池子根目录，设计池汇总与知识素材从这里读。</param>
        /// <param name="schema">合并后的池子 schema，系统提示的摘要表数据源。</param>
        /// <param name="driverName">面向的 driver 名称，决定产物目录名。</param>
        /// <param name="conflictList">冲突列表；null 视为读不成，冲突清单写占位并声明不作数。</param>
        public static IReadOnlyList<string> Build(
            string repositoryRoot,
            string poolRoot,
            PoolSchema schema,
            string driverName,
            ConflictList conflictList)
        {
            var knowledgeDirectory = ProvisionPaths.AssistantKnowledgeDirectory(repositoryRoot, driverName);
            Directory.CreateDirectory(knowledgeDirectory);

            var systemPromptFile = Path.Combine(ProvisionPaths.AssistantPackageDirectory(repositoryRoot, driverName), "system-prompt.md");
            var designSummaryFile = Path.Combine(knowledgeDirectory, "design-digest.md");
            var conflictListFile = Path.Combine(knowledgeDirectory, "conflicts.md");
            var glossaryFile = Path.Combine(knowledgeDirectory, "glossary.md");
            var examplesFile = Path.Combine(knowledgeDirectory, "examples.md");
            var moduleListFile = Path.Combine(knowledgeDirectory, "modules.md");
            var importGuideFile = Path.Combine(ProvisionPaths.AssistantPackageDirectory(repositoryRoot, driverName), "import-guide.md");

            WriteAll(systemPromptFile, BuildSystemPrompt(schema));
            WriteAll(designSummaryFile, BuildDesignSummary(poolRoot, conflictList));
            WriteAll(conflictListFile, BuildConflictList(conflictList));
            WriteAll(glossaryFile, BuildGlossary(poolRoot));
            WriteAll(examplesFile, BuildExamples(poolRoot));
            WriteAll(moduleListFile, BuildModuleList(repositoryRoot, poolRoot));
            WriteAll(importGuideFile, BuildImportGuide());

            return PackageFiles(repositoryRoot, driverName);
        }

        /// <summary>
        /// 只列assistant-package七个文件的路径，不碰磁盘；供干跑列出将要生成的文件用。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">面向的 driver 名称。</param>
        public static IReadOnlyList<string> ProspectiveFiles(string repositoryRoot, string driverName)
        {
            return PackageFiles(repositoryRoot, driverName);
        }

        /// <summary>assistant-package七个文件的绝对路径列表，顺序即写盘顺序。</summary>
        private static IReadOnlyList<string> PackageFiles(string repositoryRoot, string driverName)
        {
            var knowledgeDirectory = ProvisionPaths.AssistantKnowledgeDirectory(repositoryRoot, driverName);
            var systemPromptFile = Path.Combine(ProvisionPaths.AssistantPackageDirectory(repositoryRoot, driverName), "system-prompt.md");
            var designSummaryFile = Path.Combine(knowledgeDirectory, "design-digest.md");
            var conflictListFile = Path.Combine(knowledgeDirectory, "conflicts.md");
            var glossaryFile = Path.Combine(knowledgeDirectory, "glossary.md");
            var examplesFile = Path.Combine(knowledgeDirectory, "examples.md");
            var moduleListFile = Path.Combine(knowledgeDirectory, "modules.md");
            var importGuideFile = Path.Combine(ProvisionPaths.AssistantPackageDirectory(repositoryRoot, driverName), "import-guide.md");

            return new[]
            {
                systemPromptFile,
                designSummaryFile,
                conflictListFile,
                glossaryFile,
                examplesFile,
                moduleListFile,
                importGuideFile
            };
        }

        /// <summary>组系统提示：角色、必须遵守、schema 摘要、分类型必填与填写指南五节。</summary>
        private static string BuildSystemPrompt(PoolSchema schema)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# 需求助手系统提示");
            builder.AppendLine();
            builder.AppendLine("## 角色");
            builder.AppendLine();
            builder.AppendLine("你是策划提需求时的助手，帮策划把需求写清楚、写进 schema 的框架里。");
            builder.AppendLine("你的价值排序是「设计一致性把关 > 格式合规」：先保证需求与既有设计一致，再保证格式合规。");
            builder.AppendLine();
            builder.AppendLine("## 必须遵守");
            builder.AppendLine();
            builder.AppendLine("1. 需求必须落在 schema 声明的字段里，不发明 schema 之外的字段。");
            builder.AppendLine("2. 分类型必填不能少：按需求的类型补齐该类型要求的必填字段。");
            builder.AppendLine("3. 发现与既有设计冲突时，先指出冲突再帮着写。");
            builder.AppendLine("4. 新需求碰到「conflicts.md」里列出的涉区 id 时，先提醒提出人那块还挂着未销账的冲突，再继续填写。");
            builder.AppendLine();
            builder.AppendLine("## schema 摘要");
            builder.AppendLine();
            builder.AppendLine("| 字段 | 类型 | 必填 | 枚举值 | 所有权 |");
            builder.AppendLine("|---|---|---|---|---|");
            foreach (var field in schema.Fields)
            {
                var enumText = string.Join("、", field.EnumValues);
                builder.AppendLine($"| {field.Name} | {field.FieldType} | {(field.IsRequired ? "是" : "否")} | {enumText} | {field.Ownership} |");
            }

            builder.AppendLine();
            builder.AppendLine("## 分类型必填");
            builder.AppendLine();
            builder.AppendLine("| 类型 | 必填字段 |");
            builder.AppendLine("|---|---|");
            var typeKeys = new List<string>(schema.RequiredByType.Keys);
            typeKeys.Sort(StringComparer.Ordinal);
            foreach (var typeKey in typeKeys)
            {
                builder.AppendLine($"| {typeKey} | {string.Join("、", schema.RequiredByType[typeKey])} |");
            }

            builder.AppendLine();
            builder.AppendLine("## 填写指南");
            builder.AppendLine();
            builder.AppendLine("- 验收标准要能一条条勾。");
            builder.AppendLine("- 描述写「要什么」不写「怎么实现」。");
            builder.AppendLine("- 不确定归属哪个专项就留空。");
            return builder.ToString();
        }

        /// <summary>
        /// 组设计池摘要：冲突区域标注在最前，之后每个 md 出一节，文件缺失时给占位文案。
        /// 冲突区域标注只在冲突列表读成且有未决冲突时出现；读不成时改插一句声明，零未决时不插。
        /// </summary>
        private static string BuildDesignSummary(string poolRoot, ConflictList conflictList)
        {
            var summaryDirectory = PoolPaths.DesignSummaryDirectory(poolRoot);
            if (!Directory.Exists(summaryDirectory))
            {
                return "暂无设计汇总。";
            }

            var files = Directory.GetFiles(summaryDirectory, "*.md").ToList();
            files.Sort(StringComparer.Ordinal);
            if (files.Count == 0)
            {
                return "暂无设计汇总。";
            }

            var builder = new StringBuilder();
            AppendConflictMark(builder, conflictList);
            for (var i = 0; i < files.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendLine();
                }

                var fileName = Path.GetFileNameWithoutExtension(files[i]);
                builder.AppendLine($"## {fileName}");
                builder.AppendLine();
                var lines = File.ReadAllLines(files[i]);
                var head = lines.Take(MaxSummaryLines).ToArray();
                builder.AppendLine(string.Join("\n", head));
            }

            return builder.ToString();
        }

        /// <summary>
        /// 组冲突清单：只列未销账的冲突与涉区 id，供助手在策划提新需求时提醒「这块还挂着账」。
        /// 读不成时写一句声明并声明不作数，绝不许写成「暂无冲突」——助手拿着一份假清单去打包票比没有清单糟。
        /// </summary>
        private static string BuildConflictList(ConflictList conflictList)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# 冲突清单");
            builder.AppendLine();
            builder.AppendLine("> 这份清单来自冲突列表，只列**未销账**的。新需求如果碰到下面这些区域，");
            builder.AppendLine("> 提醒策划：这块还挂着账，先看看之前是怎么决定的。");
            builder.AppendLine();

            if (conflictList == null || conflictList.LoadFailureReason.Length > 0)
            {
                var reason = conflictList?.LoadFailureReason ?? "";
                var reasonText = string.IsNullOrEmpty(reason) ? "" : $"：{reason}";
                builder.AppendLine($"（冲突列表没读成{reasonText}；这份清单不作数）");
                builder.AppendLine();
                AppendConflictListSection(builder, "未决冲突", Array.Empty<string>());
                AppendConflictListSection(builder, "涉区 id（碰到这些 id 就要提醒）", Array.Empty<string>());
                return builder.ToString();
            }

            var report = ConflictDebtView.All(conflictList);
            if (report.Items.Count == 0)
            {
                builder.AppendLine("当前没有未决冲突。");
                builder.AppendLine();
                AppendConflictListSection(builder, "未决冲突", Array.Empty<string>());
                AppendConflictListSection(builder, "涉区 id（碰到这些 id 就要提醒）", Array.Empty<string>());
                return builder.ToString();
            }

            var summaries = new List<string>();
            foreach (var item in report.Items)
            {
                summaries.Add(item.Summary);
            }

            AppendConflictListSection(builder, "未决冲突", summaries);
            AppendConflictListSection(builder, "涉区 id（碰到这些 id 就要提醒）", ConflictDebtView.AffectedIdentifiers(report));
            return builder.ToString();
        }

        /// <summary>渲染冲突清单里的一个小节：标题恒在，没内容写「- 无」。</summary>
        private static void AppendConflictListSection(StringBuilder builder, string title, IReadOnlyList<string> items)
        {
            builder.AppendLine($"## {title}");
            builder.AppendLine();
            if (items.Count == 0)
            {
                builder.AppendLine("- 无");
                return;
            }

            foreach (var item in items)
            {
                builder.AppendLine($"- {item}");
            }
        }

        /// <summary>
        /// 在设计池摘要最前面插冲突区域标注：读不成插声明，有未决插区域 id，零未决不插。
        /// </summary>
        private static void AppendConflictMark(StringBuilder builder, ConflictList conflictList)
        {
            if (conflictList == null || conflictList.LoadFailureReason.Length > 0)
            {
                var reason = conflictList?.LoadFailureReason ?? "";
                var reasonText = string.IsNullOrEmpty(reason) ? "" : $"：{reason}";
                builder.AppendLine($"> ⚠ 冲突列表没读成{reasonText}；本摘要未标注冲突区域。");
                builder.AppendLine();
                return;
            }

            var report = ConflictDebtView.All(conflictList);
            if (report.Items.Count == 0)
            {
                return;
            }

            var identifiers = string.Join("、", ConflictDebtView.AffectedIdentifiers(report));
            builder.AppendLine($"> ⚠ 冲突区域：{identifiers}——这些 id 上还挂着未销账的冲突，涉及它们的新需求要先问清楚。");
            builder.AppendLine();
        }

        /// <summary>组术语表：读知识目录下的术语表.json，渲染两列表；拿不到给占位文案。</summary>
        private static string BuildGlossary(string poolRoot)
        {
            var filePath = Path.Combine(ProvisionPaths.KnowledgeDirectory(poolRoot), "术语表.json");
            if (!File.Exists(filePath))
            {
                return "暂无术语表。";
            }

            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("条目", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return "暂无术语表。";
            }

            var builder = new StringBuilder();
            builder.AppendLine("| 词 | 释义 |");
            builder.AppendLine("|---|---|");
            foreach (var entry in entries.EnumerateArray())
            {
                builder.AppendLine($"| {ReadStringOrEmpty(entry, "词")} | {ReadStringOrEmpty(entry, "释义")} |");
            }

            return builder.ToString();
        }

        /// <summary>组正反例：读知识目录下的正反例.json，渲染两个小节；拿不到给占位文案。</summary>
        private static string BuildExamples(string poolRoot)
        {
            var filePath = Path.Combine(ProvisionPaths.KnowledgeDirectory(poolRoot), "正反例.json");
            if (!File.Exists(filePath))
            {
                return "暂无正反例。";
            }

            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = document.RootElement;
            var positive = ReadStringArray(root, "正例");
            var negative = ReadStringArray(root, "反例");

            var builder = new StringBuilder();
            builder.AppendLine("## 被验收的写法");
            builder.AppendLine();
            foreach (var item in positive)
            {
                builder.AppendLine($"- {item}");
            }

            builder.AppendLine();
            builder.AppendLine("## 被打回的写法");
            builder.AppendLine();
            foreach (var item in negative)
            {
                builder.AppendLine($"- {item}");
            }

            return builder.ToString();
        }

        /// <summary>组模块清单：优先读知识目录下的模块清单.json，缺失时扫工程模块目录。</summary>
        private static string BuildModuleList(string repositoryRoot, string poolRoot)
        {
            var filePath = Path.Combine(ProvisionPaths.KnowledgeDirectory(poolRoot), "模块清单.json");
            if (File.Exists(filePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(filePath));
                var modules = ReadStringArray(document.RootElement, "模块");
                if (modules.Count > 0)
                {
                    return RenderBulletList(modules);
                }
            }

            var modulesRoot = Path.Combine(repositoryRoot, "UnityProject", "Assets", "Game", "Scripts", "Modules");
            if (Directory.Exists(modulesRoot))
            {
                var directoryNames = new List<string>();
                foreach (var directoryPath in Directory.GetDirectories(modulesRoot))
                {
                    var name = Path.GetFileName(directoryPath);
                    if (!name.StartsWith(".", StringComparison.Ordinal))
                    {
                        directoryNames.Add(name);
                    }
                }

                if (directoryNames.Count > 0)
                {
                    directoryNames.Sort(StringComparer.Ordinal);
                    return RenderBulletList(directoryNames);
                }
            }

            return "暂无模块清单。";
        }

        /// <summary>组导入说明：固定四步加过期警告，不出现任何下游平台的名字。</summary>
        private static string BuildImportGuide()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# 配置包导入说明");
            builder.AppendLine();
            builder.AppendLine("按下面四步把本配置包导入下游平台：");
            builder.AppendLine();
            builder.AppendLine("1. 在下游平台新建助手。");
            builder.AppendLine("2. 把「system-prompt.md」全文贴进系统提示框。");
            builder.AppendLine("3. 把「知识」目录下四个文件逐个上传为知识库文件。");
            builder.AppendLine("4. 回到本仓库跑一次门禁对账，确认指纹一致。");
            builder.AppendLine();
            builder.AppendLine("> 警告：fingerprint.json 变了就必须重新走一遍本流程，否则助手用的是过期知识。");
            return builder.ToString();
        }

        /// <summary>把一组字符串渲染成无序列表。</summary>
        private static string RenderBulletList(IReadOnlyList<string> items)
        {
            var builder = new StringBuilder();
            foreach (var item in items)
            {
                builder.AppendLine($"- {item}");
            }

            return builder.ToString();
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读字符串数组；缺失或类型不对给空列表。</summary>
        private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
        {
            var values = new List<string>();
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out var listElement)
                || listElement.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in listElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    values.Add(item.GetString() ?? "");
                }
            }

            return values;
        }

        /// <summary>写一个 UTF-8 文件。</summary>
        private static void WriteAll(string filePath, string content)
        {
            File.WriteAllText(filePath, content, new UTF8Encoding(false));
        }

        /// <summary>设计池摘要每个文件最多取的行数。</summary>
        private const int MaxSummaryLines = 40;
    }
}
