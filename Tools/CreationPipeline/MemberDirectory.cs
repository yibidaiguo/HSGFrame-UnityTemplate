using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>成员表里的一条成员：open_id、姓名、默认职责清单与是否具备确认权。</summary>
    public sealed class PoolMember
    {
        /// <summary>
        /// 构造一条成员。
        /// </summary>
        /// <param name="openIdentifier">成员的 open_id。</param>
        /// <param name="name">成员姓名。</param>
        /// <param name="defaultDuties">默认职责清单，传 null 视为空列表。</param>
        /// <param name="isConfirmer">是否具备确认权。</param>
        public PoolMember(string openIdentifier, string name, IReadOnlyList<string> defaultDuties, bool isConfirmer)
        {
            OpenIdentifier = openIdentifier;
            Name = name;
            DefaultDuties = defaultDuties ?? Array.Empty<string>();
            IsConfirmer = isConfirmer;
        }

        /// <summary>成员的 open_id。</summary>
        public string OpenIdentifier { get; }

        /// <summary>成员姓名。</summary>
        public string Name { get; }

        /// <summary>默认职责清单。</summary>
        public IReadOnlyList<string> DefaultDuties { get; }

        /// <summary>是否具备确认权。</summary>
        public bool IsConfirmer { get; }
    }

    /// <summary>
    /// 成员目录：读 Pools/组织/成员.json，供路由按默认职责与姓名查人。
    /// 文件缺失、JSON 坏掉或根不是数组时一律退化为空目录，不抛异常，原因记在 LoadFailureReason。
    /// </summary>
    public sealed class MemberDirectory
    {
        /// <summary>
        /// 构造一份成员目录。
        /// </summary>
        /// <param name="members">成员列表，传 null 视为空列表。</param>
        /// <param name="loadFailureReason">加载失败原因，正常加载为空串。</param>
        public MemberDirectory(IReadOnlyList<PoolMember> members, string loadFailureReason)
        {
            Members = members ?? Array.Empty<PoolMember>();
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>
        /// 从池根加载成员目录：读 &lt;池根&gt;/组织/成员.json。
        /// 文件不存在、JSON 语法错误、根不是数组时返回空目录不抛异常，原因记进 LoadFailureReason；
        /// 单个条目缺 open_id 时跳过该条目。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static MemberDirectory Load(string poolRoot)
        {
            var filePath = Path.Combine(PoolPaths.OrganizationDirectory(poolRoot), "成员.json");
            if (!File.Exists(filePath))
            {
                return new MemberDirectory(null, $"成员文件不存在：{filePath}");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new MemberDirectory(null, $"成员文件解析失败：{filePath}：{exception.Message}");
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                {
                    return new MemberDirectory(null, $"成员文件根必须是数组：{filePath}");
                }

                var members = new List<PoolMember>();
                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object
                        || !element.TryGetProperty("open_id", out var openIdentifierElement)
                        || openIdentifierElement.ValueKind != JsonValueKind.String
                        || string.IsNullOrEmpty(openIdentifierElement.GetString()))
                    {
                        continue;
                    }

                    var openIdentifier = openIdentifierElement.GetString();
                    members.Add(new PoolMember(
                        openIdentifier,
                        ReadStringOrEmpty(element, "姓名"),
                        ReadDuties(element),
                        ReadBool(element, "确认人")));
                }

                return new MemberDirectory(members, "");
            }
        }

        /// <summary>成员列表。</summary>
        public IReadOnlyList<PoolMember> Members { get; }

        /// <summary>加载失败原因，正常加载为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 按默认职责查人：返回默认职责含该职责的全部成员，按 OpenIdentifier 的序数序排序。
        /// </summary>
        /// <param name="duty">职责名。</param>
        public IReadOnlyList<PoolMember> ByDuty(string duty)
        {
            var matches = new List<PoolMember>();
            foreach (var member in Members)
            {
                foreach (var memberDuty in member.DefaultDuties)
                {
                    if (string.Equals(memberDuty, duty, StringComparison.Ordinal))
                    {
                        matches.Add(member);
                        break;
                    }
                }
            }

            matches.Sort(static (left, right) => string.CompareOrdinal(left.OpenIdentifier, right.OpenIdentifier));
            return matches;
        }

        /// <summary>
        /// 按姓名精确匹配查人，找不到返回 null。
        /// </summary>
        /// <param name="name">成员姓名。</param>
        public PoolMember FindByName(string name)
        {
            foreach (var member in Members)
            {
                if (string.Equals(member.Name, name, StringComparison.Ordinal))
                {
                    return member;
                }
            }

            return null;
        }

        /// <summary>
        /// 按 open_id 精确匹配查人，找不到返回 null。
        /// </summary>
        /// <param name="openIdentifier">成员 open_id。</param>
        public PoolMember FindByOpenIdentifier(string openIdentifier)
        {
            foreach (var member in Members)
            {
                if (string.Equals(member.OpenIdentifier, openIdentifier, StringComparison.Ordinal))
                {
                    return member;
                }
            }

            return null;
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读默认职责：必须是字符串数组，否则给空列表。</summary>
        private static IReadOnlyList<string> ReadDuties(JsonElement element)
        {
            var duties = new List<string>();
            if (!element.TryGetProperty("默认职责", out var dutiesElement) || dutiesElement.ValueKind != JsonValueKind.Array)
            {
                return duties;
            }

            foreach (var dutyElement in dutiesElement.EnumerateArray())
            {
                if (dutyElement.ValueKind == JsonValueKind.String)
                {
                    duties.Add(dutyElement.GetString() ?? "");
                }
            }

            return duties;
        }

        /// <summary>读布尔属性；缺失或类型不对给 false。</summary>
        private static bool ReadBool(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
        }
    }
}
