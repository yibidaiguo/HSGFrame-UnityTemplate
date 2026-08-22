using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>会话历史里的一轮：谁说的、说了什么、什么时候。</summary>
    public sealed class AssistantHistoryTurn
    {
        /// <summary>构造一轮历史。</summary>
        /// <param name="role">说话的人：用户 / 助手 / 分隔。</param>
        /// <param name="text">这一轮的话。</param>
        /// <param name="at">时间，原样字符串。</param>
        public AssistantHistoryTurn(string role, string text, string at)
        {
            Role = role ?? "";
            Text = text ?? "";
            At = at ?? "";
        }

        /// <summary>说话的人：用户 / 助手 / 分隔。</summary>
        public string Role { get; }

        /// <summary>这一轮的话。</summary>
        public string Text { get; }

        /// <summary>时间，原样字符串。</summary>
        public string At { get; }

        /// <summary>这一条是不是话题分隔线。</summary>
        public bool IsTopicBreak
        {
            get { return string.Equals(Role, BreakRole, StringComparison.Ordinal); }
        }

        /// <summary>用户说的话。</summary>
        public const string UserRole = "用户";

        /// <summary>助手说的话。</summary>
        public const string AssistantRole = "助手";

        /// <summary>话题分隔线：读历史只读它之后的。</summary>
        public const string BreakRole = "分隔";
    }

    /// <summary>
    /// 一条会话的历史：<c>&lt;仓库根&gt;/_Tasks/conversations/history/&lt;会话标识&gt;.jsonl</c>，一行一轮，**只追加**。
    ///
    /// 为什么要有：助手此前每一轮只把用户当前这句话发给执行后端，前面聊过什么一个字都不带——
    /// 于是人刚说完「类型是系统」，下一句问「标题呢」，再下一句又从头问一遍类型。
    /// 会话是一段连续的事，历史不带上去，助手就只能是个健忘的填表机器。
    ///
    /// 为什么「开新话题」是往里追加一条分隔线，而不是删文件：删了就查不了
    /// 「当时到底聊了什么才建出这条需求」。分隔线让读取只取最近一段，而账本仍然完整
    /// （与设计池「记录只追加不改写」同源）。
    ///
    /// 读取有两道上限（轮数与字数），从**尾部往回取**：一段聊了两百轮的会话全量塞进提示词，
    /// 一是烧钱，二是模型会被最早那几轮的旧口径带跑。
    /// </summary>
    public static class AssistantConversationHistory
    {
        /// <summary>写 JSON 的选项：本机是 .NET 10 preview SDK，必须从 Default 复制着构造。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>缺省最多带回多少轮。</summary>
        public const int DefaultMaxTurns = 24;

        /// <summary>缺省最多带回多少字（按尾部裁）。</summary>
        public const int DefaultMaxCharacters = 6000;

        /// <summary>历史目录：&lt;仓库根&gt;/_Tasks/conversations/history。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string HistoryDirectory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Tasks", "conversations", "history");
        }

        /// <summary>
        /// 一条会话的历史文件路径。会话标识来自下游（飞书是 <c>oc_xxx</c>），
        /// 但**不许拿它直接拼路径**——下游换一个带斜杠或点的标识就能写到目录外面去。
        /// 这里把字母数字与连字符之外的字符一律换成下划线（决策 1：路径全 ASCII）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="conversationIdentifier">会话标识。</param>
        public static string HistoryFilePath(string repositoryRoot, string conversationIdentifier)
        {
            return Path.Combine(HistoryDirectory(repositoryRoot), SafeFileName(conversationIdentifier) + ".jsonl");
        }

        /// <summary>
        /// 追加一轮。写不动只是没记住上下文，不该把这一轮的回话带崩——所以吞掉 IO 异常并返回 false。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="conversationIdentifier">会话标识。</param>
        /// <param name="role">说话的人：用户 / 助手 / 分隔。</param>
        /// <param name="text">这一轮的话。</param>
        /// <param name="now">当前时间，由调用方给（要可复现，决策 58）。</param>
        public static bool Append(
            string repositoryRoot,
            string conversationIdentifier,
            string role,
            string text,
            DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(conversationIdentifier))
            {
                return false;
            }

            var record = new JsonObject
            {
                ["时间"] = now.ToString("o"),
                ["角色"] = role ?? "",
                ["文本"] = text ?? ""
            };

            try
            {
                Directory.CreateDirectory(HistoryDirectory(repositoryRoot));
                File.AppendAllText(
                    HistoryFilePath(repositoryRoot, conversationIdentifier),
                    record.ToJsonString(WriteOptions) + Environment.NewLine,
                    new UTF8Encoding(false));
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// 开一个新话题：往历史里追加一条分隔线。之后 <see cref="Read"/> 只会读到分隔线之后的内容，
        /// 等于把上下文丢了，而账本还在。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="conversationIdentifier">会话标识。</param>
        /// <param name="reason">为什么开新话题，写进账本。</param>
        /// <param name="now">当前时间。</param>
        public static bool StartNewTopic(
            string repositoryRoot,
            string conversationIdentifier,
            string reason,
            DateTimeOffset now)
        {
            return Append(repositoryRoot, conversationIdentifier, AssistantHistoryTurn.BreakRole, reason ?? "", now);
        }

        /// <summary>
        /// 读最近一段历史：最后一条分隔线之后的轮次，再按轮数与字数两道上限从尾部往回取。
        /// 文件不存在、读不动、某一行坏了，都当「这一行没有」——历史是锦上添花，
        /// 绝不许因为它让这一轮回不出话。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="conversationIdentifier">会话标识。</param>
        /// <param name="maxTurns">最多带回多少轮。</param>
        /// <param name="maxCharacters">最多带回多少字。</param>
        public static IReadOnlyList<AssistantHistoryTurn> Read(
            string repositoryRoot,
            string conversationIdentifier,
            int maxTurns = DefaultMaxTurns,
            int maxCharacters = DefaultMaxCharacters)
        {
            if (string.IsNullOrWhiteSpace(conversationIdentifier))
            {
                return Array.Empty<AssistantHistoryTurn>();
            }

            string[] lines;
            try
            {
                var filePath = HistoryFilePath(repositoryRoot, conversationIdentifier);
                if (!File.Exists(filePath))
                {
                    return Array.Empty<AssistantHistoryTurn>();
                }

                lines = File.ReadAllLines(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Array.Empty<AssistantHistoryTurn>();
            }

            var turns = new List<AssistantHistoryTurn>();
            foreach (var line in lines)
            {
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                JsonNode node;
                try
                {
                    node = JsonNode.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (node is not JsonObject record)
                {
                    continue;
                }

                var turn = new AssistantHistoryTurn(
                    ReadString(record, "角色"),
                    ReadString(record, "文本"),
                    ReadString(record, "时间"));

                // 分隔线本身不进结果：它只是个界桩，把它前面的全丢掉。
                if (turn.IsTopicBreak)
                {
                    turns.Clear();
                    continue;
                }

                turns.Add(turn);
            }

            if (maxTurns > 0 && turns.Count > maxTurns)
            {
                turns.RemoveRange(0, turns.Count - maxTurns);
            }

            if (maxCharacters > 0 && turns.Count > 0)
            {
                var total = 0;
                var keepFrom = turns.Count;
                for (var index = turns.Count - 1; index >= 0; index--)
                {
                    total += turns[index].Text.Length;
                    if (total > maxCharacters)
                    {
                        break;
                    }

                    keepFrom = index;
                }

                // 一轮都放不下时也要留最后一轮：宁可超一点字数，也不许把「用户刚说的上一句」丢掉。
                if (keepFrom >= turns.Count)
                {
                    keepFrom = turns.Count - 1;
                }

                if (keepFrom > 0)
                {
                    turns.RemoveRange(0, keepFrom);
                }
            }

            return turns;
        }

        /// <summary>
        /// 把历史渲染成提示词里的一段。空历史给空串——调用方据此决定要不要出这一节，
        /// 别让提示词里出现一个「（无）」的空章节，那只会让模型以为「之前什么都没聊」是条重要信息。
        /// </summary>
        /// <param name="turns">历史轮次。</param>
        public static string RenderForPrompt(IReadOnlyList<AssistantHistoryTurn> turns)
        {
            if (turns == null || turns.Count == 0)
            {
                return "";
            }

            var builder = new StringBuilder();
            foreach (var turn in turns)
            {
                builder.Append(turn.Role.Length == 0 ? "?" : turn.Role).Append("：").AppendLine(turn.Text);
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>把会话标识变成安全的文件名：只留字母数字、连字符与下划线，其余换下划线。</summary>
        /// <param name="conversationIdentifier">会话标识。</param>
        public static string SafeFileName(string conversationIdentifier)
        {
            var text = (conversationIdentifier ?? "").Trim();
            if (text.Length == 0)
            {
                return "unknown";
            }

            var builder = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                var isSafe = (character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character == '-'
                    || character == '_';
                builder.Append(isSafe ? character : '_');
            }

            // 太长的标识截断，但缀一段哈希，免得两个长标识截成同一个名字后共用一份历史。
            var name = builder.ToString();
            return name.Length <= 80 ? name : name.Substring(0, 64) + "-" + AssistantServePrompt.ShortHash(text);
        }

        /// <summary>读对象里的字符串键；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonObject record, string propertyName)
        {
            return record.TryGetPropertyValue(propertyName, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var text)
                ? text
                : "";
        }
    }
}
