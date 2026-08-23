using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 项目进度文档：把一份进度快照渲染成 <c>_Generated/Progress/index.md</c>，
    /// 再由 <see cref="ProgressDocumentPusher"/> 推成知识库里的一份文档。
    ///
    /// **正文里一个时间戳都不许有**。理由在 <see cref="RequirementDocumentSyncState.HashBody"/>：
    /// 「要不要再推」判的是正文哈希，而 frontmatter 不进哈希。生成时间写进正文的话，
    /// 每跑一次同步正文哈希都变，于是每跑一次都往知识库刷一版全文，
    /// 人翻修改历史时看到的全是「只有时间那一行不一样」的空版本。
    /// 所以时间住在 frontmatter，正文只有数据。
    /// </summary>
    public static class ProgressDocumentRenderer
    {
        /// <summary>生成区开始标记——这份文档整篇都是生成的，标记只是给解析器认 frontmatter 用。</summary>
        public const string GeneratedRegionBegin = "<!-- 进度生成区开始 -->";

        /// <summary>生成区结束标记。</summary>
        public const string GeneratedRegionEnd = "<!-- 进度生成区结束 -->";

        /// <summary>进度文档路径：_Generated/Progress/index.md。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string DocumentFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot ?? "", "_Generated", "Progress", "index.md");
        }

        /// <summary>
        /// 渲染全文：frontmatter（标题 + 生成时间 + 同步账占位）+ 正文（全局一张表 + 逐条需求一张表）。
        /// </summary>
        /// <param name="projectName">项目名，进标题。</param>
        /// <param name="engineSnapshot">工程侧快照。</param>
        /// <param name="inbound">回流账（下游拥有的那几格）。</param>
        /// <param name="schema">权威侧表，决定列顺序与「以哪侧为准」那一列。</param>
        /// <param name="moment">生成时间，写进 frontmatter。</param>
        /// <param name="syncState">已有的同步账；第一次给一份四项全空的。</param>
        public static string Render(
            string projectName,
            ProgressSnapshot engineSnapshot,
            ProgressSnapshot inbound,
            ProgressSyncSchema schema,
            string moment,
            RequirementDocumentSyncState syncState)
        {
            var engine = engineSnapshot ?? new ProgressSnapshot(null, null);
            var inboundSnapshot = inbound ?? new ProgressSnapshot(null, null);
            var fields = schema?.Fields ?? Array.Empty<ProgressSyncField>();
            var title = (string.IsNullOrWhiteSpace(projectName) ? "项目" : projectName.Trim()) + " 项目进度";

            var builder = new StringBuilder();
            builder.Append("---\n");
            builder.Append("标题: ").Append(title).Append('\n');
            builder.Append("生成时间: ").Append(moment ?? "").Append('\n');
            builder.Append("---\n\n");
            builder.Append("# ").Append(title).Append("\n\n");
            builder.Append("> 这份文档由 `sync.progress` 生成，**在这里改字不会回流**——\n");
            builder.Append("> 归下游的那几格请在任务表里改，归工程的那几格由引擎自己算。\n");
            builder.Append("> 哪一格归谁见下面「字段归属」那张表。\n\n");

            builder.Append("## 一、总览\n\n");
            builder.Append("| 项 | 值 |\n| --- | --- |\n");
            foreach (var pair in engine.Global.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                builder.Append("| ").Append(Escape(pair.Key)).Append(" | ").Append(Escape(pair.Value)).Append(" |\n");
            }

            builder.Append("\n## 二、逐条进度\n\n");
            if (engine.Entries.Count == 0)
            {
                builder.Append("池子里还没有需求。\n");
            }
            else
            {
                builder.Append("| 需求 |");
                foreach (var field in fields)
                {
                    builder.Append(' ').Append(Escape(field.Name)).Append(" |");
                }

                builder.Append("\n| --- |");
                foreach (var unused in fields)
                {
                    builder.Append(" --- |");
                }

                builder.Append('\n');

                foreach (var entry in engine.Entries)
                {
                    builder.Append("| ").Append(Escape(entry.Identifier)).Append(" |");
                    var inboundEntry = inboundSnapshot.Find(entry.Identifier);
                    foreach (var field in fields)
                    {
                        var value = field.IsEngineOwned
                            ? entry.Value(field.Name)
                            : (inboundEntry?.Value(field.Name) ?? "");
                        builder.Append(' ').Append(Escape(value)).Append(" |");
                    }

                    builder.Append('\n');
                }
            }

            builder.Append("\n## 三、字段归属\n\n");
            builder.Append("| 字段 | 以哪侧为准 | 下游那一列 | 说明 |\n| --- | --- | --- | --- |\n");
            foreach (var field in fields)
            {
                builder.Append("| ").Append(Escape(field.Name))
                    .Append(" | ").Append(Escape(field.Authority))
                    .Append(" | ").Append(Escape(field.DownstreamColumn))
                    .Append(" | ").Append(Escape(field.Note))
                    .Append(" |\n");
            }

            builder.Append('\n').Append(GeneratedRegionBegin).Append('\n');
            builder.Append(GeneratedRegionEnd).Append('\n');

            return RequirementDocumentSyncState.Write(
                builder.ToString(),
                syncState ?? new RequirementDocumentSyncState("", "", "", ""));
        }

        /// <summary>
        /// 读已有进度文档里的同步账；文档不在或解析不了给一份四项全空的。
        /// 读不出来时**不许当成「没推过」**以外的任何意思——四项全空正好就是那个意思，
        /// 于是下一次推会新建一份而不是覆盖错人。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static RequirementDocumentSyncState ReadSyncState(string repositoryRoot)
        {
            var filePath = DocumentFile(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return new RequirementDocumentSyncState("", "", "", "");
            }

            try
            {
                var text = File.ReadAllText(filePath);
                if (!RequirementDocument.TryParse(text, GeneratedRegionBegin, GeneratedRegionEnd, out var parsed, out _))
                {
                    return new RequirementDocumentSyncState("", "", "", "");
                }

                return RequirementDocumentSyncState.Read(parsed);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new RequirementDocumentSyncState("", "", "", "");
            }
        }

        /// <summary>把一格值塞进 markdown 表格：竖线与换行会把表格拆散，换掉。</summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return value.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");
        }
    }
}
