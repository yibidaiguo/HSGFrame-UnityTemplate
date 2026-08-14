using System.Collections.Generic;

namespace Template.Logic.Data.Level
{
    /// <summary>关卡结构校验：检查区块清单一致性、实体编号唯一性与必填字段。</summary>
    public static class LevelValidator
    {
        /// <summary>逐条校验关卡，返回中文错误消息列表，全通过时为空。</summary>
        public static IReadOnlyList<string> Validate(LevelDefinition level, IReadOnlyDictionary<string, LevelChunk> chunksByName)
        {
            var errors = new List<string>();

            foreach (var chunkName in level.ChunkNames)
            {
                if (!chunksByName.ContainsKey(chunkName))
                {
                    errors.Add($"区块 {chunkName} 的文件缺失");
                }
            }

            foreach (var chunkName in chunksByName.Keys)
            {
                if (!level.ChunkNames.Contains(chunkName))
                {
                    errors.Add($"区块 {chunkName} 未登记进关卡清单");
                }
            }

            var seenEntityIds = new HashSet<string>();
            foreach (var chunk in chunksByName.Values)
            {
                foreach (var placement in chunk.Placements)
                {
                    if (string.IsNullOrEmpty(placement.EntityId))
                    {
                        errors.Add("实体编号为空");
                    }
                    else if (!seenEntityIds.Add(placement.EntityId))
                    {
                        errors.Add($"实体编号 {placement.EntityId} 重复");
                    }

                    if (string.IsNullOrEmpty(placement.EntityKind))
                    {
                        errors.Add("实体类别为空");
                    }
                }
            }

            return errors;
        }
    }
}
