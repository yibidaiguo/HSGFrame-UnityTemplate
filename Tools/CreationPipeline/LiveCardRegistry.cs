using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 记「这条会话里，最后一张还带着按钮的卡是哪一条消息」。
    ///
    /// 为什么要有：飞书的卡片**点完不会自己消失，翻上去还能点**。
    /// 聊到第十轮时，第三轮那张「一键建需求」还亮着——人手一滑点下去，
    /// 建出来的是三轮之前那份早就聊废了的草稿。
    /// 出图那种更贵：翻上去点一下就是又一批图，真花钱。
    ///
    /// 所以规矩是：**只有最新那张卡上的按钮算数**。下一轮一开始就把上一张的按钮撤掉——
    /// 不是等它跑完，是**模型一开始想就撤**：人正等着回复的那几十秒，
    /// 恰恰是最容易手滑去点上面那张旧卡的时候。
    ///
    /// 一条会话一个文件，内容只有一个消息标识。用文件不用内存：
    /// 助手是常驻进程但会重启，重启后那些旧卡还在飞书上亮着。
    /// </summary>
    public static class LiveCardRegistry
    {
        /// <summary>台账目录：&lt;仓库根&gt;/_Tasks/conversations/live-cards。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string Directory(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "_Tasks", "conversations", "live-cards");
        }

        /// <summary>某条会话的台账文件。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="conversationIdentifier">会话标识。</param>
        public static string FilePathFor(string repositoryRoot, string conversationIdentifier)
        {
            return Path.Combine(
                Directory(repositoryRoot),
                AssistantConversationHistory.SafeFileName(conversationIdentifier) + ".json");
        }

        /// <summary>
        /// 记下这条会话最新那张带按钮的卡。
        /// 写不下去只当没记——后果是那张旧卡的按钮会多留一会儿，不该让整轮失败。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="conversationIdentifier">会话标识。</param>
        /// <param name="messageIdentifier">卡片所在消息的标识。</param>
        public static bool Remember(string repositoryRoot, string conversationIdentifier, string messageIdentifier)
        {
            if (conversationIdentifier.Length == 0 || messageIdentifier.Length == 0)
            {
                return false;
            }

            try
            {
                var path = FilePathFor(repositoryRoot, conversationIdentifier);
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
                var body = new JsonObject
                {
                    ["消息标识"] = messageIdentifier,
                    ["记于"] = DateTimeOffset.Now.ToString("o")
                };

                File.WriteAllText(path, body.ToJsonString(WriteOptions), new UTF8Encoding(false));
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// 取这条会话上一张带按钮的卡；没有给空串。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="conversationIdentifier">会话标识。</param>
        public static string Read(string repositoryRoot, string conversationIdentifier)
        {
            if (conversationIdentifier.Length == 0)
            {
                return "";
            }

            var path = FilePathFor(repositoryRoot, conversationIdentifier);
            if (!File.Exists(path))
            {
                return "";
            }

            try
            {
                return JsonNode.Parse(File.ReadAllText(path)) is JsonObject root
                    && root["消息标识"] is JsonValue value
                    && value.TryGetValue<string>(out var identifier)
                    ? identifier
                    : "";
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 读不动就当没有：多留一会儿按钮，比让整轮崩掉强。
                return "";
            }
        }

        /// <summary>忘掉这条会话的记录。撤过按钮之后调，免得下一轮再去撤同一条。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="conversationIdentifier">会话标识。</param>
        public static void Forget(string repositoryRoot, string conversationIdentifier)
        {
            try
            {
                var path = FilePathFor(repositoryRoot, conversationIdentifier);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 删不掉只是下一轮会多撤一次同一张卡——那一刀是幂等的，无害。
            }
        }

        /// <summary>写盘选项：中文原样，不缩进（就一行两个字段）。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}
