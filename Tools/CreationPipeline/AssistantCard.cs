using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>动作：按这张卡上的出图请求，真去下游生一批图回来。</summary>
        public const string GenerateAction = "出图";

        /// <summary>动作：把出来的那张界面设计图按元素拆成一张张单图，落进正式环境。</summary>
        public const string CutAction = "拆图";

        /// <summary>
        /// 一张卡上最多贴几张图。
        ///
        /// 拆一屏能出几十个元素，几十张图刷下来聊天框没法看，上传也要几十次往返。
        /// 贴前几张让人一眼知道拆成了什么样，剩下的报路径——它们已经在引擎的正式落点里，
        /// 在 Project 面板里扫一眼比在聊天里一张张翻快得多。
        /// </summary>
        public const int MaximumImagesOnCard = 6;

        /// <summary>
        /// 动作：知道这一趟要花多少次调用，接着拆。
        ///
        /// 为什么要多这一步：重绘是**一个元素一次生图调用**，而视觉模型标框很舍得——
        /// 一张背包界面标出过 86 个框，那就是 86 次调用。这种量级不该由机器替人决定。
        /// 少于门槛的直接跑，不拿一个多余的确认去烦人。
        /// </summary>
        public const string ConfirmCutAction = "确认重绘";

        /// <summary>
        /// 动作：照这条需求出一份界面规格与白块功能图。
        ///
        /// **单独一个按钮，不跟「一键建需求」并成一步**：它要调执行后端（花钱、要等），
        /// 而且不是每条需求都有界面——「把背包系统现状纳入知识库」这种就没有。
        /// 与出图那条同一规矩：花不花钱焊死在人手里。
        /// </summary>
        public const string InterfaceAction = "出功能图";

        /// <summary>
        /// 动作：给一个模块出/刷新一份策划案。
        ///
        /// **不必先有需求。** 策划案是模块的正本，一个模块一份、常驻；
        /// 而需求是一件要做的事。逼人先建一条需求才能拿到模块正本，
        /// 等于为了记录现状先编一件事出来——池子里会多出一堆「补录 XXX」的假需求。
        /// </summary>
        public const string PlanAction = "出策划案";

        /// <summary>按钮动作：把附件收进正式资产目录。**这一步真写正式环境**，所以必须由人点。</summary>
        public const string SubmitAssetAction = "提交资产";

        /// <summary>按钮动作：真去下游生成模型。与出图同理——花钱的动作由人点。</summary>
        public const string ModelAction = "生成模型";

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
        /// <param name="imagePaths">要贴在卡上的本地图片路径；下游负责上传。</param>
        public AssistantCard(
            string title,
            string bodyText,
            IReadOnlyList<KeyValuePair<string, string>> entries,
            IReadOnlyList<string> openQuestions,
            IReadOnlyList<AssistantCardButton> buttons,
            IReadOnlyList<string> imagePaths = null)
        {
            ImagePaths = imagePaths ?? Array.Empty<string>();
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
        /// 要贴在卡上的**本地**图片路径。引擎不认识下游怎么传图——
        /// 它只说「把这几个文件贴上去」，上传与拼版是桥的事（决策 93）。
        /// </summary>
        public IReadOnlyList<string> ImagePaths { get; }

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

            var images = new JsonArray();
            foreach (var path in ImagePaths)
            {
                images.Add(path);
            }

            return new JsonObject
            {
                ["标题"] = Title,
                ["正文"] = BodyText,
                ["条目"] = entries,
                ["待确认"] = questions,
                ["按钮"] = buttons,
                ["图片"] = images
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

            // **不把草稿 key 摆到标题上**：它是内容哈希，不是编号，
            // 摆出去人会以为「REQ-0002」前面还有个 REQ-0001，去池子里翻却翻不着。
            // 真正的 REQ 号在落池子那一刻才发，那时它对应一条真实存在的需求。
            var title = "需求草稿　等你点一下";
            return new AssistantCard(title, bodyText, entries, openQuestions, buttons);
        }

        /// <summary>
        /// 按一份出图请求组一张确认卡：主按钮是「出图」——点了才真去下游生图。
        ///
        /// **为什么也要等人点**：生图是花钱的，而且一次出好几张。
        /// 助手把「画什么」整理清楚给人看一眼，比出完再让人说「不是这个」省得多。
        ///
        /// **还有待确认的点就不给这个按钮**：模型自己挂着问题，说明它自己也没底，
        /// 那时描述里写的是「具体画面内容待确认」——拿它去生图是照着一句废话烧钱。
        /// </summary>
        /// <param name="identifier">出图请求的留底 key（内容哈希，只在按钮携带里流转，不上标题）。</param>
        /// <param name="request">出图请求：资产类型 / 命名 / 描述 / 变体数。</param>
        /// <param name="bodyText">正文：助手自己整理的那段话。</param>
        /// <param name="openQuestions">还想确认的点。</param>
        public static AssistantCard ForImageRequest(
            string identifier,
            JsonObject request,
            string bodyText,
            IReadOnlyList<string> openQuestions)
        {
            var entries = new List<KeyValuePair<string, string>>();
            foreach (var name in new[] { "资产类型", "命名", "描述", "变体数", "宽", "高" })
            {
                if (request != null && request.TryGetPropertyValue(name, out var value) && value != null)
                {
                    var text = Flatten(value);
                    if (text.Length > 0)
                    {
                        entries.Add(new KeyValuePair<string, string>(name, text));
                    }
                }
            }

            var buttons = new List<AssistantCardButton>();

            // **自己还有要确认的，就不给出图按钮。**
            // 模型挂着「这张图给哪个界面用」「要什么风格」这类问题时，说明它自己也没底；
            // 那时描述里写的是「具体画面内容待确认」，拿它去生图就是照着一句废话烧钱，
            // 出来的东西一定不是人要的。先把问题问完，聊清楚了下一轮自然会给按钮。
            var settled = openQuestions == null || openQuestions.Count == 0;
            if (settled)
            {
                buttons.Add(new AssistantCardButton(
                    "出图", GenerateAction, new JsonObject { ["出图请求id"] = identifier ?? "" }, isPrimary: true));
            }

            buttons.Add(new AssistantCardButton("开新话题", NewTopicAction, new JsonObject(), isPrimary: false));

            // 标题**不摆 key**：它是内容哈希，不是编号；摆出去人会当成「第几张图」，
            // 而真正能对上图的号是出完图才有的资产 id。
            var title = settled ? "出图请求　点了才真出" : "出图请求　还差两句话";

            var body = settled
                ? bodyText
                : bodyText + "\n\n（上面这两点你回一句我就给出图按钮——现在描述还立不住，出了也是废的。）";

            return new AssistantCard(title, body, entries, openQuestions, buttons);
        }

        /// <summary>
        /// 按同一份出图请求重组一张卡，只换状态与「给不给按钮」。
        /// 用来**就地改掉已经发出去的那张**：点了就撤按钮，失败了再把它换回来。
        ///
        /// 为什么必须能撤：卡片上的按钮点完不会自己消失，而出图要跑几十秒——
        /// 那期间按钮还亮着，连点几下就是连着出好几批，真花钱。
        /// </summary>
        /// <param name="identifier">出图请求的留底 key。</param>
        /// <param name="request">出图请求。</param>
        /// <param name="title">标题。</param>
        /// <param name="bodyText">正文。</param>
        /// <param name="withButton">给不给「出图」按钮。</param>
        public static AssistantCard ForImageRequestStatus(
            string identifier,
            JsonObject request,
            string title,
            string bodyText,
            bool withButton)
        {
            var entries = new List<KeyValuePair<string, string>>();
            foreach (var name in new[] { "资产类型", "命名", "描述", "变体数", "宽", "高" })
            {
                if (request != null && request.TryGetPropertyValue(name, out var value) && value != null)
                {
                    var text = Flatten(value);
                    if (text.Length > 0)
                    {
                        entries.Add(new KeyValuePair<string, string>(name, text));
                    }
                }
            }

            var buttons = new List<AssistantCardButton>();
            if (withButton)
            {
                buttons.Add(new AssistantCardButton(
                    "出图", GenerateAction, new JsonObject { ["出图请求id"] = identifier ?? "" }, isPrimary: true));
            }

            buttons.Add(new AssistantCardButton("开新话题", NewTopicAction, new JsonObject(), isPrimary: false));
            return new AssistantCard(title, bodyText, entries, Array.Empty<string>(), buttons);
        }

        /// <summary>
        /// 组一张「图出来了」的卡：把变体贴上去让人挑。
        /// UI 那一类还带一个「拆图」按钮——整屏定了方向，下一步才是按元素切成单图。
        /// </summary>
        /// <param name="bodyText">说明。</param>
        /// <param name="imagePaths">变体图的本地路径。</param>
        /// <param name="assetIdentifier">出来的资产 id；给了且是 UI 类才配「拆图」按钮。</param>
        /// <param name="canCut">这一批能不能拆（只有界面设计图才谈得上按元素拆）。</param>
        public static AssistantCard ForGeneratedImages(
            string bodyText,
            IReadOnlyList<string> imagePaths,
            string assetIdentifier = "",
            bool canCut = false)
        {
            var buttons = new List<AssistantCardButton>();

            // 拆图是「定稿之后」的一步：先出一张整屏定方向，方向对了才谈按元素切开。
            // 所以按钮挂在**结果卡**上，不挂在出图请求卡上。
            //
            // **每张变体各给一个按钮**：出了几张就有几个候选，人挑哪张拆哪张。
            // 从前只给一个按钮、写死拆第一张——那等于替人做了选片这个决定，
            // 而他想拆的常常是第三张。
            if (canCut && !string.IsNullOrWhiteSpace(assetIdentifier))
            {
                var count = imagePaths?.Count ?? 0;
                for (var index = 0; index < count; index++)
                {
                    var carried = new JsonObject
                    {
                        ["资产id"] = assetIdentifier,
                        ["变体序号"] = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    };
                    buttons.Add(new AssistantCardButton(
                        count == 1 ? "拆图" : "拆第 " + (index + 1) + " 张",
                        CutAction,
                        carried,
                        isPrimary: index == 0));
                }
            }

            buttons.Add(new AssistantCardButton("开新话题", NewTopicAction, new JsonObject(), isPrimary: false));

            return new AssistantCard(
                canCut ? "出图完成　对了就点拆图" : "出图完成",
                bodyText,
                Array.Empty<KeyValuePair<string, string>>(),
                Array.Empty<string>(),
                buttons,
                imagePaths);
        }

        /// <summary>
        /// 标框标得太多时的「要花这些调用，确定吗」卡。
        /// </summary>
        /// <param name="assetIdentifier">要拆的资产 id。</param>
        /// <param name="variantIndex">拆第几张变体。</param>
        /// <param name="elementCount">标到多少个元素。</param>
        /// <param name="bodyText">正文：把数量、时长、花销说在明处。</param>
        public static AssistantCard ForCutConfirmation(
            string assetIdentifier, int variantIndex, int elementCount, string bodyText)
        {
            var carried = new JsonObject
            {
                ["资产id"] = assetIdentifier,
                ["变体序号"] = variantIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };

            return new AssistantCard(
                "拆图　标到 " + elementCount + " 个元素",
                bodyText,
                System.Array.Empty<System.Collections.Generic.KeyValuePair<string, string>>(),
                Array.Empty<string>(),
                new[] { new AssistantCardButton("知道了，接着拆", ConfirmCutAction, carried, isPrimary: true) },
                Array.Empty<string>());
        }

        /// <summary>
        /// 提交资产那张卡：正文说清**推出来了什么**（类型 / 落点 / 落地叫什么），
        /// 主按钮是「提交资产」——点了才真往正式资产目录写。
        ///
        /// **推不出来的那几条摆进「待确认」而不是按钮上**：那是要人回话的问题，
        /// 不是一个点得下去的动作。此时不给主按钮——给了等于请人点一个必然失败的东西。
        /// </summary>
        /// <param name="sourcePath">附件在本机的路径，按钮要带着它。</param>
        /// <param name="plan">推断结果。</param>
        /// <param name="bodyText">正文。</param>
        public static AssistantCard ForAssetSubmit(string sourcePath, AssetSubmissionPlan plan, string bodyText)
        {
            var facts = new List<KeyValuePair<string, string>>();
            if (plan != null)
            {
                if (plan.AssetType.Length > 0)
                {
                    facts.Add(new KeyValuePair<string, string>("类型", plan.AssetType));
                }

                if (plan.DestinationPath.Length > 0)
                {
                    facts.Add(new KeyValuePair<string, string>("落点", plan.DestinationPath));
                }
            }

            var pending = new List<string>();
            if (plan != null)
            {
                pending.AddRange(plan.Blockers);
                pending.AddRange(plan.Questions);
            }

            var buttons = new List<AssistantCardButton>();
            if (plan != null && plan.CanProceed)
            {
                buttons.Add(new AssistantCardButton(
                    SubmitAssetAction,
                    SubmitAssetAction,
                    new JsonObject
                    {
                        ["源文件"] = sourcePath ?? "",
                        ["资产类型"] = plan.AssetType,
                        ["命名"] = Path.GetFileNameWithoutExtension(plan.FileName)
                    },
                    isPrimary: true));
            }

            buttons.Add(new AssistantCardButton("开新话题", NewTopicAction, new JsonObject(), isPrimary: false));

            return new AssistantCard(
                "提交资产",
                bodyText ?? "",
                facts,
                pending,
                buttons,
                Array.Empty<string>());
        }

        /// <summary>
        /// 生成模型那张卡：与出图那张同形状——先给人看要建什么，点了才真去下游。
        /// 理由也同一个：**建模型花钱**，不给人看一眼就开跑，钱花在误解上。
        /// </summary>
        /// <param name="identifier">模型请求 id（内容哈希）。</param>
        /// <param name="request">模型请求。</param>
        /// <param name="bodyText">正文。</param>
        /// <param name="pending">待确认的那几条。</param>
        public static AssistantCard ForModelRequest(
            string identifier, JsonObject request, string bodyText, IReadOnlyList<string> pending)
        {
            var facts = new List<KeyValuePair<string, string>>();
            if (request != null)
            {
                foreach (var key in new[] { "命名", "描述" })
                {
                    if (request.TryGetPropertyValue(key, out var node) && node is JsonValue value
                        && value.TryGetValue<string>(out var text) && text.Length > 0)
                    {
                        facts.Add(new KeyValuePair<string, string>(key, text));
                    }
                }
            }

            return new AssistantCard(
                "生成模型",
                bodyText ?? "",
                facts,
                pending ?? Array.Empty<string>(),
                new[]
                {
                    new AssistantCardButton(
                        ModelAction, ModelAction, new JsonObject { ["模型请求id"] = identifier ?? "" }, isPrimary: true),
                    new AssistantCardButton("开新话题", NewTopicAction, new JsonObject(), isPrimary: false)
                },
                Array.Empty<string>());
        }

        /// <summary>
        /// 要一份模块策划案时那张卡：主按钮是「出策划案」——点了才真去建/刷新。
        /// </summary>
        /// <param name="moduleName">模块名。</param>
        /// <param name="bodyText">正文。</param>
        public static AssistantCard ForPlanRequest(string moduleName, string bodyText)
        {
            return new AssistantCard(
                "模块策划案　" + (moduleName ?? ""),
                bodyText ?? "",
                Array.Empty<KeyValuePair<string, string>>(),
                Array.Empty<string>(),
                new[]
                {
                    new AssistantCardButton(
                        PlanAction, PlanAction, new JsonObject { ["模块"] = moduleName ?? "" }, isPrimary: true),
                    new AssistantCardButton("开新话题", NewTopicAction, new JsonObject(), isPrimary: false)
                },
                Array.Empty<string>());
        }

        /// <summary>
        /// 需求建成之后那张卡：正文说清落在哪了，按钮问「要不要顺手出一份功能图」。
        ///
        /// 把这一步做成按钮而不是自动跑，是因为它**要调执行后端**，
        /// 而且不是每条需求都有界面——「把某个系统的现状纳入知识库」这种就没有。
        /// </summary>
        /// <param name="bodyText">建成之后的三段话。</param>
        /// <param name="requirementIdentifier">真正的需求号，按钮要带着它。</param>
        public static AssistantCard ForCreatedRequirement(string bodyText, string requirementIdentifier)
        {
            var buttons = new List<AssistantCardButton>();
            if (!string.IsNullOrWhiteSpace(requirementIdentifier))
            {
                buttons.Add(new AssistantCardButton(
                    "出功能图",
                    InterfaceAction,
                    new JsonObject { ["需求id"] = requirementIdentifier },
                    isPrimary: true));
            }

            buttons.Add(new AssistantCardButton("开新话题", NewTopicAction, new JsonObject(), isPrimary: false));

            return new AssistantCard(
                "建好了　" + (requirementIdentifier ?? ""),
                bodyText,
                Array.Empty<KeyValuePair<string, string>>(),
                Array.Empty<string>(),
                buttons,
                Array.Empty<string>());
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
