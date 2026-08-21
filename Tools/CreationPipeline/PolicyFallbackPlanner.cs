using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一次策略回落计划的结果：真的要改成「人审」的键、本来就是「人审」不用动的键，
    /// 以及每个键一条的中文说明。
    /// </summary>
    public sealed class PolicyFallbackResult
    {
        /// <summary>
        /// 构造一份策略回落计划结果。
        /// </summary>
        /// <param name="changedKeys">真的要改成「人审」的键，序数序。</param>
        /// <param name="alreadyManualKeys">本来就是「人审」、不用动的键，序数序。</param>
        /// <param name="notes">中文说明，每个键一条。</param>
        internal PolicyFallbackResult(
            IReadOnlyList<string> changedKeys,
            IReadOnlyList<string> alreadyManualKeys,
            IReadOnlyList<string> notes)
        {
            ChangedKeys = changedKeys ?? Array.Empty<string>();
            AlreadyManualKeys = alreadyManualKeys ?? Array.Empty<string>();
            Notes = notes ?? Array.Empty<string>();
        }

        /// <summary>真的要改成「人审」的键，序数序。</summary>
        public IReadOnlyList<string> ChangedKeys { get; }

        /// <summary>本来就是「人审」、不用动的键，序数序。</summary>
        public IReadOnlyList<string> AlreadyManualKeys { get; }

        /// <summary>中文说明，每个键一条。</summary>
        public IReadOnlyList<string> Notes { get; }
    }

    /// <summary>
    /// 策略回落规划器：抽查发现问题后，把出问题那条改动所属的策略键从「自动放行」回落成「人审」。
    /// 回落方向永远是收紧（自动放行 → 人审），照锁定决策 37 这在任意层都合法，所以不需要查
    /// 「可覆盖」清单；反过来这个类永远不许把任何键写成「自动放行」。只写项目层，永不写基线——
    /// 基线是底线，回落去改基线会把「数据推不翻基线」这条弄脏。
    /// </summary>
    public static class PolicyFallbackPlanner
    {
        private const string AutomaticRelease = "自动放行";
        private const string ManualReview = "人审";

        /// <summary>
        /// 规划策略回落：纯函数，不写盘。对每个 scope 组出键「&lt;风险级&gt;.&lt;范围&gt;」，
        /// 用 catalog.Decide 查现值：现值是「自动放行」→ 进 ChangedKeys；现值已经是「人审」
        /// → 进 AlreadyManualKeys。grade 或 scopes 为空 → 三个列表全空。
        /// </summary>
        /// <param name="catalog">放行策略目录。</param>
        /// <param name="grade">风险级：低 / 常规 / 高。</param>
        /// <param name="scopes">本次改动涉及的范围。</param>
        public static PolicyFallbackResult Plan(ReleasePolicyCatalog catalog, string grade, IReadOnlyList<string> scopes)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(grade) || scopes == null || scopes.Count == 0)
            {
                return new PolicyFallbackResult(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            }

            var changedKeys = new List<string>();
            var alreadyManualKeys = new List<string>();
            var notes = new List<string>();

            foreach (var scope in scopes)
            {
                var key = grade + "." + scope;
                if (string.Equals(catalog.Decide(grade, scope), AutomaticRelease, StringComparison.Ordinal))
                {
                    changedKeys.Add(key);
                    notes.Add($"{key}：从「自动放行」回落成「人审」");
                }
                else
                {
                    alreadyManualKeys.Add(key);
                    notes.Add($"{key}：本来就是「人审」，不用动");
                }
            }

            changedKeys.Sort(StringComparer.Ordinal);
            alreadyManualKeys.Sort(StringComparer.Ordinal);
            notes.Sort(StringComparer.Ordinal);

            return new PolicyFallbackResult(changedKeys, alreadyManualKeys, notes);
        }

        /// <summary>
        /// 应用策略回落计划：把 plan.ChangedKeys 里的每个键写成「人审」，落到项目层文件
        /// Specifications/Project/release-policy.json（合并写，不是覆盖写——只改「策略」对象里这几个键，
        /// 文件里其余的键与其余顶层字段一字不动；文件不存在就建一个只含「策略」的最小文档）。
        /// ChangedKeys 为空 → 什么都不写，返回空列表。永远只写「人审」，永不写「自动放行」。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="plan">策略回落计划。</param>
        public static IReadOnlyList<string> Apply(string repositoryRoot, PolicyFallbackResult plan)
        {
            if (plan == null || plan.ChangedKeys.Count == 0)
            {
                return Array.Empty<string>();
            }

            var filePath = SpecificationPaths.ProjectReleasePolicyFile(repositoryRoot);

            JsonObject root;
            if (File.Exists(filePath))
            {
                var parsed = JsonNode.Parse(File.ReadAllText(filePath));
                if (parsed is not JsonObject parsedRoot)
                {
                    throw new InvalidOperationException($"项目放行策略文件顶层必须是对象：{filePath}");
                }

                root = parsedRoot;
            }
            else
            {
                root = new JsonObject();
            }

            if (root["策略"] is not JsonObject policies)
            {
                policies = new JsonObject();
                root["策略"] = policies;
            }

            var appliedKeys = new List<string>();
            foreach (var key in plan.ChangedKeys)
            {
                policies[key] = ManualReview;
                appliedKeys.Add(key);
            }

            appliedKeys.Sort(StringComparer.Ordinal);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return appliedKeys;
        }

        /// <summary>写盘选项：以 Default 为基类（.NET 10 下裸构造序列化含字符串元素的 JsonObject 会抛），缩进 + 不转义中文。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        private static JsonSerializerOptions CreateWriteOptions()
        {
            return new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }
    }
}
