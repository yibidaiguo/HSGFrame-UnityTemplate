using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次供给对账的结果：扫到的 driver、已供给数与逐条发现。</summary>
    public sealed class ProvisionReconcileReport
    {
        /// <summary>
        /// 构造一次供给对账的结果。
        /// </summary>
        /// <param name="driverNames">扫到的全部 driver 名，序数序。</param>
        /// <param name="provisionedCount">有指纹文件的 driver 数。</param>
        /// <param name="findings">对账发现。</param>
        public ProvisionReconcileReport(
            IReadOnlyList<string> driverNames,
            int provisionedCount,
            IReadOnlyList<PoolFinding> findings)
        {
            DriverNames = driverNames ?? Array.Empty<string>();
            ProvisionedCount = provisionedCount;
            Findings = findings ?? Array.Empty<PoolFinding>();
        }

        /// <summary>扫到的全部 driver 名，序数序。</summary>
        public IReadOnlyList<string> DriverNames { get; }

        /// <summary>有指纹文件的 driver 数。</summary>
        public int ProvisionedCount { get; }

        /// <summary>对账发现。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }
    }

    /// <summary>
    /// 供给对账：扫 <c>Bridges/</c> 下的全部 driver，逐个对账供给指纹。
    /// 指纹缺失视为「未供给」放行，哈希失配或 driver 自述损坏才出问题。
    /// </summary>
    public static class ProvisionReconciler
    {
        /// <summary>
        /// 扫全部 driver 并对账指纹，返回汇总报告。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static ProvisionReconcileReport Reconcile(string repositoryRoot, string poolRoot)
        {
            var driverNames = DiscoverDriverNames(repositoryRoot);
            if (driverNames.Count == 0)
            {
                return new ProvisionReconcileReport(driverNames, 0, new List<PoolFinding>());
            }

            // 两个哈希只算一次：全部 driver 共享同一份合并 schema 与设计池汇总。
            var schemaHash = ProvisionFingerprint.ComputeSchemaHash(PoolSchemaLoader.Load(poolRoot, "需求"));
            var designDigestHash = ProvisionFingerprint.ComputeDesignDigestHash(poolRoot);

            var findings = new List<PoolFinding>();
            var provisionedCount = 0;
            foreach (var driverName in driverNames)
            {
                try
                {
                    BridgeDriverDescriptor.Load(repositoryRoot, driverName);
                }
                catch (InvalidOperationException exception)
                {
                    findings.Add(new PoolFinding(
                        RepositoryRelative(repositoryRoot, BridgeDriverDescriptor.DriverFile(repositoryRoot, driverName)),
                        exception.Message,
                        "按子文档 05 §二 补齐 driver 自述的必填字段",
                        "Bridges/<名>/driver.json"));
                    // 自述损坏不中断对账，继续扫下一个 driver。
                }

                var fingerprintPath = ProvisionPaths.FingerprintFile(repositoryRoot, driverName);
                if (File.Exists(fingerprintPath))
                {
                    provisionedCount++;
                }

                findings.AddRange(ProvisionFingerprint.Reconcile(fingerprintPath, schemaHash, designDigestHash));
            }

            return new ProvisionReconcileReport(driverNames, provisionedCount, findings);
        }

        /// <summary>列 <c>Bridges/</c> 下一级含 driver.json 的目录名，序数序。</summary>
        private static List<string> DiscoverDriverNames(string repositoryRoot)
        {
            var bridgesDirectory = Path.Combine(repositoryRoot, "Bridges");
            var driverNames = new List<string>();
            if (!Directory.Exists(bridgesDirectory))
            {
                return driverNames;
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(bridgesDirectory))
            {
                if (File.Exists(Path.Combine(directoryPath, "driver.json")))
                {
                    driverNames.Add(Path.GetFileName(directoryPath));
                }
            }

            driverNames.Sort(StringComparer.Ordinal);
            return driverNames;
        }

        /// <summary>把绝对路径转成仓库相对路径，正斜杠。</summary>
        private static string RepositoryRelative(string repositoryRoot, string fullPath)
        {
            return Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(fullPath)).Replace('\\', '/');
        }
    }
}
