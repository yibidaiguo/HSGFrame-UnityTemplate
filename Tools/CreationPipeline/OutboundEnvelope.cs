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
    /// 出站意图信封：一次出站事件的完整意图——回写字段、卡片路由结果与摘要。
    /// 本批只产意图信封落文件，真正发卡片由后续批次的 driver 消费。
    /// </summary>
    public sealed class OutboundEnvelope
    {
        /// <summary>
        /// 构造一封出站意图信封。
        /// </summary>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="eventName">出站事件名，如「待验收」。</param>
        /// <param name="moment">事件发生时刻。</param>
        /// <param name="writeBackFields">要回写下游的字段，传 null 视为空字典。</param>
        /// <param name="routing">本次事件的卡片路由结果。</param>
        /// <param name="summary">一句中文摘要。</param>
        public OutboundEnvelope(
            string requirementIdentifier,
            string eventName,
            DateTimeOffset moment,
            IReadOnlyDictionary<string, string> writeBackFields,
            CardRoutingResult routing,
            string summary)
        {
            RequirementIdentifier = requirementIdentifier;
            Event = eventName;
            Moment = moment;
            WriteBackFields = writeBackFields ?? new Dictionary<string, string>();
            Routing = routing;
            Summary = summary ?? "";
        }

        /// <summary>需求 id，如「REQ-0042」。</summary>
        public string RequirementIdentifier { get; }

        /// <summary>出站事件名，如「待验收」。</summary>
        public string Event { get; }

        /// <summary>事件发生时刻。</summary>
        public DateTimeOffset Moment { get; }

        /// <summary>要回写下游的字段。</summary>
        public IReadOnlyDictionary<string, string> WriteBackFields { get; }

        /// <summary>本次事件的卡片路由结果。</summary>
        public CardRoutingResult Routing { get; }

        /// <summary>一句中文摘要。</summary>
        public string Summary { get; }

        /// <summary>写盘选项：缩进 + 不转义中文，与需求文件保持一致。</summary>
        private static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

        /// <summary>
        /// 把信封写成 JSON 文件并返回写出的路径。
        /// 落点 _Generated/出站，文件名 &lt;yyyyMMdd-HHmmss&gt;-&lt;需求id&gt;-&lt;事件&gt;.json，目录不存在就建。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="envelope">要写盘的信封。</param>
        public static string Write(string repositoryRoot, OutboundEnvelope envelope)
        {
            var directory = PipelinePaths.OutboundDirectory(repositoryRoot);
            Directory.CreateDirectory(directory);

            var fileName = $"{envelope.Moment:yyyyMMdd-HHmmss}-{envelope.RequirementIdentifier}-{envelope.Event}.json";
            var filePath = Path.Combine(directory, fileName);

            var writeBack = new JsonObject();
            foreach (var pair in envelope.WriteBackFields)
            {
                writeBack[pair.Key] = pair.Value;
            }

            var recipients = new JsonArray();
            foreach (var recipient in envelope.Routing.Recipients)
            {
                recipients.Add(recipient);
            }

            var content = new JsonObject
            {
                ["需求id"] = envelope.RequirementIdentifier,
                ["事件"] = envelope.Event,
                ["时间"] = envelope.Moment.ToString("o"),
                ["回写"] = writeBack,
                ["卡片"] = new JsonObject
                {
                    ["类型"] = envelope.Routing.CardType,
                    ["职责"] = envelope.Routing.Duty,
                    ["收件人"] = recipients,
                    ["命中步骤"] = envelope.Routing.Step.ToString(),
                    ["理由"] = envelope.Routing.Reason
                },
                ["摘要"] = envelope.Summary
            };

            File.WriteAllText(filePath, content.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return filePath;
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
