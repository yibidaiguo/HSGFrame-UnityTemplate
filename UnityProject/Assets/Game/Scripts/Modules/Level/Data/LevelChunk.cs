using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Template.Level.Data
{
    /// <summary>关卡区块：区块名与其包含的逻辑实体摆放。</summary>
    public class LevelChunk
    {
        [JsonPropertyName("区块名")]
        public string ChunkName { get; set; }

        [JsonPropertyName("实体清单")]
        public List<LogicEntityPlacement> Placements { get; set; } = new List<LogicEntityPlacement>();
    }
}
