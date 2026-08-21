using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 下游域路由表：把 port 名路由到 driver 名、把实现名解析成可执行 + 参数。
    /// 数据来自 <c>Tools/CreationPipeline/Config/downstream.json</c>，该文件进 git、只存密钥「名」不存密钥「值」。
    /// 文件不存在是正常状态（表为空）；文件存在但解析失败才是坏（Loaded=false，整份表不可信）。
    /// </summary>
    public sealed class BridgeRouteTable
    {
        /// <summary>文件不存在时 TryResolve 给出的原因。</summary>
        public const string MissingFileReason = "下游路由表不存在";

        /// <summary>
        /// 构造一份路由表。
        /// </summary>
        /// <param name="loaded">文件存在且解析成功，或文件不存在；false 表示文件坏掉。</param>
        /// <param name="portRoutes">port 名 → driver 名。</param>
        /// <param name="implementations">实现名 → 实现（可执行 + 参数）。</param>
        /// <param name="loadFailureReason">文件坏掉时的原因；正常时为 ""。</param>
        public BridgeRouteTable(
            bool loaded,
            IReadOnlyDictionary<string, string> portRoutes,
            IReadOnlyDictionary<string, BridgeImplementation> implementations,
            string loadFailureReason)
            : this(loaded, portRoutes, implementations, loadFailureReason, sourceFileExists: false)
        {
        }

        /// <summary>
        /// 构造一份路由表。
        /// </summary>
        /// <param name="loaded">文件存在且解析成功，或文件不存在；false 表示文件坏掉。</param>
        /// <param name="portRoutes">port 名 → driver 名。</param>
        /// <param name="implementations">实现名 → 实现（可执行 + 参数）。</param>
        /// <param name="loadFailureReason">文件坏掉时的原因；正常时为 ""。</param>
        /// <param name="sourceFileExists">路由表文件是否真实存在。</param>
        public BridgeRouteTable(
            bool loaded,
            IReadOnlyDictionary<string, string> portRoutes,
            IReadOnlyDictionary<string, BridgeImplementation> implementations,
            string loadFailureReason,
            bool sourceFileExists)
        {
            Loaded = loaded;
            PortRoutes = portRoutes ?? new Dictionary<string, string>(StringComparer.Ordinal);
            Implementations = implementations ?? new Dictionary<string, BridgeImplementation>(StringComparer.Ordinal);
            LoadFailureReason = loadFailureReason ?? "";
            SourceFileExists = sourceFileExists;
        }

        /// <summary>文件存在且解析成功，或文件不存在；false 表示文件坏掉，整份表不可信。</summary>
        public bool Loaded { get; }

        /// <summary>port 名 → driver 名。</summary>
        public IReadOnlyDictionary<string, string> PortRoutes { get; }

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

                var portRoutes = new Dictionary<string, string>(StringComparer.Ordinal);
                if (root.TryGetProperty("域路由", out var portElement) && portElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in portElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            portRoutes[property.Name] = property.Value.GetString() ?? "";
                        }
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
        /// 按 port 名查 driver 名。
        /// </summary>
        /// <param name="portName">port 名，如「模型加工」。</param>
        /// <param name="driverName">命中的 driver 名。</param>
        /// <param name="reason">查不到时的原因。</param>
        public bool TryResolvePort(string portName, out string driverName, out string reason)
        {
            if (!Loaded)
            {
                driverName = "";
                reason = LoadFailureReason;
                return false;
            }

            if (PortRoutes.TryGetValue(portName ?? "", out driverName))
            {
                reason = "";
                return true;
            }

            driverName = "";
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
