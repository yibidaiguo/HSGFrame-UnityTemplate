using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一次冲突探测的报告：候选列表、参与比对的存量条数、加载失败原因，以及
    /// 「扫过了没有」这个关键区分——零候选与压根没扫成是两个分支（决策 42），
    /// 面板与门禁靠 Scanned 字段区分，不许把「没扫成」说成「未发现冲突」。
    /// </summary>
    public sealed class ConflictDetectionReport
    {
        /// <summary>
        /// 构造一次冲突探测报告。
        /// </summary>
        /// <param name="candidates">冲突候选，按确定性排序。</param>
        /// <param name="scannedCount">真正参与比对的存量需求条数。</param>
        /// <param name="loadFailureReason">读不动需求目录或个别文件坏时的原因；正常为空串。</param>
        /// <param name="scanned">是否扫过了：需求目录不存在或新需求不在池子里时是 false。</param>
        internal ConflictDetectionReport(
            IReadOnlyList<ConflictCandidate> candidates,
            int scannedCount,
            string loadFailureReason,
            bool scanned)
        {
            Candidates = candidates;
            ScannedCount = scannedCount;
            LoadFailureReason = loadFailureReason;
            Scanned = scanned;
        }

        /// <summary>冲突候选，按确定性排序（分数降序、旧 id 序数序、判据序数序）。</summary>
        public IReadOnlyList<ConflictCandidate> Candidates { get; }

        /// <summary>真正参与比对的存量需求条数。</summary>
        public int ScannedCount { get; }

        /// <summary>读不动需求目录或个别文件坏时的原因；正常为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>是否扫过了：需求目录不存在或新需求不在池子里时是 false。</summary>
        public bool Scanned { get; }
    }

    /// <summary>
    /// 冲突自动探测器：把一条新需求与池子里其余存量需求逐条比对，产出冲突候选。
    /// 这是个纯计算器——不写盘、不调 ConflictList.Append、不改任何需求文件
    /// （同决策 52 的道理：探测出候选和挂账是两件事，挂账要人或命令层显式做）。
    /// 三条判据全部确定性，一个随机数都不许有。
    /// </summary>
    public static class ConflictDetector
    {
        /// <summary>判据名：标题相似。</summary>
        private const string TitleSimilarReason = "标题相似";

        /// <summary>判据名：共用设计记录。</summary>
        private const string SharedDesignReason = "共用设计记录";

        /// <summary>判据名：验收标准重合。</summary>
        private const string AcceptanceOverlapReason = "验收标准重合";

        /// <summary>
        /// 探测新需求与存量需求的冲突候选。新需求 id 不在需求池里时 Scanned=false 并给原因；
        /// 个别文件坏时跳过那一份、原因累加进 LoadFailureReason，其余照常比对。
        /// 本方法一个字都不写盘。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="newRequirementIdentifier">新需求 id，形如 REQ-0042。</param>
        public static ConflictDetectionReport Detect(string poolRoot, string newRequirementIdentifier)
        {
            var requirementsDirectory = PoolPaths.RequirementsDirectory(poolRoot);
            if (!Directory.Exists(requirementsDirectory))
            {
                return new ConflictDetectionReport(
                    Array.Empty<ConflictCandidate>(),
                    0,
                    $"需求目录不存在：{requirementsDirectory}",
                    false);
            }

            RequirementData newRequirement = null;
            var requirements = new List<RequirementData>();
            var failures = new List<string>();
            foreach (var filePath in Directory.EnumerateFiles(requirementsDirectory, "REQ-*.json", SearchOption.TopDirectoryOnly))
            {
                if (!TryReadRequirement(filePath, out var requirement, out var failureReason))
                {
                    failures.Add(failureReason);
                    continue;
                }

                if (string.Equals(requirement.Identifier, newRequirementIdentifier, StringComparison.Ordinal))
                {
                    newRequirement = requirement;
                }

                requirements.Add(requirement);
            }

            if (newRequirement == null)
            {
                return new ConflictDetectionReport(
                    Array.Empty<ConflictCandidate>(),
                    0,
                    $"新需求 {newRequirementIdentifier} 不在需求池里",
                    false);
            }

            var candidates = new List<ConflictCandidate>();
            var scannedCount = 0;
            foreach (var existing in requirements)
            {
                if (string.Equals(existing.Identifier, newRequirementIdentifier, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(existing.State, "已作废", StringComparison.Ordinal))
                {
                    continue;
                }

                scannedCount++;
                ComparePair(newRequirement, existing, candidates);
            }

            candidates.Sort(CompareCandidates);
            var reason = failures.Count == 0 ? "" : string.Join("；", failures);
            return new ConflictDetectionReport(candidates, scannedCount, reason, true);
        }

        /// <summary>逐条判据：模块有交集才比标题，共用设计记录、同专项下验收标准逐字重合。命中一条产一条候选，不合并。</summary>
        private static void ComparePair(RequirementData newRequirement, RequirementData existing, List<ConflictCandidate> candidates)
        {
            if (HasCommonModule(newRequirement.Modules, existing.Modules))
            {
                var dice = TitleDice(newRequirement.Title, existing.Title);
                if (dice >= 0.0)
                {
                    candidates.Add(new ConflictCandidate(
                        existing.Identifier,
                        newRequirement.Identifier,
                        TitleSimilarReason,
                        dice,
                        $"标题 bigram 相似度 {dice.ToString("0.000")}：「{newRequirement.Title}」vs「{existing.Title}」"));
                }
            }

            var sharedDesignRecords = CommonStrings(newRequirement.DesignRecordIdentifiers, existing.DesignRecordIdentifiers);
            if (sharedDesignRecords.Count > 0)
            {
                var score = 0.5 + 0.1 * Math.Min(sharedDesignRecords.Count, 3);
                var shown = sharedDesignRecords.Count <= 3
                    ? string.Join("、", sharedDesignRecords)
                    : string.Join("、", sharedDesignRecords.GetRange(0, 3)) + "…";
                candidates.Add(new ConflictCandidate(
                    existing.Identifier,
                    newRequirement.Identifier,
                    SharedDesignReason,
                    score,
                    $"共用设计记录：{shown}"));
            }

            if (newRequirement.SpecialProject.Length > 0
                && string.Equals(newRequirement.SpecialProject, existing.SpecialProject, StringComparison.Ordinal))
            {
                var overlapped = CommonTrimmedStrings(newRequirement.AcceptanceCriteria, existing.AcceptanceCriteria);
                if (overlapped.Count > 0)
                {
                    var score = 0.6 + 0.1 * Math.Min(overlapped.Count, 3);
                    candidates.Add(new ConflictCandidate(
                        existing.Identifier,
                        newRequirement.Identifier,
                        AcceptanceOverlapReason,
                        score,
                        $"验收标准重合：「{Truncate(overlapped[0], 40)}」"));
                }
            }
        }

        /// <summary>排序：先分数降序，再旧 id 序数序，再判据序数序；绝不许依赖文件系统枚举顺序。</summary>
        private static int CompareCandidates(ConflictCandidate left, ConflictCandidate right)
        {
            var byScore = right.Score.CompareTo(left.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            var byOldIdentifier = string.CompareOrdinal(left.OldIdentifier, right.OldIdentifier);
            if (byOldIdentifier != 0)
            {
                return byOldIdentifier;
            }

            return string.CompareOrdinal(left.Reason, right.Reason);
        }

        /// <summary>两条的模块数组是否有交集；任一为空或缺失视为无交集。</summary>
        private static bool HasCommonModule(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var rightSet = new HashSet<string>(right, StringComparer.Ordinal);
            foreach (var moduleName in left)
            {
                if (rightSet.Contains(moduleName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>标题的字符 bigram Dice 系数：按出现次数取 min 的多重集交集。任一标题为空返回 -1（不比）；
        /// 非空标题即便一个 bigram 都不重合也产候选（Score 0，低档只在需求上标注，不发卡）。</summary>
        private static double TitleDice(string left, string right)
        {
            var leftBigrams = BuildBigrams(left);
            var rightBigrams = BuildBigrams(right);
            var leftTotal = TotalBigramCount(leftBigrams);
            var rightTotal = TotalBigramCount(rightBigrams);
            if (leftTotal == 0 || rightTotal == 0)
            {
                return -1.0;
            }

            var intersection = 0;
            foreach (var pair in leftBigrams)
            {
                if (rightBigrams.TryGetValue(pair.Key, out var rightCount))
                {
                    intersection += Math.Min(pair.Value, rightCount);
                }
            }

            return 2.0 * intersection / (leftTotal + rightTotal);
        }

        /// <summary>把标题拆成相邻两字的多重集；长度 1 的标题退化成该字本身一个元素。</summary>
        private static Dictionary<string, int> BuildBigrams(string title)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(title))
            {
                return result;
            }

            if (title.Length == 1)
            {
                result[title] = 1;
                return result;
            }

            for (var i = 0; i < title.Length - 1; i++)
            {
                var bigram = title.Substring(i, 2);
                result.TryGetValue(bigram, out var count);
                result[bigram] = count + 1;
            }

            return result;
        }

        /// <summary>多重集的总元素数（含重复），即全部出现次数之和。</summary>
        private static int TotalBigramCount(Dictionary<string, int> bigrams)
        {
            var total = 0;
            foreach (var pair in bigrams)
            {
                total += pair.Value;
            }

            return total;
        }

        /// <summary>两个字符串列表的集合交集，按序数序排列（先出现的优先）。</summary>
        private static List<string> CommonStrings(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var rightSet = new HashSet<string>(right, StringComparer.Ordinal);
            var common = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in left)
            {
                if (rightSet.Contains(value) && seen.Add(value))
                {
                    common.Add(value);
                }
            }

            common.Sort(StringComparer.Ordinal);
            return common;
        }

        /// <summary>两条验收标准各自 Trim 后逐字（Ordinal）相同的条目，按序数序排列。</summary>
        private static List<string> CommonTrimmedStrings(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var rightSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in right)
            {
                rightSet.Add(value == null ? "" : value.Trim());
            }

            var common = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in left)
            {
                var trimmed = value == null ? "" : value.Trim();
                if (rightSet.Contains(trimmed) && seen.Add(trimmed))
                {
                    common.Add(trimmed);
                }
            }

            common.Sort(StringComparer.Ordinal);
            return common;
        }

        /// <summary>超长文本截断：超过 maxLength 截断并加省略号，否则原样返回。</summary>
        private static string Truncate(string text, int maxLength)
        {
            return text.Length > maxLength ? text.Substring(0, maxLength) + "…" : text;
        }

        /// <summary>读一份需求文件；JSON 解析不了或缺 id 返回 false 并给原因，其余字段宽松读。</summary>
        private static bool TryReadRequirement(string filePath, out RequirementData requirement, out string failureReason)
        {
            requirement = null;
            failureReason = "";
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(filePath));
                if (root is not JsonObject obj)
                {
                    failureReason = $"{Path.GetFileName(filePath)}：顶层不是对象，已跳过";
                    return false;
                }

                if (!TryReadString(obj, "id", out var identifier) || identifier.Length == 0)
                {
                    failureReason = $"{Path.GetFileName(filePath)}：缺少 id，已跳过";
                    return false;
                }

                requirement = new RequirementData(
                    identifier,
                    ReadStringOrEmpty(obj, "标题"),
                    ReadStringArray(obj, "模块"),
                    ReadStringArray(obj, "关联设计记录"),
                    ReadStringOrEmpty(obj, "专项"),
                    ReadStringArray(obj, "验收标准"),
                    ReadStringOrEmpty(obj, "状态"));
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                failureReason = $"{Path.GetFileName(filePath)}：{exception.Message}，已跳过";
                return false;
            }
        }

        /// <summary>读必须为字符串的键；缺失、null 或类型不对返回 false。</summary>
        private static bool TryReadString(JsonObject obj, string key, out string value)
        {
            value = "";
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue)
            {
                return false;
            }

            if (jsonValue.GetValueKind() != JsonValueKind.String)
            {
                return false;
            }

            value = jsonValue.GetValue<string>() ?? "";
            return true;
        }

        /// <summary>读字符串键，缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonObject obj, string key)
        {
            return TryReadString(obj, key, out var value) ? value : "";
        }

        /// <summary>读字符串数组键；缺失、不是数组或元素类型不对时按空数组处理。</summary>
        private static IReadOnlyList<string> ReadStringArray(JsonObject obj, string key)
        {
            var result = new List<string>();
            if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
            {
                return result;
            }

            foreach (var element in array)
            {
                if (element is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String)
                {
                    result.Add(jsonValue.GetValue<string>() ?? "");
                }
            }

            return result;
        }

        /// <summary>一条需求的可比对字段：id、标题、模块、关联设计记录、专项、验收标准与状态。</summary>
        private sealed class RequirementData
        {
            /// <summary>
            /// 构造一条需求的可比对视图。
            /// </summary>
            /// <param name="identifier">需求 id。</param>
            /// <param name="title">标题。</param>
            /// <param name="modules">模块数组。</param>
            /// <param name="designRecordIdentifiers">关联设计记录 id 数组。</param>
            /// <param name="specialProject">专项 id。</param>
            /// <param name="acceptanceCriteria">验收标准数组。</param>
            /// <param name="state">状态。</param>
            internal RequirementData(
                string identifier,
                string title,
                IReadOnlyList<string> modules,
                IReadOnlyList<string> designRecordIdentifiers,
                string specialProject,
                IReadOnlyList<string> acceptanceCriteria,
                string state)
            {
                Identifier = identifier;
                Title = title;
                Modules = modules;
                DesignRecordIdentifiers = designRecordIdentifiers;
                SpecialProject = specialProject;
                AcceptanceCriteria = acceptanceCriteria;
                State = state;
            }

            /// <summary>需求 id。</summary>
            internal string Identifier { get; }

            /// <summary>标题。</summary>
            internal string Title { get; }

            /// <summary>模块数组。</summary>
            internal IReadOnlyList<string> Modules { get; }

            /// <summary>关联设计记录 id 数组。</summary>
            internal IReadOnlyList<string> DesignRecordIdentifiers { get; }

            /// <summary>专项 id。</summary>
            internal string SpecialProject { get; }

            /// <summary>验收标准数组。</summary>
            internal IReadOnlyList<string> AcceptanceCriteria { get; }

            /// <summary>状态。</summary>
            internal string State { get; }
        }
    }
}
