using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 装机清单里的一件东西：编辑器的桥接包、下游的节点/模型、随仓库走的驱动脚本。
    /// 「状态」只有四种，含义严格区分：已装 / 缺 / 未验（本机看不出来，要跑一次探测或让编辑器解析一次）/
    /// 无需安装。没证据的一律记「未验」，绝不当成「已装」——这一页存在的全部意义就是别把没有的说成有。
    /// </summary>
    public sealed class HostPackageEntry
    {
        /// <summary>
        /// 构造一件装机清单条目。
        /// </summary>
        /// <param name="name">包名 / 依赖名 / 脚本名。</param>
        /// <param name="category">类别：编辑器包 / 节点 / 模型 / lora / 驱动脚本。</param>
        /// <param name="versionRequirement">清单要的版本或 git 记号；没写就是空串。</param>
        /// <param name="state">状态：已装 / 缺 / 未验 / 无需安装。</param>
        /// <param name="evidence">判成这个状态的依据，写成人话（哪个文件、哪个目录、哪次探测）。</param>
        /// <param name="source">来源：git 地址、下载页；没有就是空串。</param>
        /// <param name="installCommand">安装命令；空串表示清单没给，得照来源页面自己装。</param>
        /// <param name="nextStep">下一步动作；已装时为空串。</param>
        public HostPackageEntry(
            string name,
            string category,
            string versionRequirement,
            string state,
            string evidence,
            string source,
            string installCommand,
            string nextStep)
        {
            Name = name ?? "";
            Category = category ?? "";
            VersionRequirement = versionRequirement ?? "";
            State = state ?? "";
            Evidence = evidence ?? "";
            Source = source ?? "";
            InstallCommand = installCommand ?? "";
            NextStep = nextStep ?? "";
        }

        /// <summary>包名 / 依赖名 / 脚本名。</summary>
        public string Name { get; }

        /// <summary>类别：编辑器包 / 节点 / 模型 / lora / 驱动脚本。</summary>
        public string Category { get; }

        /// <summary>清单要的版本或 git 记号；没写就是空串。</summary>
        public string VersionRequirement { get; }

        /// <summary>状态：已装 / 缺 / 未验 / 无需安装。</summary>
        public string State { get; }

        /// <summary>判成这个状态的依据。</summary>
        public string Evidence { get; }

        /// <summary>来源：git 地址、下载页；没有就是空串。</summary>
        public string Source { get; }

        /// <summary>安装命令；空串表示清单没给。</summary>
        public string InstallCommand { get; }

        /// <summary>下一步动作；已装时为空串。</summary>
        public string NextStep { get; }
    }

    /// <summary>
    /// 装机清单里一个**能在面板上改**的配置字段。
    ///
    /// 密钥与非密钥在这里分成两种东西，差别只在「值」这一栏：
    /// 非密钥（地址、可执行文件、超时秒）把当前值带出来，页面预填进输入框，改完就地保存；
    /// 密钥的「值」**恒为空串**——写这一侧 2026-08-22 起放开了，读这一侧一寸没让：
    /// 值不出现在任何接口返回里，页面上只显示「已配 / 未配」，输入框每次都是空的。
    /// </summary>
    public sealed class HostConfigFieldEntry
    {
        /// <summary>
        /// 构造一个可改字段。
        /// </summary>
        /// <param name="name">字段名。</param>
        /// <param name="fieldType">自述里写的类型：string / number / boolean / secret。</param>
        /// <param name="isSecret">是不是密钥字段。</param>
        /// <param name="value">当前值；**密钥恒为空串**，调用方不许往里塞密钥的值。</param>
        /// <param name="isConfigured">配没配：非密钥看值非空，密钥看键在不在。</param>
        /// <param name="hint">一句提示：这个字段该填什么。</param>
        /// <param name="options">可选值清单；空表示这一格是自由输入。</param>
        /// <param name="optionSourceNote">选项从哪来的一句话；没有选项来源时为空串。</param>
        /// <param name="isModelField">这一格是不是这个 driver 的「模型」字段（声明了「选项来源: 探测.模型」）。</param>
        /// <param name="autoNote">「自动」这一档现在会挑谁的一句话；不是模型格时为空串。</param>
        public HostConfigFieldEntry(
            string name,
            string fieldType,
            bool isSecret,
            string value,
            bool isConfigured,
            string hint,
            IReadOnlyList<string> options = null,
            string optionSourceNote = "",
            bool isModelField = false,
            string autoNote = "")
        {
            Name = name ?? "";
            FieldType = fieldType ?? "";
            IsSecret = isSecret;
            Value = isSecret ? "" : (value ?? "");
            IsConfigured = isConfigured;
            Hint = hint ?? "";
            Options = options ?? Array.Empty<string>();
            OptionSourceNote = optionSourceNote ?? "";
            IsModelField = isModelField;
            AutoNote = autoNote ?? "";
        }

        /// <summary>字段名。</summary>
        public string Name { get; }

        /// <summary>自述里写的类型：string / number / boolean / secret。</summary>
        public string FieldType { get; }

        /// <summary>是不是密钥字段。</summary>
        public bool IsSecret { get; }

        /// <summary>当前值；密钥恒为空串（值永不读出来）。</summary>
        public string Value { get; }

        /// <summary>配没配：非密钥看值非空，密钥看键在不在。</summary>
        public bool IsConfigured { get; }

        /// <summary>一句提示：这个字段该填什么。</summary>
        public string Hint { get; }

        /// <summary>
        /// 可选值清单；空表示这一格是自由输入。
        ///
        /// 来源由 driver.json 的「配置schema」里那个字段的「选项来源」声明，
        /// 现在只认 <c>探测.模型</c>：值取自那份 driver 最近一次能力探测的产出。
        /// 也就是说**它是跟着「地址」走的**——换个地址重跑一次试跑，这一格的选项就换一批。
        ///
        /// 清单为空**不等于没有可选值**，多半是还没探过。所以页面不许把它变成一个
        /// 只能从空列表里挑的死格子：永远得留一条自己填的路。
        /// </summary>
        public IReadOnlyList<string> Options { get; }

        /// <summary>选项从哪来的一句话，给页面显示；没有选项来源时为空串。</summary>
        public string OptionSourceNote { get; }

        /// <summary>
        /// 这一格是不是这个 driver 的「模型」字段。是的话页面**永远**给它一个下拉，
        /// 而且下拉里永远有「自动」这一档——哪怕清单是空的：
        /// 「自动」不需要清单也能选（它只是「别钉死」），清单空只影响它挑不挑得出来。
        /// </summary>
        public bool IsModelField { get; }

        /// <summary>
        /// 「自动」这一档现在会挑谁，写成一句话；不是模型格时为空串。
        /// 页面把它摆在那一格底下——不摆，人选了「自动」就只能猜将来会发生什么。
        /// </summary>
        public string AutoNote { get; }
    }

    /// <summary>
    /// 装机清单里的一个宿主：一个编辑器，或一个下游服务（driver 名由 Bridges/ 下的目录决定，这里不写死任何一个）。
    /// 「本体」与「桥接包」分开报：本体是那个软件本身装没装，桥接包是我们要往它里面塞的东西。
    /// 这两件事真的会分家——加工站装了但路径没填、编辑器装了但包没解析，都是每天会遇到的状态。
    /// </summary>
    public sealed class HostInventoryRow
    {
        /// <summary>
        /// 构造一行宿主。
        /// </summary>
        /// <param name="name">宿主名：编辑器那一行固定是 unity，其余取 Bridges/ 下的目录名。</param>
        /// <param name="kind">种类：编辑器 / 本机服务 / 线上服务。</param>
        /// <param name="hostState">本体状态：已装 / 缺 / 未验 / 无需安装。</param>
        /// <param name="hostDetail">本体状态的依据，写成人话。</param>
        /// <param name="hostVersion">本体版本；判不出来是空串。</param>
        /// <param name="hostNextStep">本体的下一步动作；已装时为空串。</param>
        /// <param name="packages">这个宿主要装的桥接包 / 插件 / 脚本。</param>
        /// <param name="editableFields">能在面板上改的配置字段；没有就是空表。</param>
        /// <param name="declarations">这个宿主名下的插件声明原文，供面板改它们用；没有就是空表。</param>
        /// <param name="notes">补充说明，逐条一句话。</param>
        /// <param name="trialCommand">能在面板上跑一次的命令（试跑 / 探测）；没有就是空串。</param>
        /// <param name="loadFailureReason">这一行读不出来时的原因；正常为空串。</param>
        /// <param name="probeCommand">这个 driver 自述里写的能力探测命令；没有就是空串。</param>
        public HostInventoryRow(
            string name,
            string kind,
            string hostState,
            string hostDetail,
            string hostVersion,
            string hostNextStep,
            IReadOnlyList<HostPackageEntry> packages,
            IReadOnlyList<HostConfigFieldEntry> editableFields,
            IReadOnlyList<EditorPluginEntry> declarations,
            IReadOnlyList<string> notes,
            string trialCommand,
            string loadFailureReason,
            string probeCommand = "")
        {
            Name = name ?? "";
            Kind = kind ?? "";
            HostState = hostState ?? "";
            HostDetail = hostDetail ?? "";
            HostVersion = hostVersion ?? "";
            HostNextStep = hostNextStep ?? "";
            Packages = packages ?? Array.Empty<HostPackageEntry>();
            EditableFields = editableFields ?? Array.Empty<HostConfigFieldEntry>();
            Declarations = declarations ?? Array.Empty<EditorPluginEntry>();
            Notes = notes ?? Array.Empty<string>();
            TrialCommand = trialCommand ?? "";
            LoadFailureReason = loadFailureReason ?? "";
            ProbeCommand = probeCommand ?? "";
        }

        /// <summary>宿主名。</summary>
        public string Name { get; }

        /// <summary>种类：编辑器 / 本机服务 / 线上服务。</summary>
        public string Kind { get; }

        /// <summary>本体状态：已装 / 缺 / 未验 / 无需安装。</summary>
        public string HostState { get; }

        /// <summary>本体状态的依据。</summary>
        public string HostDetail { get; }

        /// <summary>本体版本；判不出来是空串。</summary>
        public string HostVersion { get; }

        /// <summary>本体的下一步动作；已装时为空串。</summary>
        public string HostNextStep { get; }

        /// <summary>这个宿主要装的桥接包 / 插件 / 脚本。</summary>
        public IReadOnlyList<HostPackageEntry> Packages { get; }

        /// <summary>能在面板上改的配置字段；没有就是空表。</summary>
        public IReadOnlyList<HostConfigFieldEntry> EditableFields { get; }

        /// <summary>这个宿主名下的插件声明原文，供面板改它们用；没有就是空表。</summary>
        public IReadOnlyList<EditorPluginEntry> Declarations { get; }

        /// <summary>补充说明，逐条一句话。</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>能在面板上跑一次的命令；没有就是空串。</summary>
        public string TrialCommand { get; }

        /// <summary>这一行读不出来时的原因；正常为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>
        /// 这个 driver 自述里写的能力探测命令；没有就是空串。
        /// 面板拿它做两件事：模型那一格旁边的「重探」按钮，以及**存完「地址」之后自动重探一次**——
        /// 换了地址不重探，那一格的清单就还是上一个地址的，而这件事平时一点都看不出来。
        /// </summary>
        public string ProbeCommand { get; }
    }

    /// <summary>
    /// 装机清单：把「每个编辑器 / 每个下游要装什么、装没装、还差什么」按宿主列一遍。
    ///
    /// 数据全部来自已有的真相，这里一份都不另立：
    /// 编辑器包看 <c>UnityProject/Packages/manifest.json</c> 与 <c>UnityProject/Library/PackageCache/</c>；
    /// 下游依赖看 <c>Bridges/&lt;driver&gt;/dependencies.json</c> 与那份 driver 的能力探测输出；
    /// 本体路径看 <c>local.json</c> 的「下游配置」（路径不是密钥，可以读值做存在性检查）。
    ///
    /// 密钥红线（决策 5、78）：密钥只判**键在不在**，值一次都不读、不打印、不写进任何返回文案。
    /// 只读不写：本类不安装任何东西、不改 manifest、不动 local.json——它只报告现状与下一步。
    /// </summary>
    public static class HostPackageInventory
    {
        /// <summary>状态：有实打实的证据说明它在。</summary>
        public const string StateInstalled = "已装";

        /// <summary>状态：证据说明它不在。</summary>
        public const string StateMissing = "缺";

        /// <summary>状态：本机看不出来，要跑一次探测或让编辑器解析一次才知道。</summary>
        public const string StateUnverified = "未验";

        /// <summary>状态：这一项本来就不用装（线上服务、随仓库走的脚本）。</summary>
        public const string StateNotNeeded = "无需安装";

        /// <summary>Unity 官方包的包名前缀：它们跟着编辑器版本走，不算桥接包。</summary>
        private const string UnityOfficialPackagePrefix = "com.unity.";

        /// <summary>
        /// 列一遍装机清单：Unity 编辑器一行在前，其余按 Bridges/ 下的 driver 名序数序。
        /// 任何一行读不出来都仍然产出该行并写清原因（决策 43：烂在库里的必须让人看见）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录（绝对路径）。</param>
        public static IReadOnlyList<HostInventoryRow> Build(string repositoryRoot)
        {
            var pluginManifest = EditorPluginManifest.Load(repositoryRoot);
            var rows = new List<HostInventoryRow>();
            var unityRow = BuildUnityHost(repositoryRoot, pluginManifest);
            if (unityRow != null)
            {
                rows.Add(unityRow);
            }

            var settings = LocalBridgeSettings.Load(repositoryRoot);
            foreach (var driverName in EnumerateDriverNames(repositoryRoot))
            {
                rows.Add(BuildDriverHost(repositoryRoot, driverName, settings, pluginManifest));
            }

            var unclaimedRow = BuildUnclaimedPluginRow(repositoryRoot, pluginManifest, rows);
            if (unclaimedRow != null)
            {
                rows.Add(unclaimedRow);
            }

            return rows;
        }

        /// <summary>
        /// 收尾行：清单坏了、或某条声明的「宿主」在这个仓库里根本不存在时才产出，其余时候返回 null。
        /// 不产出这一行的话，那些声明会静悄悄地谁都不挂——人以为声明了就管上了，其实一条都没查
        /// （决策 43：烂在库里的必须让人看见）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="pluginManifest">插件声明清单。</param>
        /// <param name="rows">已经产出的宿主行，用来判一条声明有没有落到真宿主上。</param>
        private static HostInventoryRow BuildUnclaimedPluginRow(
            string repositoryRoot,
            EditorPluginManifest pluginManifest,
            IReadOnlyList<HostInventoryRow> rows)
        {
            if (!pluginManifest.Loaded)
            {
                return new HostInventoryRow(
                    "插件声明", "声明", StateUnverified, "插件声明清单坏了，一条都没查", "",
                    "把 Tools/CreationPipeline/Config/editor-plugins.json 修成合法 JSON 再来",
                    Array.Empty<HostPackageEntry>(), Array.Empty<HostConfigFieldEntry>(),
                    Array.Empty<EditorPluginEntry>(), Array.Empty<string>(), "", pluginManifest.LoadFailureReason);
            }

            var hostNames = new HashSet<string>(rows.Select(row => row.Name), StringComparer.Ordinal);
            var unclaimed = pluginManifest.Entries
                .Where(entry => !hostNames.Contains(entry.HostName))
                .ToList();
            if (unclaimed.Count == 0)
            {
                return null;
            }

            var packages = unclaimed
                .Select(entry => PluginPackageEntry(repositoryRoot, entry))
                .ToList();
            return new HostInventoryRow(
                "插件声明", "声明", StateUnverified,
                $"有 {unclaimed.Count} 条声明的「宿主」在这个仓库里找不到", "",
                "把「宿主」改成 unity 或 Bridges/ 下的目录名，否则这些声明谁都不管",
                packages,
                Array.Empty<HostConfigFieldEntry>(),
                unclaimed,
                new List<string> { "宿主名对不上的声明挂在这里，免得它们静悄悄地谁都不挂。" },
                "",
                "");
        }

        /// <summary>
        /// Unity 编辑器这一行：本体按 ProjectVersion.txt 的版本探常见装机路径，
        /// 桥接包取 manifest.json 里**非官方**的那些包（官方包跟着编辑器版本走，不是我们要装的东西）。
        /// UnityProject/ 不存在时返回 null——纯 .NET 的复制体里没有 Unity 工程，那不是错。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="pluginManifest">插件声明清单：包管理器看不见的那类插件从这里来。</param>
        private static HostInventoryRow BuildUnityHost(string repositoryRoot, EditorPluginManifest pluginManifest)
        {
            var unityProjectDirectory = Path.Combine(repositoryRoot, "UnityProject");
            if (!Directory.Exists(unityProjectDirectory))
            {
                return null;
            }

            var (version, versionNote) = ReadUnityVersion(unityProjectDirectory);
            var editorPath = version.Length == 0
                ? null
                : SetupInspector.UnityEditorCandidates(version).FirstOrDefault(File.Exists);

            string hostState;
            string hostDetail;
            string hostNextStep;
            if (version.Length == 0)
            {
                hostState = StateUnverified;
                hostDetail = versionNote;
                hostNextStep = "把 UnityProject/ProjectSettings/ProjectVersion.txt 修好，否则判不了该装哪个版本";
            }
            else if (editorPath != null)
            {
                hostState = StateInstalled;
                hostDetail = $"{version} 在 {editorPath}";
                hostNextStep = "";
            }
            else
            {
                hostState = StateMissing;
                hostDetail = $"按版本 {version} 在常见装机路径都没找到编辑器";
                hostNextStep = "用 Unity Hub 装这个版本，或跑 unity-cmd.ps1 时用 -UnityExecutable 指路径";
            }

            var notes = new List<string>();
            var packages = new List<HostPackageEntry>(ReadUnityPackages(unityProjectDirectory, notes, out var manifestFailure));
            packages.AddRange(PluginEntriesFor(repositoryRoot, pluginManifest, "unity"));
            notes.Add("编辑器包不由面板安装：改 UnityProject/Packages/manifest.json 之后，要用 Unity 打开一次工程让包管理器解析。");
            notes.Add("解包进 Assets/ 的插件（厂商的 .unitypackage）包管理器看不见，靠 Tools/CreationPipeline/Config/editor-plugins.json 声明才管得上。");

            return new HostInventoryRow(
                "unity",
                "编辑器",
                hostState,
                hostDetail,
                version,
                hostNextStep,
                packages,
                Array.Empty<HostConfigFieldEntry>(),
                DeclarationsFor(pluginManifest, "unity"),
                notes,
                "",
                manifestFailure);
        }

        /// <summary>读 ProjectVersion.txt 里的编辑器版本；读不到时第二项给原因。</summary>
        /// <param name="unityProjectDirectory">Unity 工程目录。</param>
        private static (string Version, string Note) ReadUnityVersion(string unityProjectDirectory)
        {
            var versionFile = Path.Combine(unityProjectDirectory, "ProjectSettings", "ProjectVersion.txt");
            if (!File.Exists(versionFile))
            {
                return ("", "UnityProject/ProjectSettings/ProjectVersion.txt 不存在，判不了版本");
            }

            try
            {
                var versionLine = File.ReadLines(versionFile)
                    .FirstOrDefault(line => line.StartsWith("m_EditorVersion:", StringComparison.Ordinal));
                var version = versionLine == null ? "" : versionLine.Split(':', 2)[1].Trim();
                return version.Length == 0
                    ? ("", "ProjectVersion.txt 里读不到 m_EditorVersion")
                    : (version, "");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return ("", "ProjectVersion.txt 读不动：" + exception.Message);
            }
        }

        /// <summary>
        /// 读 Unity 的桥接包清单：manifest.json 的 dependencies 里去掉 com.unity.* 之后的那些。
        /// 判据分两支——file: 的看包目录里有没有 package.json（那是随仓库走的本地包）；
        /// git / 版本号的看 Library/PackageCache 里 Unity 解析出来没有。
        /// PackageCache 整个不存在时一律记「未验」：那说明这台机器没用 Unity 打开过工程，
        /// 把它记成「缺」等于把「没查过」说成「没有」。
        /// </summary>
        /// <param name="unityProjectDirectory">Unity 工程目录。</param>
        /// <param name="notes">补充说明，读的过程中往里追加。</param>
        /// <param name="manifestFailure">manifest 读不出来时的原因；正常给空串。</param>
        private static IReadOnlyList<HostPackageEntry> ReadUnityPackages(
            string unityProjectDirectory,
            List<string> notes,
            out string manifestFailure)
        {
            manifestFailure = "";
            var packagesDirectory = Path.Combine(unityProjectDirectory, "Packages");
            var manifestFile = Path.Combine(packagesDirectory, "manifest.json");
            if (!File.Exists(manifestFile))
            {
                manifestFailure = "找不到 UnityProject/Packages/manifest.json，列不出编辑器包";
                return Array.Empty<HostPackageEntry>();
            }

            var dependencies = new List<(string Name, string Reference)>();
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(manifestFile)))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("dependencies", out var dependencyElement)
                        || dependencyElement.ValueKind != JsonValueKind.Object)
                    {
                        manifestFailure = "manifest.json 里没有 dependencies 对象，列不出编辑器包";
                        return Array.Empty<HostPackageEntry>();
                    }

                    foreach (var property in dependencyElement.EnumerateObject())
                    {
                        dependencies.Add((property.Name, property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() ?? ""
                            : ""));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                manifestFailure = "manifest.json 读不出来：" + exception.Message;
                return Array.Empty<HostPackageEntry>();
            }

            var officialCount = dependencies.Count(pair => pair.Name.StartsWith(UnityOfficialPackagePrefix, StringComparison.Ordinal));
            if (officialCount > 0)
            {
                notes.Add($"另有 {officialCount} 个 com.unity.* 官方包不在这张表里：它们跟着编辑器版本走，不是要装的桥接包。");
            }

            var packageCacheDirectory = Path.Combine(unityProjectDirectory, "Library", "PackageCache");
            var resolvedNames = ReadPackageCacheNames(packageCacheDirectory);
            if (resolvedNames == null)
            {
                notes.Add("UnityProject/Library/PackageCache/ 不存在：这台机器没用 Unity 打开过工程，外部包一律记「未验」。");
            }

            var entries = new List<HostPackageEntry>();
            foreach (var pair in dependencies
                .Where(candidate => !candidate.Name.StartsWith(UnityOfficialPackagePrefix, StringComparison.Ordinal))
                .OrderBy(candidate => candidate.Name, StringComparer.Ordinal))
            {
                entries.Add(pair.Reference.StartsWith("file:", StringComparison.Ordinal)
                    ? LocalUnityPackageEntry(packagesDirectory, pair.Name, pair.Reference)
                    : ResolvedUnityPackageEntry(resolvedNames, pair.Name, pair.Reference));
            }

            return entries;
        }

        /// <summary>file: 形态的本地包：包目录里有 package.json 才算在。</summary>
        /// <param name="packagesDirectory">Unity 工程的 Packages 目录。</param>
        /// <param name="name">包名。</param>
        /// <param name="reference">manifest 里写的依赖值。</param>
        private static HostPackageEntry LocalUnityPackageEntry(string packagesDirectory, string name, string reference)
        {
            var relativePath = reference.Substring("file:".Length);
            string packageFile;
            try
            {
                packageFile = Path.GetFullPath(Path.Combine(packagesDirectory, relativePath, "package.json"));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is PathTooLongException)
            {
                return new HostPackageEntry(
                    name, "编辑器包", "本地", StateMissing,
                    "manifest 里的路径解析不了：" + exception.Message, reference, "",
                    "把 manifest.json 里这一条的路径改对");
            }

            return File.Exists(packageFile)
                ? new HostPackageEntry(name, "编辑器包", "本地", StateInstalled, $"{relativePath}/package.json 在", reference, "", "")
                : new HostPackageEntry(
                    name, "编辑器包", "本地", StateMissing,
                    $"manifest 指的 {relativePath}/package.json 不存在", reference, "",
                    "这是随仓库走的本地包：路径对不上就是仓库不全或 manifest 写错了，不用去下载");
        }

        /// <summary>git / 版本号形态的外部包：看 Library/PackageCache 里 Unity 解析出来没有。</summary>
        /// <param name="resolvedNames">PackageCache 下的目录名；null 表示那个目录不存在。</param>
        /// <param name="name">包名。</param>
        /// <param name="reference">manifest 里写的依赖值。</param>
        private static HostPackageEntry ResolvedUnityPackageEntry(
            IReadOnlyList<string> resolvedNames,
            string name,
            string reference)
        {
            var requirement = VersionRequirementOf(reference);
            if (resolvedNames == null)
            {
                return new HostPackageEntry(
                    name, "编辑器包", requirement, StateUnverified,
                    "Library/PackageCache/ 不存在，查不到解析结果", reference, "",
                    "用 Unity 打开一次工程（或跑 Tools/Gates/gate-unity.ps1），让包管理器解析一次再看");
            }

            var prefix = name + "@";
            var hit = resolvedNames.FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
            return hit != null
                ? new HostPackageEntry(
                    name, "编辑器包", requirement, StateInstalled,
                    $"Library/PackageCache/{hit} 在", reference, "", "")
                : new HostPackageEntry(
                    name, "编辑器包", requirement, StateMissing,
                    "Library/PackageCache 里没有它的解析结果", reference, "",
                    "用 Unity 打开一次工程让包管理器解析；这类包要能连上来源，断网时解析不出来");
        }

        /// <summary>从 manifest 的依赖值里取版本记号：git 地址取 # 后面那段，其余原样。</summary>
        /// <param name="reference">manifest 里写的依赖值。</param>
        private static string VersionRequirementOf(string reference)
        {
            var hashIndex = reference.LastIndexOf('#');
            return hashIndex >= 0 && hashIndex < reference.Length - 1
                ? reference.Substring(hashIndex + 1)
                : reference;
        }

        /// <summary>列 PackageCache 下的目录名；目录不存在返回 null（「没查过」与「查了没有」是两支）。</summary>
        /// <param name="packageCacheDirectory">PackageCache 目录。</param>
        private static IReadOnlyList<string> ReadPackageCacheNames(string packageCacheDirectory)
        {
            if (!Directory.Exists(packageCacheDirectory))
            {
                return null;
            }

            try
            {
                return Directory.EnumerateDirectories(packageCacheDirectory)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// 一个 driver 这一行：本体按形态判（本地看可执行文件 / 地址，线上不用装），
        /// 桥接包 = 依赖清单逐条对着能力探测输出查在不在，外加随仓库走的驱动脚本。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="settings">本机配置。</param>
        /// <param name="pluginManifest">插件声明清单：手装进宿主目录的那类插件从这里来。</param>
        private static HostInventoryRow BuildDriverHost(
            string repositoryRoot,
            string driverName,
            LocalBridgeSettings settings,
            EditorPluginManifest pluginManifest)
        {
            BridgeDriverDescriptor descriptor;
            try
            {
                descriptor = BridgeDriverDescriptor.Load(repositoryRoot, driverName);
            }
            catch (InvalidOperationException exception)
            {
                return new HostInventoryRow(
                    driverName, "下游", StateUnverified, "driver.json 读不出来", "", "",
                    Array.Empty<HostPackageEntry>(), Array.Empty<HostConfigFieldEntry>(),
                    Array.Empty<EditorPluginEntry>(), Array.Empty<string>(), "", exception.Message);
            }

            var isLocal = string.Equals(descriptor.Form, "本地", StringComparison.Ordinal);
            var notes = new List<string>();
            if (!settings.Loaded)
            {
                notes.Add("本机配置读不出来：" + settings.LoadFailureReason + "（本体状态按「未配」算）");
            }

            AppendSecretNote(descriptor, settings, notes);

            var (hostState, hostDetail, hostNextStep) = isLocal
                ? LocalHostState(repositoryRoot, driverName, descriptor, settings)
                : (StateNotNeeded, "线上服务，本机不装东西", "");
            if (!isLocal)
            {
                notes.Add("线上服务没有本机桥接包：能不能用只看密钥键与地址配没配，看「下游」页。");
            }

            var packages = new List<HostPackageEntry>();
            if (isLocal)
            {
                packages.AddRange(ReadDriverDependencies(repositoryRoot, driverName, notes));
            }

            packages.AddRange(PluginEntriesFor(repositoryRoot, pluginManifest, driverName));
            packages.AddRange(ReadDriverScripts(repositoryRoot, driverName, descriptor, settings));

            return new HostInventoryRow(
                driverName,
                isLocal ? "本机服务" : "线上服务",
                hostState,
                hostDetail,
                "",
                hostNextStep,
                packages,
                ReadEditableFields(repositoryRoot, driverName, descriptor, settings),
                DeclarationsFor(pluginManifest, driverName),
                notes,
                descriptor.TrialCommand,
                "",
                ReadProbeCommand(repositoryRoot, driverName));
        }

        /// <summary>密钥键配没配写成一句说明。只报键名与「在不在」，值一次都不读（决策 5、78）。</summary>
        /// <param name="descriptor">driver 自述。</param>
        /// <param name="settings">本机配置。</param>
        /// <param name="notes">补充说明，往里追加一条。</param>
        private static void AppendSecretNote(
            BridgeDriverDescriptor descriptor,
            LocalBridgeSettings settings,
            List<string> notes)
        {
            if (descriptor.SecretFieldNames.Count == 0)
            {
                return;
            }

            var missing = descriptor.SecretFieldNames
                .Where(field => !settings.TryGetSecret(field, out var value) || value.Length == 0)
                .ToList();
            notes.Add(missing.Count == 0
                ? $"密钥键齐了（{descriptor.SecretFieldNames.Count} 个，只判键在不在，值不读）"
                : $"密钥键还缺：{string.Join("、", missing)}（去 Doc/creation-pipeline-user-setup.md 第二节拿）");
        }

        /// <summary>
        /// 本地形态 driver 的本体状态：有「可执行文件」字段的看那个文件在不在，
        /// 有「地址」字段的看上次试跑有没有对着**这个**地址跑通——服务在不在只有试跑才知道。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="descriptor">driver 自述。</param>
        /// <param name="settings">本机配置。</param>
        private static (string State, string Detail, string NextStep) LocalHostState(
            string repositoryRoot,
            string driverName,
            BridgeDriverDescriptor descriptor,
            LocalBridgeSettings settings)
        {
            var hasConfiguration = settings.TryGetDriverConfiguration(driverName, out var configuration);
            if (descriptor.ConfigurationFieldNames.Contains("可执行文件"))
            {
                var executablePath = hasConfiguration
                    && configuration.TryGetProperty("可执行文件", out var executable)
                    && executable.ValueKind == JsonValueKind.String
                        ? executable.GetString() ?? ""
                        : "";
                if (executablePath.Length == 0)
                {
                    return (StateMissing, "local.json 里没填「可执行文件」",
                        $"装好本体，再把真实路径填进 下游配置.{driverName}.可执行文件");
                }

                return File.Exists(executablePath)
                    ? (StateInstalled, "「可执行文件」指的路径存在", "")
                    : (StateMissing, "「可执行文件」指的路径不存在",
                        $"装好本体，或把 下游配置.{driverName}.可执行文件 改成真实路径");
            }

            if (descriptor.ConfigurationFieldNames.Contains("地址"))
            {
                var address = hasConfiguration
                    && configuration.TryGetProperty("地址", out var addressElement)
                    && addressElement.ValueKind == JsonValueKind.String
                        ? addressElement.GetString() ?? ""
                        : "";
                return address.Length == 0
                    ? (StateMissing, "local.json 里没填「地址」", $"起好服务，再把地址填进 下游配置.{driverName}.地址")
                    : AddressHostState(repositoryRoot, driverName, address);
            }

            return (StateUnverified, "自述里没有可执行文件、也没有地址，判不出本体装没装", "");
        }

        /// <summary>
        /// 有「地址」字段的本地 driver：本体状态跟着**上一次试跑的产出**走。
        ///
        /// 试跑（bridge.probe）只有在下游真答话时才会落下探测产出，那份产出顶上盖着「探于哪个地址」的章。
        /// 章跟现在配的地址对得上 → 这台机器上确实有它，记「已装」；对不上 → 那是上一个地址的战果，
        /// 换地址不重探是这条链路上最容易发生的事，记回「未验」并点名两个地址；
        /// 没盖章（老产出）或压根没产出 → 判据没凑齐，还是「未验」。
        ///
        /// 这一条以前是写死的「未验」：试跑跑通了、依赖也一条条染绿了，本体那一格还挂着
        /// 「点试跑一次」——卡片看上去永远不更新，人只会反复点同一个按钮（决策 42 的反面：
        /// 「查过了有」也得说出来，不然跟没查过没区别）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="address">本机现在配的地址；调用方保证非空。</param>
        private static (string State, string Detail, string NextStep) AddressHostState(
            string repositoryRoot,
            string driverName,
            string address)
        {
            const string NeverTried = "点「试跑一次」，跑通了才算这台机器上真有它";
            CapabilityProbeResult probeResult;
            try
            {
                probeResult = CapabilityProbeResult.LoadFromFile(ProvisionPaths.ProbeResultFile(repositoryRoot, driverName));
            }
            catch (InvalidOperationException)
            {
                return (StateUnverified, "地址配了；服务在不在只有试跑才知道", NeverTried);
            }

            if (probeResult.ProbedEndpoint.Length == 0)
            {
                return (StateUnverified, "上次试跑的产出没盖地址章，判不出它试的是不是现在这个地址",
                    "再点一次「试跑一次」，新产出会盖上章");
            }

            if (!string.Equals(probeResult.ProbedEndpoint, address, StringComparison.Ordinal))
            {
                return (StateUnverified,
                    $"上次试跑连的是「{probeResult.ProbedEndpoint}」，现在配的是「{address}」",
                    "地址换过了，重点一次「试跑一次」");
            }

            return (StateInstalled,
                probeResult.ProbedAtText.Length == 0
                    ? "上次试跑连上了这个地址"
                    : $"上次试跑连上了这个地址（{probeResult.ProbedAtText}）",
                "");
        }

        /// <summary>
        /// 依赖清单逐条对着能力探测输出查在不在。探测输出还没有时全部记「未验」并给出探测命令——
        /// 「没探过」与「探过了没有」必须分开（决策 42），把没探过渲染成「缺」会让人白装一遍。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="notes">补充说明，读的过程中往里追加。</param>
        private static IReadOnlyList<HostPackageEntry> ReadDriverDependencies(
            string repositoryRoot,
            string driverName,
            List<string> notes)
        {
            if (!DependencyManifest.Exists(repositoryRoot, driverName))
            {
                notes.Add($"Bridges/{driverName}/dependencies.json 不存在：这个 driver 没声明要装的东西。");
                return Array.Empty<HostPackageEntry>();
            }

            DependencyManifest manifest;
            try
            {
                manifest = DependencyManifest.Load(repositoryRoot, driverName);
            }
            catch (InvalidOperationException exception)
            {
                notes.Add("依赖清单读不出来：" + exception.Message);
                return Array.Empty<HostPackageEntry>();
            }

            CapabilityProbeResult probeResult = null;
            var probeFailure = "";
            try
            {
                probeResult = CapabilityProbeResult.LoadFromFile(ProvisionPaths.ProbeResultFile(repositoryRoot, driverName));
            }
            catch (InvalidOperationException exception)
            {
                probeFailure = exception.Message;
            }

            if (probeResult == null)
            {
                notes.Add($"还没探测过这台机器（{probeFailure}）：下面每一条都是「未验」，不是「缺」。");
            }

            var entries = new List<HostPackageEntry>();
            foreach (var entry in manifest.Entries)
            {
                if (probeResult == null)
                {
                    entries.Add(new HostPackageEntry(
                        entry.Name, entry.Category, entry.Version, StateUnverified,
                        "还没有能力探测输出", entry.Source, entry.InstallCommand,
                        $"先跑 bridge.probe --Driver {driverName}，探完才知道这台机器上有没有它"));
                    continue;
                }

                entries.Add(probeResult.Contains(entry.Category, entry.Name)
                    ? new HostPackageEntry(
                        entry.Name, entry.Category, entry.Version, StateInstalled,
                        "上次探测在这台机器上探到了它", entry.Source, entry.InstallCommand, "")
                    : new HostPackageEntry(
                        entry.Name, entry.Category, entry.Version, StateMissing,
                        "上次探测没探到它", entry.Source, entry.InstallCommand,
                        entry.InstallCommand.Length > 0
                            ? "按安装命令装，装完重跑一次探测"
                            : "照来源页面装，装完重跑一次探测"));
            }

            return entries;
        }

        /// <summary>
        /// 读一个 driver 能在面板上改的配置字段：自述「配置schema」里的全部字段，加上「密钥字段」点名的那些。
        ///
        /// 非密钥字段把当前值带出来（路径、地址不是密钥，页面要预填进输入框才谈得上「改」）；
        /// 密钥字段只判键在不在，值一次都不取——<see cref="LocalBridgeSettings.TryGetSecret"/> 的 out 参数
        /// 在这里只用来判「有没有」，随即丢掉，绝不往外带（决策 5、78 的读侧）。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="descriptor">driver 自述。</param>
        /// <param name="settings">本机配置。</param>
        private static IReadOnlyList<HostConfigFieldEntry> ReadEditableFields(
            string repositoryRoot,
            string driverName,
            BridgeDriverDescriptor descriptor,
            LocalBridgeSettings settings)
        {
            var secretNames = new HashSet<string>(descriptor.SecretFieldNames, StringComparer.Ordinal);
            var schemaTypes = ReadSchemaTypes(repositoryRoot, driverName);
            var schemaNotes = ReadSchemaNotes(repositoryRoot, driverName);
            var optionSources = ReadSchemaOptionSources(repositoryRoot, driverName);
            var hasConfiguration = settings.TryGetDriverConfiguration(driverName, out var configuration);
            var fields = new List<HostConfigFieldEntry>();

            foreach (var fieldName in descriptor.ConfigurationFieldNames.OrderBy(name => name, StringComparer.Ordinal))
            {
                var fieldType = schemaTypes.TryGetValue(fieldName, out var declared) ? declared : "";
                var isSecret = secretNames.Contains(fieldName) || string.Equals(fieldType, "secret", StringComparison.Ordinal);
                if (isSecret)
                {
                    fields.Add(SecretField(fieldName, settings));
                    continue;
                }

                var value = hasConfiguration ? ReadConfigurationValue(configuration, fieldName) : "";
                var options = ReadFieldOptions(repositoryRoot, driverName, fieldName, optionSources, out var optionSourceNote);

                // 模型格：声明了「选项来源: 探测.模型」的那一格。它比别的可选格多两样东西——
                // 一档永远在的「自动」，和一句「自动现在会挑谁」。
                var isModelField = optionSources != null
                    && optionSources.TryGetValue(fieldName, out var source)
                    && string.Equals(source, "探测.模型", StringComparison.Ordinal);
                var autoNote = "";
                if (isModelField)
                {
                    var autoPick = ModelSelection.PreviewAuto(repositoryRoot, driverName, out var note);
                    autoNote = autoPick.Length == 0
                        ? $"选「{ModelSelection.AutoSentinel}」的话，现在挑不出来：{note}"
                        : $"选「{ModelSelection.AutoSentinel}」的话，现在会挑「{autoPick}」；{note}";
                }

                fields.Add(new HostConfigFieldEntry(
                    fieldName, fieldType.Length == 0 ? "string" : fieldType, false, value, value.Length > 0,
                    HintFor(fieldName, driverName, schemaNotes), options, optionSourceNote, isModelField, autoNote));
            }

            // 「密钥字段」数组点名、但配置 schema 里没有的密钥，照样得给一格——
            // 那才是最常见的长相（密钥住在顶层，schema 里通常压根不写它）。
            foreach (var secretName in descriptor.SecretFieldNames.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (fields.Any(field => string.Equals(field.Name, secretName, StringComparison.Ordinal)))
                {
                    continue;
                }

                fields.Add(SecretField(secretName, settings));
            }

            return fields;
        }

        /// <summary>拼一格密钥字段：只判键在不在，值不取、不带出去。</summary>
        /// <param name="secretName">密钥键名。</param>
        /// <param name="settings">本机配置。</param>
        private static HostConfigFieldEntry SecretField(string secretName, LocalBridgeSettings settings)
        {
            // out 参数拿到的值在这一行之后就不再被引用：判完「有没有」就丢。
            var isConfigured = settings.TryGetSecret(secretName, out var value) && value.Length > 0;
            return new HostConfigFieldEntry(
                secretName, "secret", true, "", isConfigured,
                "密钥：写得进、永不读回。页面只报「已配 / 未配」，输入框每次都是空的。");
        }

        /// <summary>读 driver.json「配置schema」里每个字段声明的类型；没声明的字段不进表，由调用方当 string 处理。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        private static Dictionary<string, string> ReadSchemaTypes(string repositoryRoot, string driverName)
        {
            return ReadSchemaDeclarations(repositoryRoot, driverName, "类型");
        }

        /// <summary>
        /// 读 driver.json「配置schema」里每个字段声明的「说明」：这一格该填什么，一句话。
        ///
        /// **说明写在自述里而不是这份代码里**，因为「哪个键是哪个」是那个下游自己的事：
        /// 飞书的「知识空间标识」「多维表格标识」「策划文档父节点」摆在一起全是一串 token，
        /// 认错一个就把需求写进别人的地盘。这句话得跟着字段走——加一个字段顺手写一句，
        /// 而不是回头改一个越堆越长的 switch。没声明的字段不进表，退回 <see cref="HintFor"/> 的兜底。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        private static Dictionary<string, string> ReadSchemaNotes(string repositoryRoot, string driverName)
        {
            return ReadSchemaDeclarations(repositoryRoot, driverName, "说明");
        }

        /// <summary>
        /// 读 driver.json「配置schema」里每个字段的某一句声明（类型 / 说明 / 选项来源）。
        /// 没声明、或值不是字符串的字段不进表；文件读不动时给空表——
        /// 自述在这之前已经 Load 过一次，这里再失败几乎不可能，不值得为它把整张清单判红。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="declarationName">要读哪一句声明，给键名。</param>
        private static Dictionary<string, string> ReadSchemaDeclarations(
            string repositoryRoot,
            string driverName,
            string declarationName)
        {
            var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(BridgeDriverDescriptor.DriverFile(repositoryRoot, driverName))))
                {
                    if (document.RootElement.TryGetProperty("配置schema", out var schema) && schema.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in schema.EnumerateObject())
                        {
                            if (property.Value.ValueKind == JsonValueKind.Object
                                && property.Value.TryGetProperty(declarationName, out var declaration)
                                && declaration.ValueKind == JsonValueKind.String)
                            {
                                declarations[property.Name] = declaration.GetString() ?? "";
                            }
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 当这一句没声明处理即可。
            }

            return declarations;
        }

        /// <summary>读 driver.json「配置schema」里每个字段声明的「选项来源」；没声明的字段不进表。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        private static Dictionary<string, string> ReadSchemaOptionSources(string repositoryRoot, string driverName)
        {
            return ReadSchemaDeclarations(repositoryRoot, driverName, "选项来源");
        }

        /// <summary>
        /// 按「选项来源」取一个字段的可选值。现在只认 <c>探测.模型</c> / <c>探测.节点</c> / <c>探测.lora</c>：
        /// 值取自那份 driver 最近一次能力探测的产出文件。
        ///
        /// **探测产出是跟着「地址」走的**——换个地址重跑一次试跑，这一格的选项就换一批。
        /// 这正是「根据填的地址选模型」那件事的落点：不是把模型名写死在代码或自述里，
        /// 而是问下游它自己有什么。
        ///
        /// 没探过 → 返回空清单，并在说明里写清「还没探过」而不是「没有可选的」。
        /// 这两件事差得远：前者是「去点一下试跑」，后者是「这个下游没有模型」。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="fieldName">字段名。</param>
        /// <param name="optionSources">字段名 → 选项来源。</param>
        /// <param name="note">选项从哪来的一句话。</param>
        private static IReadOnlyList<string> ReadFieldOptions(
            string repositoryRoot,
            string driverName,
            string fieldName,
            IReadOnlyDictionary<string, string> optionSources,
            out string note)
        {
            note = "";
            if (optionSources == null || !optionSources.TryGetValue(fieldName, out var source) || source.Length == 0)
            {
                return Array.Empty<string>();
            }

            const string probePrefix = "探测.";
            if (!source.StartsWith(probePrefix, StringComparison.Ordinal))
            {
                note = $"自述里写的选项来源「{source}」还不认，这一格按自由输入处理";
                return Array.Empty<string>();
            }

            var category = source.Substring(probePrefix.Length);
            var probePath = ProvisionPaths.ProbeResultFile(repositoryRoot, driverName);
            if (!File.Exists(probePath))
            {
                note = "还没探过下游，选项是空的——先填好地址与密钥，点一次「试跑一次」，这里就会列出它自己报的清单";
                return Array.Empty<string>();
            }

            var probeResult = CapabilityProbeResult.LoadFromFile(probePath);
            var items = string.Equals(category, "模型", StringComparison.Ordinal) ? probeResult.Models
                : string.Equals(category, "节点", StringComparison.Ordinal) ? probeResult.Nodes
                : string.Equals(category, "lora", StringComparison.OrdinalIgnoreCase) ? probeResult.Loras
                : null;

            if (items == null)
            {
                note = $"自述里写的选项来源「{source}」还不认，这一格按自由输入处理";
                return Array.Empty<string>();
            }

            var names = items.Select(item => item.Name).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            names.Sort(StringComparer.Ordinal);
            note = names.Count == 0
                ? "上次探测回来的清单是空的——地址对不对、这个账号开通了什么，都可能是原因"
                : $"这 {names.Count} 项是上次探测时下游自己报的；换了地址要重探一次";

            // 探测产出上盖着「探于哪个地址」的章。它跟现在配的地址对不上，就说明这批清单是
            // 上一个地址留下的——这件事不点名，人根本看不出来（选项看上去一切正常）。
            var currentEndpoint = ReadCurrentEndpoint(repositoryRoot, driverName);
            if (probeResult.ProbedEndpoint.Length > 0
                && currentEndpoint.Length > 0
                && !string.Equals(probeResult.ProbedEndpoint, currentEndpoint, StringComparison.Ordinal))
            {
                note += $"。⚠ 这批是对着「{probeResult.ProbedEndpoint}」探的，现在配的是「{currentEndpoint}」——重探一次再挑";
            }

            return names;
        }

        /// <summary>读一个 driver 现在配的「地址」；没配、读不到本机配置时给空串。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        private static string ReadCurrentEndpoint(string repositoryRoot, string driverName)
        {
            var settings = LocalBridgeSettings.Load(repositoryRoot);
            if (!settings.Loaded
                || !settings.TryGetDriverConfiguration(driverName, out var configuration)
                || configuration.ValueKind != JsonValueKind.Object
                || !configuration.TryGetProperty("地址", out var endpoint)
                || endpoint.ValueKind != JsonValueKind.String)
            {
                return "";
            }

            return endpoint.GetString() ?? "";
        }

        /// <summary>读 driver.json 里写的「能力探测」命令；没写或读不动时给空串。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        private static string ReadProbeCommand(string repositoryRoot, string driverName)
        {
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(BridgeDriverDescriptor.DriverFile(repositoryRoot, driverName))))
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("能力探测", out var command)
                        && command.ValueKind == JsonValueKind.String)
                    {
                        return command.GetString() ?? "";
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 自述已经 Load 过一次，这里再失败几乎不可能；当没有探测命令处理即可。
            }

            return "";
        }

        /// <summary>把一个非密钥配置值读成字符串：数字与布尔按原样文本给，缺失给空串。</summary>
        /// <param name="configuration">这个 driver 的配置对象。</param>
        /// <param name="fieldName">字段名。</param>
        private static string ReadConfigurationValue(JsonElement configuration, string fieldName)
        {
            if (!configuration.TryGetProperty(fieldName, out var value))
            {
                return "";
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => ""
            };
        }

        /// <summary>
        /// 给一个字段配一句「该填什么」。**自述里写了「说明」的，以自述为准**：那句话跟字段住在一起。
        /// 这里剩下的几句是所有 driver 长得都一样的通用格（可执行文件 / 地址 / 超时秒）的兜底，
        /// 都不认识就不硬编一句废话，只报它落在本机配置的哪个键上。
        /// </summary>
        /// <param name="fieldName">字段名。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="schemaNotes">自述里每个字段声明的「说明」；null 当作一句都没声明。</param>
        private static string HintFor(
            string fieldName,
            string driverName,
            IReadOnlyDictionary<string, string> schemaNotes = null)
        {
            if (schemaNotes != null
                && schemaNotes.TryGetValue(fieldName, out var declared)
                && declared.Length > 0)
            {
                return declared;
            }

            if (string.Equals(fieldName, "可执行文件", StringComparison.Ordinal))
            {
                return "本体装在哪：给到可执行文件本身的绝对路径";
            }

            if (string.Equals(fieldName, "地址", StringComparison.Ordinal))
            {
                return "服务地址：带协议与端口，填完点「试跑一次」验它通不通";
            }

            if (string.Equals(fieldName, "超时秒", StringComparison.Ordinal))
            {
                return "一次调用等多久算超时";
            }

            return $"写进 下游配置.{driverName}.{fieldName}";
        }

        /// <summary>某个宿主名下的插件声明原文。页面要拿它预填「改这一条」的表单，所以原样带出去。</summary>
        /// <param name="pluginManifest">插件声明清单。</param>
        /// <param name="hostName">宿主名。</param>
        private static IReadOnlyList<EditorPluginEntry> DeclarationsFor(EditorPluginManifest pluginManifest, string hostName)
        {
            return pluginManifest.Entries
                .Where(entry => string.Equals(entry.HostName, hostName, StringComparison.Ordinal))
                .ToList();
        }

        /// <summary>某个宿主名下的插件声明，按清单里的顺序（已按宿主、名称排过）。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="pluginManifest">插件声明清单。</param>
        /// <param name="hostName">宿主名。</param>
        private static IReadOnlyList<HostPackageEntry> PluginEntriesFor(
            string repositoryRoot,
            EditorPluginManifest pluginManifest,
            string hostName)
        {
            return pluginManifest.Entries
                .Where(entry => string.Equals(entry.HostName, hostName, StringComparison.Ordinal))
                .Select(entry => PluginPackageEntry(repositoryRoot, entry))
                .ToList();
        }

        /// <summary>
        /// 把一条插件声明判成一行装机条目。三支分明：
        /// 标志路径没填 → 未验（判不了，不是没装）；路径在 → 已装；路径不在 → 缺，下一步就是声明里的安装步骤。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="entry">插件声明。</param>
        private static HostPackageEntry PluginPackageEntry(string repositoryRoot, EditorPluginEntry entry)
        {
            const string category = "编辑器插件";
            if (entry.MarkerPath.Length == 0)
            {
                return new HostPackageEntry(
                    entry.Name, category, entry.Version, StateUnverified,
                    "声明里还没填「标志路径」，判不了它装没装", entry.Source, "",
                    "装完之后把它在宿主目录下的落点（目录或文件）填进 editor-plugins.json 的「标志路径」");
            }

            var fullPath = Path.Combine(repositoryRoot, entry.MarkerPath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(fullPath) || File.Exists(fullPath))
            {
                return new HostPackageEntry(
                    entry.Name, category, entry.Version, StateInstalled,
                    $"{entry.MarkerPath} 在", entry.Source, "", "");
            }

            return new HostPackageEntry(
                entry.Name, category, entry.Version, StateMissing,
                $"{entry.MarkerPath} 不存在", entry.Source, "",
                entry.InstallSteps.Length > 0 ? entry.InstallSteps : "照来源页面手工装，装完这一行自己变绿");
        }

        /// <summary>
        /// 随仓库走的驱动脚本：Bridges/&lt;driver&gt;/scripts/ 下的东西。**这一类分两支，别混：**
        ///
        /// 一、**散落文件**：不往宿主里装——本地形态的加工站是在调用时用命令行参数
        /// （`--background --python &lt;脚本&gt;` 那一类）把脚本现喂进去的，
        /// 所以状态恒为「无需安装」，判据只有「文件在不在」。
        ///
        /// 二、**目录型包**（目录里有一份 plugin.json，见 <see cref="DriverScriptPackage"/>）：**是真要装的**。
        /// 有些宿主的扩展必须先拷进它自己的扩展目录才会被加载，所以它有「装没装」这件事，
        /// 判据是宿主安装目录下那个标志文件在不在。
        ///
        /// 判不了的时候一律「未验」，不许写成「缺」——没查过就说没有，跟把没查过说成有一样是撒谎。
        /// scripts/ 目录不存在就一条都不产出。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="descriptor">driver 自述：用来判它允不允许装脚本包（有没有「安装目录」这一格）。</param>
        /// <param name="settings">本机配置：安装目录的值从这里来。</param>
        private static IReadOnlyList<HostPackageEntry> ReadDriverScripts(
            string repositoryRoot,
            string driverName,
            BridgeDriverDescriptor descriptor,
            LocalBridgeSettings settings)
        {
            var scriptDirectory = DriverScriptPackage.ScriptsDirectory(repositoryRoot, driverName);
            if (!Directory.Exists(scriptDirectory))
            {
                return Array.Empty<HostPackageEntry>();
            }

            var entries = new List<HostPackageEntry>();

            try
            {
                entries.AddRange(Directory.EnumerateFiles(scriptDirectory)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .Select(name => new HostPackageEntry(
                        name, "驱动脚本", "", StateNotNeeded,
                        $"Bridges/{driverName}/scripts/{name} 随仓库走", "", "", "")));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Array.Empty<HostPackageEntry>();
            }

            var supportsInstall = descriptor != null
                && descriptor.ConfigurationFieldNames.Contains(DriverScriptPackage.InstallRootFieldName, StringComparer.Ordinal);
            var installRoot = supportsInstall ? ReadConfiguredInstallRoot(driverName, settings) : "";

            foreach (var package in DriverScriptPackage.LoadAll(repositoryRoot, driverName))
            {
                entries.Add(ScriptPackageEntry(driverName, package, supportsInstall, installRoot));
            }

            return entries;
        }

        /// <summary>
        /// 一个目录型脚本包这一行。四种长相：包坏了 / driver 不支持装 / 没配安装目录 / 真去查了标志文件。
        /// 前三种都是「未验」——判据都还没凑齐，此时染绿或染红都是撒谎。
        /// </summary>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="package">脚本包。</param>
        /// <param name="supportsInstall">driver 自述里有没有「安装目录」这一格。</param>
        /// <param name="installRoot">本机配的安装目录；空串表示没配。</param>
        private static HostPackageEntry ScriptPackageEntry(
            string driverName,
            DriverScriptPackage package,
            bool supportsInstall,
            string installRoot)
        {
            if (!package.Loaded)
            {
                return new HostPackageEntry(
                    package.Name, "驱动脚本", "", StateUnverified,
                    package.LoadFailureReason, "", "",
                    $"把 Bridges/{driverName}/scripts/{package.Name}/{DriverScriptPackage.ManifestFileName} 补对，补对了这一行才判得了装没装");
            }

            if (!supportsInstall)
            {
                return new HostPackageEntry(
                    package.Name, "驱动脚本", "", StateUnverified,
                    $"Bridges/{driverName}/driver.json 的「配置schema」里没有「{DriverScriptPackage.InstallRootFieldName}」这一格，判不了装没装",
                    "", "",
                    $"在那份 driver.json 里加上「{DriverScriptPackage.InstallRootFieldName}」这一格（默认值留空）");
            }

            if (installRoot.Length == 0)
            {
                return new HostPackageEntry(
                    package.Name, "驱动脚本", "", StateUnverified,
                    $"{driverName} 的「{DriverScriptPackage.InstallRootFieldName}」还没配，不知道该去哪儿找它",
                    "", "",
                    $"在面板 {driverName} 卡里填「{DriverScriptPackage.InstallRootFieldName}」（宿主根目录），填完这一行自己变绿或变红");
            }

            var installCommand = $"bridge.script.install --Driver {driverName} --Name {package.Name}";
            string markerPath;
            try
            {
                markerPath = Path.GetFullPath(package.MarkerPathUnder(installRoot));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                return new HostPackageEntry(
                    package.Name, "驱动脚本", "", StateUnverified,
                    $"落点路径算不出来：{exception.Message}", "", installCommand,
                    $"检查 {driverName} 的「{DriverScriptPackage.InstallRootFieldName}」填的是不是一个合法路径");
            }

            if (File.Exists(markerPath))
            {
                return new HostPackageEntry(
                    package.Name, "驱动脚本", "", StateInstalled,
                    $"标志文件在：{markerPath}", "", "", "");
            }

            var activation = package.ActivationNote.Length > 0
                ? "；" + package.ActivationNote
                : "";
            return new HostPackageEntry(
                package.Name, "驱动脚本", "", StateMissing,
                $"标志文件不在：{markerPath}", "", installCommand,
                $"点卡上的安装按钮，或跑 {installCommand}{activation}");
        }

        /// <summary>读本机配的安装目录；没配、读不出来一律空串（两者结论一样：不知道去哪儿找）。</summary>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="settings">本机配置。</param>
        private static string ReadConfiguredInstallRoot(string driverName, LocalBridgeSettings settings)
        {
            if (settings == null
                || !settings.TryGetDriverConfiguration(driverName, out var configuration)
                || configuration.ValueKind != JsonValueKind.Object)
            {
                return "";
            }

            return configuration.TryGetProperty(DriverScriptPackage.InstallRootFieldName, out var value)
                && value.ValueKind == JsonValueKind.String
                ? (value.GetString() ?? "").Trim()
                : "";
        }

        /// <summary>枚举 Bridges 下带 driver.json 的目录名，序数序。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        private static IEnumerable<string> EnumerateDriverNames(string repositoryRoot)
        {
            var bridgesDirectory = Path.Combine(repositoryRoot, "Bridges");
            if (!Directory.Exists(bridgesDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateDirectories(bridgesDirectory)
                .Where(directory => File.Exists(Path.Combine(directory, "driver.json")))
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
