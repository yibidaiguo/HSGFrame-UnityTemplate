using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一条需求在进度上的那一行：id、标题与各字段的当前值。</summary>
    public sealed class ProgressEntry
    {
        /// <summary>
        /// 构造一行。
        /// </summary>
        /// <param name="identifier">需求 id，如「REQ-0042」。</param>
        /// <param name="fields">字段名 → 值；值一律是字符串，因为下游那一侧读回来也只能是字符串。</param>
        public ProgressEntry(string identifier, IReadOnlyDictionary<string, string> fields)
        {
            Identifier = identifier ?? "";
            Fields = fields ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>需求 id。</summary>
        public string Identifier { get; }

        /// <summary>字段名 → 值。</summary>
        public IReadOnlyDictionary<string, string> Fields { get; }

        /// <summary>读一个字段；没有给空串。</summary>
        /// <param name="name">字段名。</param>
        public string Value(string name)
        {
            return Fields.TryGetValue(name ?? "", out var value) ? value ?? "" : "";
        }
    }

    /// <summary>
    /// 一份项目进度快照：逐条需求的那几格 + 一组全局数字。
    ///
    /// 同一个形状在三处用：**仓库侧**（<see cref="CollectFromRepository"/> 从池子与 _Tasks 里算出来）、
    /// **下游侧**（任务表读回来，见 <see cref="FromDownstreamRows"/>）、
    /// **上次同步的基线**（<see cref="ProgressSyncBaseline"/> 存的就是它）。
    /// 三份同形状才比得了——形状各写各的话，「这一格变没变」就成了一堆一次性转换代码，
    /// 而漏掉一个字段的后果是它永远同步不上，且没有任何地方会报错。
    /// </summary>
    public sealed class ProgressSnapshot
    {
        /// <summary>
        /// 构造一份快照。
        /// </summary>
        /// <param name="entries">逐条需求的行，按 id 序。</param>
        /// <param name="global">全局数字：门禁、队列、模式、计数。</param>
        public ProgressSnapshot(IReadOnlyList<ProgressEntry> entries, IReadOnlyDictionary<string, string> global)
        {
            Entries = entries ?? Array.Empty<ProgressEntry>();
            Global = global ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>逐条需求的行，按 id 序。</summary>
        public IReadOnlyList<ProgressEntry> Entries { get; }

        /// <summary>全局数字：门禁结论、队列长度、引擎模式、需求数、未决冲突数。</summary>
        public IReadOnlyDictionary<string, string> Global { get; }

        /// <summary>按 id 找一行；没有给 null。</summary>
        /// <param name="identifier">需求 id。</param>
        public ProgressEntry Find(string identifier)
        {
            return Entries.FirstOrDefault(entry => string.Equals(entry.Identifier, identifier, StringComparison.Ordinal));
        }

        /// <summary>字段名：需求标题。</summary>
        public const string TitleField = "标题";

        /// <summary>字段名：任务状态机的阶段。</summary>
        public const string StageField = "阶段";

        /// <summary>字段名：门禁。</summary>
        public const string GateField = "门禁";

        /// <summary>字段名：产出件数。</summary>
        public const string OutputField = "产出";

        /// <summary>
        /// 从仓库算一份进度快照：池子里每条需求一行，_Tasks 里有状态就取状态，没有就写「尚未开跑」。
        ///
        /// **一个异常都不许漏出去**：这份快照同时供命令、面板与出站三处用，
        /// 某条需求的 JSON 坏了不该让整个进度页打不开——那一行降级成文字，其余照常。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        public static ProgressSnapshot CollectFromRepository(string repositoryRoot, string poolRoot)
        {
            var gateConclusion = ReadGateConclusion(repositoryRoot);
            var entries = new List<ProgressEntry>();

            foreach (var identifier in ListRequirementIdentifiers(poolRoot))
            {
                var fields = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [TitleField] = ReadRequirementTitle(poolRoot, identifier),
                    [StageField] = DescribeStage(repositoryRoot, identifier),
                    [GateField] = DescribeGate(repositoryRoot, identifier, gateConclusion),
                    [OutputField] = DescribeOutputs(repositoryRoot, identifier)
                };
                entries.Add(new ProgressEntry(identifier, fields));
            }

            var global = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["门禁"] = gateConclusion,
                ["引擎模式"] = EngineSettings.ToChineseName(EngineSettings.Load(repositoryRoot).Mode),
                ["队列长度"] = ExecutionQueue.Load(poolRoot).Entries.Count.ToString(CultureInfo.InvariantCulture),
                ["需求数"] = entries.Count.ToString(CultureInfo.InvariantCulture),
                ["未决冲突"] = ConflictList.Load(poolRoot).PendingCount().ToString(CultureInfo.InvariantCulture)
            };

            return new ProgressSnapshot(entries, global);
        }

        /// <summary>
        /// 把下游任务表读回来的那批行折成一份快照：一行一条需求，列名按权威侧表翻回字段名。
        /// **没有「需求id」那一列的行直接丢掉**——它对不上任何一条需求，
        /// 硬塞进来会在比对时变成一条「下游多出来一条需求」的假冲突。
        /// </summary>
        /// <param name="rows">下游行：每行是 列名 → 值。</param>
        /// <param name="schema">权威侧表，用来把列名翻成字段名。</param>
        /// <param name="identifierColumn">存需求 id 的列名。</param>
        public static ProgressSnapshot FromDownstreamRows(
            IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
            ProgressSyncSchema schema,
            string identifierColumn)
        {
            var entries = new List<ProgressEntry>();
            foreach (var row in rows ?? Array.Empty<IReadOnlyDictionary<string, string>>())
            {
                if (row == null || !row.TryGetValue(identifierColumn ?? "", out var identifier) || string.IsNullOrWhiteSpace(identifier))
                {
                    continue;
                }

                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var field in schema?.Fields ?? Array.Empty<ProgressSyncField>())
                {
                    fields[field.Name] = row.TryGetValue(field.DownstreamColumn, out var value) ? value ?? "" : "";
                }

                entries.Add(new ProgressEntry(identifier.Trim(), fields));
            }

            entries.Sort((left, right) => string.CompareOrdinal(left.Identifier, right.Identifier));
            return new ProgressSnapshot(entries, new Dictionary<string, string>(StringComparer.Ordinal));
        }

        /// <summary>
        /// 把回流账并进这份快照：**仓库侧对策划端那几格的值，源头是回流账，不是空串**。
        ///
        /// 不并的后果是这条链最隐蔽的一种病：工程侧对「进展」这类格永远拿不出值，
        /// 于是每一轮比对都得出「下游单边改过」，同样两格被反复"回流"一次又一次，永不收敛；
        /// 更糟的是等下游再改一次，`engineMoved` 与 `downstreamMoved` 就会同时为真，
        /// 判出一条**假冲突**——而人打开冲突页看到的是一件根本没发生过的争抢。
        ///
        /// 真跑出来的：第 3 轮回流 2 格（对），第 4 轮什么都没动却又回流同样 2 格（错）。
        /// </summary>
        /// <param name="inbound">回流账快照。</param>
        /// <param name="plannerFieldNames">归策划端的字段名——只并这几格，工程格一格都不许被盖。</param>
        public ProgressSnapshot MergePlannerFields(ProgressSnapshot inbound, IEnumerable<string> plannerFieldNames)
        {
            var plannerFields = new HashSet<string>(plannerFieldNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (plannerFields.Count == 0)
            {
                return this;
            }

            var merged = new List<ProgressEntry>();
            foreach (var entry in Entries)
            {
                var fields = new Dictionary<string, string>(entry.Fields, StringComparer.Ordinal);
                var inboundEntry = inbound?.Find(entry.Identifier);
                foreach (var name in plannerFields)
                {
                    fields[name] = inboundEntry?.Value(name) ?? "";
                }

                merged.Add(new ProgressEntry(entry.Identifier, fields));
            }

            return new ProgressSnapshot(merged, Global);
        }

        /// <summary>摊成 JSON（基线落盘与面板接口共用一个形状）。</summary>
        public JsonObject ToJson()
        {
            var rows = new JsonArray();
            foreach (var entry in Entries)
            {
                var fields = new JsonObject();
                foreach (var pair in entry.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    fields[pair.Key] = pair.Value;
                }

                rows.Add(new JsonObject { ["id"] = entry.Identifier, ["字段"] = fields });
            }

            var global = new JsonObject();
            foreach (var pair in Global.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                global[pair.Key] = pair.Value;
            }

            return new JsonObject { ["需求"] = rows, ["全局"] = global };
        }

        /// <summary>从 <see cref="ToJson"/> 的形状读回来；形状不对时给一份空快照。</summary>
        /// <param name="root">JSON 对象。</param>
        public static ProgressSnapshot FromJson(JsonNode root)
        {
            var entries = new List<ProgressEntry>();
            var global = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root is not JsonObject rootObject)
            {
                return new ProgressSnapshot(entries, global);
            }

            if (rootObject["需求"] is JsonArray rows)
            {
                foreach (var row in rows)
                {
                    if (row is not JsonObject rowObject)
                    {
                        continue;
                    }

                    var identifier = rowObject["id"]?.GetValue<string>() ?? "";
                    if (identifier.Length == 0)
                    {
                        continue;
                    }

                    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (rowObject["字段"] is JsonObject fieldObject)
                    {
                        foreach (var pair in fieldObject)
                        {
                            fields[pair.Key] = pair.Value?.ToString() ?? "";
                        }
                    }

                    entries.Add(new ProgressEntry(identifier, fields));
                }
            }

            if (rootObject["全局"] is JsonObject globalObject)
            {
                foreach (var pair in globalObject)
                {
                    global[pair.Key] = pair.Value?.ToString() ?? "";
                }
            }

            return new ProgressSnapshot(entries, global);
        }

        /// <summary>列出池子里的需求 id，按序。目录不在时给空表。</summary>
        /// <param name="poolRoot">池子根目录。</param>
        public static IReadOnlyList<string> ListRequirementIdentifiers(string poolRoot)
        {
            var directory = PoolPaths.RequirementsDirectory(poolRoot ?? "");
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.GetDirectories(directory)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>读需求标题；读不到给空串。</summary>
        private static string ReadRequirementTitle(string poolRoot, string identifier)
        {
            var filePath = PoolPaths.RequirementFile(poolRoot, identifier);
            if (!File.Exists(filePath))
            {
                return "";
            }

            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(filePath)))
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("标题", out var title)
                        && title.ValueKind == JsonValueKind.String)
                    {
                        return title.GetString() ?? "";
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return "";
            }

            return "";
        }

        /// <summary>把任务状态说成一格：「阶段/子状态」，没开跑就是「尚未开跑」。</summary>
        private static string DescribeStage(string repositoryRoot, string identifier)
        {
            if (!TaskState.TryLoad(repositoryRoot, identifier, out var state, out _))
            {
                return "尚未开跑";
            }

            var stage = string.IsNullOrEmpty(state.Stage) ? "未知" : state.Stage;
            return string.IsNullOrEmpty(state.SubState) ? stage : stage + "/" + state.SubState;
        }

        /// <summary>
        /// 把门禁说成一格：这条需求卡在某道关卡待审时说那道关卡，
        /// 没卡着就说全局门禁那一次的结论。两者选一是有意的——
        /// 人看这一格想知道的是「这条现在过不过得去」，卡在关卡时那才是真答案。
        /// </summary>
        private static string DescribeGate(string repositoryRoot, string identifier, string gateConclusion)
        {
            if (TaskState.TryLoad(repositoryRoot, identifier, out var state, out _)
                && !string.IsNullOrEmpty(state.PendingGate))
            {
                return "待审：" + state.PendingGate;
            }

            return gateConclusion;
        }

        /// <summary>
        /// 读全局门禁结论。**推法只在 <see cref="GateReportConclusion"/> 里有一份**——
        /// 这里曾经自己照一个不存在的「结论」键读，于是进度页永远说「未跑」，
        /// 而总览页同时说「绿」。两页对同一件事给两个答案，比两页都错更难查。
        /// </summary>
        private static string ReadGateConclusion(string repositoryRoot)
        {
            return GateReportConclusion.Read(repositoryRoot);
        }

        /// <summary>模型文件的扩展名，产出计数时与图分开数。</summary>
        private static readonly string[] ModelExtensions = { ".glb", ".gltf", ".fbx", ".obj", ".usdz" };

        /// <summary>图片文件的扩展名。</summary>
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".tga", ".psd" };

        /// <summary>
        /// 数产出：_Tasks/&lt;需求id&gt;/30-outputs/ 底下的图与模型各几件。
        /// **边车 .provenance.json 不算产出**——它是产出的账不是产出本身，
        /// 算进去的话每出一张图这一格就跳两下。
        /// </summary>
        private static string DescribeOutputs(string repositoryRoot, string identifier)
        {
            var outputRoot = Path.Combine(PipelinePaths.TaskDirectory(repositoryRoot ?? "", identifier), "30-outputs");
            if (!Directory.Exists(outputRoot))
            {
                return "无";
            }

            var imageCount = 0;
            var modelCount = 0;
            var otherCount = 0;
            try
            {
                foreach (var filePath in Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories))
                {
                    if (filePath.EndsWith(".provenance.json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var extension = Path.GetExtension(filePath);
                    if (ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        imageCount++;
                    }
                    else if (ModelExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        modelCount++;
                    }
                    else
                    {
                        otherCount++;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return "数不动";
            }

            if (imageCount + modelCount + otherCount == 0)
            {
                return "无";
            }

            var parts = new List<string>();
            if (imageCount > 0)
            {
                parts.Add("图 " + imageCount.ToString(CultureInfo.InvariantCulture));
            }

            if (modelCount > 0)
            {
                parts.Add("模型 " + modelCount.ToString(CultureInfo.InvariantCulture));
            }

            if (otherCount > 0)
            {
                parts.Add("其他 " + otherCount.ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(" · ", parts);
        }
    }
}
