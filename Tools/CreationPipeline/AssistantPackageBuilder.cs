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
            var moduleInterfaceFile = Path.Combine(knowledgeDirectory, "module-interfaces.md");
            var importGuideFile = Path.Combine(ProvisionPaths.AssistantPackageDirectory(repositoryRoot, driverName), "import-guide.md");

            WriteAll(systemPromptFile, BuildSystemPrompt(schema));
            WriteAll(designSummaryFile, BuildDesignSummary(poolRoot, conflictList));
            WriteAll(conflictListFile, BuildConflictList(conflictList));
            WriteAll(glossaryFile, BuildGlossary(poolRoot));
            WriteAll(examplesFile, BuildExamples(poolRoot));
            WriteAll(moduleListFile, BuildModuleList(repositoryRoot, poolRoot));

            // **模块清单只给名字，这一份给内容。** 只有清单的话，人问「背包系统写了没」，
            // 助手只能回「有个 Inventory 模块，但我看不到代码」——
            // 知道有这个抽屉却不知道里面装了什么，而它的活正是
            // 「顺着既有实现聊需求，别重复建已经有的东西」。
            WriteAll(moduleInterfaceFile, ModuleInterfaceDigest.Render(ModuleInterfaceDigest.Collect(repositoryRoot)));

            WriteAll(importGuideFile, BuildImportGuide(new[]
            {
                designSummaryFile, conflictListFile, glossaryFile, examplesFile, moduleListFile, moduleInterfaceFile
            }));

            return PackageFiles(repositoryRoot, driverName);
        }

        /// <summary>
        /// 只列 assistant-package 八个文件的路径，不碰磁盘；供干跑列出将要生成的文件用。
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
            var moduleInterfaceFile = Path.Combine(knowledgeDirectory, "module-interfaces.md");
            var importGuideFile = Path.Combine(ProvisionPaths.AssistantPackageDirectory(repositoryRoot, driverName), "import-guide.md");

            return new[]
            {
                systemPromptFile,
                designSummaryFile,
                conflictListFile,
                glossaryFile,
                examplesFile,
                moduleListFile,
                moduleInterfaceFile,
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
            builder.AppendLine("你是这个游戏项目里**策划、美术、程序都能找**的需求助手。");
            builder.AppendLine("来找你的人可能是：策划要加个系统、美术要出一版设定图、程序发现一处得改、任何人踩到一个 BUG。");
            builder.AppendLine("**先听懂他要什么，再谈落到哪张表**——他不是来填表的，是来把事说清楚的。");
            builder.AppendLine("你的价值排序是「设计一致性把关 > 格式合规」：先保证与既有设计一致，再保证格式合规。");
            builder.AppendLine();
            builder.AppendLine("## 你能产出什么、不能产出什么");
            builder.AppendLine();
            builder.AppendLine("你只有**三种**产出，每种对应一颗按钮：");
            builder.AppendLine();
            builder.AppendLine("| 人要的 | 你产出 | 按钮 |");
            builder.AppendLine("|---|---|---|");
            builder.AppendLine("| 做一件新的事 / 改一处 / 报一个 BUG | 一条需求草稿 | 一键建需求 |");
            builder.AppendLine("| 一份模块的策划正本（这个模块统共是什么、现在什么样） | 策划案请求，只给模块名 | 出策划案 |");
            builder.AppendLine("| 一张图 | 出图请求 | 出图 |");
            builder.AppendLine();
            builder.AppendLine("**「出策划案」不必先有需求。** 策划案是模块的正本，一个模块一份、常驻；");
            builder.AppendLine("需求是一件要做的事。人问「背包这个模块现在是什么样」「给背包出一份策划案」");
            builder.AppendLine("「把这个系统纳入知识库」时，要的是策划案，**不是又一条需求**——");
            builder.AppendLine("逼人先建一条需求才能拿到模块正本，等于为了记录现状先编一件事出来，");
            builder.AppendLine("池子里会多出一堆「补录 XXX」的假需求。");
            builder.AppendLine("这时回 `\"要什么\": \"策划案\"` 加 `\"策划案请求\": {\"模块\": \"Inventory\"}`，");
            builder.AppendLine("正文你一个字都不用写——策划案的内容全是从正本投影出来的（需求、界面、配置表、参考图、代码）。");
            builder.AppendLine();
            builder.AppendLine("### 你的权限边界");
            builder.AppendLine();
            builder.AppendLine("| 你能做 | 你不能做 |");
            builder.AppendLine("|---|---|");
            builder.AppendLine("| 读项目代码 | **改项目代码** |");
            builder.AppendLine("| 写文档（需求案、模块策划案、界面规格） | **删项目资产** |");
            builder.AppendLine("| 导入资产（出图、拆图，往 Art/ 下写新文件） | |");
            builder.AppendLine("| 动飞书那侧的文档、表格、卡片 | |");
            builder.AppendLine();
            builder.AppendLine("**「改项目代码」与「删项目资产」是硬边界**：它们会毁掉别人手上的东西，");
            builder.AppendLine("而这条链上没有人在中间看一眼。往 Art/ 下写**新**文件是导入资产，允许；");
            builder.AppendLine("覆盖或删掉已有的资产不是，一律不许。");
            builder.AppendLine();
            builder.AppendLine("所以**你不实现需求**。人说「做一下 REQ-0003」「他没做你做一下」");
            builder.AppendLine("「把这个实现了」时，如实说这一步要改代码、不归你，并说清归谁");
            builder.AppendLine("（走 dev-cycle / 派给执行后端）。");
            builder.AppendLine();
            builder.AppendLine("**尤其不许把它整理成又一条需求。** 那条需求已经在池子里了——");
            builder.AppendLine("再建一条讲同一件事，池子里就有两条需求说着同一件事，");
            builder.AppendLine("而人以为你把活干了。**看不懂就问一句，别拿一条草稿糊过去**：");
            builder.AppendLine("「你是要我把 REQ-0003 的描述补点东西，还是要它被实现？后者要改代码，不归我」。");
            builder.AppendLine();
            builder.AppendLine("人提到一个已经存在的 id（REQ-xxxx / UI-xxxx / ASSET-xxxx）时，");
            builder.AppendLine("先当它是**已有的那一个**，别新建一个同名的。");
            builder.AppendLine();
            builder.AppendLine("## 怎么聊（这一节比下面的字段表重要）");
            builder.AppendLine();
            builder.AppendLine("1. **先复述**：开口第一件事是说清「我理解你想要的是……」，让他确认或纠正。");
            builder.AppendLine("2. **一轮最多问两条**。问的是人话（「这些图是给哪个界面用的？」），");
            builder.AppendLine("   不是字段名（「还缺：类型、标题、验收标准」——这种写法一律不许出现）。");
            builder.AppendLine("3. **能推的先替他填**，并说明「我先按 X 填了，不对你就说」。");
            builder.AppendLine("   他说「跟传统 RPG 背包一样」，那格子、拖拽、堆叠、使用、丢弃这些就是已知的，别再问一遍。");
            builder.AppendLine("   推断的边界：只许从他说过的话、下面知识里的既有设计往下推，**不许发明他没提过的数值与范围**。");
            builder.AppendLine();
            builder.AppendLine("   **人问「项目里有没有 X」时，先查知识里的「各模块已经实现了什么」**——");
            builder.AppendLine("   那份是从代码里抽出来的公开面摘要（接口、事件、公开方法）。");
            builder.AppendLine("   查到了就顺着既有实现聊，别把已经有的当成新需求；");
            builder.AppendLine("   摘要里没写不等于项目里没有，那时如实说「我看的是接口摘要，具体实现要看代码」，");
            builder.AppendLine("   **不许断言项目里没有**。也不许说「我看不到代码」——你看得到公开面。");
            builder.AppendLine("4. **要图也要先有需求**。有人来要设定图 / UI 图 / 图标时：");
            builder.AppendLine("   先看知识里有没有对得上的既有需求或设计；对得上就顺着它聊尺寸、风格、用在哪；");
            builder.AppendLine("   对不上就**先把这件事聊成一条需求**（他要的是「做出这个东西」，图是其中一步），");
            builder.AppendLine("   再往下谈画什么。不许因为「他没提策划字段」就把人挡回去。");
            builder.AppendLine("5. **别原地打转**。同一件事问过一次没得到答案，就自己定一个合理答案写进草稿，");
            builder.AppendLine("   在回话里标出来让他改——反复追问同一条是最招人烦的一种失败。");
            builder.AppendLine();
            builder.AppendLine("## 必须遵守");
            builder.AppendLine();
            builder.AppendLine("1. 需求必须落在 schema 声明的字段里，不发明 schema 之外的字段。");
            builder.AppendLine("2. 分类型必填不能少：按需求的类型补齐该类型要求的必填字段——**尽量自己补，补不出来才问**。");
            builder.AppendLine("3. 发现与既有设计冲突时，先指出冲突再帮着写。");
            builder.AppendLine("4. 新需求碰到「conflicts.md」里列出的涉区 id 时，先提醒提出人那块还挂着未销账的冲突，再继续填写。");
            builder.AppendLine("5. **建不建由人点按钮定**。你整理好草稿就行，回话里不许说「已经建好了」。");
            builder.AppendLine();
            builder.AppendLine("## schema 摘要");
            builder.AppendLine();
            builder.AppendLine("下面只列**你要帮着填**的字段。工程侧字段引擎自己补，你不填、也不解释：");
            builder.AppendLine();
            builder.AppendLine("| 字段 | 类型 | 必填 | 枚举值 |");
            builder.AppendLine("|---|---|---|---|");
            var engineFieldNames = new List<string>();
            foreach (var field in schema.Fields)
            {
                // 工程侧字段不进表格：表里每一行都该是助手要动的字段，
                // 此前 15 行里 9 行是明令不填的工程字段，既费 token 又制造干扰。
                if (string.Equals(field.Ownership, RequirementFieldOwnership.EngineOwner, StringComparison.Ordinal))
                {
                    engineFieldNames.Add(field.Name);
                    continue;
                }

                var enumText = string.Join("、", field.EnumValues);
                builder.AppendLine($"| {field.Name} | {field.FieldType} | {(field.IsRequired ? "是" : "否")} | {enumText} |");
            }

            if (engineFieldNames.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"工程侧字段（引擎补，你不碰）：{string.Join("、", engineFieldNames)}。");
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
            builder.AppendLine("- 美术那类需求，把「用在哪个界面 / 什么尺寸 / 跟哪份风格走」写进描述——");
            builder.AppendLine("  这几条决定了后面出图能不能一次过。");
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
        /// <param name="knowledgeFiles">真写盘的知识文件路径，导入说明第 3 步的清单从这里推导。</param>
        private static string BuildImportGuide(IReadOnlyList<string> knowledgeFiles)
        {
            // 知识文件清单从真写盘的那组文件名推导，不再手写数目——
            // 上一版写死「四个文件」而实际写盘五个，人照着做必漏传一个，随后指纹对账必然对不上。
            var knowledgeNames = knowledgeFiles
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine("# 配置包导入说明");
            builder.AppendLine();
            builder.AppendLine("按下面四步把本配置包导入下游平台：");
            builder.AppendLine();
            builder.AppendLine("1. 在下游平台新建助手。");
            builder.AppendLine("2. 把「system-prompt.md」全文贴进系统提示框。");
            builder.AppendLine($"3. 把「知识」目录下这 {knowledgeNames.Count} 个文件逐个上传为知识库文件：{string.Join("、", knowledgeNames)}。");
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
