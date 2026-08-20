using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 模型机检：面数、材质数、贴图规格、包围盒合理区间、骨骼数五项，一次报全部。
    /// 规格数据缺对应键时那一项跳过并出 finding，不许静默跳过——把没查的说成查过是最典型的假绿。
    /// </summary>
    public static class ModelInspector
    {
        /// <summary>模型域：机检只对模型域有意义。</summary>
        private const string ModelDomain = "资产.模型";

        /// <summary>
        /// 对一份模型度量跑机检五项，返回全部发现。
        /// 资产类型不认识或域不是模型域时各出一条 finding 并直接返回。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="request">资产请求。</param>
        /// <param name="metrics">模型度量，由加工站产出。</param>
        /// <param name="moduleName">业务模块名，取 规范/业务/&lt;模块&gt;/ 的就近覆盖；留空表示只用基线与项目两层。</param>
        public static IReadOnlyList<PoolFinding> Inspect(string repositoryRoot, AssetRequest request, ModelMetrics metrics, string moduleName)
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
                    $"资产类型「{request.AssetType}」不在资产规格数据里，机检无法进行",
                    "在资产规格数据里加这个类型，或改成已有类型",
                    "规范/基线/资产规格.基线.json"));
                fatal = true;
            }

            if (!string.Equals(request.Domain, ModelDomain, StringComparison.Ordinal))
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    $"域「{request.Domain}」不是「{ModelDomain}」，机检只对模型域有意义",
                    "把资产请求的域改成资产.模型，或换一份模型资产请求",
                    "规范/基线/资产规格.基线.json"));
                fatal = true;
            }

            if (fatal)
            {
                return findings;
            }

            foreach (var missingName in metrics.MissingFieldNames)
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    $"模型度量缺「{missingName}」，该项按 0 处理",
                    "让加工站把「" + missingName + "」算出来再重新机检",
                    "模型度量文件"));
            }

            var values = spec.Values;
            CheckIntegerExceed(values, "最大面数", metrics.TriangleCount, "面数", requestIdentifier, findings);
            CheckIntegerExceed(values, "最大材质数", metrics.MaterialCount, "材质数", requestIdentifier, findings);
            CheckIntegerExceed(values, "贴图尺寸", metrics.TextureSize, "贴图尺寸", requestIdentifier, findings);
            CheckBoundingBox(values, metrics, requestIdentifier, findings);
            CheckIntegerExceed(values, "最大骨骼数", metrics.BoneCount, "骨骼数", requestIdentifier, findings);

            return findings;
        }

        /// <summary>机检数值项：度量值超过规格上限时出一条 finding，文案带实际值与上限。</summary>
        private static void CheckIntegerExceed(
            IReadOnlyDictionary<string, string> values,
            string specKey,
            int actual,
            string displayName,
            string requestIdentifier,
            List<PoolFinding> findings)
        {
            var limit = ReadDecimalSpecValue(values, "规格." + specKey);
            if (limit == null)
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    $"规格数据缺「{specKey}」，{displayName}这一项没查",
                    "在资产规格数据的该类型下补 规格." + specKey,
                    "规范/基线/资产规格.基线.json"));
                return;
            }

            if ((decimal)actual > limit.Value)
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    $"{displayName} {actual} 超过上限 {TrimDecimal(limit.Value)}",
                    "减面或换用更简单的模型，使该值不高于规格上限",
                    "规范/基线/资产规格.基线.json"));
            }
        }

        /// <summary>机检包围盒：逐轴超过上限各出一条；三轴全零出一条，理由点出「全零」。</summary>
        private static void CheckBoundingBox(
            IReadOnlyDictionary<string, string> values,
            ModelMetrics metrics,
            string requestIdentifier,
            List<PoolFinding> findings)
        {
            var limit = ReadDecimalSpecValue(values, "规格.包围盒上限米");
            if (limit == null)
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    "规格数据缺「包围盒上限米」，包围盒合理区间这一项没查",
                    "在资产规格数据的该类型下补 规格.包围盒上限米",
                    "规范/基线/资产规格.基线.json"));
                return;
            }

            var axes = new (string Name, decimal Value)[]
            {
                ("x", metrics.BoundingBoxX),
                ("y", metrics.BoundingBoxY),
                ("z", metrics.BoundingBoxZ)
            };

            if (metrics.BoundingBoxX == 0m && metrics.BoundingBoxY == 0m && metrics.BoundingBoxZ == 0m)
            {
                findings.Add(new PoolFinding(
                    requestIdentifier,
                    $"包围盒全零，八成是加工站没算出来；上限是 {TrimDecimal(limit.Value)} 米",
                    "让加工站把包围盒算出来再重新机检，别拿全零当真值进 Unity",
                    "模型度量文件"));
                return;
            }

            foreach (var axis in axes)
            {
                if (axis.Value > limit.Value)
                {
                    findings.Add(new PoolFinding(
                        requestIdentifier,
                        $"包围盒 {axis.Name} 轴 {TrimDecimal(axis.Value)} 米超过上限 {TrimDecimal(limit.Value)} 米",
                        "缩放模型或改规格，别让大一百倍的模型进 Unity",
                        "规范/基线/资产规格.基线.json"));
                }
            }
        }

        /// <summary>读规格数据的数值键；键缺失、解析失败返回 null，字符串值去掉 JSON 引号。</summary>
        private static decimal? ReadDecimalSpecValue(IReadOnlyDictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out var raw))
            {
                return null;
            }

            var text = raw;
            try
            {
                var node = JsonNode.Parse(raw);
                if (node is JsonValue value && value.TryGetValue<string>(out var stringText))
                {
                    text = stringText;
                }
            }
            catch (JsonException)
            {
                // 解析失败按原始文本尝试解析数字，仍失败返回 null。
            }

            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            return null;
        }

        /// <summary>把 decimal 按最简形式转文本：整数值不带小数点，避免「5.0 米」这类怪文案。</summary>
        private static string TrimDecimal(decimal value)
        {
            return value == decimal.Truncate(value) ? ((long)value).ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
