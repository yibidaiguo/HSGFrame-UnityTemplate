using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Template.Logic.Data.Level
{
    /// <summary>关卡元信息：关卡名、环境名与区块清单。</summary>
    public class LevelDefinition
    {
        [JsonPropertyName("关卡名")]
        public string LevelName { get; set; }

        [JsonPropertyName("环境")]
        public string EnvironmentName { get; set; }

        [JsonPropertyName("区块清单")]
        public List<string> ChunkNames { get; set; } = new List<string>();
    }
}
