using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 供给产物在仓库根之下的路径拼装：建表描述、专项表、校验错误文案、指纹与assistant-package，
    /// 全部落在 _Generated/Bridges/&lt;driver&gt; 之下，以仓库根目录为起点。纯路径拼接，不碰磁盘。
    /// </summary>
    public static class ProvisionPaths
    {
        /// <summary>某 driver 的供给产物目录：_Generated/Bridges/&lt;driver&gt;。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string GeneratedBridgeDirectory(string repositoryRoot, string driverName)
        {
            return Path.Combine(repositoryRoot, "_Generated", "Bridges", driverName);
        }

        /// <summary>建表描述文件：_Generated/Bridges/&lt;driver&gt;/table-description.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string TableDescriptionFile(string repositoryRoot, string driverName)
        {
            return Path.Combine(GeneratedBridgeDirectory(repositoryRoot, driverName), "table-description.json");
        }

        /// <summary>专项表文件：_Generated/Bridges/&lt;driver&gt;/epic-table.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string EpicTableFile(string repositoryRoot, string driverName)
        {
            return Path.Combine(GeneratedBridgeDirectory(repositoryRoot, driverName), "epic-table.json");
        }

        /// <summary>校验错误文案文件：_Generated/Bridges/&lt;driver&gt;/validation-messages.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string ValidationMessageFile(string repositoryRoot, string driverName)
        {
            return Path.Combine(GeneratedBridgeDirectory(repositoryRoot, driverName), "validation-messages.json");
        }

        /// <summary>指纹文件：_Generated/Bridges/&lt;driver&gt;/fingerprint.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string FingerprintFile(string repositoryRoot, string driverName)
        {
            return Path.Combine(GeneratedBridgeDirectory(repositoryRoot, driverName), "fingerprint.json");
        }

        /// <summary>
        /// 能力探测结果文件：<c>_Generated/Probes/&lt;driver&gt;/probe-result.json</c>。
        /// **刻意不落 `_Generated/Bridges/&lt;driver&gt;/`**，那里是供给产物的地盘：
        /// `gate.provision` 靠「那个目录在不在」判断这个 driver 声称已供给，
        /// 探测结果一写进去，那个 driver 就被当成「已供给却缺一堆产物」而判红（P8 批次 8 真踩过）。
        /// 两者本来就是反方向的东西——**供给产物是推给下游的、进 git 供对账（决策 12）；
        /// 探测结果是从下游读回来的、跟机器走、不进 git**。目录分开，语义才不打架。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string ProbeResultFile(string repositoryRoot, string driverName)
        {
            return Path.Combine(repositoryRoot, "_Generated", "Probes", driverName, "probe-result.json");
        }

        /// <summary>assistant-package目录：_Generated/Bridges/&lt;driver&gt;/assistant-package。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string AssistantPackageDirectory(string repositoryRoot, string driverName)
        {
            return Path.Combine(GeneratedBridgeDirectory(repositoryRoot, driverName), "assistant-package");
        }

        /// <summary>assistant-package的知识目录：_Generated/Bridges/&lt;driver&gt;/assistant-package/知识。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string AssistantKnowledgeDirectory(string repositoryRoot, string driverName)
        {
            return Path.Combine(AssistantPackageDirectory(repositoryRoot, driverName), "knowledge");
        }

        /// <summary>助手知识素材目录：&lt;池根&gt;/知识。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static string KnowledgeDirectory(string poolRoot)
        {
            return Path.Combine(poolRoot, "Knowledge");
        }
    }
}
