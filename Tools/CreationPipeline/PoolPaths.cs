using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>池子各子目录与 schema 文件的路径拼装，全部以池根目录为起点。</summary>
    public static class PoolPaths
    {
        /// <summary>基线 schema 目录：Schema/基线。</summary>
        public static string SchemaBaselineDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Schema", "基线");
        }

        /// <summary>项目扩展 schema 目录：Schema/项目。</summary>
        public static string SchemaProjectDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Schema", "项目");
        }

        /// <summary>收件箱目录：Inbox。</summary>
        public static string InboxDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Inbox");
        }

        /// <summary>专项认领的收件箱目录：Inbox/专项。</summary>
        public static string EpicInboxDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Inbox", "专项");
        }

        /// <summary>需求目录：Requirements。</summary>
        public static string RequirementsDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Requirements");
        }

        /// <summary>专项目录：专项。</summary>
        public static string EpicsDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "专项");
        }

        /// <summary>组织目录：组织。</summary>
        public static string OrganizationDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "组织");
        }

        /// <summary>设计记录目录：Designs/记录。</summary>
        public static string DesignRecordsDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Designs", "记录");
        }

        /// <summary>设计汇总目录：Designs/汇总。</summary>
        public static string DesignSummaryDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Designs", "汇总");
        }

        /// <summary>设计定稿目录：Designs/定稿。</summary>
        public static string DesignFinalDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Designs", "定稿");
        }

        /// <summary>某实体的基线 schema 文件：Schema/基线/&lt;实体名&gt;.schema.json。</summary>
        public static string BaselineSchemaFile(string poolRoot, string entityName)
        {
            return Path.Combine(poolRoot, "Schema", "基线", $"{entityName}.schema.json");
        }

        /// <summary>某实体的项目扩展 schema 文件：Schema/项目/&lt;实体名&gt;.扩展.json。</summary>
        public static string ProjectSchemaFile(string poolRoot, string entityName)
        {
            return Path.Combine(poolRoot, "Schema", "项目", $"{entityName}.扩展.json");
        }

        /// <summary>已确认待执行的任务队列文件：队列.json。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static string QueueFile(string poolRoot)
        {
            return Path.Combine(poolRoot, "队列.json");
        }
    }
}
