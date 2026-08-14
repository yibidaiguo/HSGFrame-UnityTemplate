using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Toolkit.Gates
{
    /// <summary>改动文件路径白名单校验：判断改动路径是否落在声明的前缀白名单内。</summary>
    public static class FileWhitelistChecker
    {
        /// <summary>
        /// 校验改动路径列表是否全部落在白名单前缀内。
        /// </summary>
        /// <param name="changedPaths">改动文件路径列表。</param>
        /// <param name="configuration">门禁配置。</param>
        public static IReadOnlyList<GateFinding> Check(IEnumerable<string> changedPaths, GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            var whitelist = (configuration.ChangedPathWhitelist ?? Array.Empty<string>())
                .Select(NormalizeSlashes)
                .ToList();

            // 空名单表示不限制：模板自己就是仓库根时（独立模板仓库），整棵树都是产出区，
            // 再列白名单等于把每个顶层目录抄一遍，没有信息量。
            if (whitelist.Count == 0)
            {
                return findings;
            }

            foreach (var path in changedPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var normalized = NormalizeSlashes(path.Trim());
                var allowed = whitelist.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (!allowed)
                {
                    findings.Add(new GateFinding(
                        normalized,
                        "改动路径落在白名单之外",
                        "把改动限定在任务书声明的白名单目录内",
                        "Template/Tools/Gates/Config/gate-config.json"));
                }
            }

            return findings;
        }

        private static string NormalizeSlashes(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
