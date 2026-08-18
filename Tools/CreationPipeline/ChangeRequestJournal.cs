using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次变更请求落盘的结果：本次的时间戳文件路径与累积文件路径。</summary>
    public sealed class ChangeRecordResult
    {
        /// <summary>
        /// 构造一次变更落盘结果。
        /// </summary>
        /// <param name="timestampedFilePath">本次变更的时间戳文件路径。</param>
        /// <param name="accumulatedFilePath">累积变更文件路径。</param>
        public ChangeRecordResult(string timestampedFilePath, string accumulatedFilePath)
        {
            TimestampedFilePath = timestampedFilePath;
            AccumulatedFilePath = accumulatedFilePath;
        }

        /// <summary>本次变更的时间戳文件路径。</summary>
        public string TimestampedFilePath { get; }

        /// <summary>累积变更文件路径。</summary>
        public string AccumulatedFilePath { get; }
    }

    /// <summary>已锁定需求的下游改动落盘：时间戳文件逐次留痕，累积文件合并为最新 diff。</summary>
    public static class ChangeRequestJournal
    {
        /// <summary>写盘选项：缩进 + 不转义中文。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>
        /// 把一次字段级变更写进变更目录：先写 &lt;时间戳&gt;.json，再把本次字段改动合并进累积.json。
        /// 目录不存在时连同 _Tasks/&lt;REQ&gt;/变更/ 整条链一起建。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="envelope">发起变更的信封。</param>
        /// <param name="fieldDiff">字段名到一句中文改动描述的映射。</param>
        /// <param name="moment">变更发生时刻，用于时间戳与「时间」字段。</param>
        public static ChangeRecordResult Record(
            string repositoryRoot,
            string requirementIdentifier,
            InboxEnvelope envelope,
            IReadOnlyDictionary<string, string> fieldDiff,
            DateTimeOffset moment)
        {
            var changeDirectory = PipelinePaths.ChangeDirectory(repositoryRoot, requirementIdentifier);
            Directory.CreateDirectory(changeDirectory);

            var timestamp = moment.ToString("yyyyMMdd-HHmmss");
            var timestampedPath = Path.Combine(changeDirectory, timestamp + ".json");
            var diffObject = ToJsonObject(fieldDiff);

            var changeContent = new JsonObject
            {
                ["需求id"] = requirementIdentifier,
                ["渠道"] = envelope.Channel,
                ["记录id"] = envelope.RecordIdentifier,
                ["修订"] = envelope.Revision,
                ["时间"] = moment.ToString("o"),
                ["字段改动"] = diffObject.DeepClone()
            };
            File.WriteAllText(timestampedPath, changeContent.ToJsonString(WriteOptions), new UTF8Encoding(false));

            var accumulatedPath = PipelinePaths.AccumulatedChangeFile(repositoryRoot, requirementIdentifier);
            var accumulated = File.Exists(accumulatedPath)
                ? JsonNode.Parse(File.ReadAllText(accumulatedPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();

            accumulated["需求id"] = requirementIdentifier;
            accumulated["最后更新"] = moment.ToString("o");

            var accumulatedDiff = accumulated["字段改动"] as JsonObject ?? new JsonObject();
            foreach (var pair in fieldDiff)
            {
                accumulatedDiff[pair.Key] = pair.Value;
            }

            accumulated["字段改动"] = accumulatedDiff;
            File.WriteAllText(accumulatedPath, accumulated.ToJsonString(WriteOptions), new UTF8Encoding(false));

            return new ChangeRecordResult(timestampedPath, accumulatedPath);
        }

        private static JsonObject ToJsonObject(IReadOnlyDictionary<string, string> map)
        {
            var result = new JsonObject();
            foreach (var pair in map)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }

        private static JsonSerializerOptions CreateWriteOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }
    }
}
