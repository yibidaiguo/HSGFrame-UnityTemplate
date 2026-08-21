using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>把一条拒收结论写盘成拒收单：固定落 _Generated/拒收，同名记录重跑覆盖同一文件。</summary>
    public static class RejectionNotice
    {
        /// <summary>写盘选项：缩进 + 不转义中文，与需求文件保持一致。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>
        /// 写拒收单并返回写出的文件路径。文件名不带时间戳，同一记录重跑覆盖同一文件，保证幂等。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="envelope">被拒收的信封。</param>
        /// <param name="findings">全部校验发现，逐条写入「理由」。</param>
        /// <param name="moment">拒收发生时刻，写入「时间」。</param>
        public static string Write(string repositoryRoot, InboxEnvelope envelope, IReadOnlyList<PoolFinding> findings, DateTimeOffset moment)
        {
            var directory = PipelinePaths.RejectionDirectory(repositoryRoot);
            Directory.CreateDirectory(directory);

            var fileName = $"{envelope.Channel}-{envelope.RecordIdentifier}-{envelope.Revision}.json";
            var filePath = Path.Combine(directory, fileName);

            var reasons = new JsonArray();
            foreach (var finding in findings)
            {
                reasons.Add(new JsonObject
                {
                    ["位置"] = finding.Location,
                    ["原因"] = finding.Reason,
                    ["修复"] = finding.FixAction,
                    ["参考"] = finding.ReferenceExamplePath
                });
            }

            var content = new JsonObject
            {
                ["渠道"] = envelope.Channel,
                ["记录id"] = envelope.RecordIdentifier,
                ["修订"] = envelope.Revision,
                ["时间"] = moment.ToString("o"),
                ["结论"] = "rejected",
                ["人话"] = $"这条需求有 {findings.Count} 处不合格，改完保存会自动重走入库。",
                ["理由"] = reasons
            };

            File.WriteAllText(filePath, content.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return filePath;
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
