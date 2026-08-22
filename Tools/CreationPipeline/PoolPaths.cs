using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>池子各子目录与 schema 文件的路径拼装，全部以池根目录为起点。</summary>
    public static class PoolPaths
    {
        /// <summary>
        /// 实体名（领域词汇，中文）→ schema 文件名词干（ASCII）。
        ///
        /// **这张表存在的理由**：决策 1 要求路径全 ASCII，而实体名是**数据里的领域词汇**，
        /// 中文留着才读得懂（`"实体": "需求"` 出现在 schema、信封、卡片、面板文案里，
        /// 改它等于把整套词汇换一遍）。两件事因此必须解耦：
        /// **名字用中文，文件名用 ASCII，中间隔一张显式的表。**
        /// 表里没有的实体按原名返回——那样路径门禁会把它列出来，
        /// 比悄悄拼一个中文文件名强。
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> EntityFileStems =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["需求"] = "requirement",
                ["工作项"] = "work-item",
                ["设计记录"] = "design-record",
                ["asset-requests"] = "asset-request",
                ["溯源"] = "provenance"
            };

        /// <summary>
        /// 实体名 → 文件名词干；表里没有的按原名返回。
        /// </summary>
        /// <param name="entityName">实体名，如「需求」。</param>
        public static string EntityFileStem(string entityName)
        {
            var name = entityName ?? "";
            return EntityFileStems.TryGetValue(name, out var stem) ? stem : name;
        }

        /// <summary>基线 schema 目录：Schema/Baseline。</summary>
        public static string SchemaBaselineDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Schema", "Baseline");
        }

        /// <summary>项目扩展 schema 目录：Schema/Project。</summary>
        public static string SchemaProjectDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Schema", "Project");
        }

        /// <summary>收件箱目录：Inbox。</summary>
        public static string InboxDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Inbox");
        }

        /// <summary>专项认领的收件箱目录：Inbox/Epics。</summary>
        public static string EpicInboxDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Inbox", "Epics");
        }

        /// <summary>需求根目录：Requirements。一条需求是这底下的**一个目录**，不是一个文件。</summary>
        public static string RequirementsDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Requirements");
        }

        /// <summary>
        /// 一条需求自己的目录：Requirements/REQ-0042。
        ///
        /// **这一族访问器存在的理由**：需求从「一个文件」改成「一个目录」那次迁移，
        /// 全仓有 8 处在各自 `Path.Combine(RequirementsDirectory(root), id + ".json")`。
        /// 散着拼的后果不是改起来累，而是**下次再加一份随需求走的东西**
        /// （文档、媒体、快照都是这次加的）时，你没有任何办法知道还漏了谁。
        /// 所以路径一律经这里，调用方不许再自己拼（决策 99）。
        /// </summary>
        /// <param name="poolRoot">池根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static string RequirementDirectory(string poolRoot, string requirementIdentifier)
        {
            return Path.Combine(RequirementsDirectory(poolRoot), requirementIdentifier ?? "");
        }

        /// <summary>需求骨架 JSON：Requirements/REQ-0042/requirement.json。</summary>
        /// <param name="poolRoot">池根目录。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        public static string RequirementFile(string poolRoot, string requirementIdentifier)
        {
            return Path.Combine(RequirementDirectory(poolRoot, requirementIdentifier), RequirementFileName);
        }

        /// <summary>需求文档正文：Requirements/REQ-0042/index.md。</summary>
        /// <param name="poolRoot">池根目录。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        public static string RequirementDocument(string poolRoot, string requirementIdentifier)
        {
            return Path.Combine(RequirementDirectory(poolRoot, requirementIdentifier), RequirementDocumentFileName);
        }

        /// <summary>需求媒体目录：Requirements/REQ-0042/media。图片与视频本体落这里。</summary>
        /// <param name="poolRoot">池根目录。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        public static string RequirementMediaDirectory(string poolRoot, string requirementIdentifier)
        {
            return Path.Combine(RequirementDirectory(poolRoot, requirementIdentifier), RequirementMediaDirectoryName);
        }

        /// <summary>需求文档快照目录：Requirements/REQ-0042/snapshots。覆盖对侧内容前的留底（决策 101）。</summary>
        /// <param name="poolRoot">池根目录。</param>
        /// <param name="requirementIdentifier">需求 id。</param>
        public static string RequirementSnapshotsDirectory(string poolRoot, string requirementIdentifier)
        {
            return Path.Combine(RequirementDirectory(poolRoot, requirementIdentifier), "snapshots");
        }

        /// <summary>需求骨架 JSON 的固定文件名。</summary>
        public const string RequirementFileName = "requirement.json";

        /// <summary>需求文档正文的固定文件名。</summary>
        public const string RequirementDocumentFileName = "index.md";

        /// <summary>
        /// 需求媒体目录的固定目录名。文档正文里的媒体引用写成 <c>media/x.png</c>，
        /// 认那种引用的地方要拿这个常量比，别再各自写一遍字面量（决策 99 的推论一）。
        /// </summary>
        public const string RequirementMediaDirectoryName = "media";

        /// <summary>
        /// 枚举池子里现存的全部需求 id（Requirements 下的一级子目录名，按序排）。
        /// 目录不存在返回空序列；**不看目录里有没有 requirement.json**——
        /// 「目录在而骨架缺」是校验器要报的违规，不是枚举器该悄悄跳过的东西。
        /// </summary>
        /// <param name="poolRoot">池根目录。</param>
        public static IReadOnlyList<string> EnumerateRequirementIdentifiers(string poolRoot)
        {
            var directory = RequirementsDirectory(poolRoot);
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            var identifiers = new List<string>();
            foreach (var path in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                identifiers.Add(Path.GetFileName(path));
            }

            identifiers.Sort(StringComparer.Ordinal);
            return identifiers;
        }

        /// <summary>专项目录：专项。</summary>
        public static string EpicsDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Epics");
        }

        /// <summary>组织目录：组织。</summary>
        public static string OrganizationDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Organization");
        }

        /// <summary>设计记录目录：Designs/Records。</summary>
        public static string DesignRecordsDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Designs", "Records");
        }

        /// <summary>设计汇总目录：Designs/Digest。</summary>
        public static string DesignSummaryDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Designs", "Digest");
        }

        /// <summary>设计定稿目录：Designs/Final。</summary>
        public static string DesignFinalDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Designs", "Final");
        }

        /// <summary>某实体的基线 schema 文件：Schema/Baseline/&lt;实体名&gt;.schema.json。</summary>
        public static string BaselineSchemaFile(string poolRoot, string entityName)
        {
            return Path.Combine(poolRoot, "Schema", "Baseline", $"{EntityFileStem(entityName)}.schema.json");
        }

        /// <summary>某实体的项目扩展 schema 文件：Schema/Project/&lt;实体名&gt;.扩展.json。</summary>
        public static string ProjectSchemaFile(string poolRoot, string entityName)
        {
            return Path.Combine(poolRoot, "Schema", "Project", $"{EntityFileStem(entityName)}.extension.json");
        }

        /// <summary>已确认待执行的任务队列文件：queue.json。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static string QueueFile(string poolRoot)
        {
            return Path.Combine(poolRoot, "queue.json");
        }

        /// <summary>冲突列表文件：Designs/conflicts.json。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static string ConflictListFile(string poolRoot)
        {
            return Path.Combine(poolRoot, "Designs", "conflicts.json");
        }

        /// <summary>意见库目录：审查意见。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static string ReviewOpinionDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "ReviewOpinions");
        }

        /// <summary>晋升提案目录：晋升提案。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static string PromotionProposalDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Promotions");
        }

        /// <summary>放行流水文件：release-ledger.json。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static string ReleaseLedgerFile(string poolRoot)
        {
            return Path.Combine(poolRoot, "release-ledger.json");
        }

        /// <summary>冲突裁决流水文件：Designs/conflict-decisions.json。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static string ConflictDecisionLedgerFile(string poolRoot)
        {
            return Path.Combine(poolRoot, "Designs", "conflict-decisions.json");
        }
    }
}
