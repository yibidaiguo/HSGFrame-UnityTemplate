using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>意见库里的一条打回意见：问题类别 / 模块 / 可规则化性 / 原文引用 / 时间。</summary>
    public sealed class ReviewOpinion
    {
        /// <summary>
        /// 构造一条打回意见。
        /// </summary>
        /// <param name="identifier">意见 id，形如 OP-0001。</param>
        /// <param name="category">问题类别，如「空引用未防」。</param>
        /// <param name="moduleName">涉及模块，如「签到」。</param>
        /// <param name="rulability">可规则化性：可代码化 / 可提示词化 / 不可规则化。</param>
        /// <param name="quotation">原文引用，打回意见里的一句话。</param>
        /// <param name="moment">记录时刻，ISO 8601 字符串。</param>
        public ReviewOpinion(
            string identifier,
            string category,
            string moduleName,
            string rulability,
            string quotation,
            string moment)
        {
            Identifier = identifier ?? "";
            Category = category ?? "";
            ModuleName = moduleName ?? "";
            Rulability = rulability ?? "";
            Quotation = quotation ?? "";
            Moment = moment ?? "";
        }

        /// <summary>意见 id，形如 OP-0001。</summary>
        public string Identifier { get; }

        /// <summary>问题类别，如「空引用未防」。</summary>
        public string Category { get; }

        /// <summary>涉及模块，如「签到」。</summary>
        public string ModuleName { get; }

        /// <summary>可规则化性：可代码化 / 可提示词化 / 不可规则化。</summary>
        public string Rulability { get; }

        /// <summary>原文引用，打回意见里的一句话。</summary>
        public string Quotation { get; }

        /// <summary>记录时刻，ISO 8601 字符串。</summary>
        public string Moment { get; }
    }

    /// <summary>
    /// 意见库（Pools/ReviewOpinions/）：协作层的沉淀闭环账本，只追加、不许改写已有条目。
    /// 每条意见一个 OP-xxxx.json 文件；目录不存在是正常状态（空意见库），Load 返回空库不报错。
    /// </summary>
    public sealed class ReviewOpinionBook
    {
        /// <summary>可规则化性的三个合法值，顺序即从最严到最松（晋升提案平票时取更严的那个）。</summary>
        public static readonly string[] AllowedRulabilityValues = { "可代码化", "可提示词化", "不可规则化" };

        private readonly IReadOnlyList<ReviewOpinion> _opinions;

        /// <summary>
        /// 构造一份意见库视图。
        /// </summary>
        /// <param name="opinions">全部意见，按 id 序数序。</param>
        /// <param name="loadFailureReason">加载失败原因，正常时为空串。</param>
        public ReviewOpinionBook(IReadOnlyList<ReviewOpinion> opinions, string loadFailureReason)
        {
            _opinions = opinions ?? Array.Empty<ReviewOpinion>();
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>全部意见，按 id 序数序排列。</summary>
        public IReadOnlyList<ReviewOpinion> Opinions
        {
            get { return _opinions; }
        }

        /// <summary>加载失败原因；正常（含空库）为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 从池根加载意见库：逐文件读 &lt;池根&gt;/审查意见/OP-xxxx.json。
        /// 目录不存在返回空库、原因空串（空意见库是正常状态）；单个坏文件跳过并累加原因到
        /// LoadFailureReason，不让一份坏条目把整库读没。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static ReviewOpinionBook Load(string poolRoot)
        {
            var directory = PoolPaths.ReviewOpinionDirectory(poolRoot);
            if (!Directory.Exists(directory))
            {
                return new ReviewOpinionBook(Array.Empty<ReviewOpinion>(), "");
            }

            var opinions = new List<ReviewOpinion>();
            var failures = new List<string>();
            foreach (var filePath in Directory.EnumerateFiles(directory, "OP-*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(filePath));
                    if (root is not JsonObject entryObject)
                    {
                        failures.Add($"{Path.GetFileName(filePath)}：顶层不是对象，已跳过");
                        continue;
                    }

                    if (!TryReadOpinion(entryObject, out var opinion, out var failureReason))
                    {
                        failures.Add($"{Path.GetFileName(filePath)}：{failureReason}，已跳过");
                        continue;
                    }

                    opinions.Add(opinion);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    failures.Add($"{Path.GetFileName(filePath)}：{exception.Message}，已跳过");
                }
            }

            opinions.Sort((left, right) => string.CompareOrdinal(left.Identifier, right.Identifier));
            var reason = failures.Count == 0 ? "" : string.Join("；", failures);
            return new ReviewOpinionBook(opinions, reason);
        }

        /// <summary>
        /// 往意见库追加一条意见：扫现存最大号 +1，写一个新文件。只追加，绝不改写已有条目。
        /// 可规则化性不在三个合法值里时抛 InvalidOperationException，文案列出全部合法值。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="category">问题类别，如「空引用未防」。</param>
        /// <param name="moduleName">涉及模块，如「签到」。</param>
        /// <param name="rulability">可规则化性：可代码化 / 可提示词化 / 不可规则化。</param>
        /// <param name="quotation">原文引用，打回意见里的一句话。</param>
        /// <param name="moment">记录时刻，ISO 8601 字符串。</param>
        public static ReviewOpinion Append(
            string poolRoot,
            string category,
            string moduleName,
            string rulability,
            string quotation,
            string moment)
        {
            if (!IsAllowedRulability(rulability))
            {
                throw new InvalidOperationException(
                    $"可规则化性「{rulability}」不合法；合法值是：{string.Join("、", AllowedRulabilityValues)}");
            }

            var directory = PoolPaths.ReviewOpinionDirectory(poolRoot);
            Directory.CreateDirectory(directory);
            var nextNumber = ScanNextNumber(directory);
            var identifier = "OP-" + nextNumber.ToString().PadLeft(4, '0');
            var filePath = Path.Combine(directory, identifier + ".json");

            var content = new JsonObject
            {
                ["id"] = identifier,
                ["问题类别"] = category,
                ["模块"] = moduleName,
                ["可规则化性"] = rulability,
                ["原文引用"] = quotation,
                ["时间"] = moment
            };

            File.WriteAllText(filePath, content.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return new ReviewOpinion(identifier, category, moduleName, rulability, quotation, moment);
        }

        /// <summary>可规则化性是否在三个合法值里。</summary>
        /// <param name="rulability">可规则化性值。</param>
        public static bool IsAllowedRulability(string rulability)
        {
            foreach (var allowed in AllowedRulabilityValues)
            {
                if (string.Equals(rulability, allowed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>写盘选项：以 Default 为基类（.NET 10 下裸构造序列化含字符串元素的 JsonObject 会抛），缩进 + 不转义中文。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>扫意见库目录里 OP-四位数字 的最大号 +1；目录为空返回 1。</summary>
        private static int ScanNextNumber(string directory)
        {
            var maxNumber = 0;
            foreach (var filePath in Directory.EnumerateFiles(directory, "OP-*.json", SearchOption.TopDirectoryOnly))
            {
                var match = Regex.Match(Path.GetFileName(filePath), "^OP-(\\d{4})\\.json$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > maxNumber)
                {
                    maxNumber = number;
                }
            }

            return maxNumber + 1;
        }

        /// <summary>读一条意见；id 缺失或类型不对算坏文件，其余字段宽松读（门禁负责查空）。</summary>
        private static bool TryReadOpinion(JsonObject obj, out ReviewOpinion opinion, out string failureReason)
        {
            opinion = null;
            failureReason = "";

            if (!TryReadString(obj, "id", out var identifier) || identifier.Length == 0)
            {
                failureReason = "缺少 id";
                return false;
            }

            opinion = new ReviewOpinion(
                identifier,
                ReadStringOrEmpty(obj, "问题类别"),
                ReadStringOrEmpty(obj, "模块"),
                ReadStringOrEmpty(obj, "可规则化性"),
                ReadStringOrEmpty(obj, "原文引用"),
                ReadStringOrEmpty(obj, "时间"));
            return true;
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
