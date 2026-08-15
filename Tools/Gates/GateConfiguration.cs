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

        /// <summary>豁免长度检查的文档路径（正斜杠、仓库相对）。</summary>
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

        /// <summary>测试源文件的 glob 模式。</summary>
        public IReadOnlyList<string> TestFileGlobs { get; set; }

        /// <summary>宿主项目专属名字黑名单：出现在标识符、菜单路径、路径字面量里就报。
        /// **宿主专属**，落在 gate-config.host.json 里。</summary>
        public IReadOnlyList<string> GenericNameBlacklist { get; set; }

        /// <summary>通用性检查的整文件豁免：按仓库相对路径前缀豁免的文件。</summary>
        public IReadOnlyList<string> GenericNameExemptPaths { get; set; }

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
