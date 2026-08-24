using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 执行后端一轮回答的解析结果。
    ///
    /// **解析失败绝不许当成「没什么要建的」**（决策 42）：解析失败是一支，
    /// 「模型说信息不够」是另一支，两支的回话内容完全不同，合并会把故障印成正常结论。
    /// </summary>
    public sealed class AssistantServeReply
    {
        /// <summary>
        /// 构造一份解析结果。
        /// </summary>
        /// <param name="parsed">解析成功与否。</param>
        /// <param name="replyText">给人看的回话。</param>
        /// <param name="wantsRequirement">模型认为这轮该不该建需求。</param>
        /// <param name="missingItems">模型说还缺什么。</param>
        /// <param name="draft">需求草稿；没有时为 null。</param>
        /// <param name="parseFailureReason">解析失败原因；成功时为空串。</param>
        /// <param name="intentSummary">模型对「这个人想干什么」的一句话复述；没有时为空串。</param>
        /// <param name="wantedThing">这一轮人要的是什么：功能 / 图；没说时为空串。</param>
        /// <param name="imageRequest">出图请求；不是要图那一支时为 null。</param>
        /// <param name="cutFeedback">拆图意见；不是改拆图那一支时为空串。</param>
        /// <param name="planModule">要策划案的模块名；不是这一支时为空串。</param>
        /// <param name="readCodeFiles">要读的代码文件；不是这一支时为空表。</param>
        /// <param name="assetSubmitRequest">提交资产请求；不是这一支时为 null。</param>
        /// <param name="modelRequest">生成模型请求；不是这一支时为 null。</param>
        public AssistantServeReply(
            bool parsed,
            string replyText,
            bool wantsRequirement,
            IReadOnlyList<string> missingItems,
            JsonObject draft,
            string parseFailureReason,
            string intentSummary = "",
            string wantedThing = "",
            JsonObject imageRequest = null,
            string cutFeedback = "",
            string planModule = "",
            IReadOnlyList<string> readCodeFiles = null,
            JsonObject assetSubmitRequest = null,
            JsonObject modelRequest = null)
        {
            WantedThing = wantedThing ?? "";
            ImageRequest = imageRequest;
            AssetSubmitRequest = assetSubmitRequest;
            ModelRequest = modelRequest;
            CutFeedback = cutFeedback ?? "";
            PlanModule = (planModule ?? "").Trim();
            ReadCodeFiles = readCodeFiles ?? Array.Empty<string>();
            Parsed = parsed;
            ReplyText = replyText ?? "";
            WantsRequirement = wantsRequirement;
            MissingItems = missingItems ?? Array.Empty<string>();
            Draft = draft;
            ParseFailureReason = parseFailureReason ?? "";
            IntentSummary = intentSummary ?? "";
        }

        /// <summary>解析成功与否。</summary>
        public bool Parsed { get; }

        /// <summary>给人看的回话。</summary>
        public string ReplyText { get; }

        /// <summary>模型认为这轮该不该建需求。解析失败时恒为 false。</summary>
        public bool WantsRequirement { get; }

        /// <summary>
        /// 模型这一轮想跟人确认的点（契约键「要问的问题」，兼容旧键「还缺什么」）。
        /// 这里**不是**给人看的必填字段清单——把 schema 字段名罗列给人，正是助手最招人烦的老毛病。
        /// </summary>
        public IReadOnlyList<string> MissingItems { get; }

        /// <summary>模型对「这个人想干什么」的一句话复述；没有时为空串。</summary>
        public string IntentSummary { get; }

        /// <summary>这一轮人要的是什么：<see cref="WantFeature"/> 或 <see cref="WantImage"/>；没说时为空串。</summary>
        public string WantedThing { get; }

        /// <summary>出图请求（资产类型 / 命名 / 描述 / 变体数）；不是要图那一支时为 null。</summary>
        public JsonObject ImageRequest { get; }

        /// <summary>「要什么」的取值：人要的是能跑起来的功能。</summary>
        public const string WantFeature = "功能";

        /// <summary>「要什么」的取值：人要的是一张图。</summary>
        public const string WantImage = "图";

        /// <summary>「要什么」的取值：人在说上一次拆图哪儿不对。</summary>
        public const string WantRecut = "改拆图";

        /// <summary>「要什么」的第四种：一份模块策划案。</summary>
        public const string WantPlan = "策划案";

        /// <summary>
        /// 「要什么」的第五种：**先读几个代码文件再回答**。
        ///
        /// 它与别的四种不同——它不是一种产出，是一次**中途取材**：
        /// 引擎读完文件会把内容贴回提示词再问一遍模型，最终产出还是那四种之一。
        /// </summary>
        public const string WantReadCode = "读代码";

        /// <summary>
        /// 「要什么」的第六种：**把人丢过来的那个文件收进项目**。
        ///
        /// 与「图」那一支正好相反——图是**现产**一个文件，这一支是把**已经有的**
        /// 一个文件规范化后落进正式资产目录。两者混起来的后果很具体：
        /// 人甩来一张画好的图说「收进项目」，助手却拿它当参考图又生了一张新的，
        /// 钱花了、活没干，而人给的那张还躺在临时目录里。
        /// </summary>
        public const string WantAssetSubmit = "提交资产";

        /// <summary>「要什么」的第七种：生成一个 3D 模型。</summary>
        public const string WantModel = "模型";

        /// <summary>提交资产请求（资产类型 / 模块 / 命名）；不是这一支时为 null。</summary>
        public JsonObject AssetSubmitRequest { get; }

        /// <summary>生成模型请求（描述 / 命名）；不是这一支时为 null。</summary>
        public JsonObject ModelRequest { get; }

        /// <summary>
        /// 这一轮是不是「把这个文件收进项目」。
        /// **判据只有「要什么」这一样**：与出图不同，提交资产的那几格
        /// （类型 / 模块 / 命名）本来就允许推不出来——推不出来才要问人，
        /// 而问人这件事要发生在**已经认出这是一次提交**之后。
        /// 若也要求请求对象非空，模型每次没推全就会掉回闲聊那一支，人永远等不到那两条问题。
        /// </summary>
        public bool WantsAssetSubmit
        {
            get { return string.Equals(WantedThing, WantAssetSubmit, StringComparison.Ordinal); }
        }

        /// <summary>
        /// 这一轮是不是「生成一个模型」。判据与出图同款：
        /// 「要什么」加「模型请求」两样都在——只说要模型却没说建什么，
        /// 那是没说完，该接着聊，不该去下游烧额度。
        /// </summary>
        public bool WantsModel
        {
            get { return string.Equals(WantedThing, WantModel, StringComparison.Ordinal) && ModelRequest != null; }
        }

        /// <summary>人这次说的拆图意见；不是改拆图那一支时为空串。</summary>
        public string CutFeedback { get; }

        /// <summary>
        /// 这一轮是不是「上次拆得不对，改一改」。
        /// **判据是「要什么」加「拆图意见」两样都在**：说要改却没说哪儿不对，
        /// 那就没有可改的依据，该接着问而不是拿一句空话去重拆。
        /// </summary>
        /// <summary>
        /// 要策划案的是哪个模块；不是这一支时为空串。
        ///
        /// **模块名是这一支唯一的入参**：策划案的内容全是从正本投影出来的
        /// （需求、界面、配置表、参考图、代码），模型不需要、也不许提供正文。
        /// </summary>
        public string PlanModule { get; }

        /// <summary>要读哪几个代码文件（仓库相对路径）；不是这一支时为空表。</summary>
        public IReadOnlyList<string> ReadCodeFiles { get; }

        /// <summary>这一轮是不是在要求先读代码。</summary>
        public bool WantsReadCode
        {
            get
            {
                return string.Equals(WantedThing, WantReadCode, StringComparison.Ordinal)
                    && ReadCodeFiles.Count > 0;
            }
        }

        /// <summary>这一轮是不是在要一份模块策划案。</summary>
        public bool WantsPlan
        {
            get
            {
                return string.Equals(WantedThing, WantPlan, StringComparison.Ordinal)
                    && PlanModule.Length > 0;
            }
        }

        public bool WantsRecut
        {
            get { return string.Equals(WantedThing, WantRecut, StringComparison.Ordinal) && CutFeedback.Length > 0; }
        }

        /// <summary>
        /// 这一轮是不是「要一张图」。**判据是「要什么」加「出图请求」两样都在**：
        /// 只说要图却没给出图请求，等于没说画什么——那时该接着聊，不该去生图烧钱。
        /// </summary>
        public bool WantsImage
        {
            get { return string.Equals(WantedThing, WantImage, StringComparison.Ordinal) && ImageRequest != null; }
        }

        /// <summary>需求草稿；没有时为 null。</summary>
        public JsonObject Draft { get; }

        /// <summary>解析失败原因；成功时为空串。</summary>
        public string ParseFailureReason { get; }

        /// <summary>解析失败时的结果：回话如实说「我没读懂模型的回答」，绝不冒充正常结论。</summary>
        /// <param name="reason">失败原因。</param>
        public static AssistantServeReply NotParsed(string reason)
        {
            return new AssistantServeReply(
                parsed: false,
                replyText: "我这边没能读懂执行后端的回答，所以这一轮什么都没建。原因：" + reason,
                wantsRequirement: false,
                missingItems: Array.Empty<string>(),
                draft: null,
                parseFailureReason: reason);
        }

        /// <summary>
        /// 解析模型回答。容忍最常见的两种脏：外面包了 ```json 代码块、前后有闲话。
        /// 但**不容忍缺「回话」**——没有回话就等于没法给人回复，那是失败不是降级。
        /// </summary>
        /// <param name="modelText">模型回答原文。</param>
        /// <param name="reply">解析结果，无论成功失败都非 null。</param>
        public static bool TryParse(string modelText, out AssistantServeReply reply)
        {
            if (string.IsNullOrWhiteSpace(modelText))
            {
                reply = NotParsed("执行后端回了空文本");
                return false;
            }

            var json = ExtractJsonObject(modelText);
            if (json.Length == 0)
            {
                reply = NotParsed("回答里找不到一份 JSON 对象（原文前 200 字：" + Preview(modelText) + "）");
                return false;
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException exception)
            {
                reply = NotParsed("回答里那段 JSON 解析失败：" + exception.Message);
                return false;
            }

            if (node is not JsonObject root)
            {
                reply = NotParsed("回答的顶层不是 JSON 对象");
                return false;
            }

            var replyText = ReadString(root, "回话");
            if (replyText.Trim().Length == 0)
            {
                reply = NotParsed("回答里没有「回话」，没法给人回复");
                return false;
            }

            var wants = ReadBool(root, "要不要建需求");

            // 「要问的问题」是现在的契约键，「还缺什么」是上一版的。两个都读，
            // 是因为提示词版本一变、旧模型缓存与新契约会并存一阵，只读新键会让那一阵的问题全丢。
            var missing = ReadStringArray(root, "要问的问题");
            if (missing.Count == 0)
            {
                missing = ReadStringArray(root, "还缺什么");
            }

            var intent = ReadString(root, "我理解你想干的");
            var wanted = ReadString(root, "要什么");
            var cutFeedback = ReadString(root, "拆图意见");

            // 策划案请求只带一个模块名——正文全是从正本投影出来的，模型不供正文。
            var planModule = "";
            if (root.TryGetPropertyValue("策划案请求", out var planNode) && planNode is JsonObject planObject)
            {
                planModule = ReadString(planObject, "模块");
            }

            IReadOnlyList<string> readCodeFiles = Array.Empty<string>();
            if (root.TryGetPropertyValue("读代码请求", out var readNode) && readNode is JsonObject readObject)
            {
                readCodeFiles = ReadStringArray(readObject, "文件");
            }

            JsonObject imageRequest = null;
            if (root.TryGetPropertyValue("出图请求", out var imageNode) && imageNode is JsonObject imageObject)
            {
                imageRequest = (JsonObject)imageObject.DeepClone();
            }

            JsonObject assetSubmitRequest = null;
            if (root.TryGetPropertyValue("提交资产请求", out var submitNode) && submitNode is JsonObject submitObject)
            {
                assetSubmitRequest = (JsonObject)submitObject.DeepClone();
            }

            JsonObject modelRequest = null;
            if (root.TryGetPropertyValue("模型请求", out var modelNode) && modelNode is JsonObject modelObject)
            {
                modelRequest = (JsonObject)modelObject.DeepClone();
            }

            JsonObject draft = null;
            if (root.TryGetPropertyValue("需求草稿", out var draftNode) && draftNode is JsonObject draftObject)
            {
                draft = (JsonObject)draftObject.DeepClone();
            }

            if (wants && draft == null)
            {
                // 说要建却没给草稿，是自相矛盾。按「不建」处理并在回话里说清楚，
                // 不许悄悄当成「建了一个空需求」。
                reply = new AssistantServeReply(
                    parsed: true,
                    replyText: replyText + "\n\n（引擎注：模型说要建需求却没给草稿内容，这一轮没有写表。）",
                    wantsRequirement: false,
                    missingItems: missing,
                    draft: null,
                    parseFailureReason: "",
                    intentSummary: intent,
                    wantedThing: wanted,
                    imageRequest: imageRequest,
                    cutFeedback: cutFeedback,
                    planModule: planModule,
                    readCodeFiles: readCodeFiles,
                    assetSubmitRequest: assetSubmitRequest,
                    modelRequest: modelRequest);
                return true;
            }

            reply = new AssistantServeReply(
                true, replyText, wants, missing, draft, "", intent, wanted, imageRequest, cutFeedback,
                planModule, readCodeFiles, assetSubmitRequest, modelRequest);
            return true;
        }

        /// <summary>从一段文本里抠出第一个花括号配平的 JSON 对象；抠不到给空串。</summary>
        private static string ExtractJsonObject(string text)
        {
            var depth = 0;
            var start = -1;
            var inString = false;
            var escaped = false;
            for (var index = 0; index < text.Length; index++)
            {
                var ch = text[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '{')
                {
                    if (depth == 0)
                    {
                        start = index;
                    }

                    depth++;
                    continue;
                }

                if (ch == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        return text.Substring(start, index - start + 1);
                    }
                }
            }

            return "";
        }

        /// <summary>读字符串键；缺失或类型不对给空串。</summary>
        private static string ReadString(JsonObject root, string propertyName)
        {
            if (root.TryGetPropertyValue(propertyName, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                return text ?? "";
            }

            return "";
        }

        /// <summary>读布尔键；缺失或类型不对给 false（保守：默认不建需求）。</summary>
        private static bool ReadBool(JsonObject root, string propertyName)
        {
            if (root.TryGetPropertyValue(propertyName, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var flag))
            {
                return flag;
            }

            return false;
        }

        /// <summary>读字符串数组键；缺失给空列表，元素里非字符串的跳过。</summary>
        private static IReadOnlyList<string> ReadStringArray(JsonObject root, string propertyName)
        {
            var items = new List<string>();
            if (root.TryGetPropertyValue(propertyName, out var node) && node is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                    {
                        items.Add(text);
                    }
                }
            }

            return items;
        }

        /// <summary>原文预览：截断到 200 字。</summary>
        private static string Preview(string text)
        {
            return text.Length <= 200 ? text : text.Substring(0, 200) + "…（已截断）";
        }
    }
}
