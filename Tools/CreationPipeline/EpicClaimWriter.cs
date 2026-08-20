using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次认领写盘的结果：写没写进去，以及一句说清为什么的中文。</summary>
    public sealed class ClaimWriteResult
    {
        /// <summary>
        /// 构造一次写盘结果。
        /// </summary>
        /// <param name="written">是否真的写盘了。</param>
        /// <param name="reason">中文说明，说清写了或没写为什么。</param>
        public ClaimWriteResult(bool written, string reason)
        {
            Written = written;
            Reason = reason ?? "";
        }

        /// <summary>是否真的写盘了。</summary>
        public bool Written { get; }

        /// <summary>中文说明，说清写了或没写为什么。</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// 专项认领写盘：显式认领可跨默认职责，隐式认领仅限默认职责内。
    /// 两个方法都只改专项文件的「认领」字段，其余字段一字不动；职责名只许 美术/程序/策划。
    /// </summary>
    public static class EpicClaimWriter
    {
        /// <summary>合法职责的固定清单，与专项表认领列一致。</summary>
        private static readonly string[] AllowedDuties = { "美术", "程序", "策划" };

        /// <summary>写盘选项：缩进 + 不转义中文，与专项文件保持一致。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>
        /// 记一次显式认领：可跨默认职责，不查成员表的默认职责。
        /// 该职责已含这个 open_id 时不写，否则追加进该职责列表末尾并写盘。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="epicIdentifier">专项 id，如「EP-0003」。</param>
        /// <param name="duty">职责名，只许 美术/程序/策划。</param>
        /// <param name="openIdentifier">成员的 open_id。</param>
        public static ClaimWriteResult RecordExplicitClaim(string poolRoot, string epicIdentifier, string duty, string openIdentifier)
        {
            var guard = GuardCommon(poolRoot, epicIdentifier, duty, openIdentifier);
            if (guard != null)
            {
                return guard;
            }

            var epicFilePath = EpicFilePath(poolRoot, epicIdentifier);
            if (ClaimsContain(epicFilePath, duty, openIdentifier))
            {
                return new ClaimWriteResult(false, $"职责「{duty}」已认领过 open_id「{openIdentifier}」，不重复写");
            }

            try
            {
                WriteClaim(epicFilePath, duty, openIdentifier);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new ClaimWriteResult(false, $"写入专项文件失败：{exception.Message}");
            }

            return new ClaimWriteResult(true, $"显式认领已写入：专项 {epicIdentifier} 职责 {duty} 追加 open_id「{openIdentifier}」");
        }

        /// <summary>
        /// 记一次隐式认领：首次处理该专项卡片即隐式认领，仅限默认职责内。
        /// 两条硬前提缺一不写：① 该专项该职责当前一个认领人都没有；② 这个 open_id 的默认职责包含 duty。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="epicIdentifier">专项 id，如「EP-0003」。</param>
        /// <param name="duty">职责名，只许 美术/程序/策划。</param>
        /// <param name="openIdentifier">成员的 open_id。</param>
        public static ClaimWriteResult RecordImplicitClaim(string poolRoot, string epicIdentifier, string duty, string openIdentifier)
        {
            var guard = GuardCommon(poolRoot, epicIdentifier, duty, openIdentifier);
            if (guard != null)
            {
                return guard;
            }

            var epicFilePath = EpicFilePath(poolRoot, epicIdentifier);
            if (DutyAlreadyClaimed(epicFilePath, duty))
            {
                return new ClaimWriteResult(false, $"职责「{duty}」已有认领人，隐式认领不写");
            }

            var member = MemberDirectory.Load(poolRoot).FindByOpenIdentifier(openIdentifier);
            var inDefaultDuty = member != null && ContainsDuty(member.DefaultDuties, duty);
            if (!inDefaultDuty)
            {
                return new ClaimWriteResult(false, $"open_id「{openIdentifier}」的默认职责不含「{duty}」，隐式认领不许跨默认职责");
            }

            try
            {
                WriteClaim(epicFilePath, duty, openIdentifier);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new ClaimWriteResult(false, $"写入专项文件失败：{exception.Message}");
            }

            return new ClaimWriteResult(true, $"隐式认领已写入：专项 {epicIdentifier} 职责 {duty} 追加 open_id「{openIdentifier}」");
        }

        /// <summary>两个方法共有的前置校验：职责名合法、专项文件存在；通过返回 null。</summary>
        private static ClaimWriteResult GuardCommon(string poolRoot, string epicIdentifier, string duty, string openIdentifier)
        {
            if (string.IsNullOrWhiteSpace(epicIdentifier))
            {
                return new ClaimWriteResult(false, "专项 id 为空，不写");
            }

            if (string.IsNullOrWhiteSpace(openIdentifier))
            {
                return new ClaimWriteResult(false, "open_id 为空，不写");
            }

            var validDuty = false;
            foreach (var allowed in AllowedDuties)
            {
                if (string.Equals(duty, allowed, StringComparison.Ordinal))
                {
                    validDuty = true;
                    break;
                }
            }

            if (!validDuty)
            {
                return new ClaimWriteResult(false, $"职责「{duty}」不是合法职责；合法职责只有 美术、程序、策划");
            }

            var epicFilePath = EpicFilePath(poolRoot, epicIdentifier);
            if (!File.Exists(epicFilePath))
            {
                return new ClaimWriteResult(false, $"专项文件不存在：{epicFilePath}；不凭空建专项，专项由策划端创建后再同步认领");
            }

            return null;
        }

        /// <summary>专项文件路径：&lt;池根&gt;/专项/&lt;专项id&gt;.json。</summary>
        private static string EpicFilePath(string poolRoot, string epicIdentifier)
        {
            return Path.Combine(PoolPaths.EpicsDirectory(poolRoot), epicIdentifier + ".json");
        }

        /// <summary>该专项该职责的认领列表里是否已含这个 open_id。</summary>
        private static bool ClaimsContain(string epicFilePath, string duty, string openIdentifier)
        {
            if (TryReadClaims(epicFilePath, out var claims)
                && claims.TryGetValue(duty, out var identifiers))
            {
                foreach (var identifier in identifiers)
                {
                    if (string.Equals(identifier, openIdentifier, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>该专项该职责是否已有一个认领人。</summary>
        private static bool DutyAlreadyClaimed(string epicFilePath, string duty)
        {
            return TryReadClaims(epicFilePath, out var claims)
                && claims.TryGetValue(duty, out var identifiers)
                && identifiers.Count > 0;
        }

        /// <summary>读专项文件的「认领」为「职责 → open_id 列表」；文件读不出或根不是对象返回 false。</summary>
        private static bool TryReadClaims(string epicFilePath, out IReadOnlyDictionary<string, IReadOnlyList<string>> claims)
        {
            claims = new Dictionary<string, IReadOnlyList<string>>();
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(epicFilePath));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("认领", out var claimsElement)
                    || claimsElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var property in claimsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var identifiers = new List<string>();
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            identifiers.Add(item.GetString() ?? "");
                        }
                    }

                    result[property.Name] = identifiers;
                }

                claims = result;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return false;
            }
        }

        /// <summary>把 open_id 追加进专项文件「认领.职责」列表末尾，其余字段原样保留，整体写回。</summary>
        private static void WriteClaim(string epicFilePath, string duty, string openIdentifier)
        {
            var node = JsonNode.Parse(File.ReadAllText(epicFilePath));
            if (node is not JsonObject epicObject)
            {
                throw new JsonException($"专项文件根必须是 JSON 对象：{epicFilePath}");
            }

            if (epicObject["认领"] is not JsonObject claims)
            {
                claims = new JsonObject();
                epicObject["认领"] = claims;
            }

            if (claims[duty] is not JsonArray identifiers)
            {
                identifiers = new JsonArray();
                claims[duty] = identifiers;
            }

            identifiers.Add(openIdentifier);

            File.WriteAllText(epicFilePath, epicObject.ToJsonString(WriteOptions), new UTF8Encoding(false));
        }

        /// <summary>职责列表里是否含指定职责。</summary>
        private static bool ContainsDuty(IReadOnlyList<string> duties, string duty)
        {
            foreach (var candidate in duties)
            {
                if (string.Equals(candidate, duty, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static JsonSerializerOptions CreateWriteOptions()
        {
            // 以 JsonSerializerOptions.Default 为基类带上默认 TypeInfoResolver：
            // 信封里的 JsonArray 含字符串元素，.NET 10 下无 resolver 的 options 序列化它们会抛异常。
            return new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }
    }
}
