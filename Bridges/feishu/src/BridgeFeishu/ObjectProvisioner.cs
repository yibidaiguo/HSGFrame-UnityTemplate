using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Template.Toolkit.CreationPipeline;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 确保对象在（ensure）：这个项目要用的四样东西——知识空间、多维表格、任务表、需求文档父节点——
    /// **配了就用，没配就建，建完把 id 交回去**，由引擎回填进下游对象台账。
    ///
    /// 为什么值得单开一个动作：
    /// 1. **自己建出来的对象，应用就是所有者**。人手工建的表与节点，应用默认只有读，
    ///    于是建表回 403、建节点回 131006——今天真撞了两次。让链路自己建，这道门根本不存在。
    /// 2. **换台机器不会重来一遍**。id 进台账、台账进 git，clone 下来就还是同一批对象；
    ///    没有这一层，新机器一跑就在下游建出第二套表，数据分家。
    ///
    /// 「配了就用」不是无条件信任：台账里那个 id **要先验它还在不在**。
    /// 人在飞书里把表删了是常事，删了之后台账那份就是个死号——
    /// 验不过就当没有、重新建，而不是拿着死号一路报错到底。
    ///
    /// 四样东西全部落在**知识空间里**：多维表格也是空间下的一个节点（obj_type=bitable），
    /// 不散落在个人云空间——那样换个人就找不到了。
    /// </summary>
    public static class ObjectProvisioner
    {
        /// <summary>协议契约版本。</summary>
        private const string ContractVersion = "1.0.0";

        /// <summary>缺省超时秒数，配置里没有时用。</summary>
        private const int DefaultTimeoutSeconds = 60;

        /// <summary>台账里知识空间那一格的键名。</summary>
        public const string SpaceKey = "知识空间标识";

        /// <summary>台账里需求文档父节点那一格的键名。</summary>
        public const string RequirementDocumentParentKey = "需求文档父节点";

        /// <summary>
        /// 台账里模块策划案父节点那一格的键名。
        ///
        /// **与需求文档父节点分开是刻意的**：一个模块一份、常驻、随验收更新，
        /// 与「一条需求一份、做完归档」摆在同一个父节点下，几十条需求会把
        /// 十来份模块正本淹掉——而人往知识库里找的，九成是模块正本那一份。
        /// </summary>
        public const string ModulePlanParentKey = "模块策划案父节点";

        /// <summary>
        /// 台账里策划设计库文档那一格的键名。
        /// **与美术库分开是刻意的**：「断签从第 1 天重计」和「UI 图标一律 Q 版」
        /// 是两种东西——读者不同、改的人不同、过期的方式也不同。
        /// 堆在一份文档里，两边都得从一堆不相干的条目里筛自己那部分。
        /// </summary>
        public const string GameDesignDocumentKey = "策划设计库文档";

        /// <summary>台账里美术设计库文档那一格的键名。</summary>
        public const string ArtDesignDocumentKey = "美术设计库文档";

        /// <summary>台账里多维表格那一格的键名。</summary>
        public const string BitableKey = "多维表格标识";

        /// <summary>台账里任务表那一格的键名。</summary>
        public const string TaskTableKey = "任务表标识";

        /// <summary>没给标题时，新建知识空间叫什么。</summary>
        private const string DefaultSpaceTitle = "项目协作空间";

        /// <summary>没给标题时，模块策划案的父节点叫什么。对应仓库里的 Pools/Designs/Modules/。</summary>
        private const string DefaultModulePlanParentTitle = "模块策划案";

        /// <summary>没给标题时，需求文档的父节点叫什么。对应仓库里的 Pools/Requirements/。</summary>
        private const string DefaultRequirementDocumentParentTitle = "需求文档";

        /// <summary>策划设计库文档的缺省标题。</summary>
        private const string DefaultGameDesignDocumentTitle = "策划设计库";

        /// <summary>美术设计库文档的缺省标题。</summary>
        private const string DefaultArtDesignDocumentTitle = "美术设计库";

        /// <summary>没给标题时，多维表格那个节点叫什么。</summary>
        private const string DefaultBitableTitle = "任务管理";

        /// <summary>没给名字时，任务表叫什么。</summary>
        private const string DefaultTaskTableName = "任务";

        /// <summary>
        /// 执行 ensure 动作：四样对象逐个「验 → 缺就建」，把最终 id 全部交回去。
        /// 干跑只报「哪几样缺、要建什么」，一个写请求都不发（决策 92）。
        /// </summary>
        /// <param name="request">请求信封：配置含 应用标识 / 飞书应用密钥 / 超时秒，
        /// 以及台账压进来的 知识空间标识 / 需求文档父节点 / 多维表格标识 / 任务表标识；
        /// 载荷可给 空间标题 / 需求父节点标题 / 多维表格标题 / 任务表名 与 干跑（缺省 true）。</param>
        public static BridgeResponse Ensure(BridgeRequest request)
        {
            var appId = ReadConfigurationString(request, "应用标识", "");
            var secretKey = ReadConfigurationString(request, "飞书应用密钥", "");
            var timeoutSeconds = ReadConfigurationInt(request, "超时秒", DefaultTimeoutSeconds);
            var isDryRun = ReadPayloadBool(request, "干跑", defaultValue: true);

            if (appId.Length == 0)
            {
                return Failure("凭据无效", "应用标识未配置（配置键「应用标识」为空）", retryable: false);
            }

            if (secretKey.Length == 0)
            {
                return Failure("凭据无效", "飞书应用密钥未配置（配置键「飞书应用密钥」为空）", retryable: false);
            }

            var state = new EnsureState
            {
                AppId = appId,
                SecretKey = secretKey,
                TimeoutSeconds = timeoutSeconds,
                IsDryRun = isDryRun,
                SpaceTitle = ReadPayloadString(request, "空间标题", DefaultSpaceTitle),
                RequirementDocumentParentTitle = ReadPayloadString(request, "需求文档父节点标题", DefaultRequirementDocumentParentTitle),
                ModulePlanParentTitle = ReadPayloadString(request, "模块策划案父节点标题", DefaultModulePlanParentTitle),
                BitableTitle = ReadPayloadString(request, "多维表格标题", DefaultBitableTitle),
                TaskTableName = ReadPayloadString(request, "任务表名", DefaultTaskTableName)
            };

            state.SpaceId = ReadConfigurationString(request, SpaceKey, "");
            state.RequirementDocumentParent = ReadConfigurationString(request, RequirementDocumentParentKey, "");
            state.ModulePlanParent = ReadConfigurationString(request, ModulePlanParentKey, "");
            state.GameDesignDocument = ReadConfigurationString(request, GameDesignDocumentKey, "");
            state.ArtDesignDocument = ReadConfigurationString(request, ArtDesignDocumentKey, "");
            state.BitableToken = ReadConfigurationString(request, BitableKey, "");
            state.TaskTableId = ReadConfigurationString(request, TaskTableKey, "");

            if (!EnsureSpace(state, out var spaceFailure))
            {
                return spaceFailure;
            }

            if (!EnsureRequirementDocumentParent(state, out var parentFailure))
            {
                return parentFailure;
            }

            if (!EnsureModulePlanParent(state, out var planParentFailure))
            {
                return planParentFailure;
            }

            if (!EnsureBitable(state, out var bitableFailure))
            {
                return bitableFailure;
            }

            if (!EnsureTaskTable(state, out var tableFailure))
            {
                return tableFailure;
            }

            // 设计库那两份文档跟别的对象一视同仁：**没有就建，有就沿用**，
            // 建完 id 回填台账（换台机器不会重建一份）。
            if (!EnsureDesignDocument(state, GameDesignDocumentKey, state.GameDesignDocument,
                state.GameDesignDocumentTitle, out var gameFailure, token => state.GameDesignDocument = token))
            {
                return gameFailure;
            }

            if (!EnsureDesignDocument(state, ArtDesignDocumentKey, state.ArtDesignDocument,
                state.ArtDesignDocumentTitle, out var artFailure, token => state.ArtDesignDocument = token))
            {
                return artFailure;
            }

            var objects = new JsonObject
            {
                [SpaceKey] = state.SpaceId,
                [RequirementDocumentParentKey] = state.RequirementDocumentParent,
                [ModulePlanParentKey] = state.ModulePlanParent,
                [BitableKey] = state.BitableToken,
                [TaskTableKey] = state.TaskTableId,
                [GameDesignDocumentKey] = state.GameDesignDocument,
                [ArtDesignDocumentKey] = state.ArtDesignDocument
            };

            var payload = new JsonObject
            {
                ["干跑"] = isDryRun,
                ["对象"] = objects,
                ["新建"] = ToArray(state.Created),
                ["沿用"] = ToArray(state.Reused),
                ["重建"] = ToArray(state.Recreated)
            };

            if (state.SkippedColumns.Count > 0)
            {
                payload["建不出来的列"] = ToArray(state.SkippedColumns);
            }

            return Success(JsonSerializer.SerializeToElement(payload));
        }

        /// <summary>确保知识空间在：验旧的还在不在，不在就建一个新的。</summary>
        private static bool EnsureSpace(EnsureState state, out BridgeResponse failure)
        {
            failure = null;

            if (state.SpaceId.Length > 0)
            {
                var probe = FeishuClient.Send("GET", FeishuClient.WikiSpaceUrl(state.SpaceId), null, state.AppId, state.SecretKey, state.TimeoutSeconds);
                if (probe.Succeeded)
                {
                    state.Reused.Add(SpaceKey + "=" + state.SpaceId);
                    return true;
                }

                if (IsPermissionDenied(probe))
                {
                    failure = PermissionFailure(SpaceKey, state.SpaceId, "这个知识空间");
                    return false;
                }

                // 空间真的不在了（多半是人在飞书里删了）。死号不留：当作没有，重新建一个。
                state.Recreated.Add(SpaceKey + "（旧的 " + state.SpaceId + " 已经不在了）");
                state.SpaceId = "";
            }

            if (state.IsDryRun)
            {
                state.Created.Add(SpaceKey + "：将建一个叫「" + state.SpaceTitle + "」的知识空间");
                state.SpaceId = "<干跑未建>";
                return true;
            }

            var body = new JsonObject
            {
                ["name"] = state.SpaceTitle,
                ["description"] = "由创作管线自动创建：需求文档与任务表都住在这里。"
            }.ToJsonString();

            var call = FeishuClient.Send("POST", FeishuClient.WikiSpacesUrl(), body, state.AppId, state.SecretKey, state.TimeoutSeconds);
            if (!call.Succeeded)
            {
                // 建知识空间这一支飞书**只认用户身份**（user_access_token），应用身份一律回
                // 「Invalid access token」。这不是配错了什么，是这个接口本来就不给应用建——
                // 直接把原始 HTTP 400 抛给人，他会去翻密钥、翻权限，翻半天翻不到东西。
                // 其余三样（节点、多维表格、表）没有这个限制，空间一旦到位就全能自动建。
                failure = Failure(
                    "凭据无效",
                    "建知识空间这一步飞书只认用户身份，应用建不了（接口回的是「Invalid access token」）。"
                    + "空间得你手工建一个：飞书 → 知识库 → 新建知识空间 → 设置 → 成员 → 把这个应用加进来给「可编辑」，"
                    + "然后把 space_id 填进配置。**只有空间这一样要手工**——填完再跑一次，"
                    + "需求父节点、多维表格、任务表都会自己建出来并回填台账。"
                    + "（原始回复：" + (call.Response?.Error?.HumanText ?? "") + "）",
                    retryable: false);
                return false;
            }

            var spaceId = ReadString(call.ResponseBody, "data", "space", "space_id");
            if (spaceId.Length == 0)
            {
                failure = Failure("下游报错", "建知识空间的响应里没有 space_id，没法证明真建出来了", retryable: false);
                return false;
            }

            state.SpaceId = spaceId;
            state.Created.Add(SpaceKey + "=" + spaceId + "（新建，标题「" + state.SpaceTitle + "」）");
            return true;
        }

        /// <summary>确保需求文档的父节点在：它对应仓库里的 Pools/Requirements/，一条需求一个子节点挂在它下面。</summary>
        private static bool EnsureRequirementDocumentParent(EnsureState state, out BridgeResponse failure)
        {
            return EnsureNode(
                state,
                RequirementDocumentParentKey,
                state.RequirementDocumentParent,
                state.RequirementDocumentParentTitle,
                "docx",
                out failure,
                token => state.RequirementDocumentParent = token);
        }

        /// <summary>确保模块策划案的父节点在：它对应仓库里的 Pools/Designs/Modules/，一个模块一个子节点。</summary>
        /// <param name="state">这一趟的状态。</param>
        /// <param name="failure">失败时的协议响应。</param>
        private static bool EnsureModulePlanParent(EnsureState state, out BridgeResponse failure)
        {
            return EnsureNode(
                state,
                ModulePlanParentKey,
                state.ModulePlanParent,
                state.ModulePlanParentTitle,
                "docx",
                out failure,
                token => state.ModulePlanParent = token);
        }

        /// <summary>
        /// 确保设计库的一份文档在。**走的是与需求文档同一条 EnsureNode**——
        /// 那条路已经跑通过，不另起一套。
        ///
        /// 选文档而不是多维表格：设计库要看的是「有哪些资产、什么风格」，
        /// 一张 markdown 表格就够；而多维表格要另建 base 里的表、另写一套列定义，
        /// 换来的筛选能力这里用不上。真需要筛的那天再换，台账里换个键的事。
        /// </summary>
        /// <param name="state">这一轮的状态。</param>
        /// <param name="key">台账键名。</param>
        /// <param name="current">台账里现有的值。</param>
        /// <param name="title">要建时叫什么。</param>
        /// <param name="failure">失败响应。</param>
        /// <param name="assign">把最终值写回状态。</param>
        private static bool EnsureDesignDocument(
            EnsureState state,
            string key,
            string current,
            string title,
            out BridgeResponse failure,
            Action<string> assign)
        {
            return EnsureNode(state, key, current, title, "docx", out failure, assign);
        }

        /// <summary>确保多维表格在：它也是知识空间下的一个节点（obj_type=bitable），obj_token 就是 app_token。</summary>
        private static bool EnsureBitable(EnsureState state, out BridgeResponse failure)
        {
            return EnsureNode(
                state,
                BitableKey,
                state.BitableToken,
                state.BitableTitle,
                "bitable",
                out failure,
                token => state.BitableToken = token);
        }

        /// <summary>
        /// 确保知识空间下的某个节点在。验的是**它还在不在**，不是「叫什么名字」——
        /// 人在飞书里改个名是常事，按名字认会当场认丢。
        /// </summary>
        /// <param name="state">这一轮的状态。</param>
        /// <param name="key">台账里的键名，用在账里。</param>
        /// <param name="current">台账里现有的值；空表示没有。</param>
        /// <param name="title">要建时叫什么。</param>
        /// <param name="objectType">节点类型：docx / bitable。</param>
        /// <param name="failure">失败响应；返回 true 时不要看它。</param>
        /// <param name="assign">把最终值写回状态。</param>
        private static bool EnsureNode(
            EnsureState state,
            string key,
            string current,
            string title,
            string objectType,
            out BridgeResponse failure,
            Action<string> assign)
        {
            failure = null;

            if (current.Length > 0)
            {
                var probe = FeishuClient.Send("GET", FeishuClient.WikiGetNodeUrl(current), null, state.AppId, state.SecretKey, state.TimeoutSeconds);
                if (probe.Succeeded)
                {
                    state.Reused.Add(key + "=" + current);
                    assign(current);
                    return true;
                }

                if (IsPermissionDenied(probe))
                {
                    failure = PermissionFailure(key, current, "这个节点");
                    return false;
                }

                state.Recreated.Add(key + "（旧的 " + current + " 已经不在了）");
            }

            if (state.IsDryRun)
            {
                state.Created.Add(key + "：将在空间下建一个叫「" + title + "」的 " + objectType + " 节点");
                assign("<干跑未建>");
                return true;
            }

            if (state.SpaceId.Length == 0 || state.SpaceId.StartsWith("<", StringComparison.Ordinal))
            {
                failure = Failure("请求不合协议", "还没有知识空间，建不了它下面的节点", retryable: false);
                return false;
            }

            var body = new JsonObject
            {
                ["obj_type"] = objectType,
                ["node_type"] = "origin",
                ["title"] = title
            }.ToJsonString();

            var call = FeishuClient.Send("POST", FeishuClient.WikiNodesUrl(state.SpaceId), body, state.AppId, state.SecretKey, state.TimeoutSeconds);
            if (!call.Succeeded)
            {
                failure = call.Response;
                return false;
            }

            // 文档节点认 node_token（挂子节点要用它），多维表格认 obj_token（那就是 app_token）。
            // 两者是不同的东西，取错一个后面每一次调用都会走岔。
            var isBitable = string.Equals(objectType, "bitable", StringComparison.Ordinal);
            var value = isBitable
                ? ReadString(call.ResponseBody, "data", "node", "obj_token")
                : ReadString(call.ResponseBody, "data", "node", "node_token");
            if (value.Length == 0)
            {
                failure = Failure(
                    "下游报错",
                    "建节点的响应里没有 " + (isBitable ? "obj_token" : "node_token") + "，没法证明真建出来了",
                    retryable: false);
                return false;
            }

            assign(value);
            state.Created.Add(key + "=" + value + "（新建，标题「" + title + "」）");
            return true;
        }

        /// <summary>确保任务表在：台账里那张还在就用，不在就按名字找，再没有就照任务模板建一张。</summary>
        private static bool EnsureTaskTable(EnsureState state, out BridgeResponse failure)
        {
            failure = null;

            if (state.IsDryRun)
            {
                if (state.TaskTableId.Length > 0)
                {
                    state.Reused.Add(TaskTableKey + "=" + state.TaskTableId + "（干跑没去验它还在不在）");
                    return true;
                }

                state.Created.Add(TaskTableKey + "：将建一张叫「" + state.TaskTableName + "」的表，"
                    + TaskTableColumns.Length + " 列");
                state.Created.Add("是否延期（公式列，建完表再单独补一刀）");
                state.TaskTableId = "<干跑未建>";
                return true;
            }

            if (state.BitableToken.Length == 0 || state.BitableToken.StartsWith("<", StringComparison.Ordinal))
            {
                failure = Failure("请求不合协议", "还没有多维表格，建不了里面的表", retryable: false);
                return false;
            }

            var listCall = FeishuClient.Send(
                "GET",
                FeishuClient.BitableUrl(state.BitableToken, "tables?page_size=100"),
                null,
                state.AppId,
                state.SecretKey,
                state.TimeoutSeconds);
            if (!listCall.Succeeded)
            {
                failure = listCall.Response;
                return false;
            }

            var existingById = "";
            var existingByName = "";
            if (listCall.ResponseBody.ValueKind == JsonValueKind.Object
                && listCall.ResponseBody.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var tableId = ReadString(item, "table_id");
                    var name = ReadString(item, "name");
                    if (state.TaskTableId.Length > 0 && string.Equals(tableId, state.TaskTableId, StringComparison.Ordinal))
                    {
                        existingById = tableId;
                    }

                    if (string.Equals(name, state.TaskTableName, StringComparison.Ordinal))
                    {
                        existingByName = tableId;
                    }
                }
            }

            if (existingById.Length > 0)
            {
                state.Reused.Add(TaskTableKey + "=" + existingById);
                return true;
            }

            if (state.TaskTableId.Length > 0)
            {
                state.Recreated.Add(TaskTableKey + "（旧的 " + state.TaskTableId + " 已经不在了）");
            }

            if (existingByName.Length > 0)
            {
                // 按名字捡回来：多半是台账丢了而表还在。捡回来比再建一张强——
                // 再建一张的后果是两张同名表，人分不出该看哪一张。
                state.TaskTableId = existingByName;
                state.Reused.Add(TaskTableKey + "=" + existingByName + "（台账里没有，按表名「" + state.TaskTableName + "」认回来的）");
                return true;
            }

            var fields = new JsonArray();
            foreach (var column in TaskTableColumns)
            {
                fields.Add(column.ToJson());
            }

            var body = new JsonObject
            {
                ["table"] = new JsonObject
                {
                    ["name"] = state.TaskTableName,
                    ["fields"] = fields
                }
            }.ToJsonString();

            var createCall = FeishuClient.Send(
                "POST",
                FeishuClient.BitableUrl(state.BitableToken, "tables"),
                body,
                state.AppId,
                state.SecretKey,
                state.TimeoutSeconds);
            if (!createCall.Succeeded)
            {
                failure = createCall.Response;
                return false;
            }

            var createdId = ReadString(createCall.ResponseBody, "data", "table_id");
            if (createdId.Length == 0)
            {
                failure = Failure("下游报错", "建表的响应里没有 table_id，没法证明真建出来了", retryable: false);
                return false;
            }

            state.TaskTableId = createdId;
            state.Created.Add(TaskTableKey + "=" + createdId + "（新建，" + TaskTableColumns.Length + " 列）");

            AddOverdueColumn(state);
            RemoveDefaultTable(state);
            return true;
        }

        /// <summary>
        /// 补上「是否延期」这一列。**公式列走的是「新增字段」那一刀，不是建表那一刀**——
        /// 建表时把公式塞进 fields 里飞书不收，建完单独加一次却是好的（实证过）。
        /// 加不上不算建表失败：表本身已经好了，缺一列在账里说出来就行。
        /// </summary>
        /// <param name="state">这一轮的状态。</param>
        private static void AddOverdueColumn(EnsureState state)
        {
            var body = new JsonObject
            {
                ["field_name"] = OverdueColumnName,
                ["type"] = FormulaTypeCode,
                ["property"] = new JsonObject { ["formula_expression"] = OverdueFormula }
            }.ToJsonString();

            var call = FeishuClient.Send(
                "POST",
                FeishuClient.BitableUrl(state.BitableToken, "tables/" + Uri.EscapeDataString(state.TaskTableId) + "/fields"),
                body,
                state.AppId,
                state.SecretKey,
                state.TimeoutSeconds);
            if (call.Succeeded)
            {
                state.Created.Add(OverdueColumnName + "（公式列）");
                return;
            }

            state.SkippedColumns.Add(OverdueColumnName + "（公式列没加上：" + (call.Response?.Error?.HumanText ?? "") + "）");
        }

        /// <summary>
        /// 把飞书建 base 时自带的那张默认空表删掉。
        ///
        /// 它排在第一位，人点进多维表格看到的是它——一张空表，于是以为「任务行没加上」。
        /// 这是我们建 base 时飞书塞的，不是人的数据，清掉是分内事。
        ///
        /// **条件卡得很死**：只删「不是我们刚建的那张任务表」且「一条记录都没有」的表。
        /// 删下游的东西只有一次机会，宁可留着碍眼，也不许删掉人正在用的表。
        /// </summary>
        /// <param name="state">这一轮的状态。</param>
        private static void RemoveDefaultTable(EnsureState state)
        {
            var listCall = FeishuClient.Send(
                "GET",
                FeishuClient.BitableUrl(state.BitableToken, "tables?page_size=100"),
                null,
                state.AppId,
                state.SecretKey,
                state.TimeoutSeconds);
            if (!listCall.Succeeded)
            {
                return;
            }

            if (listCall.ResponseBody.ValueKind != JsonValueKind.Object
                || !listCall.ResponseBody.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in items.EnumerateArray())
            {
                var tableId = ReadString(item, "table_id");
                if (tableId.Length == 0 || string.Equals(tableId, state.TaskTableId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsEmptyTable(state, tableId))
                {
                    continue;
                }

                var deleteCall = FeishuClient.Send(
                    "DELETE",
                    FeishuClient.BitableUrl(state.BitableToken, "tables/" + Uri.EscapeDataString(tableId)),
                    null,
                    state.AppId,
                    state.SecretKey,
                    state.TimeoutSeconds);
                if (deleteCall.Succeeded)
                {
                    state.Created.Add("顺手删掉建 base 时自带的空表「" + ReadString(item, "name") + "」");
                }
            }
        }

        /// <summary>这张表是不是一条记录都没有。查不动一律当「有」——删表这件事只许在确凿时做。</summary>
        /// <param name="state">这一轮的状态。</param>
        /// <param name="tableId">表 id。</param>
        private static bool IsEmptyTable(EnsureState state, string tableId)
        {
            var call = FeishuClient.Send(
                "GET",
                FeishuClient.BitableUrl(state.BitableToken, "tables/" + Uri.EscapeDataString(tableId) + "/records?page_size=1"),
                null,
                state.AppId,
                state.SecretKey,
                state.TimeoutSeconds);
            if (!call.Succeeded)
            {
                return false;
            }

            return !(call.ResponseBody.ValueKind == JsonValueKind.Object
                && call.ResponseBody.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array
                && items.GetArrayLength() > 0);
        }

        /// <summary>「是否延期」这一列的列名。</summary>
        private const string OverdueColumnName = "是否延期";

        /// <summary>飞书公式字段的类型码。</summary>
        private const int FormulaTypeCode = 20;

        /// <summary>
        /// 「是否延期」的公式：有预计完成日期、已经过了、而实际完成日期还空着，才算延期。
        /// 三个条件缺一不可——只看「过没过预计日期」的话，已经做完的任务也会被判成延期。
        /// </summary>
        private const string OverdueFormula =
            "IF(AND(CurrentValue.[预计完成日期],TODAY()>CurrentValue.[预计完成日期],NOT(CurrentValue.[实际完成日期])),\"是\",\"否\")";

        /// <summary>任务表的一列：名字、飞书字段类型码、以及类型自己的属性。</summary>
        private sealed class TaskColumn
        {
            /// <summary>构造一列。</summary>
            /// <param name="name">列名。</param>
            /// <param name="type">飞书字段类型码。</param>
            /// <param name="property">类型属性；没有给 null。</param>
            public TaskColumn(string name, int type, JsonObject property = null)
            {
                Name = name;
                Type = type;
                Property = property;
            }

            /// <summary>列名。</summary>
            public string Name { get; }

            /// <summary>飞书字段类型码。</summary>
            public int Type { get; }

            /// <summary>类型属性；没有给 null。</summary>
            public JsonObject Property { get; }

            /// <summary>摊成建表接口要的形状。</summary>
            public JsonObject ToJson()
            {
                var field = new JsonObject
                {
                    ["field_name"] = Name,
                    ["type"] = Type
                };
                if (Property != null)
                {
                    field["property"] = Property.DeepClone();
                }

                return field;
            }
        }

        /// <summary>
        /// 任务表的列，照飞书任务模板那张表来（文本 1 / 单选 3 / 日期 5 / 人员 11 / 超链接 15）。
        ///
        /// 比模板多两列：**需求id** 与 **需求文档**。任务与需求得连得上——
        /// 飞书的关联列只能关联同一个多维表格里的记录，而需求是知识空间里的一份文档，
        /// 关联不过去，所以用一列超链接存文档地址（`doc.push` 推完会把它交回来）。
        ///
        /// 模板里的「是否延期」是公式列，建表接口不收公式表达式，只能建完手工补。
        /// 少了什么要**说出来**，不许默默少一列。
        /// </summary>
        private static readonly TaskColumn[] TaskTableColumns =
        {
            new TaskColumn("任务描述", 1),
            new TaskColumn("需求id", 1),
            new TaskColumn("需求文档", 15),
            new TaskColumn("任务执行人", 11, new JsonObject { ["multiple"] = false }),
            new TaskColumn("进展", 3, new JsonObject
            {
                ["options"] = new JsonArray
                {
                    new JsonObject { ["name"] = "未开始" },
                    new JsonObject { ["name"] = "进行中" },
                    new JsonObject { ["name"] = "已停滞" },
                    new JsonObject { ["name"] = "已完成" }
                }
            }),
            new TaskColumn("重要紧急程度", 3, new JsonObject
            {
                ["options"] = new JsonArray
                {
                    new JsonObject { ["name"] = "重要紧急" },
                    new JsonObject { ["name"] = "重要不紧急" },
                    new JsonObject { ["name"] = "紧急不重要" },
                    new JsonObject { ["name"] = "不重要不紧急" }
                }
            }),
            new TaskColumn("开始日期", 5, new JsonObject { ["date_formatter"] = "yyyy/MM/dd", ["auto_fill"] = false }),
            new TaskColumn("预计完成日期", 5, new JsonObject { ["date_formatter"] = "yyyy/MM/dd", ["auto_fill"] = false }),
            new TaskColumn("实际完成日期", 5, new JsonObject { ["date_formatter"] = "yyyy/MM/dd", ["auto_fill"] = false }),
            new TaskColumn("最新进展记录", 1),
            new TaskColumn("任务情况总结", 1)
        };

        /// <summary>一轮 ensure 的状态：凭据、四样对象的当前值、以及给人看的账。</summary>
        private sealed class EnsureState
        {
            /// <summary>飞书应用标识。</summary>
            public string AppId;

            /// <summary>飞书应用密钥，只进 HTTP 请求，绝不进任何文案。</summary>
            public string SecretKey;

            /// <summary>单次调用超时秒数。</summary>
            public int TimeoutSeconds;

            /// <summary>只算不发。</summary>
            public bool IsDryRun;

            /// <summary>知识空间 space_id。</summary>
            public string SpaceId = "";

            /// <summary>模块策划案父节点 node_token。</summary>
            public string ModulePlanParent = "";

            /// <summary>模块策划案父节点标题。</summary>
            public string ModulePlanParentTitle = DefaultModulePlanParentTitle;

            /// <summary>需求文档父节点 node_token。</summary>
            public string RequirementDocumentParent = "";

            /// <summary>多维表格 app_token。</summary>
            public string BitableToken = "";

            /// <summary>任务表 table_id。</summary>
            public string TaskTableId = "";

            /// <summary>要建知识空间时叫什么。</summary>
            public string SpaceTitle = DefaultSpaceTitle;

            /// <summary>要建需求父节点时叫什么。</summary>
            public string RequirementDocumentParentTitle = DefaultRequirementDocumentParentTitle;

            /// <summary>要建多维表格节点时叫什么。</summary>
            public string BitableTitle = DefaultBitableTitle;

            /// <summary>要建任务表时叫什么。</summary>
            public string TaskTableName = DefaultTaskTableName;

            /// <summary>策划设计库文档的节点 token。</summary>
            public string GameDesignDocument = "";

            /// <summary>美术设计库文档的节点 token。</summary>
            public string ArtDesignDocument = "";

            /// <summary>要建策划设计库文档时叫什么。</summary>
            public string GameDesignDocumentTitle = DefaultGameDesignDocumentTitle;

            /// <summary>要建美术设计库文档时叫什么。</summary>
            public string ArtDesignDocumentTitle = DefaultArtDesignDocumentTitle;

            /// <summary>这一轮新建了哪些。</summary>
            public readonly List<string> Created = new List<string>();

            /// <summary>这一轮沿用了哪些。</summary>
            public readonly List<string> Reused = new List<string>();

            /// <summary>哪些是因为旧的已经不在而重建的。</summary>
            public readonly List<string> Recreated = new List<string>();

            /// <summary>建不出来的列，要如实报。</summary>
            public readonly List<string> SkippedColumns = new List<string>();
        }

        /// <summary>
        /// 这次失败是不是「有这个东西但应用没权限」。**它与「不存在」处置完全相反**：
        /// 不存在该重新建一个，没权限该停下来让人去授权——把没权限当成不存在，
        /// 每跑一次就在人家知识库里多建一个空间，越建越多还谁都没发现。
        /// 131006 是知识库那支的权限码；99991672 是凭据整个不对，那更不该往下建。
        /// </summary>
        /// <param name="call">一次探测调用的结果。</param>
        private static bool IsPermissionDenied(FeishuClient.HttpCall call)
        {
            return call != null && (call.BusinessCode == 131006 || call.BusinessCode == 99991672);
        }

        /// <summary>没权限时的失败响应：说清是哪一格、哪个 id，以及两条出路。</summary>
        /// <param name="key">台账里的键名。</param>
        /// <param name="identifier">那个对象的 id。</param>
        /// <param name="what">人话说这是个什么东西。</param>
        private static BridgeResponse PermissionFailure(string key, string identifier, string what)
        {
            return Failure(
                "凭据无效",
                what + "（" + key + "=" + identifier + "）是在的，但这个应用没权限读它，所以我没敢当它不存在去重建——"
                + "重建会在你的知识库里多出一个空壳。两条路：一是去飞书把应用加成协作者并给编辑权，"
                + "二是把台账（" + DownstreamObjectLedger.RelativeLedgerPath() + "）与 local.json 里的这个 id 清掉，"
                + "让链路自己建一个属于它的。",
                retryable: false);
        }

        /// <summary>把一串字符串摊成 JSON 数组。</summary>
        private static JsonArray ToArray(IReadOnlyList<string> values)
        {
            var array = new JsonArray();
            foreach (var value in values)
            {
                array.Add(value);
            }

            return array;
        }

        /// <summary>成功响应。</summary>
        private static BridgeResponse Success(JsonElement payload)
        {
            return BridgeResponse.Success(ContractVersion, payload);
        }

        /// <summary>失败响应。</summary>
        private static BridgeResponse Failure(string code, string humanText, bool retryable)
        {
            return BridgeResponse.Failure(ContractVersion, code, humanText, retryable);
        }

        /// <summary>读请求配置里的字符串键；缺失给缺省值。</summary>
        private static string ReadConfigurationString(BridgeRequest request, string key, string fallback)
        {
            if (request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? fallback;
            }

            return fallback;
        }

        /// <summary>读请求配置里的整数键；缺失、类型不对给缺省值。</summary>
        private static int ReadConfigurationInt(BridgeRequest request, string key, int fallback)
        {
            if (request.Configuration.ValueKind == JsonValueKind.Object
                && request.Configuration.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out var number))
            {
                return number;
            }

            return fallback;
        }

        /// <summary>读载荷里的字符串键；缺失或为空给缺省值。</summary>
        private static string ReadPayloadString(BridgeRequest request, string key, string fallback)
        {
            if (request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString() ?? "";
                if (text.Trim().Length > 0)
                {
                    return text;
                }
            }

            return fallback;
        }

        /// <summary>读载荷里的布尔键；缺失给缺省值。</summary>
        private static bool ReadPayloadBool(BridgeRequest request, string key, bool defaultValue)
        {
            if (request.Payload.ValueKind == JsonValueKind.Object
                && request.Payload.TryGetProperty(key, out var element))
            {
                if (element.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (element.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
            }

            return defaultValue;
        }

        /// <summary>从响应体里按一串键逐级读字符串；中途缺一级就给空串。</summary>
        private static string ReadString(JsonElement element, params string[] path)
        {
            var current = element;
            foreach (var key in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
                {
                    return "";
                }
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : "";
        }
    }
}
