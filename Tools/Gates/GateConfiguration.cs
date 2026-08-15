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

        /// <summary>单文档行数上限。</summary>
        public int DocumentLineLimit { get; set; }

        /// <summary>豁免长度检查的文档路径（正斜杠、仓库相对）。</summary>
        public IReadOnlyList<string> DocumentExemptions { get; set; }

        /// <summary>改动文件路径白名单前缀。</summary>
        public IReadOnlyList<string> ChangedPathWhitelist { get; set; }

        /// <summary>源码扫描要跳过的目录名，用来排除第三方与生成物（如 HybridCLRData）。</summary>
        public IReadOnlyList<string> SourceScanSkipSegments { get; set; }

        /// <summary>测试源文件的 glob 模式。</summary>
        public IReadOnlyList<string> TestFileGlobs { get; set; }

        /// <summary>宿主项目专属名字黑名单：出现在标识符、菜单路径、路径字面量里就报。</summary>
        public IReadOnlyList<string> GenericNameBlacklist { get; set; }

        /// <summary>通用性检查的整文件豁免：按仓库相对路径前缀豁免的文件。</summary>
        public IReadOnlyList<string> GenericNameExemptPaths { get; set; }

        /// <summary>从配置文件读取门禁配置。</summary>
        /// <param name="configPath">gate-config.json 的路径。</param>
        public static GateConfiguration LoadFromFile(string configPath)
        {
            var json = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<GateConfiguration>(json, options);
        }
    }
}
