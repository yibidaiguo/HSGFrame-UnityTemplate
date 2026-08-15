using System;
using System.Collections.Generic;
using System.IO;
using Template.Logic.Data.Level;
using Xunit;

namespace Template.Logic.Tests
{
    /// <summary>关卡 JSON 与内存模型的往返及校验测试。</summary>
    public class LevelRoundTripTests
    {
        [Fact]
        public void LevelDefinitionRoundTripsThroughJson()
        {
            var level = new LevelDefinition
            {
                LevelName = "村庄",
                EnvironmentName = "晴天_黄昏",
            };
            level.ChunkNames.Add("区块_村口");
            level.ChunkNames.Add("区块_广场");

            var json = LevelSerializer.ToJson(level);
            var roundTripped = LevelSerializer.LevelFromJson(json);

            Assert.Equal(level.LevelName, roundTripped.LevelName);
            Assert.Equal(level.EnvironmentName, roundTripped.EnvironmentName);
            Assert.Equal(level.ChunkNames, roundTripped.ChunkNames);
        }

        [Fact]
        public void LevelChunkRoundTripsThroughJson()
        {
            var chunk = new LevelChunk { ChunkName = "区块_村口" };
            chunk.Placements.Add(new LogicEntityPlacement
            {
                EntityId = "村口_守卫_01",
                EntityKind = "NPC",
                Position = new LevelVector3(12.5f, 0.0f, -3.25f),
                RotationAngle = 90.0f,
            });
            chunk.Placements.Add(new LogicEntityPlacement
            {
                EntityId = "村口_触发器_01",
                EntityKind = "触发器",
                Position = new LevelVector3(0.0f, 0.0f, 0.0f),
                RotationAngle = 45.0f,
            });
            chunk.Placements[1].Parameters["对话图"] = "村口守卫对话";
            chunk.Placements[1].Parameters["阵营"] = "友方";

            var json = LevelSerializer.ToJson(chunk);
            var roundTripped = LevelSerializer.ChunkFromJson(json);

            Assert.Equal(2, roundTripped.Placements.Count);
            Assert.Equal("村口_守卫_01", roundTripped.Placements[0].EntityId);
            Assert.Equal("NPC", roundTripped.Placements[0].EntityKind);

            // 方案模块 1 要求 float 比较带容差，位置与朝向统一用三位小数容差断言。
            Assert.Equal(12.5, roundTripped.Placements[0].Position.X, 3);
            Assert.Equal(0.0, roundTripped.Placements[0].Position.Y, 3);
            Assert.Equal(-3.25, roundTripped.Placements[0].Position.Z, 3);
            Assert.Equal(90.0, roundTripped.Placements[0].RotationAngle, 3);

            Assert.Equal("村口守卫对话", roundTripped.Placements[1].Parameters["对话图"]);
            Assert.Equal("友方", roundTripped.Placements[1].Parameters["阵营"]);
        }

        [Fact]
        public void SerializerEmitsChineseKeysUnescaped()
        {
            var level = new LevelDefinition { LevelName = "村庄", EnvironmentName = "晴天_黄昏" };
            var chunk = new LevelChunk { ChunkName = "区块_村口" };
            chunk.Placements.Add(new LogicEntityPlacement { EntityId = "村口_守卫_01", EntityKind = "NPC", RotationAngle = 90.0f });

            var levelJson = LevelSerializer.ToJson(level);
            var chunkJson = LevelSerializer.ToJson(chunk);

            Assert.Contains("\"关卡名\"", levelJson);
            Assert.Contains("\"实体清单\"", chunkJson);
            Assert.Contains("\"朝向角度\"", chunkJson);

            // UnsafeRelaxedJsonEscaping 下中文原样输出，不应出现 \u 转义。
            Assert.DoesNotContain("\\u", levelJson);
            Assert.DoesNotContain("\\u", chunkJson);
        }

        [Fact]
        // 样例关卡的两个区块文件现在都在（阶段 7 补齐），这条盯的是「只喂进其中一块时另一块报缺失」，
        // 与磁盘上有几份区块文件无关，所以断言维持原样，只把名字改成它真正测的东西。
        public void FeedingOnlyOneChunkReportsTheOtherAsMissing()
        {
            var templateRoot = FindTemplateRoot();
            var levelPath = Path.Combine(templateRoot, "Levels", "村庄", "关卡.json");
            var chunkPath = Path.Combine(templateRoot, "Levels", "村庄", "区块_村口.json");

            Assert.True(File.Exists(levelPath), $"样例关卡文件不存在：{levelPath}");
            Assert.True(File.Exists(chunkPath), $"样例区块文件不存在：{chunkPath}");

            var level = LevelSerializer.LevelFromJson(File.ReadAllText(levelPath));
            var chunk = LevelSerializer.ChunkFromJson(File.ReadAllText(chunkPath));

            var chunksByName = new Dictionary<string, LevelChunk> { { chunk.ChunkName, chunk } };

            var errors = LevelValidator.Validate(level, chunksByName);

            var error = Assert.Single(errors);
            Assert.Contains("区块_广场", error);
        }

        [Fact]
        public void DuplicateEntityIdIsReported()
        {
            var level = new LevelDefinition { LevelName = "测试关卡" };
            level.ChunkNames.Add("区块_A");
            var chunk = new LevelChunk { ChunkName = "区块_A" };
            chunk.Placements.Add(new LogicEntityPlacement { EntityId = "守卫_01", EntityKind = "NPC" });
            chunk.Placements.Add(new LogicEntityPlacement { EntityId = "守卫_01", EntityKind = "NPC" });

            var chunksByName = new Dictionary<string, LevelChunk> { { chunk.ChunkName, chunk } };

            var errors = LevelValidator.Validate(level, chunksByName);

            Assert.Contains(errors, error => error.Contains("重复"));
        }

        // 测试工作目录不稳定，不能靠相对路径硬拼：从程序集目录逐级向上找带 Tools/Gates/Config 的那一级作为模板根——
        // 模板被复制成别的项目名之后，这个标记仍然成立，而目录名 "Template" 不再成立。
        private static string FindTemplateRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            var searched = new List<string>();
            while (directory != null)
            {
                searched.Add(directory.FullName);
                if (File.Exists(Path.Combine(directory.FullName, "Tools", "Gates", "Config", "gate-config.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            Assert.Fail($"未找到包含 Template 目录的仓库根，已查找：{string.Join(Environment.NewLine, searched)}");
            return string.Empty;
        }
    }
}
