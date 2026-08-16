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
                    errors.Add(ComposeError(
                        $"区块 {chunkName}",
                        "区块文件缺失",
                        "在关卡目录下补上该区块的 json 文件",
                        "Levels/村庄/区块_村口.json"));
                }
            }

            foreach (var chunkName in chunksByName.Keys)
            {
                if (!level.ChunkNames.Contains(chunkName))
                {
                    errors.Add(ComposeError(
                        $"区块 {chunkName}",
                        "区块未登记进关卡清单",
                        "把这个区块名加进 关卡.json 的区块清单，或删掉多余的区块文件",
                        "Levels/村庄/关卡.json"));
                }
            }

            var seenEntityIds = new HashSet<string>();
            foreach (var chunk in chunksByName.Values)
            {
                foreach (var placement in chunk.Placements)
                {
                    if (string.IsNullOrEmpty(placement.EntityId))
                    {
                        errors.Add(ComposeError(
                            "实体编号",
                            "实体编号为空",
                            "给这个实体补一个唯一编号",
                            "村口_守卫_01"));
                    }
                    else if (!seenEntityIds.Add(placement.EntityId))
                    {
                        errors.Add(ComposeError(
                            $"实体编号 {placement.EntityId}",
                            "实体编号重复",
                            "给重复的实体改一个唯一编号",
                            "村口_守卫_01"));
                    }

                    if (string.IsNullOrEmpty(placement.EntityKind))
                    {
                        errors.Add(ComposeError(
                            "实体类别",
                            "实体类别为空",
                            "给这个实体补上类别（NPC / 触发器 / 刷怪点 / 可交互物 / 传送点 / 任务物件）",
                            "NPC"));
                    }
                }
            }

            return errors;
        }

        private static string ComposeError(string location, string reason, string fix, string reference)
        {
            return $"位置：{location}；原因：{reason}；修复：{fix}；参考：{reference}";
        }
    }
}
