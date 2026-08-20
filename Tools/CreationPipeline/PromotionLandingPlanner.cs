using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次晋升落地的结果：成功与否、写出去的产物路径与给人看的原因。</summary>
    public sealed class PromotionLandingResult
    {
        /// <summary>
        /// 构造一次晋升落地结果。
        /// </summary>
        /// <param name="succeeded">是否成功。</param>
        /// <param name="artifactPath">写出去的产物路径，失败时为空串。</param>
        /// <param name="reason">中文原因，成功失败都要写。</param>
        public PromotionLandingResult(bool succeeded, string artifactPath, string reason)
        {
            Succeeded = succeeded;
            ArtifactPath = artifactPath ?? "";
            Reason = reason ?? "";
        }

        /// <summary>是否成功。</summary>
        public bool Succeeded { get; }

        /// <summary>写出去的产物路径，失败时为空串。</summary>
        public string ArtifactPath { get; }

        /// <summary>中文原因，成功失败都要写。</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// 晋升落地规划器：把一条已批准的提案真的变成产物。
    /// 只产产物、不改提案状态——把状态改成已落地是命令层的事（产物写成了但状态没跟上，
    /// 比状态先跳过去而产物没写要好查得多）。检查器去向写一份草案 Markdown；
    /// 预审规则去向往 规范/项目/预审规则.json 合并追加一条规则。
    /// </summary>
    public static class PromotionLandingPlanner
    {
        /// <summary>
        /// 把一条已批准的提案落地成产物。
        /// 检查器去向：写 &lt;仓库根&gt;/提案/检查器/&lt;问题类别&gt;.md（五节草案，短于 200 行）；
        /// 预审规则去向：往 &lt;仓库根&gt;/规范/项目/预审规则.json 合并追加一条 PRR-xxxx 规则，
        /// 同一个来源提案已在里面时不重复追加（幂等跳过，仍算成功）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="record">已批准的提案。</param>
        public static PromotionLandingResult Land(string repositoryRoot, PromotionRecord record)
        {
            if (record == null)
            {
                return new PromotionLandingResult(false, "", "提案为空；只有已批准的提案能落地");
            }

            if (!string.Equals(record.State, PromotionRecord.ApprovedState, StringComparison.Ordinal))
            {
                return new PromotionLandingResult(false, "", $"提案 {record.Identifier} 的状态是「{record.State}」；只有已批准的提案能落地");
            }

            if (string.Equals(record.TargetChannel, "检查器", StringComparison.Ordinal))
            {
                return LandCheckerDraft(repositoryRoot, record);
            }

            if (string.Equals(record.TargetChannel, "预审规则", StringComparison.Ordinal))
            {
                return LandPreReviewRule(repositoryRoot, record);
            }

            return new PromotionLandingResult(false, "", $"提案 {record.Identifier} 的晋升去向是「{record.TargetChannel}」；只认 检查器 与 预审规则");
        }

        /// <summary>把检查器去向的提案写成一份五节草案 Markdown；文件必须短于 200 行。</summary>
        private static PromotionLandingResult LandCheckerDraft(string repositoryRoot, PromotionRecord record)
        {
            var fileName = SanitizeFileName(record.Category) + ".md";
            var artifactPath = Path.Combine(SpecificationPaths.CheckerDraftDirectory(repositoryRoot), fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath));

            var moduleLine = record.ModuleNames.Count == 0
                ? "无"
                : string.Join("、", record.ModuleNames);
            var quotationLines = record.Quotations.Count == 0
                ? "- 无"
                : string.Join(Environment.NewLine, record.Quotations.Select(quotation => $"- {quotation}"));

            var content = $"# 检查器草案：{record.Category}{Environment.NewLine}"
                + $"{Environment.NewLine}"
                + $"## 来自哪条提案{Environment.NewLine}"
                + $"{Environment.NewLine}"
                + $"- 提案：{record.Identifier}{Environment.NewLine}"
                + $"- 同类条数：{record.Count}{Environment.NewLine}"
                + $"- 涉及模块：{moduleLine}{Environment.NewLine}"
                + $"{Environment.NewLine}"
                + $"## 要查什么{Environment.NewLine}"
                + $"{Environment.NewLine}"
                + $"{record.Category}{Environment.NewLine}"
                + $"（这一节要人补成可判定的判据）{Environment.NewLine}"
                + $"{Environment.NewLine}"
                + $"## 原文引用{Environment.NewLine}"
                + $"{Environment.NewLine}"
                + $"{quotationLines}{Environment.NewLine}"
                + $"{Environment.NewLine}"
                + $"## 建议接进哪道门禁{Environment.NewLine}"
                + $"{Environment.NewLine}"
                + $"建议新增 `gate.{AsciiChannelName(record.Category)}`，接进 `Tools/Gates/gate.ps1` 创作管线段末尾{Environment.NewLine}";

            File.WriteAllText(artifactPath, content, new UTF8Encoding(false));
            return new PromotionLandingResult(true, artifactPath, $"检查器草案已写出：{artifactPath}");
        }

        /// <summary>把预审规则去向的提案合并追加进 规范/项目/预审规则.json；同一个来源提案不重复追加。</summary>
        private static PromotionLandingResult LandPreReviewRule(string repositoryRoot, PromotionRecord record)
        {
            var filePath = SpecificationPaths.ProjectPreReviewRuleFile(repositoryRoot);
            JsonObject root;
            JsonArray rules;
            if (File.Exists(filePath))
            {
                try
                {
                    var parsed = JsonNode.Parse(File.ReadAllText(filePath));
                    if (parsed is not JsonObject parsedObject)
                    {
                        return new PromotionLandingResult(false, "", $"预审规则文件顶层不是对象，无法合并追加：{filePath}");
                    }

                    root = parsedObject;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    return new PromotionLandingResult(false, "", $"预审规则文件读不了：{filePath}；{exception.Message}");
                }

                if (root["规则"] is not JsonArray existingRules)
                {
                    return new PromotionLandingResult(false, "", $"预审规则文件里没有「规则」数组，无法合并追加：{filePath}");
                }

                rules = existingRules;
            }
            else
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                root = new JsonObject();
                rules = new JsonArray();
                root["规则"] = rules;
            }

            foreach (var node in rules)
            {
                if (node is JsonObject ruleObject
                    && TryReadString(ruleObject, "来源提案", out var existingSource)
                    && string.Equals(existingSource, record.Identifier, StringComparison.Ordinal))
                {
                    return new PromotionLandingResult(true, filePath, $"规则已存在：{record.Identifier} 已在 {filePath} 里，幂等跳过，未重复追加");
                }
            }

            var nextNumber = ScanNextRuleNumber(filePath);
            var ruleIdentifier = "PRR-" + nextNumber.ToString().PadLeft(4, '0');
            rules.Add(new JsonObject
            {
                ["id"] = ruleIdentifier,
                ["问题类别"] = record.Category,
                ["提示词"] = $"检查是否存在「{record.Category}」这一类问题。（这一句要人改写成真正能用的判定提示词）",
                ["来源提案"] = record.Identifier
            });

            File.WriteAllText(filePath, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return new PromotionLandingResult(true, filePath, $"预审规则已追加：{ruleIdentifier} → {filePath}");
        }

        /// <summary>扫预审规则文件原文里 id 是 PRR-四位数字 的最大号 +1；文件不存在或没有匹配返回 1。</summary>
        private static int ScanNextRuleNumber(string filePath)
        {
            var maxNumber = 0;
            if (File.Exists(filePath))
            {
                var text = File.ReadAllText(filePath);
                foreach (Match match in Regex.Matches(text, "\"id\"\\s*:\\s*\"PRR-(\\d{4})\""))
                {
                    if (int.TryParse(match.Groups[1].Value, out var number) && number > maxNumber)
                    {
                        maxNumber = number;
                    }
                }
            }

            return maxNumber + 1;
        }

        /// <summary>ASCII 化的门禁名：类别里非 A-Za-z0-9 的字符一律换成空，换完为空就用 promotion。</summary>
        private static string AsciiChannelName(string category)
        {
            var builder = new StringBuilder();
            foreach (var character in category)
            {
                if ((character >= 'A' && character <= 'Z')
                    || (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9'))
                {
                    builder.Append(character);
                }
            }

            var result = builder.ToString();
            return result.Length == 0 ? "promotion" : result;
        }

        /// <summary>把路径非法字符（\ / : * ? " &lt; &gt; |）换成 _，其余原样保留（含中文）。</summary>
        private static string SanitizeFileName(string name)
        {
            var builder = new StringBuilder();
            foreach (var character in name)
            {
                if (character == '\\' || character == '/' || character == ':'
                    || character == '*' || character == '?' || character == '"'
                    || character == '<' || character == '>' || character == '|')
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
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

        /// <summary>写盘选项：以 Default 为基类（.NET 10 下裸构造序列化含字符串元素的 JsonArray 会抛），缩进 + 不转义中文。</summary>
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
