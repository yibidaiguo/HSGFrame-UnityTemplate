using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一轮专项认领入站的报告：处理条数、跳过条数、拒收清单与全部发现。</summary>
    public sealed class EpicClaimIntakeReport
    {
        /// <summary>
        /// 构造一轮入站报告。
        /// </summary>
        /// <param name="processedCount">成功写入认领的条数。</param>
        /// <param name="skippedCount">因修订不新而跳过的条数。</param>
        /// <param name="rejections">拒收清单，逐条一条。</param>
        /// <param name="findings">非拒收的发现，如被忽略的多余键。</param>
        public EpicClaimIntakeReport(
            int processedCount,
            int skippedCount,
            IReadOnlyList<PoolFinding> rejections,
            IReadOnlyList<PoolFinding> findings)
        {
            ProcessedCount = processedCount;
            SkippedCount = skippedCount;
            Rejections = rejections ?? Array.Empty<PoolFinding>();
            Findings = findings ?? Array.Empty<PoolFinding>();
        }

        /// <summary>成功写入认领的条数。</summary>
        public int ProcessedCount { get; }

        /// <summary>因修订不新而跳过的条数。</summary>
        public int SkippedCount { get; }

        /// <summary>拒收清单，逐条一条。</summary>
        public IReadOnlyList<PoolFinding> Rejections { get; }

        /// <summary>非拒收的发现，如被忽略的多余键。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }
    }

    /// <summary>
    /// 专项认领入站：扫专项收件箱，把下游同步来的认领字段写进专项文件。
    /// 认领字段归下游成员编辑所有、工程只读同步进来——入站只许改专项文件的「认领」与「来源」，
    /// 其余字段一字不动；幂等靠「来源.修订」，处理过的入站文件不删。
    /// </summary>
    public static class EpicClaimIntake
    {
        /// <summary>合法职责的固定清单，与专项表认领列一致。</summary>
        private static readonly string[] AllowedDuties = { "美术", "程序", "策划" };

        /// <summary>写盘选项：缩进 + 不转义中文，与专项文件保持一致。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>
        /// 跑一轮专项认领入站。
        /// </summary>
        /// <param name="poolRoot">池子根目录，专项收件箱与专项目录从这里展开。</param>
        public static EpicClaimIntakeReport Process(string poolRoot)
        {
            var processedCount = 0;
            var skippedCount = 0;
            var rejections = new List<PoolFinding>();
            var findings = new List<PoolFinding>();

            var inboxDirectory = PoolPaths.EpicInboxDirectory(poolRoot);
            if (!Directory.Exists(inboxDirectory))
            {
                return new EpicClaimIntakeReport(0, 0, rejections, findings);
            }

            foreach (var filePath in Directory.EnumerateFiles(inboxDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (!EpicClaimEnvelope.TryRead(filePath, out var envelope, out var failureReason))
                {
                    rejections.Add(new PoolFinding(
                        filePath,
                        $"专项认领信封无法解析：{failureReason}",
                        "把信封修成 专项id/修订/认领 齐全的 JSON",
                        "Doc/creation-pipeline-subdocs/01-pools-and-requirements.md"));
                    continue;
                }

                var epicFilePath = Path.Combine(PoolPaths.EpicsDirectory(poolRoot), envelope.EpicIdentifier + ".json");
                if (!File.Exists(epicFilePath))
                {
                    rejections.Add(new PoolFinding(
                        filePath,
                        $"专项文件不存在：{epicFilePath}；不凭空建专项，专项由策划端创建后再同步认领",
                        "先在策划端创建该专项，再重新同步认领",
                        "Doc/creation-pipeline-subdocs/01-pools-and-requirements.md"));
                    continue;
                }

                if (IsStaleRevision(epicFilePath, envelope.Revision))
                {
                    skippedCount++;
                    continue;
                }

                var invalidDuty = FirstInvalidDuty(envelope.Claims);
                if (invalidDuty.Length > 0)
                {
                    rejections.Add(new PoolFinding(
                        filePath,
                        $"职责「{invalidDuty}」不是合法职责；合法职责只有 美术、程序、策划",
                        "改回 美术/程序/策划 之一",
                        "Doc/creation-pipeline-subdocs/01-pools-and-requirements.md"));
                    continue;
                }

                try
                {
                    WriteClaimsAndSource(epicFilePath, envelope);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    rejections.Add(new PoolFinding(
                        filePath,
                        $"写入专项文件失败：{exception.Message}",
                        "检查专项文件可写后重新同步",
                        "Doc/creation-pipeline-subdocs/01-pools-and-requirements.md"));
                    continue;
                }

                processedCount++;

                if (envelope.IgnoredFieldNames.Count > 0)
                {
                    findings.Add(new PoolFinding(
                        filePath,
                        $"信封里多余的键被忽略：{string.Join("、", envelope.IgnoredFieldNames)}",
                        "认领字段之外的专项字段归策划端，下游不要经这条通道改",
                        "Doc/creation-pipeline-subdocs/02-sync-and-provisioning.md"));
                }
            }

            return new EpicClaimIntakeReport(processedCount, skippedCount, rejections, findings);
        }

        /// <summary>专项文件的「来源.修订」是否不小于信封修订；来源缺失或修订读不出按 0 计。</summary>
        private static bool IsStaleRevision(string epicFilePath, int envelopeRevision)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(epicFilePath));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("来源", out var source)
                    || source.ValueKind != JsonValueKind.Object
                    || !source.TryGetProperty("修订", out var revisionElement)
                    || revisionElement.ValueKind != JsonValueKind.Number
                    || !revisionElement.TryGetInt32(out var pooledRevision))
                {
                    return false;
                }

                return envelopeRevision <= pooledRevision;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 专项文件读不出来按无来源处理，让入站往下走，具体错误在写盘时暴露。
                return false;
            }
        }

        /// <summary>认领表里第一个不是 美术/程序/策划 的职责名；全部合法返回空串。</summary>
        private static string FirstInvalidDuty(IReadOnlyDictionary<string, IReadOnlyList<string>> claims)
        {
            foreach (var duty in claims.Keys)
            {
                var valid = false;
                foreach (var allowed in AllowedDuties)
                {
                    if (string.Equals(duty, allowed, StringComparison.Ordinal))
                    {
                        valid = true;
                        break;
                    }
                }

                if (!valid)
                {
                    return duty;
                }
            }

            return "";
        }

        /// <summary>只替换专项文件的「认领」与「来源」两个键，其余字段原样保留，整体写回。</summary>
        private static void WriteClaimsAndSource(string epicFilePath, EpicClaimEnvelope envelope)
        {
            var node = JsonNode.Parse(File.ReadAllText(epicFilePath));
            if (node is not JsonObject epicObject)
            {
                throw new JsonException($"专项文件根必须是 JSON 对象：{epicFilePath}");
            }

            var claims = new JsonObject();
            foreach (var pair in envelope.Claims)
            {
                var identifiers = new JsonArray();
                foreach (var identifier in pair.Value)
                {
                    identifiers.Add(identifier);
                }

                claims[pair.Key] = identifiers;
            }

            epicObject["认领"] = claims;
            epicObject["来源"] = new JsonObject
            {
                ["通道"] = envelope.Channel,
                ["修订"] = envelope.Revision,
                ["提交人"] = envelope.Submitter,
                ["提交时间"] = envelope.SubmitTime
            };

            File.WriteAllText(epicFilePath, epicObject.ToJsonString(WriteOptions), new UTF8Encoding(false));
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
