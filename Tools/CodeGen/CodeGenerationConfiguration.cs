using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CodeGen
{
    /// <summary>代码生成目标清单。</summary>
    public sealed class CodeGenerationConfiguration
    {
        /// <summary>全部生成目标。</summary>
        public IReadOnlyList<CodeGenerationTarget> Targets { get; set; }

        /// <summary>从 JSON 文件读取生成目标清单。</summary>
        /// <param name="configurationPath">配置文件路径。</param>
        public static CodeGenerationConfiguration LoadFromFile(string configurationPath)
        {
            return JsonSerializer.Deserialize<CodeGenerationConfiguration>(
                File.ReadAllText(configurationPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
    }
}
