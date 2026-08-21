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
                ["资产请求"] = "asset-request",
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

        /// <summary>需求目录：Requirements。</summary>
        public static string RequirementsDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Requirements");
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
