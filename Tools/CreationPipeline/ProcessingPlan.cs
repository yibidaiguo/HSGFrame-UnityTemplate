using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>加工计划里的一步：步骤名、是否启用、参数表与禁用原因。</summary>
    public sealed class ProcessingStep
    {
        /// <summary>
        /// 构造加工计划的一步。
        /// </summary>
        /// <param name="name">步骤名，如「导入」。</param>
        /// <param name="isEnabled">是否启用；禁用只是标记，步骤对象仍在计划里。</param>
        /// <param name="parameters">参数表，键按序数序排序。</param>
        /// <param name="skipReason">禁用时的中文原因；启用时为空串。</param>
        public ProcessingStep(string name, bool isEnabled, IReadOnlyDictionary<string, string> parameters, string skipReason)
        {
            Name = name ?? "";
            IsEnabled = isEnabled;
            SkipReason = skipReason ?? "";

            var ordered = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (parameters != null)
            {
                foreach (var pair in parameters)
                {
                    ordered[pair.Key] = pair.Value ?? "";
                }
            }

            Parameters = ordered;
        }

        /// <summary>步骤名，如「导入」。</summary>
        public string Name { get; }

        /// <summary>是否启用；禁用只是标记，步骤对象仍在计划里。</summary>
        public bool IsEnabled { get; }

        /// <summary>参数表，键按序数序排序。</summary>
        public IReadOnlyDictionary<string, string> Parameters { get; }

        /// <summary>禁用时的中文原因；启用时为空串。</summary>
        public string SkipReason { get; }
    }

    /// <summary>
    /// 加工计划：同一个资产请求 + 同一份规格数据，必须产出逐字节相同的计划。
    /// 步骤恒为八个、顺序恒定，禁用只是标记不删除——计划的形状稳定，加工幂等才有落点。
    /// </summary>
    public sealed class ProcessingPlan
    {
        /// <summary>
        /// 构造一份加工计划。
        /// </summary>
        /// <param name="assetIdentifier">资产 id，如「ASSET-0042-01」。</param>
        /// <param name="assetType">资产类型，如「道具模型」。</param>
        /// <param name="steps">八个加工步骤，顺序恒定。</param>
        /// <param name="findings">构建过程中发现的违规。</param>
        public ProcessingPlan(
            string assetIdentifier,
            string assetType,
            IReadOnlyList<ProcessingStep> steps,
            IReadOnlyList<PoolFinding> findings)
        {
            AssetIdentifier = assetIdentifier ?? "";
            AssetType = assetType ?? "";
            Steps = steps ?? Array.Empty<ProcessingStep>();
            Findings = findings ?? Array.Empty<PoolFinding>();
        }

        /// <summary>资产 id，如「ASSET-0042-01」。</summary>
        public string AssetIdentifier { get; }

        /// <summary>资产类型，如「道具模型」。</summary>
        public string AssetType { get; }

        /// <summary>八个加工步骤，顺序恒定。</summary>
        public IReadOnlyList<ProcessingStep> Steps { get; }

        /// <summary>构建过程中发现的违规。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }

        /// <summary>
        /// 把计划序列化成 UTF-8 语义、缩进 2、键顺序恒定的 JSON 文本。
        /// 同样的输入必须产逐字节相同的文本，任何地方都不写时间戳、随机数或绝对路径。
        /// </summary>
        public string ToJsonText()
        {
            var root = new JsonObject
            {
                ["资产id"] = AssetIdentifier,
                ["资产类型"] = AssetType,
                ["步骤"] = BuildStepsNode(),
                ["发现"] = BuildFindingsNode()
            };

            return JsonSerializer.Serialize(root, WriteOptions);
        }

        /// <summary>把八个步骤序列化成 JSON 数组，顺序即列表顺序。</summary>
        private JsonArray BuildStepsNode()
        {
            var steps = new JsonArray();
            foreach (var step in Steps)
            {
                var parameters = new JsonObject();
                foreach (var pair in step.Parameters)
                {
                    parameters[pair.Key] = pair.Value;
                }

                steps.Add(new JsonObject
                {
                    ["名称"] = step.Name,
                    ["启用"] = step.IsEnabled,
                    ["参数"] = parameters,
                    ["跳过原因"] = step.SkipReason
                });
            }

            return steps;
        }

        /// <summary>把构建发现序列化成 JSON 数组：位置 / 原因 / 修复 / 参考 四要素。</summary>
        private JsonArray BuildFindingsNode()
        {
            var findings = new JsonArray();
            foreach (var finding in Findings)
            {
                findings.Add(new JsonObject
                {
                    ["位置"] = finding.Location,
                    ["原因"] = finding.Reason,
                    ["修复"] = finding.FixAction,
                    ["参考"] = finding.ReferenceExamplePath
                });
            }

            return findings;
        }

        /// <summary>
        /// 写盘用序列化选项：以 JsonSerializerOptions.Default 为基类，中文原样输出、缩进 2。
        /// </summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}
