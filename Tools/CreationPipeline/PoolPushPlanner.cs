using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 按一次出站事件算出站意图：读需求文件、把事件映射成卡片类型与回写字段、路由卡片、写意图信封。
    /// 本方法不改需求文件本身——回写字段只是「意图」，落进信封给后续 driver 用。
    /// </summary>
    public static class PoolPushPlanner
    {
        /// <summary>一次出站计划的结果：是否成案、失败原因、组装好的信封与写出路径。</summary>
        public sealed class PoolPushResult
        {
            /// <summary>
            /// 构造一次出站计划结果。
            /// </summary>
            /// <param name="isPlanned">是否成功成案。</param>
            /// <param name="failureReason">失败原因，成功时为空串。</param>
            /// <param name="envelope">组装好的信封，失败时为 null。</param>
            /// <param name="filePath">写出的信封文件路径，失败时为空串。</param>
            public PoolPushResult(bool isPlanned, string failureReason, OutboundEnvelope envelope, string filePath)
            {
                IsPlanned = isPlanned;
                FailureReason = failureReason ?? "";
                Envelope = envelope;
                FilePath = filePath ?? "";
            }

            /// <summary>是否成功成案。</summary>
            public bool IsPlanned { get; }

            /// <summary>失败原因，成功时为空串。</summary>
            public string FailureReason { get; }

            /// <summary>组装好的信封，失败时为 null。</summary>
            public OutboundEnvelope Envelope { get; }

            /// <summary>写出的信封文件路径，失败时为空串。</summary>
            public string FilePath { get; }
        }

        /// <summary>事件名 → (卡片类型, 回写字段)。</summary>
        private static readonly IReadOnlyList<EventMapping> EventMappings = BuildEventMappings();

        /// <summary>事件映射条目：事件名、卡片类型与回写字段。</summary>
        private sealed class EventMapping
        {
            /// <summary>
            /// 构造一条事件映射。
            /// </summary>
            /// <param name="eventName">出站事件名。</param>
            /// <param name="cardType">事件对应的卡片类型。</param>
            /// <param name="writeBackFields">要回写下游的字段。</param>
            public EventMapping(string eventName, string cardType, IReadOnlyDictionary<string, string> writeBackFields)
            {
                EventName = eventName;
                CardType = cardType;
                WriteBackFields = writeBackFields;
            }

            /// <summary>出站事件名。</summary>
            public string EventName { get; }

            /// <summary>事件对应的卡片类型。</summary>
            public string CardType { get; }

            /// <summary>要回写下游的字段。</summary>
            public IReadOnlyDictionary<string, string> WriteBackFields { get; }
        }

        /// <summary>
        /// 按事件算出站意图并写意图信封。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录，出站信封从这里展开。</param>
        /// <param name="poolRoot">池子根目录，Requirements/组织/专项 从这里展开。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="eventName">出站事件名，如「待验收」。</param>
        /// <param name="moment">事件发生时刻。</param>
        public static PoolPushResult Plan(
            string repositoryRoot,
            string poolRoot,
            string requirementIdentifier,
            string eventName,
            DateTimeOffset moment)
        {
            var requirementFilePath = Path.Combine(PoolPaths.RequirementsDirectory(poolRoot), requirementIdentifier + ".json");
            if (!File.Exists(requirementFilePath))
            {
                return Failed($"需求文件不存在：{requirementFilePath}");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(requirementFilePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return Failed($"需求文件无法解析：{requirementFilePath}：{exception.Message}");
            }

            string title;
            string epicIdentifier;
            string submitterName;
            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return Failed($"需求文件根必须是 JSON 对象：{requirementFilePath}");
                }

                title = ReadStringOrEmpty(root, "标题");
                epicIdentifier = ReadStringOrEmpty(root, "专项");
                submitterName = ReadSubmitter(root);
            }

            EventMapping mapping = null;
            foreach (var candidate in EventMappings)
            {
                if (string.Equals(candidate.EventName, eventName, StringComparison.Ordinal))
                {
                    mapping = candidate;
                    break;
                }
            }

            if (mapping == null)
            {
                return Failed($"不认识的出站事件「{eventName}」，可用的是：{AvailableEvents()}");
            }

            var routeTable = CardRouteTable.Load(poolRoot);
            var members = MemberDirectory.Load(poolRoot);
            var claims = EpicClaimBook.Load(poolRoot);
            var routing = CardRouter.Route(
                mapping.CardType,
                epicIdentifier,
                submitterName,
                routeTable,
                members,
                claims);

            var summary = $"{requirementIdentifier} {title} {eventName}";
            var envelope = new OutboundEnvelope(
                requirementIdentifier,
                eventName,
                moment,
                mapping.WriteBackFields,
                routing,
                summary);
            var filePath = OutboundEnvelope.Write(repositoryRoot, envelope);

            return new PoolPushResult(true, "", envelope, filePath);
        }

        /// <summary>拼一个失败结果。</summary>
        private static PoolPushResult Failed(string reason)
        {
            return new PoolPushResult(false, reason, null, "");
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读来源.提交人；缺失给空串。</summary>
        private static string ReadSubmitter(JsonElement root)
        {
            if (root.TryGetProperty("来源", out var source) && source.ValueKind == JsonValueKind.Object)
            {
                return ReadStringOrEmpty(source, "提交人");
            }

            return "";
        }

        /// <summary>列出全部可用事件名，顿号分隔。</summary>
        private static string AvailableEvents()
        {
            var names = new List<string>(EventMappings.Count);
            foreach (var mapping in EventMappings)
            {
                names.Add(mapping.EventName);
            }

            return string.Join("、", names);
        }

        /// <summary>事件映射表：事件名 → 卡片类型 + 回写字段。</summary>
        private static IReadOnlyList<EventMapping> BuildEventMappings()
        {
            return new[]
            {
                new EventMapping("待验收", "待验收", new Dictionary<string, string> { ["状态"] = "待验收" }),
                new EventMapping("已完成", "完成", new Dictionary<string, string> { ["状态"] = "已完成" }),
                new EventMapping("拒收", "冲突", new Dictionary<string, string>()),
                new EventMapping("冲突", "冲突", new Dictionary<string, string>()),
                new EventMapping("停等", "喊人", new Dictionary<string, string>())
            };
        }
    }
}
