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
        public HostConfigFieldEntry(
            string name,
            string fieldType,
            bool isSecret,
            string value,
            bool isConfigured,
            string hint,
            IReadOnlyList<string> options = null,
            string optionSourceNote = "")
        {
            Name = name ?? "";
            FieldType = fieldType ?? "";
            IsSecret = isSecret;
            Value = isSecret ? "" : (value ?? "");
            IsConfigured = isConfigured;
            Hint = hint ?? "";
            Options = options ?? Array.Empty<string>();
            OptionSourceNote = optionSourceNote ?? "";
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
            string loadFailureReason)
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
                ? LocalHostState(driverName, descriptor, settings)
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
            packages.AddRange(ReadDriverScripts(repositoryRoot, driverName));

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
                "");
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
        /// 有「地址」字段的只能报「配没配」——服务在不在只有试跑才知道，那是「未验」不是「已装」。
        /// </summary>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="descriptor">driver 自述。</param>
        /// <param name="settings">本机配置。</param>
        private static (string State, string Detail, string NextStep) LocalHostState(
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
                    : (StateUnverified, "地址配了；服务在不在只有试跑才知道", "点「试跑一次」，跑通了才算这台机器上真有它");
            }

            return (StateUnverified, "自述里没有可执行文件、也没有地址，判不出本体装没装", "");
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
                fields.Add(new HostConfigFieldEntry(
                    fieldName, fieldType.Length == 0 ? "string" : fieldType, false, value, value.Length > 0,
                    HintFor(fieldName, driverName), options, optionSourceNote));
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

        /// <summary>读 driver.json「配置schema」里每个字段声明的类型；读不动时给空表。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        private static Dictionary<string, string> ReadSchemaTypes(string repositoryRoot, string driverName)
        {
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(BridgeDriverDescriptor.DriverFile(repositoryRoot, driverName))))
                {
                    if (document.RootElement.TryGetProperty("配置schema", out var schema) && schema.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in schema.EnumerateObject())
                        {
                            if (property.Value.ValueKind == JsonValueKind.Object
                                && property.Value.TryGetProperty("类型", out var type)
                                && type.ValueKind == JsonValueKind.String)
                            {
                                types[property.Name] = type.GetString() ?? "";
                            }
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 自述已经 Load 过一次，这里再失败几乎不可能；类型当 string 处理即可。
            }

            return types;
        }

        /// <summary>读 driver.json「配置schema」里每个字段声明的「选项来源」；没声明的字段不进表。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        private static Dictionary<string, string> ReadSchemaOptionSources(string repositoryRoot, string driverName)
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(BridgeDriverDescriptor.DriverFile(repositoryRoot, driverName))))
                {
                    if (document.RootElement.TryGetProperty("配置schema", out var schema) && schema.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in schema.EnumerateObject())
                        {
                            if (property.Value.ValueKind == JsonValueKind.Object
                                && property.Value.TryGetProperty("选项来源", out var source)
                                && source.ValueKind == JsonValueKind.String)
                            {
                                sources[property.Name] = source.GetString() ?? "";
                            }
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 自述已经 Load 过一次，这里再失败几乎不可能；当没有选项来源处理即可。
            }

            return sources;
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
                : $"这 {names.Count} 项是上次「试跑一次」时下游自己报的；换了地址要重探一次";
            return names;
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

        /// <summary>给几个常见字段配一句「该填什么」；不认识的字段不硬编一句废话。</summary>
        /// <param name="fieldName">字段名。</param>
        /// <param name="driverName">driver 名称。</param>
        private static string HintFor(string fieldName, string driverName)
        {
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
        /// 随仓库走的驱动脚本：Bridges/&lt;driver&gt;/scripts/ 下的文件。
        /// 它们不往宿主里装——本地形态的加工站是以 --background --python 把脚本现喂进去的，所以状态是「无需安装」，
        /// 判据只有「文件在不在」。目录不存在就一条都不产出。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        private static IReadOnlyList<HostPackageEntry> ReadDriverScripts(string repositoryRoot, string driverName)
        {
            var scriptDirectory = Path.Combine(BridgeDriverDescriptor.DriverDirectory(repositoryRoot, driverName), "scripts");
            if (!Directory.Exists(scriptDirectory))
            {
                return Array.Empty<HostPackageEntry>();
            }

            try
            {
                return Directory.EnumerateFiles(scriptDirectory)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .Select(name => new HostPackageEntry(
                        name, "驱动脚本", "", StateNotNeeded,
                        $"Bridges/{driverName}/scripts/{name} 随仓库走", "", "", ""))
                    .ToList();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Array.Empty<HostPackageEntry>();
            }
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
