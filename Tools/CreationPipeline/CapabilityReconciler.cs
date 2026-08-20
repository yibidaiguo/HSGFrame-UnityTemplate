using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次能力对账的结果：driver 名、依赖总数、满足数与逐条发现。</summary>
    public sealed class CapabilityReconcileReport
    {
        /// <summary>
        /// 构造一次能力对账的结果。
        /// </summary>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="dependencyCount">依赖清单里的依赖总数。</param>
        /// <param name="satisfiedCount">探测结果里满足的依赖数。</param>
        /// <param name="findings">对账发现。</param>
        public CapabilityReconcileReport(
            string driverName,
            int dependencyCount,
            int satisfiedCount,
            IReadOnlyList<PoolFinding> findings)
        {
            DriverName = driverName ?? "";
            DependencyCount = dependencyCount;
            SatisfiedCount = satisfiedCount;
            Findings = findings ?? Array.Empty<PoolFinding>();
        }

        /// <summary>driver 名称。</summary>
        public string DriverName { get; }

        /// <summary>依赖清单里的依赖总数。</summary>
        public int DependencyCount { get; }

        /// <summary>探测结果里满足的依赖数。</summary>
        public int SatisfiedCount { get; }

        /// <summary>对账发现。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }
    }

    /// <summary>
    /// 能力对账：把本地形态 driver 的依赖清单逐条对着能力探测结果查「在不在」。
    /// 缺一项就出一条发现，文案带缺什么、来源与怎么装；版本不比对——这一版只查在不在。
    /// </summary>
    public static class CapabilityReconciler
    {
        /// <summary>
        /// 逐条依赖查探测结果，返回汇总报告。
        /// </summary>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="manifest">依赖清单。</param>
        /// <param name="probeResult">能力探测结果。</param>
        public static CapabilityReconcileReport Reconcile(
            string driverName,
            DependencyManifest manifest,
            CapabilityProbeResult probeResult)
        {
            var findings = new List<PoolFinding>();
            var satisfiedCount = 0;
            foreach (var entry in manifest.Entries)
            {
                if (probeResult.Contains(entry.Category, entry.Name))
                {
                    satisfiedCount++;
                    continue;
                }

                var installHint = string.IsNullOrWhiteSpace(entry.InstallCommand)
                    ? "清单没给安装命令，照来源页面自行安装"
                    : $"按来源安装：{entry.InstallCommand}";
                findings.Add(new PoolFinding(
                    $"Bridges/{driverName}/依赖清单.json",
                    $"缺依赖「{entry.Name}」（类别：{entry.Category}），来源：{entry.Source}",
                    installHint,
                    entry.Source));
            }

            return new CapabilityReconcileReport(driverName, manifest.Entries.Count, satisfiedCount, findings);
        }
    }
}
