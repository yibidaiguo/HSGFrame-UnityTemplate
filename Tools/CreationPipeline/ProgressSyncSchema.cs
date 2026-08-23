using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>进度同步表里的一格：字段名、权威侧、下游那一列叫什么。</summary>
    public sealed class ProgressSyncField
    {
        /// <summary>
        /// 构造一格。
        /// </summary>
        /// <param name="name">字段名，仓库侧与面板上用的就是它。</param>
        /// <param name="authority">权威侧：工程 / 策划端。</param>
        /// <param name="downstreamColumn">下游任务表里对应的列名。</param>
        /// <param name="note">这一格为什么归那一侧，给人看的一句话。</param>
        public ProgressSyncField(string name, string authority, string downstreamColumn, string note)
        {
            Name = name ?? "";
            Authority = authority ?? "";
            DownstreamColumn = downstreamColumn ?? "";
            Note = note ?? "";
        }

        /// <summary>字段名。</summary>
        public string Name { get; }

        /// <summary>权威侧：<see cref="RequirementFieldOwnership.EngineOwner"/> 或 <see cref="RequirementFieldOwnership.PlannerOwner"/>。</summary>
        public string Authority { get; }

        /// <summary>下游任务表里对应的列名。</summary>
        public string DownstreamColumn { get; }

        /// <summary>这一格为什么归那一侧。</summary>
        public string Note { get; }

        /// <summary>这一格归不归工程侧。</summary>
        public bool IsEngineOwned => string.Equals(Authority, RequirementFieldOwnership.EngineOwner, StringComparison.Ordinal);
    }

    /// <summary>
    /// 进度同步的字段权威侧表（<c>Config/progress-sync.json</c>）。
    ///
    /// **不另造一套所有权概念**：权威侧的取值就是需求案那两个
    /// （<see cref="RequirementFieldOwnership.EngineOwner"/> / <see cref="RequirementFieldOwnership.PlannerOwner"/>），
    /// 含义也一模一样——子文档 02 §一那句「同步永远单向复制，所有权之外的改动出站直接覆盖下游」。
    /// 进度这一路唯一多出来的是：**两边相对上次同步都动过时不许覆盖**，
    /// 那是冲突，落 <see cref="ConflictList"/> 让人看见（见 <see cref="ProgressSyncPlanner"/>）。
    ///
    /// 表进 git，因为它是契约：「这一格以哪侧为准」不该是读代码才知道的事。
    /// </summary>
    public sealed class ProgressSyncSchema
    {
        /// <summary>当前契约版本。</summary>
        public const string ContractVersion = "1.0.0";

        /// <summary>
        /// 构造一份权威侧表。
        /// </summary>
        /// <param name="fields">字段清单，按文件里的顺序。</param>
        /// <param name="loadFailureReason">加载失败原因；正常为空串。</param>
        public ProgressSyncSchema(IReadOnlyList<ProgressSyncField> fields, string loadFailureReason)
        {
            Fields = fields ?? Array.Empty<ProgressSyncField>();
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>字段清单，按文件里的顺序（渲染时也照这个顺序，diff 才稳）。</summary>
        public IReadOnlyList<ProgressSyncField> Fields { get; }

        /// <summary>
        /// 加载失败原因；正常为空串。
        /// **文件缺失算失败**——与下游对象台账那份不同：台账空着意味着「还没建对象，去建」，
        /// 而权威侧表空着意味着「一格都不同步」，那会让 sync.progress 静默什么都不做还报成功。
        /// </summary>
        public string LoadFailureReason { get; }

        /// <summary>权威侧表文件路径：Tools/CreationPipeline/Config/progress-sync.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string SchemaFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "progress-sync.json");
        }

        /// <summary>归工程侧的字段。</summary>
        public IReadOnlyList<ProgressSyncField> EngineFields()
        {
            return Fields.Where(field => field.IsEngineOwned).ToList();
        }

        /// <summary>归策划端的字段。</summary>
        public IReadOnlyList<ProgressSyncField> PlannerFields()
        {
            return Fields.Where(field => !field.IsEngineOwned).ToList();
        }

        /// <summary>按字段名找一格；没有给 null。</summary>
        /// <param name="name">字段名。</param>
        public ProgressSyncField Find(string name)
        {
            return Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// 读权威侧表。文件缺失、JSON 坏掉、权威侧取值不认识，三种都带原因返回空表——
        /// 一格都同步不了时要**说出来**，不许假装同步过了。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static ProgressSyncSchema Load(string repositoryRoot)
        {
            var filePath = SchemaFile(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return Empty($"权威侧表不存在：{filePath}");
            }

            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(filePath)))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return Empty($"权威侧表顶层不是对象：{filePath}");
                    }

                    if (!root.TryGetProperty("字段", out var fieldArray) || fieldArray.ValueKind != JsonValueKind.Array)
                    {
                        return Empty("权威侧表缺「字段」数组");
                    }

                    var fields = new List<ProgressSyncField>();
                    var failures = new List<string>();
                    foreach (var item in fieldArray.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            failures.Add("「字段」里有一项不是对象");
                            continue;
                        }

                        var name = ReadString(item, "名称");
                        var authority = ReadString(item, "权威侧");
                        var column = ReadString(item, "下游列");
                        if (name.Length == 0)
                        {
                            failures.Add("有一项缺「名称」");
                            continue;
                        }

                        if (!IsKnownAuthority(authority))
                        {
                            failures.Add($"字段「{name}」的权威侧「{authority}」不认识，只有「{RequirementFieldOwnership.EngineOwner}」与「{RequirementFieldOwnership.PlannerOwner}」");
                            continue;
                        }

                        if (column.Length == 0)
                        {
                            failures.Add($"字段「{name}」缺「下游列」：不知道它在任务表里对应哪一列就没法同步");
                            continue;
                        }

                        fields.Add(new ProgressSyncField(name, authority, column, ReadString(item, "说明")));
                    }

                    return new ProgressSyncSchema(fields, failures.Count == 0 ? "" : string.Join("；", failures));
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return Empty($"权威侧表读不了：{exception.Message}");
            }
        }

        /// <summary>权威侧取值认不认识。</summary>
        private static bool IsKnownAuthority(string authority)
        {
            return string.Equals(authority, RequirementFieldOwnership.EngineOwner, StringComparison.Ordinal)
                || string.Equals(authority, RequirementFieldOwnership.PlannerOwner, StringComparison.Ordinal);
        }

        /// <summary>空表 + 原因。</summary>
        private static ProgressSyncSchema Empty(string reason)
        {
            return new ProgressSyncSchema(Array.Empty<ProgressSyncField>(), reason);
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }
    }
}
