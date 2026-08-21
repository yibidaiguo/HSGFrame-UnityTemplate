using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 加工计划构建：从资产请求 + 规格数据算出八步加工参数。
    /// 加工计划是加工报告的输入，也是「加工幂等」的落点——同一个资产请求 + 同一份规格数据，
    /// 必须产出逐字节相同的计划。八步固定、顺序固定，禁用只是标记不删除。
    /// </summary>
    public static class ProcessingPlanBuilder
    {
        /// <summary>模型域：加工计划只对模型域有意义。</summary>
        private const string ModelDomain = "资产.模型";

        /// <summary>八步固定顺序：子文档 06 §三·3 的原顺序。</summary>
        private static readonly string[] StepNames =
        {
            "导入", "统一单位", "pivot归位", "减面", "UV", "烘法线", "命名", "导出"
        };

        /// <summary>
        /// 从资产请求与规格数据构建一份加工计划。
        /// 资产类型不认识或域不是模型域时，八步全部禁用并各出一条 finding。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="request">资产请求。</param>
        /// <param name="moduleName">业务模块名，取 Specifications/Business/&lt;模块&gt;/ 的就近覆盖；留空表示只用基线与项目两层。</param>
        public static ProcessingPlan Build(string repositoryRoot, AssetRequest request, string moduleName)
        {
            var findings = new List<PoolFinding>();
            var catalog = AssetSpecCatalog.Load(repositoryRoot, moduleName ?? "");
            var spec = catalog.Find(request.AssetType);
            var requestIdentifier = string.IsNullOrWhiteSpace(request.Identifier) ? "资产请求" : request.Identifier;

            var fatal = false;
            if (spec == null)
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    $"资产类型「{request.AssetType}」不在资产规格数据里，加工计划无法构建",
                    "在资产规格数据里加这个类型，或改成已有类型",
                    "Specifications/Baseline/asset-spec.baseline.json"));
                fatal = true;
            }

            if (!string.Equals(request.Domain, ModelDomain, StringComparison.Ordinal))
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    $"域「{request.Domain}」不是「{ModelDomain}」，加工计划只对模型域有意义",
                    "把资产请求的域改成资产.模型，或换一份模型资产请求",
                    "Specifications/Baseline/asset-spec.baseline.json"));
                fatal = true;
            }

            if (fatal)
            {
                var skipReason = spec == null ? "资产类型不认识" : "加工计划只对模型域有意义";
                return new ProcessingPlan(
                    request.Identifier,
                    request.AssetType,
                    BuildAllDisabledSteps(skipReason),
                    findings);
            }

            // 规格值优先取资产请求里的那一份，请求没有该键时才回落到规格目录。
            // 请求里的规格是 art.request 按规格数据填的、且只允许被 brief 收紧
            // （P2 批次 2 那套「收紧任意、放宽受限」，放宽的值在建请求时就没被采纳）。
            // 只读目录的话，收紧过的值永远到不了加工步骤——那条链路会在这里断掉。
            var values = MergeRequestSpecification(spec.Values, request.Specification);
            var steps = new List<ProcessingStep>(StepNames.Length)
            {
                new ProcessingStep("导入", true, new Dictionary<string, string>
                {
                    ["源格式"] = ReadSpecValue(values, "规格.格式") ?? ""
                }, ""),
                BuildConditionalStep("统一单位", "单位", "目标单位", values, requestIdentifier, findings),
                BuildConditionalStep("pivot归位", "轴心", "pivot", values, requestIdentifier, findings),
                BuildConditionalStep("减面", "最大面数", "目标面数", values, requestIdentifier, findings),
                new ProcessingStep("UV", true, new Dictionary<string, string>
                {
                    ["通道"] = "0"
                }, ""),
                new ProcessingStep("烘法线", false, new Dictionary<string, string>(), "基线未开启，需按资产类型显式配置"),
                BuildNamingStep(values, request, requestIdentifier, findings),
                BuildExportStep(values, request, requestIdentifier, findings)
            };

            return new ProcessingPlan(request.Identifier, request.AssetType, steps, findings);
        }

        /// <summary>
        /// 把资产请求里的规格覆盖到规格目录的扁平表上：目录的键形如「规格.最大面数」，
        /// 请求的键是不带前缀的「最大面数」。请求里有的键以请求为准，没有的沿用目录。
        /// 请求的规格为空时原样返回目录。
        /// </summary>
        /// <param name="catalogValues">规格目录合并出来的扁平值表。</param>
        /// <param name="requestSpecification">资产请求里的规格，键不带「规格.」前缀。</param>
        private static IReadOnlyDictionary<string, string> MergeRequestSpecification(
            IReadOnlyDictionary<string, string> catalogValues,
            IReadOnlyDictionary<string, string> requestSpecification)
        {
            if (requestSpecification == null || requestSpecification.Count == 0)
            {
                return catalogValues;
            }

            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in catalogValues)
            {
                merged[pair.Key] = pair.Value;
            }

            foreach (var pair in requestSpecification)
            {
                merged["规格." + pair.Key] = pair.Value;
            }

            return merged;
        }

        /// <summary>八步全禁用的计划：类型不认识或域不对时用，形状与正常计划一致。</summary>
        private static IReadOnlyList<ProcessingStep> BuildAllDisabledSteps(string skipReason)
        {
            var steps = new List<ProcessingStep>(StepNames.Length);
            foreach (var name in StepNames)
            {
                steps.Add(new ProcessingStep(name, false, new Dictionary<string, string>(), skipReason));
            }

            return steps;
        }

        /// <summary>构建一个启用条件 = 规格数据里有某个键的步骤；缺键时禁用并出 finding。</summary>
        private static ProcessingStep BuildConditionalStep(
            string stepName,
            string specKey,
            string parameterKey,
            IReadOnlyDictionary<string, string> values,
            string requestIdentifier,
            List<PoolFinding> findings)
        {
            var value = ReadSpecValue(values, "规格." + specKey);
            if (value == null)
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    $"规格数据缺「{specKey}」，步骤「{stepName}」禁用",
                    "在资产规格数据的该类型下补 规格." + specKey,
                    "Specifications/Baseline/asset-spec.baseline.json"));
                return new ProcessingStep(stepName, false, new Dictionary<string, string>(), $"规格数据缺「{specKey}」");
            }

            return new ProcessingStep(stepName, true, new Dictionary<string, string>
            {
                [parameterKey] = value
            }, "");
        }

        /// <summary>构建「命名」步骤：需要规格数据的命名模式与请求的命名。</summary>
        private static ProcessingStep BuildNamingStep(
            IReadOnlyDictionary<string, string> values,
            AssetRequest request,
            string requestIdentifier,
            List<PoolFinding> findings)
        {
            var pattern = ReadSpecValue(values, "命名模式");
            if (pattern == null)
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    "规格数据缺「命名模式」，步骤「命名」禁用",
                    "在资产规格数据的该类型下补 命名模式",
                    "Specifications/Baseline/asset-spec.baseline.json"));
                return new ProcessingStep("命名", false, new Dictionary<string, string>(), "规格数据缺「命名模式」");
            }

            return new ProcessingStep("命名", true, new Dictionary<string, string>
            {
                ["命名模式"] = pattern,
                ["命名"] = request.NamingText ?? ""
            }, "");
        }

        /// <summary>构建「导出」步骤：格式来自规格数据、落点来自请求。</summary>
        private static ProcessingStep BuildExportStep(
            IReadOnlyDictionary<string, string> values,
            AssetRequest request,
            string requestIdentifier,
            List<PoolFinding> findings)
        {
            var format = ReadSpecValue(values, "规格.格式");
            if (format == null)
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    "规格数据缺「格式」，导出步骤的格式参数为空",
                    "在资产规格数据的该类型下补 规格.格式",
                    "Specifications/Baseline/asset-spec.baseline.json"));
            }

            return new ProcessingStep("导出", true, new Dictionary<string, string>
            {
                ["格式"] = format ?? "",
                ["落点"] = request.Destination ?? ""
            }, "");
        }

        /// <summary>
        /// 读规格数据的扁平化键值：字符串值去掉 JSON 引号，数字与布尔保留原始文本。
        /// 键不存在返回 null；JSON 解析失败时原样返回原始文本。
        /// </summary>
        private static string ReadSpecValue(IReadOnlyDictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out var raw))
            {
                return null;
            }

            try
            {
                var node = JsonNode.Parse(raw);
                if (node is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    return text;
                }

                return raw;
            }
            catch (JsonException)
            {
                return raw;
            }
        }
    }
}
