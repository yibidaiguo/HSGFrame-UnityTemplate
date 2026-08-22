using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>卡片上的一个按钮：给人点的文案、点了算什么动作、点的时候把什么带回来。</summary>
    public sealed class AssistantCardButton
    {
        /// <summary>构造一个按钮。</summary>
        /// <param name="label">按钮文案。</param>
        /// <param name="action">动作名，见 <see cref="AssistantCard"/> 上的动作常量。</param>
        /// <param name="value">点击时带回来的键值，至少要够引擎找回这张卡对应的草稿。</param>
        /// <param name="isPrimary">是不是主按钮（下游据此上色）。</param>
        public AssistantCardButton(string label, string action, JsonObject value, bool isPrimary)
        {
            Label = label ?? "";
            Action = action ?? "";
            Value = value ?? new JsonObject();
            IsPrimary = isPrimary;
        }

        /// <summary>按钮文案。</summary>
        public string Label { get; }

        /// <summary>动作名。</summary>
        public string Action { get; }

        /// <summary>点击时带回来的键值。</summary>
        public JsonObject Value { get; }

        /// <summary>是不是主按钮。</summary>
        public bool IsPrimary { get; }
    }

    /// <summary>
    /// 助手回话卡片的**归一形状**：标题、正文、整理好的条目、待确认项、按钮。
    ///
    /// 为什么要有这一层：此前助手把「还缺这些：· 需求类型 · 验收标准 ……」直接甩给人，
    /// 等于让人对着 schema 填表——人来找助手就是为了不填表。改成
    /// **助手先把话整理成一张卡，人看一眼点个按钮**，规则该由 AI 消化，不该由人背。
    ///
    /// 这里刻意不认识任何下游：卡片长什么样是下游知识（决策 93），飞书桥拿这份归一数据
    /// 去拼 interactive 卡片，换一个下游只换桥，引擎侧一个字不动。
    /// </summary>
    public sealed class AssistantCard
    {
        /// <summary>动作：把这张卡对应的草稿真建成需求，并叫醒引擎。</summary>
        public const string CreateAction = "创建需求";

        /// <summary>动作：丢掉这条会话的上下文，从头聊。</summary>
        public const string NewTopicAction = "开新话题";

        /// <summary>动作：这张卡不对，接着聊改它。</summary>
        public const string ReviseAction = "接着改";

        /// <summary>构造一张卡片。</summary>
        /// <param name="title">卡片标题。</param>
        /// <param name="bodyText">正文：助手整理出来的人话。</param>
        /// <param name="entries">整理好的条目，顺序即展示顺序。</param>
        /// <param name="openQuestions">还想跟人确认的点；空表示没有。</param>
        /// <param name="buttons">按钮。</param>
        public AssistantCard(
            string title,
            string bodyText,
            IReadOnlyList<KeyValuePair<string, string>> entries,
            IReadOnlyList<string> openQuestions,
            IReadOnlyList<AssistantCardButton> buttons)
        {
            Title = title ?? "";
            BodyText = bodyText ?? "";
            Entries = entries ?? Array.Empty<KeyValuePair<string, string>>();
            OpenQuestions = openQuestions ?? Array.Empty<string>();
            Buttons = buttons ?? Array.Empty<AssistantCardButton>();
        }

        /// <summary>卡片标题。</summary>
        public string Title { get; }

        /// <summary>正文：助手整理出来的人话。</summary>
        public string BodyText { get; }

        /// <summary>整理好的条目。</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Entries { get; }

        /// <summary>还想跟人确认的点。</summary>
        public IReadOnlyList<string> OpenQuestions { get; }

        /// <summary>按钮。</summary>
        public IReadOnlyList<AssistantCardButton> Buttons { get; }

        /// <summary>
        /// 摊成归一 JSON，塞进桥请求的载荷。键全中文，与协议其余部分一致。
        /// </summary>
        public JsonObject ToJson()
        {
            var entries = new JsonArray();
            foreach (var entry in Entries)
            {
                entries.Add(new JsonObject { ["名称"] = entry.Key, ["值"] = entry.Value });
            }

            var questions = new JsonArray();
            foreach (var question in OpenQuestions)
            {
                questions.Add(question);
            }

            var buttons = new JsonArray();
            foreach (var button in Buttons)
            {
                buttons.Add(new JsonObject
                {
                    ["文案"] = button.Label,
                    ["动作"] = button.Action,
                    ["携带"] = button.Value.DeepClone(),
                    ["主按钮"] = button.IsPrimary
                });
            }

            return new JsonObject
            {
                ["标题"] = Title,
                ["正文"] = BodyText,
                ["条目"] = entries,
                ["待确认"] = questions,
                ["按钮"] = buttons
            };
        }

        /// <summary>
        /// 退化成纯文本：下游发不了卡片（或干跑）时，至少要能把同样的内容发出去。
        /// **不许因为卡片发不成就什么都不回**——那是这次翻车的老毛病。
        /// </summary>
        public string ToPlainText()
        {
            var builder = new StringBuilder();
            if (Title.Length > 0)
            {
                builder.AppendLine("【" + Title + "】");
            }

            if (BodyText.Length > 0)
            {
                builder.AppendLine(BodyText);
            }

            if (Entries.Count > 0)
            {
                builder.AppendLine();
                foreach (var entry in Entries)
                {
                    builder.Append("· ").Append(entry.Key).Append("：").AppendLine(entry.Value);
                }
            }

            if (OpenQuestions.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("想跟你确认：");
                foreach (var question in OpenQuestions)
                {
                    builder.Append("· ").AppendLine(question);
                }
            }

            if (Buttons.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("（你的客户端没显示按钮的话，直接回「" + Buttons[0].Label + "」也算数。）");
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// 按一份已校验通过的草稿组一张确认卡：条目照 schema 的字段顺序摆，
        /// 主按钮是「一键建需求」，另配一个「开新话题」。
        ///
        /// 工程侧字段（id / 状态 / 来源 …）不进条目：那些是引擎补的，摆出来只会让人以为要他管。
        /// </summary>
        /// <param name="identifier">草稿的需求 id。</param>
        /// <param name="draft">补全后的草稿。</param>
        /// <param name="schema">合并后的需求 schema，决定条目顺序与哪些字段归策划端。</param>
        /// <param name="bodyText">正文：助手自己整理的那段话。</param>
        /// <param name="openQuestions">还想确认的点。</param>
        public static AssistantCard ForDraft(
            string identifier,
            JsonObject draft,
            PoolSchema schema,
            string bodyText,
            IReadOnlyList<string> openQuestions)
        {
            var entries = new List<KeyValuePair<string, string>>();
            var shown = new HashSet<string>(StringComparer.Ordinal);

            void Add(string name)
            {
                if (name == null || shown.Contains(name))
                {
                    return;
                }

                if (draft != null && draft.TryGetPropertyValue(name, out var value) && value != null)
                {
                    var text = Flatten(value);
                    if (text.Length > 0)
                    {
                        entries.Add(new KeyValuePair<string, string>(name, text));
                        shown.Add(name);
                    }
                }
            }

            if (schema != null)
            {
                foreach (var field in schema.Fields)
                {
                    if (!string.Equals(field.Ownership, RequirementFieldOwnership.EngineOwner, StringComparison.Ordinal))
                    {
                        Add(field.Name);
                    }
                }

                // 分类型必填不在 schema.Fields 里（它们是业务字段），但正是人最关心的那几条。
                foreach (var pair in schema.RequiredByType)
                {
                    foreach (var name in pair.Value)
                    {
                        Add(name);
                    }
                }
            }

            // 草稿里还有别的策划端字段（项目层自由加的业务字段）也一并摆上，顺序按草稿本身。
            if (draft != null)
            {
                foreach (var pair in draft)
                {
                    if (!RequirementFieldOwnership.IsEngineField(schema, pair.Key))
                    {
                        Add(pair.Key);
                    }
                }
            }

            var value = new JsonObject { ["需求id"] = identifier ?? "" };
            var buttons = new List<AssistantCardButton>
            {
                new AssistantCardButton("一键建需求", CreateAction, value, isPrimary: true),
                new AssistantCardButton("开新话题", NewTopicAction, new JsonObject(), isPrimary: false)
            };

            var title = "需求草稿 " + (identifier ?? "") + "　等你点一下";
            return new AssistantCard(title, bodyText, entries, openQuestions, buttons);
        }

        /// <summary>
        /// 组一张「还没聊够」的卡：没有草稿可确认时也给按钮——至少给一个「开新话题」，
        /// 免得人被上一段跑偏的上下文困住却没有出口。
        /// </summary>
        /// <param name="bodyText">助手这一轮的话。</param>
        /// <param name="openQuestions">助手想问的点，最多摆几条。</param>
        public static AssistantCard ForConversation(string bodyText, IReadOnlyList<string> openQuestions)
        {
            var buttons = new List<AssistantCardButton>
            {
                new AssistantCardButton("开新话题", NewTopicAction, new JsonObject(), isPrimary: false)
            };

            return new AssistantCard("", bodyText, Array.Empty<KeyValuePair<string, string>>(), openQuestions, buttons);
        }

        /// <summary>把一个 JSON 值摊成一行人话：数组用「1. …　2. …」，对象直接给紧凑 JSON。</summary>
        /// <param name="value">要摊平的值。</param>
        public static string Flatten(JsonNode value)
        {
            if (value == null)
            {
                return "";
            }

            if (value is JsonArray array)
            {
                var items = array
                    .Select(item => item == null ? "" : Flatten(item))
                    .Where(text => text.Length > 0)
                    .ToList();
                if (items.Count == 0)
                {
                    return "";
                }

                if (items.Count == 1)
                {
                    return items[0];
                }

                var builder = new StringBuilder();
                for (var index = 0; index < items.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(index + 1).Append(". ").Append(items[index]);
                }

                return builder.ToString();
            }

            if (value is JsonObject)
            {
                return value.ToJsonString();
            }

            return value.ToString();
        }
    }
}
