using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 面板建需求：把网页面板的表单字段组装成合规的「panel」渠道信封写入收件箱，
    /// 供 pool.pull 扫进来入池。面板只有这一条路进池，不做任何别的写盘。
    /// </summary>
    public static class PanelDraftWriter
    {
        /// <summary>写盘选项：缩进 + 不转义中文，与需求文件保持一致。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>「panel」渠道的固定名。</summary>
        private const string ChannelName = "panel";

        /// <summary>
        /// 把表单字段组装成一条信封写入收件箱；记录 id 由 moment 拼成 panel-&lt;yyyyMMddHHmmssfff&gt;，
        /// 文件名是 &lt;记录id&gt;-1.json，全 ASCII。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="submitter">提交人，可空串。</param>
        /// <param name="title">标题，必填。</param>
        /// <param name="kind">类型，必填。</param>
        /// <param name="description">描述，可空串。</param>
        /// <param name="acceptanceCriteria">验收标准，逐条。</param>
        /// <param name="epic">专项 id，可空串。</param>
        /// <param name="extraFields">分类型附加字段（目标/玩法/现状/期望/实际/复现步骤），值空串的不写。</param>
        /// <param name="moment">提交时刻。</param>
        /// <returns>写出的信封文件路径。</returns>
        public static string Write(
            string poolRoot,
            string submitter,
            string title,
            string kind,
            string description,
            IReadOnlyList<string> acceptanceCriteria,
            string epic,
            IReadOnlyDictionary<string, string> extraFields,
            DateTimeOffset moment)
        {
            var recordIdentifier = ChannelName + "-" + moment.ToString("yyyyMMddHHmmssfff");
            var fileName = recordIdentifier + "-1.json";

            var inboxDirectory = PoolPaths.InboxDirectory(poolRoot);
            Directory.CreateDirectory(inboxDirectory);
            var filePath = Path.Combine(inboxDirectory, fileName);

            var fields = BuildFields(title, kind, description, acceptanceCriteria, epic, extraFields);

            var envelope = new JsonObject
            {
                ["渠道"] = ChannelName,
                ["记录id"] = recordIdentifier,
                ["修订"] = 1,
                ["提交人"] = submitter ?? "",
                ["提交时间"] = moment.ToString("o"),
                ["字段"] = fields
            };

            File.WriteAllText(filePath, envelope.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return filePath;
        }

        /// <summary>
        /// 拼「字段」对象：标题、类型必在；描述非空才写、验收标准非空数组才写、专项非空才写、
        /// extraFields 里值非空的逐个写入。
        /// </summary>
        /// <param name="title">标题。</param>
        /// <param name="kind">类型。</param>
        /// <param name="description">描述。</param>
        /// <param name="acceptanceCriteria">验收标准。</param>
        /// <param name="epic">专项 id。</param>
        /// <param name="extraFields">分类型附加字段。</param>
        private static JsonObject BuildFields(
            string title,
            string kind,
            string description,
            IReadOnlyList<string> acceptanceCriteria,
            string epic,
            IReadOnlyDictionary<string, string> extraFields)
        {
            var fields = new JsonObject
            {
                ["标题"] = title,
                ["类型"] = kind
            };

            if (!string.IsNullOrEmpty(description))
            {
                fields["描述"] = description;
            }

            if (acceptanceCriteria != null && acceptanceCriteria.Count > 0)
            {
                var criteriaArray = new JsonArray();
                foreach (var criterion in acceptanceCriteria)
                {
                    criteriaArray.Add(criterion);
                }

                fields["验收标准"] = criteriaArray;
            }

            if (!string.IsNullOrEmpty(epic))
            {
                fields["专项"] = epic;
            }

            if (extraFields != null)
            {
                foreach (var pair in extraFields)
                {
                    if (!string.IsNullOrEmpty(pair.Value))
                    {
                        fields[pair.Key] = pair.Value;
                    }
                }
            }

            return fields;
        }

        private static JsonSerializerOptions CreateWriteOptions()
        {
            // 以 JsonSerializerOptions.Default 为基类带上默认 TypeInfoResolver：
            // 信封 JSON 里的 JsonArray 含字符串元素，.NET 10 下无 resolver 的 options 序列化它们会抛异常。
            return new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }
    }
}
