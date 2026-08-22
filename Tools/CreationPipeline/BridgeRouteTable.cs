using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一个 port 的路由：一串**按优先级排好的候选 driver**，加一条失败时怎么办的策略。
    ///
    /// 为什么是一串而不是一个：同一个域常常有好几个能干活的下游（生图有本机跑的，也有线上中转的；
    /// 模型生成有好几家），谁当值是**运行期的选择**，不该靠改代码、也不该每次调用手打 driver 名。
    /// 候选顺序就是优先级，第一个是首选。
    ///
    /// 两条策略的区别只在「首选失败之后」：
    /// <see cref="FixedPreferredStrategy"/> 就此失败，报首选的错；
    /// <see cref="FailoverStrategy"/> 顺着往下一个试，全试完才失败，并把每一次的错都摆出来。
    ///
    /// **失败转移的前提**：候选清单里的 driver 得能吃同一份调用参数。生图这一域尤其要当心——
    /// 本机那条路的配方是一份节点图，线上那条路的配方是提示词模板，两边的配方名各是各的，
    /// 配方名对不上时转移过去必然报「读配方失败」。这一条写在《创作管线 · 要你亲手填的东西》里。
    /// </summary>
    public sealed class PortRoute
    {
        /// <summary>策略：首选固定——只用第一个候选，它失败就失败，不换人。</summary>
        public const string FixedPreferredStrategy = "首选固定";

        /// <summary>策略：失败转移——首选失败就顺着候选往下试，直到有一个成功或全部试完。</summary>
        public const string FailoverStrategy = "失败转移";

        /// <summary>
        /// 构造一条 port 路由。
        /// </summary>
        /// <param name="candidates">按优先级排好的候选 driver 名；第一个是首选。</param>
        /// <param name="strategy">策略，取 <see cref="FixedPreferredStrategy"/> 或 <see cref="FailoverStrategy"/>。</param>
        public PortRoute(IReadOnlyList<string> candidates, string strategy)
        {
            Candidates = candidates ?? Array.Empty<string>();
            Strategy = string.IsNullOrEmpty(strategy) ? FixedPreferredStrategy : strategy;
        }

        /// <summary>按优先级排好的候选 driver 名；第一个是首选。</summary>
        public IReadOnlyList<string> Candidates { get; }

        /// <summary>策略：首选固定 或 失败转移。</summary>
        public string Strategy { get; }

        /// <summary>首选 driver 名；没有候选时为空串。</summary>
        public string PreferredDriverName
        {
            get { return Candidates.Count > 0 ? Candidates[0] : ""; }
        }

        /// <summary>这条路由允不允许在首选失败后换人。</summary>
        public bool AllowsFailover
        {
            get { return string.Equals(Strategy, FailoverStrategy, StringComparison.Ordinal) && Candidates.Count > 1; }
        }

        /// <summary>某个字符串是不是合法策略名。</summary>
        /// <param name="strategy">要判定的策略名。</param>
        public static bool IsKnownStrategy(string strategy)
        {
            return string.Equals(strategy, FixedPreferredStrategy, StringComparison.Ordinal)
                || string.Equals(strategy, FailoverStrategy, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 下游域路由表：把 port 名路由到一串候选 driver（带策略）、把实现名解析成可执行 + 参数。
    /// 数据来自 <c>Tools/CreationPipeline/Config/downstream.json</c>，该文件进 git、只存密钥「名」不存密钥「值」。
    /// 文件不存在是正常状态（表为空）；文件存在但解析失败才是坏（Loaded=false，整份表不可信）。
    ///
    /// 「域路由」的每一项写成两种形状都行：
    /// <c>"某个域": "某个driver名"</c>（单 driver，等价于候选只有一个、策略首选固定）；
    /// <c>"某个域": { "候选": ["首选driver名", "备选driver名"], "策略": "失败转移" }</c>。
    /// 老形状继续认，是因为大多数域就一个下游，为它写三行 JSON 是白费。
    /// </summary>
    public sealed class BridgeRouteTable
    {
        /// <summary>文件不存在时 TryResolve 给出的原因。</summary>
        public const string MissingFileReason = "下游路由表不存在";

        /// <summary>
        /// 构造一份路由表。
        /// </summary>
        /// <param name="loaded">文件存在且解析成功，或文件不存在；false 表示文件坏掉。</param>
        /// <param name="portRoutes">port 名 → 路由（候选清单 + 策略）。</param>
        /// <param name="implementations">实现名 → 实现（可执行 + 参数）。</param>
        /// <param name="loadFailureReason">文件坏掉时的原因；正常时为 ""。</param>
        public BridgeRouteTable(
            bool loaded,
            IReadOnlyDictionary<string, PortRoute> portRoutes,
            IReadOnlyDictionary<string, BridgeImplementation> implementations,
            string loadFailureReason)
            : this(loaded, portRoutes, implementations, loadFailureReason, sourceFileExists: false)
        {
        }

        /// <summary>
        /// 构造一份路由表。
        /// </summary>
        /// <param name="loaded">文件存在且解析成功，或文件不存在；false 表示文件坏掉。</param>
        /// <param name="portRoutes">port 名 → 路由（候选清单 + 策略）。</param>
        /// <param name="implementations">实现名 → 实现（可执行 + 参数）。</param>
        /// <param name="loadFailureReason">文件坏掉时的原因；正常时为 ""。</param>
        /// <param name="sourceFileExists">路由表文件是否真实存在。</param>
        public BridgeRouteTable(
            bool loaded,
            IReadOnlyDictionary<string, PortRoute> portRoutes,
            IReadOnlyDictionary<string, BridgeImplementation> implementations,
            string loadFailureReason,
            bool sourceFileExists)
        {
            Loaded = loaded;
            PortRoutes = portRoutes ?? new Dictionary<string, PortRoute>(StringComparer.Ordinal);
            Implementations = implementations ?? new Dictionary<string, BridgeImplementation>(StringComparer.Ordinal);
            LoadFailureReason = loadFailureReason ?? "";
            SourceFileExists = sourceFileExists;
        }

        /// <summary>文件存在且解析成功，或文件不存在；false 表示文件坏掉，整份表不可信。</summary>
        public bool Loaded { get; }

        /// <summary>port 名 → 路由（候选清单 + 策略）。</summary>
        public IReadOnlyDictionary<string, PortRoute> PortRoutes { get; }

        /// <summary>实现名 → 实现（可执行 + 参数）。</summary>
        public IReadOnlyDictionary<string, BridgeImplementation> Implementations { get; }

        /// <summary>文件坏掉时的原因；正常时为 ""。</summary>
        public string LoadFailureReason { get; }

        /// <summary>路由表文件是否真实存在。</summary>
        public bool SourceFileExists { get; }

        /// <summary>
        /// 从仓库根读下游路由表。文件不存在 → Loaded=true、表为空（正常状态）；
        /// 文件存在但 JSON 坏掉或形状不对 → Loaded=false、reason 写清坏在哪。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static BridgeRouteTable Load(string repositoryRoot)
        {
            var filePath = RouteTableFile(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return new BridgeRouteTable(true, null, null, "", sourceFileExists: false);
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new BridgeRouteTable(false, null, null, $"下游路由表不是合法 JSON：{filePath}：{exception.Message}");
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new BridgeRouteTable(false, null, null, $"下游路由表不是合法 JSON：{filePath}（顶层必须是对象）");
                }

                var portRoutes = new Dictionary<string, PortRoute>(StringComparer.Ordinal);
                if (root.TryGetProperty("域路由", out var portElement) && portElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in portElement.EnumerateObject())
                    {
                        // 下划线开头的是说明字段，不是路由项——路由表里到处都写着 _说明。
                        if (property.Name.StartsWith("_", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var route = TryParsePortRoute(property.Value, out var routeReason);
                        if (route == null)
                        {
                            return new BridgeRouteTable(false, null, null, $"下游路由表的域路由「{property.Name}」不合法：{routeReason}：{filePath}");
                        }

                        portRoutes[property.Name] = route;
                    }
                }
                else if (!root.TryGetProperty("域路由", out _))
                {
                    return new BridgeRouteTable(false, null, null, $"下游路由表缺「域路由」：{filePath}");
                }
                else
                {
                    return new BridgeRouteTable(false, null, null, $"下游路由表的「域路由」必须是对象：{filePath}");
                }

                var implementations = new Dictionary<string, BridgeImplementation>(StringComparer.Ordinal);
                if (!root.TryGetProperty("实现", out var implementationElement) || implementationElement.ValueKind != JsonValueKind.Object)
                {
                    return new BridgeRouteTable(false, null, null, $"下游路由表缺「实现」或它不是对象：{filePath}");
                }

                foreach (var property in implementationElement.EnumerateObject())
                {
                    var implementation = TryParseImplementation(repositoryRoot, property.Name, property.Value, filePath);
                    if (implementation == null)
                    {
                        return new BridgeRouteTable(false, null, null, $"下游路由表的实现「{property.Name}」不合法：{filePath}");
                    }

                    implementations[property.Name] = implementation;
                }

                return new BridgeRouteTable(true, portRoutes, implementations, "", sourceFileExists: true);
            }
        }

        /// <summary>
        /// 按 port 名查**首选** driver 名。只关心「现在该找谁」的调用点用这条就够了；
        /// 要按策略在失败时换人的，用 <see cref="TryResolveRoute"/> 拿到整条候选清单。
        /// </summary>
        /// <param name="portName">port 名，如「模型加工」。</param>
        /// <param name="driverName">命中的首选 driver 名。</param>
        /// <param name="reason">查不到时的原因。</param>
        public bool TryResolvePort(string portName, out string driverName, out string reason)
        {
            driverName = "";
            if (!TryResolveRoute(portName, out var route, out reason))
            {
                return false;
            }

            driverName = route.PreferredDriverName;
            return true;
        }

        /// <summary>
        /// 按 port 名查整条路由：候选清单与策略。
        /// </summary>
        /// <param name="portName">port 名，如「生图」。</param>
        /// <param name="route">命中的路由。</param>
        /// <param name="reason">查不到时的原因。</param>
        public bool TryResolveRoute(string portName, out PortRoute route, out string reason)
        {
            route = null;
            if (!Loaded)
            {
                reason = LoadFailureReason;
                return false;
            }

            if (PortRoutes.TryGetValue(portName ?? "", out route) && route.Candidates.Count > 0)
            {
                reason = "";
                return true;
            }

            route = null;
            reason = SourceFileExists ? $"域路由表里没有「{portName}」" : MissingFileReason;
            return false;
        }

        /// <summary>
        /// 按实现名解析成可执行 + 参数；参数里的仓库相对路径已按仓库根展开。
        /// </summary>
        /// <param name="implementationName">实现名，如「bridge-xxx」。</param>
        /// <param name="executable">可执行文件名或绝对路径。</param>
        /// <param name="arguments">参数列表，相对路径已展开。</param>
        /// <param name="reason">查不到时的原因。</param>
        public bool TryResolveImplementation(string implementationName, out string executable, out IReadOnlyList<string> arguments, out string reason)
        {
            executable = "";
            arguments = Array.Empty<string>();
            if (!Loaded)
            {
                reason = LoadFailureReason;
                return false;
            }

            if (Implementations.TryGetValue(implementationName ?? "", out var implementation))
            {
                executable = implementation.Executable;
                arguments = implementation.Arguments;
                reason = "";
                return true;
            }

            reason = SourceFileExists ? $"实现表里没有「{implementationName}」" : MissingFileReason;
            return false;
        }

        /// <summary>路由表文件的路径：Tools/CreationPipeline/Config/downstream.json。</summary>
        internal static string RouteTableFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "downstream.json");
        }

        /// <summary>
        /// 解析一条域路由，两种形状都认：
        /// 字符串 <c>"某个driver名"</c> → 候选只有它、策略首选固定；
        /// 对象 <c>{"候选":["a","b"],"策略":"失败转移"}</c> → 按写的来，「策略」缺省是首选固定。
        ///
        /// 形状不对一律返回 null 让整份表判坏，不做静默降级：路由表是调用链的分岔口，
        /// 一个写歪的候选清单会让调用悄悄走到别的下游去，那比当场报错难查得多。
        /// </summary>
        /// <param name="element">这一项的 JSON 值。</param>
        /// <param name="reason">不合法时的原因。</param>
        private static PortRoute TryParsePortRoute(JsonElement element, out string reason)
        {
            reason = "";

            if (element.ValueKind == JsonValueKind.String)
            {
                var driverName = (element.GetString() ?? "").Trim();
                if (driverName.Length == 0)
                {
                    reason = "driver 名是空串";
                    return null;
                }

                return new PortRoute(new[] { driverName }, PortRoute.FixedPreferredStrategy);
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                reason = "既不是 driver 名字符串，也不是 {\"候选\":[…],\"策略\":\"…\"} 对象";
                return null;
            }

            if (!element.TryGetProperty("候选", out var candidateElement) || candidateElement.ValueKind != JsonValueKind.Array)
            {
                reason = "缺「候选」或它不是数组";
                return null;
            }

            var candidates = new List<string>();
            foreach (var item in candidateElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    reason = "「候选」里有不是字符串的项";
                    return null;
                }

                var candidate = (item.GetString() ?? "").Trim();
                if (candidate.Length == 0)
                {
                    reason = "「候选」里有空串";
                    return null;
                }

                // 同一个 driver 在候选里出现两次，失败转移会把它试两遍——白等一轮超时。
                if (candidates.Contains(candidate, StringComparer.Ordinal))
                {
                    reason = $"「候选」里「{candidate}」出现了不止一次";
                    return null;
                }

                candidates.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                reason = "「候选」是空数组，这个域等于没有下游";
                return null;
            }

            var strategy = PortRoute.FixedPreferredStrategy;
            if (element.TryGetProperty("策略", out var strategyElement))
            {
                if (strategyElement.ValueKind != JsonValueKind.String)
                {
                    reason = "「策略」不是字符串";
                    return null;
                }

                strategy = (strategyElement.GetString() ?? "").Trim();
                if (!PortRoute.IsKnownStrategy(strategy))
                {
                    reason = $"「策略」是「{strategy}」，只认「{PortRoute.FixedPreferredStrategy}」与「{PortRoute.FailoverStrategy}」";
                    return null;
                }
            }

            return new PortRoute(candidates, strategy);
        }

        /// <summary>解析一条实现：必须给出「可执行」字符串与「参数」字符串数组；参数里的相对路径按仓库根展开。</summary>
        private static BridgeImplementation TryParseImplementation(string repositoryRoot, string name, JsonElement element, string filePath)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!element.TryGetProperty("可执行", out var executableElement) || executableElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var arguments = new List<string>();
            if (!element.TryGetProperty("参数", out var argumentsElement) || argumentsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var argument in argumentsElement.EnumerateArray())
            {
                if (argument.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                arguments.Add(BridgeRoutePathExpander.Expand(repositoryRoot, argument.GetString() ?? ""));
            }

            return new BridgeImplementation(executableElement.GetString() ?? "", arguments);
        }
    }

    /// <summary>路由表里的一条实现：可执行 + 参数（参数里的仓库相对路径已按仓库根展开）。</summary>
    public sealed class BridgeImplementation
    {
        /// <summary>
        /// 构造一条实现描述。
        /// </summary>
        /// <param name="executable">可执行文件名或绝对路径。</param>
        /// <param name="arguments">参数列表；含路径分隔符的相对项已按仓库根展开。</param>
        public BridgeImplementation(string executable, IReadOnlyList<string> arguments)
        {
            Executable = executable ?? "";
            Arguments = arguments ?? Array.Empty<string>();
        }

        /// <summary>可执行文件名或绝对路径。</summary>
        public string Executable { get; }

        /// <summary>参数列表；含路径分隔符的相对项已按仓库根展开。</summary>
        public IReadOnlyList<string> Arguments { get; }
    }

    /// <summary>
    /// 路由表解析辅助：把实现表里的仓库相对路径按仓库根展开。
    /// 展开规则：参数项不是绝对路径、且含路径分隔符时，按 <c>Path.Combine(仓库根, 项)</c> 展开——
    /// 命令词（run、--project、--）与参数名不含分隔符，原样保留。
    /// </summary>
    internal static class BridgeRoutePathExpander
    {
        /// <summary>
        /// 按仓库根展开一个参数项；不是相对路径就不动。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="argument">原始参数项。</param>
        public static string Expand(string repositoryRoot, string argument)
        {
            if (string.IsNullOrEmpty(argument) || Path.IsPathRooted(argument))
            {
                return argument;
            }

            if (!argument.Contains('/') && !argument.Contains('\\'))
            {
                return argument;
            }

            return Path.GetFullPath(Path.Combine(repositoryRoot, argument));
        }
    }
}
