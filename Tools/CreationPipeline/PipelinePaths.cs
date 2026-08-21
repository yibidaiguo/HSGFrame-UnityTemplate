using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>创作管线在仓库根之下的工作目录路径拼装：任务、变更、拒收与累积文件，全部以仓库根为起点。</summary>
    public static class PipelinePaths
    {
        /// <summary>某需求的任务目录：_Tasks/&lt;REQ-xxxx&gt;。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static string TaskDirectory(string repositoryRoot, string requirementIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier);
        }

        /// <summary>某需求的变更目录：_Tasks/&lt;REQ-xxxx&gt;/变更。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static string ChangeDirectory(string repositoryRoot, string requirementIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "changes");
        }

        /// <summary>拒收单目录：_Generated/拒收。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string RejectionDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Generated", "rejected");
        }

        /// <summary>某需求的累积变更文件：_Tasks/&lt;REQ-xxxx&gt;/变更/累积.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static string AccumulatedChangeFile(string repositoryRoot, string requirementIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "changes", "accumulated.json");
        }

        /// <summary>出站意图信封目录：_Generated/出站。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string OutboundDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Generated", "outbound");
        }

        /// <summary>某需求的任务状态文件：_Tasks/&lt;REQ-xxxx&gt;/状态.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static string TaskStateFile(string repositoryRoot, string requirementIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "state.json");
        }

        /// <summary>同步水位文件：Tools/CreationPipeline/Config/sync-watermark.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string SyncWatermarkFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "sync-watermark.json");
        }

        /// <summary>某需求某版次的需求快照：_Tasks/&lt;需求id&gt;/00-requirement.v&lt;N&gt;.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="version">快照版次，从 1 起。</param>
        public static string RequirementSnapshotFile(string repositoryRoot, string requirementIdentifier, int version)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, $"00-requirement.v{version}.json");
        }

        /// <summary>变更影响文档：_Tasks/&lt;需求id&gt;/05-change-impact.md。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        public static string ChangeImpactFile(string repositoryRoot, string requirementIdentifier)
        {
            return Path.Combine(repositoryRoot, "_Tasks", requirementIdentifier, "05-change-impact.md");
        }
    }
}
