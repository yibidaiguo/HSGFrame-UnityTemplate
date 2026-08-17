using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.Gates
{
    /// <summary>门禁配置：从 gate-config.json 反序列化出的阈值与名单。</summary>
    public sealed class GateConfiguration
    {
        /// <summary>标识符缩写黑名单，例如 Mgr、Cfg。</summary>
        public IReadOnlyList<string> AbbreviationBlacklist { get; set; }

        /// <summary>
        /// 豁免缩写检查的标识符，逐字匹配。
        /// 只用于第三方 API 的成员名——那些名字由对方定，改不了，而调用点又绕不开写出它们。
        /// </summary>
        public IReadOnlyList<string> AbbreviationExemptIdentifiers { get; set; }

        /// <summary>目录名黑名单，例如 misc、common。</summary>
        public IReadOnlyList<string> DirectoryNameBlacklist { get; set; }

        /// <summary>目录名的合法命名正则。</summary>
        public string DirectoryNamePattern { get; set; }

        /// <summary>
        /// 允许以下划线开头的目录名与资产文件名。
        /// 下划线的语义是「此处内容不是人手维护的正式品」，所以放行的是机器管理区；
        /// 迁移期的过渡名字也先挂在这里，迁一块删一条，名单燃尽即规矩完全落地。
        /// </summary>
        public IReadOnlyList<string> UnderscoreExemptNames { get; set; }

        /// <summary>单文档行数上限。</summary>
        public int DocumentLineLimit { get; set; }

        /// <summary>
        /// 豁免长度检查的文档路径（正斜杠、仓库相对）。
        /// **宿主专属**，落在 gate-config.host.json 里——豁免哪份文档按定义就是宿主自己的事，
        /// 写进通用配置等于让模板记住宿主有哪些文档。
        /// </summary>
        public IReadOnlyList<string> DocumentExemptions { get; set; }

        /// <summary>改动文件路径白名单前缀。**宿主专属**，落在 gate-config.host.json 里。</summary>
        public IReadOnlyList<string> ChangedPathWhitelist { get; set; }

        /// <summary>
        /// 由常驻编辑器进程自己写的目录前缀，白名单判定前先摘出去。
        /// **宿主专属**，落在 gate-config.host.json 里——模板不该知道宿主的旧工程叫什么。
        /// </summary>
        public IReadOnlyList<string> EditorOwnedPathPrefixes { get; set; }

        /// <summary>源码扫描要跳过的目录名，用来排除第三方与生成物（如 HybridCLRData）。</summary>
        public IReadOnlyList<string> SourceScanSkipSegments { get; set; }

        /// <summary>
        /// 豁免模块边界检查的路径前缀，相对 <c>Assets/Game/Scripts</c>、用正斜杠。
        /// 这里挂两种东西：**欠账**（还没拆干净的越界引用，拆一处删一条），
        /// 以及**结构性例外**——最典型的是装配根：它按定义要构造并接线每一个模块的服务，
        /// 而 R2 没有「装配根」这个概念，所以它永远拆不干净，条目也永远不会燃尽。
        /// 两种都写在这里时，请在 host 配置里逐条注明是哪一种，否则下一个人会去拆那条拆不动的。
        /// 工具链那类天然在范围之外的东西不走这里，它写在检查器里。
        /// **宿主专属**，落在 gate-config.host.json 里——挂在哪几个路径上是宿主的工程结构决定的。
        /// </summary>
        public IReadOnlyList<string> ModuleBoundaryExemptPaths { get; set; }

        /// <summary>
        /// 豁免业务层裸日志检查的路径前缀，相对 <c>Assets/Game/Scripts</c>、用正斜杠。
        /// 里面**永久**该有一条：日志落点自己（`View/UnityConsoleLogSink.cs`）——
        /// 它就是那个把 <c>HSGFrame.Logging</c> 转到 Unity 控制台的适配器，不许它调 Debug 等于不许它存在。
        /// 除此之外的条目都是欠账，改一处删一条。
        /// </summary>
        public IReadOnlyList<string> BusinessLogExemptPaths { get; set; }

        /// <summary>加载分组为「常驻」的目录字节总预算；≤0 表示不查（R6）。</summary>
        public long ResidentBudgetBytes { get; set; }

        /// <summary>测试源文件的 glob 模式。</summary>
        public IReadOnlyList<string> TestFileGlobs { get; set; }

        /// <summary>宿主项目专属名字黑名单：出现在标识符、菜单路径、路径字面量里就报。
        /// **宿主专属**，落在 gate-config.host.json 里。</summary>
        public IReadOnlyList<string> GenericNameBlacklist { get; set; }

        /// <summary>通用性检查的整文件豁免：按仓库相对路径前缀豁免的文件。</summary>
        public IReadOnlyList<string> GenericNameExemptPaths { get; set; }

        /// <summary>
        /// 可选功能的引用范围规则。声明为可选功能的那批程序集，只有该功能包目录内的 asmdef 才许引用——
        /// 包外冒出一处引用，这个功能就摘不干净了，而这种耦合不盯着就会自己长回来。
        /// </summary>
        public IReadOnlyList<OptionalFeatureScope> OptionalFeatureScopes { get; set; }

        /// <summary>宿主专属配置的文件名，与 gate-config.json 同目录。</summary>
        public const string HostConfigurationFileName = "gate-config.host.json";

        /// <summary>
        /// 从配置文件读取门禁配置。同目录下有 <see cref="HostConfigurationFileName"/> 时，
        /// 它里面写了的项覆盖通用配置里的同名项。
        /// 分成两个文件是因为白名单前缀与编辑器自有目录是**宿主专属**的：
        /// 混在一份文件里，模板同步就会把来源仓库的目录前缀带到去向仓库。
        /// </summary>
        /// <param name="configPath">gate-config.json 的路径。</param>
        public static GateConfiguration LoadFromFile(string configPath)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var configuration = JsonSerializer.Deserialize<GateConfiguration>(File.ReadAllText(configPath), options);

            var hostConfigPath = ResolveHostConfigurationPath(configPath);
            if (hostConfigPath == null || !File.Exists(hostConfigPath))
            {
                return configuration;
            }

            var hostConfiguration = JsonSerializer.Deserialize<GateConfiguration>(File.ReadAllText(hostConfigPath), options);
            if (hostConfiguration == null)
            {
                return configuration;
            }

            if (hostConfiguration.ChangedPathWhitelist != null)
            {
                configuration.ChangedPathWhitelist = hostConfiguration.ChangedPathWhitelist;
            }

            if (hostConfiguration.EditorOwnedPathPrefixes != null)
            {
                configuration.EditorOwnedPathPrefixes = hostConfiguration.EditorOwnedPathPrefixes;
            }

            if (hostConfiguration.GenericNameBlacklist != null)
            {
                configuration.GenericNameBlacklist = hostConfiguration.GenericNameBlacklist;
            }

            if (hostConfiguration.DocumentExemptions != null)
            {
                configuration.DocumentExemptions = hostConfiguration.DocumentExemptions;
            }

            // R2 的豁免清单也归宿主：越界引用挂在哪几个路径上，是宿主自己的工程结构决定的。
            // 锁在通用配置里的后果是宿主只能改模板文件——每次合并都平白起冲突，
            // 而且模板会因此记住宿主有哪些目录（G5）。装配根这类**结构性**的例外尤其如此：
            // 它按定义要碰每个模块的服务，而 R2 没有「装配根」这个概念。
            if (hostConfiguration.ModuleBoundaryExemptPaths != null)
            {
                configuration.ModuleBoundaryExemptPaths = hostConfiguration.ModuleBoundaryExemptPaths;
            }

            return configuration;
        }

        /// <summary>推出宿主专属配置的路径：与传入的通用配置同目录。</summary>
        /// <param name="configPath">gate-config.json 的路径。</param>
        public static string ResolveHostConfigurationPath(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(configPath));
            return directory == null ? null : Path.Combine(directory, HostConfigurationFileName);
        }
    }
}
