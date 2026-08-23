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

        /// <summary>
        /// 落点合不合规：等于规格落点，或者在它下面**再加一层模块目录**。
        ///
        /// 为什么允许多一层：《结构规范-资源》的层级公式是「类型 → 功能 → 模块 → 内容」，
        /// 例子写着 <c>Art/Texture/Ui/Inventory/T_背包格子.png</c>——模块那一层本来就该有。
        /// 只认精确相等的话，所有界面的图都得平铺在功能层下，
        /// 几个界面拆下来就是几百张挤在一起，而图集是按模块建的（一个模块一图集），
        /// 分不出模块就分不出图集。
        ///
        /// **只放行一层**：再深就不是「模块」而是随手建的子目录了，那正是这道门禁要拦的。
        /// 模块名也得是 ASCII 且不含分隔符——全仓路径只许 ASCII（gate.pathascii 是 block 档），
        /// 而带斜杠的「模块名」等于往上跳目录。
        /// </summary>
        /// <param name="destination">资产请求里写的落点。</param>
        /// <param name="specDestination">资产规格数据里的落点。</param>
        private static bool IsAllowedDestination(string destination, string specDestination)
        {
            var actual = (destination ?? "").Trim();
            var expected = (specDestination ?? "").Trim();

            if (string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return true;
            }

            var prefix = expected.EndsWith("/", StringComparison.Ordinal) ? expected : expected + "/";
            if (!actual.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var rest = actual.Substring(prefix.Length).TrimEnd('/');
            if (rest.Length == 0 || rest.Contains('/'))
            {
                return false;
            }

            foreach (var character in rest)
            {
                var isAsciiLetterOrDigit = (character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9');
                if (!isAsciiLetterOrDigit)
                {
                    return false;
                }
            }

            return true;
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

            if (!IsAllowedDestination(request.Destination, spec.Destination))
            {
                findings.Add(new PoolFinding(
                    filePath,
                    $"落点「{request.Destination}」与资产规格数据的「{spec.Destination}」不一致"
                        + "（规格落点本身，或它下面再加一层模块目录，两种都收）",
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
