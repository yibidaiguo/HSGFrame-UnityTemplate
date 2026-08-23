using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Template.Toolkit.CreationPipeline;

namespace Template.Toolkit.Dashboard
{
    /// <summary>进度页上的一格：字段名、值、以哪侧为准。</summary>
    public sealed class PanelProgressCell
    {
        /// <summary>
        /// 构造一格。
        /// </summary>
        /// <param name="name">字段名。</param>
        /// <param name="value">值。</param>
        /// <param name="authority">权威侧：工程 / 策划端。</param>
        public PanelProgressCell(string name, string value, string authority)
        {
            Name = name ?? "";
            Value = value ?? "";
            Authority = authority ?? "";
        }

        /// <summary>字段名。</summary>
        [JsonPropertyName("字段")]
        public string Name { get; }

        /// <summary>值。</summary>
        [JsonPropertyName("值")]
        public string Value { get; }

        /// <summary>以哪侧为准。</summary>
        [JsonPropertyName("权威侧")]
        public string Authority { get; }
    }

    /// <summary>进度页上的一行：一条需求。</summary>
    public sealed class PanelProgressRow
    {
        /// <summary>
        /// 构造一行。
        /// </summary>
        /// <param name="identifier">需求 id。</param>
        /// <param name="cells">各字段。</param>
        public PanelProgressRow(string identifier, IReadOnlyList<PanelProgressCell> cells)
        {
            Identifier = identifier ?? "";
            Cells = cells ?? Array.Empty<PanelProgressCell>();
        }

        /// <summary>需求 id。</summary>
        [JsonPropertyName("id")]
        public string Identifier { get; }

        /// <summary>各字段。</summary>
        [JsonPropertyName("格")]
        public IReadOnlyList<PanelProgressCell> Cells { get; }
    }

    /// <summary>进度页的一整份数据：全局数字 + 逐条需求 + 同步账。</summary>
    public sealed class PanelProgressView
    {
        /// <summary>
        /// 构造一份进度视图。
        /// </summary>
        /// <param name="global">全局数字。</param>
        /// <param name="rows">逐条需求。</param>
        /// <param name="fieldNames">列顺序，与权威侧表一致。</param>
        /// <param name="lastInboundMoment">上次回流时间；没同步过是空串。</param>
        /// <param name="documentLink">进度文档在下游的链接；没推过是空串。</param>
        /// <param name="schemaFailure">权威侧表的问题；正常为空串。</param>
        public PanelProgressView(
            IReadOnlyDictionary<string, string> global,
            IReadOnlyList<PanelProgressRow> rows,
            IReadOnlyList<string> fieldNames,
            string lastInboundMoment,
            string documentLink,
            string schemaFailure)
        {
            Global = global ?? new Dictionary<string, string>(StringComparer.Ordinal);
            Rows = rows ?? Array.Empty<PanelProgressRow>();
            FieldNames = fieldNames ?? Array.Empty<string>();
            LastInboundMoment = lastInboundMoment ?? "";
            DocumentLink = documentLink ?? "";
            SchemaFailure = schemaFailure ?? "";
        }

        /// <summary>全局数字。</summary>
        [JsonPropertyName("全局")]
        public IReadOnlyDictionary<string, string> Global { get; }

        /// <summary>逐条需求。</summary>
        [JsonPropertyName("行")]
        public IReadOnlyList<PanelProgressRow> Rows { get; }

        /// <summary>列顺序。</summary>
        [JsonPropertyName("列")]
        public IReadOnlyList<string> FieldNames { get; }

        /// <summary>上次回流时间。</summary>
        [JsonPropertyName("上次回流")]
        public string LastInboundMoment { get; }

        /// <summary>进度文档在下游的链接。</summary>
        [JsonPropertyName("文档链接")]
        public string DocumentLink { get; }

        /// <summary>权威侧表的问题；正常为空串。</summary>
        [JsonPropertyName("表问题")]
        public string SchemaFailure { get; }
    }

    /// <summary>
    /// 面板进度页的数据源。
    ///
    /// **面板是视图不是第三方**：工程侧那几格现算（与 sync.progress 同一个
    /// <see cref="ProgressSnapshot.CollectFromRepository"/>），策划端那几格读回流账
    /// （<see cref="ProgressInboundLedger"/>）。面板自己不与下游说一句话——
    /// 打开一个网页就往飞书发请求，人只是想看一眼，却会因为网断了看到一页错误。
    /// 要拉新的就去跑 sync.progress，那是一次明确的动作。
    /// </summary>
    public static class CreationPanelProgress
    {
        /// <summary>
        /// 读进度页。权威侧表有问题时不抛异常——把问题当成一行字交给页面显示，
        /// 页面照常打开（这一页的价值恰恰在表没配好的时候最大）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static PanelProgressView Read(string repositoryRoot, string poolRoot)
        {
            var schema = ProgressSyncSchema.Load(repositoryRoot);
            var engine = ProgressSnapshot.CollectFromRepository(repositoryRoot, poolRoot);
            var inbound = ProgressInboundLedger.Load(repositoryRoot);

            var rows = new List<PanelProgressRow>();
            foreach (var entry in engine.Entries)
            {
                var inboundEntry = inbound.Find(entry.Identifier);
                var cells = schema.Fields
                    .Select(field => new PanelProgressCell(
                        field.Name,
                        field.IsEngineOwned ? entry.Value(field.Name) : inboundEntry?.Value(field.Name) ?? "",
                        field.Authority))
                    .ToList();
                rows.Add(new PanelProgressRow(entry.Identifier, cells));
            }

            var lastInbound = inbound.Global.TryGetValue("回流时间", out var moment) ? moment : "";
            return new PanelProgressView(
                engine.Global,
                rows,
                schema.Fields.Select(field => field.Name).ToList(),
                lastInbound,
                ProgressDocumentRenderer.ReadSyncState(repositoryRoot).Link,
                schema.LoadFailureReason);
        }
    }
}
