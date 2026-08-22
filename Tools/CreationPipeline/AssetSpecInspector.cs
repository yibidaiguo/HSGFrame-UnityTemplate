using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>资产规格门禁的判定逻辑：逐份资产请求核对类型、落点、命名与规格是否合规。</summary>
    public static class AssetSpecInspector
    {
        /// <summary>
        /// 检查一个需求下全部资产请求是否符合资产规格数据，目录不存在时返回空列表。
        /// 资产规格目录自身的 Findings 一并并进返回值。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="moduleName">业务模块名，取 Specifications/Business/&lt;模块&gt;/ 的就近覆盖；留空表示只用基线与项目两层。
        /// 这个参数必须与建请求时用的那个一致，否则「按带业务层的数据建、拿不带业务层的数据校」会误判。</param>
        public static IReadOnlyList<PoolFinding> Inspect(string repositoryRoot, string requirementIdentifier, string moduleName)
        {
            var findings = new List<PoolFinding>();
            var catalog = AssetSpecCatalog.Load(repositoryRoot, moduleName ?? "");
            findings.AddRange(catalog.Findings);

            var directory = AssetPaths.AssetRequestDirectory(repositoryRoot, requirementIdentifier);
            if (!Directory.Exists(directory))
            {
                return findings;
            }

            foreach (var filePath in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                findings.AddRange(InspectOne(AssetRequest.Read(filePath), catalog, filePath));
            }

            return findings;
        }

        /// <summary>
        /// 全扫 <c>_Tasks/</c> 下全部需求，逐需求检查资产请求；目录不存在时返回空列表。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="moduleName">业务模块名，留空表示只用基线与项目两层。</param>
        public static IReadOnlyList<PoolFinding> InspectAll(string repositoryRoot, string moduleName)
        {
            var findings = new List<PoolFinding>();
            var tasksDirectory = Path.Combine(repositoryRoot, "_Tasks");
            if (!Directory.Exists(tasksDirectory))
            {
                return findings;
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(tasksDirectory))
            {
                var requirementIdentifier = Path.GetFileName(directoryPath);
                findings.AddRange(Inspect(repositoryRoot, requirementIdentifier, moduleName));
            }

            return findings;
        }

        /// <summary>逐条核对一份资产请求：类型、落点、命名、规格键与规格值。</summary>
        private static IReadOnlyList<PoolFinding> InspectOne(AssetRequest request, AssetSpecCatalog catalog, string filePath)
        {
            var findings = new List<PoolFinding>();
            var spec = catalog.Find(request.AssetType);
            if (spec == null)
            {
                findings.Add(new PoolFinding(
                    filePath,
                    $"资产类型「{request.AssetType}」不在资产规格数据里",
                    "在 Specifications/Project/asset-spec.json 里加这个类型，或改成已有类型",
                    "Specifications/Project/asset-spec.json"));
                return findings;
            }

            if (!string.Equals(request.Destination, spec.Destination, StringComparison.Ordinal))
            {
                findings.Add(new PoolFinding(
                    filePath,
                    $"落点「{request.Destination}」与资产规格数据的「{spec.Destination}」不一致",
                    "落点由资产规格数据定，不由 brief 即兴",
                    "Specifications/Project/asset-spec.json"));
            }

            if (spec.NamingPattern.Length > 0 && !MatchesNamingPattern(request.NamingText, spec.NamingPattern))
            {
                findings.Add(new PoolFinding(
                    filePath,
                    $"命名「{request.NamingText}」不匹配该类型的命名模式「{spec.NamingPattern}」",
                    "按该类型的命名模式改命名",
                    "Specifications/Project/asset-spec.json"));
            }

            var overridable = new HashSet<string>(spec.OverridableKeys, StringComparer.Ordinal);
            foreach (var pair in request.Specification)
            {
                var fullKey = "规格." + pair.Key;
                if (!spec.Values.TryGetValue(fullKey, out var dataValue))
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        $"规格里的「{pair.Key}」不在该类型的规格数据里",
                        "去掉这个键，或先在资产规格数据里声明它",
                        "Specifications/Project/asset-spec.json"));
                    continue;
                }

                // 「可覆盖」清单里的键**随便改**——那正是「可覆盖」这三个字的意思。
                // 从前这里不看清单，一律按「只许收紧」判，于是「宽」从 1080 改成 1920
                // 被判成「放宽」。可宽高不是上限，是**目标值**：1920 不比 1080 宽松，
                // 它就是另一个尺寸；把它当上限比大小，就会得出「PC 界面比手机界面宽松」这种结论。
                // 清单之外的键才继续按「只许收紧」判——那些确实多是上限（最大面数、贴图尺寸…）。
                if (overridable.Contains(fullKey))
                {
                    continue;
                }

                if (AssetSpecCatalog.IsLoosening(dataValue, pair.Value))
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        $"规格「{pair.Key}」被 brief 从「{dataValue}」放宽成「{pair.Value}」；brief 只能收紧不能放宽",
                        "把规格值改回不宽于资产规格数据的值",
                        "Specifications/Project/asset-spec.json"));
                }
            }

            return findings;
        }

        /// <summary>命名文本是否匹配该类型的命名模式；模式非法时视为不匹配并静默跳过（数据问题不炸门禁）。</summary>
        private static bool MatchesNamingPattern(string namingText, string pattern)
        {
            try
            {
                return Regex.IsMatch(namingText ?? "", pattern);
            }
            catch (ArgumentException)
            {
                return true;
            }
        }
    }
}
