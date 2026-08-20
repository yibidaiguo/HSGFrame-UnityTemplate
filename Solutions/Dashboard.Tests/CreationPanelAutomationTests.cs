using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Template.Toolkit.Dashboard;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>面板任务图 / 冲突 / 晋升三页读取器的自动化测试：全部用系统临时目录建仓库，跑完自删。</summary>
    public sealed class CreationPanelAutomationTests : IDisposable
    {
        private readonly string _repositoryRoot;
        private readonly string _poolRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根与池根。</summary>
        public CreationPanelAutomationTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板自动化测试-" + Guid.NewGuid().ToString("N"));
            _poolRoot = Path.Combine(_repositoryRoot, "Pools");
            Directory.CreateDirectory(_poolRoot);
        }

        /// <summary>ReadTaskDag：A ← B ← C 链 → 深度 0/1/2。</summary>
        [Fact]
        public void ReadTaskDagComputesChainDepths()
        {
            var directory = WorkItemDirectory("REQ-0001");
            WriteWorkItem(directory, "WI-0001-01", Array.Empty<string>());
            WriteWorkItem(directory, "WI-0001-02", new[] { "WI-0001-01" });
            WriteWorkItem(directory, "WI-0001-03", new[] { "WI-0001-02" });

            var rows = CreationPanelReader.ReadTaskDag(_repositoryRoot, "REQ-0001");

            Assert.Equal(3, rows.Count);
            Assert.Equal("WI-0001-01", rows[0].Identifier);
            Assert.Equal(0, rows[0].Depth);
            Assert.Equal("WI-0001-02", rows[1].Identifier);
            Assert.Equal(1, rows[1].Depth);
            Assert.Equal("WI-0001-03", rows[2].Identifier);
            Assert.Equal(2, rows[2].Depth);

            // 标题走的是 WorkItemGraph 那一次读取（同源），不是面板另开一份读工作项文件。
            Assert.All(rows, row => Assert.Equal("t", row.Title));
        }

        /// <summary>ReadTaskDag：有环 → 环上节点深度是 -1 且不死循环。</summary>
        [Fact]
        public void ReadTaskDagMarksCycleNodesMinusOne()
        {
            var directory = WorkItemDirectory("REQ-0001");
            WriteWorkItem(directory, "WI-0001-01", new[] { "WI-0001-02" });
            WriteWorkItem(directory, "WI-0001-02", new[] { "WI-0001-01" });

            var rows = CreationPanelReader.ReadTaskDag(_repositoryRoot, "REQ-0001");

            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Equal(-1, row.Depth));
        }

        /// <summary>ReadConflicts：未决的 IsPending 为 true；裁决成「强制推送」的 IsPending 仍是 true；已裁决的不算。</summary>
        [Fact]
        public void ReadConflictsPendingMatchesForcePush()
        {
            WriteConflicts("""
                [
                  { "id": "CF-0001", "旧": "DESIGN-001", "新": "REQ-0002", "发现阶段": "入库", "状态": "未决", "裁决": null },
                  { "id": "CF-0002", "旧": "DESIGN-001", "新": "REQ-0003", "发现阶段": "入库", "状态": "未决", "裁决": { "人": "张三", "选择": "强制推送", "时间": "2026-08-20T10:00:00+09:00" } },
                  { "id": "CF-0003", "旧": "DESIGN-002", "新": "REQ-0004", "发现阶段": "影响评估", "状态": "已裁决", "裁决": { "人": "李四", "选择": "改新的", "时间": "2026-08-20T10:00:00+09:00" } }
                ]
                """);

            var rows = CreationPanelReader.ReadConflicts(_poolRoot);

            Assert.Equal(3, rows.Count);
            Assert.True(rows[0].IsPending);
            Assert.True(rows[1].IsPending);
            Assert.False(rows[2].IsPending);
            Assert.Equal("强制推送", rows[1].Choice);
        }

        /// <summary>ReadPromotions：阈值内的出行，阈值外的不出。</summary>
        [Fact]
        public void ReadPromotionsRespectsThreshold()
        {
            WriteOpinion("OP-0001", "空引用未防", "可代码化", "签到", "a");
            WriteOpinion("OP-0002", "空引用未防", "可代码化", "签到", "b");
            WriteOpinion("OP-0003", "空引用未防", "可代码化", "签到", "c");
            WriteOpinion("OP-0004", "命名歧义", "可提示词化", "任务", "d");

            var rows = CreationPanelReader.ReadPromotions(_poolRoot, 3);

            var row = Assert.Single(rows);
            Assert.Equal("空引用未防", row.Category);
            Assert.Equal(3, row.Count);
            Assert.Equal("检查器", row.TargetChannel);
        }

        /// <summary>三个方法在数据完全不存在时都返回空列表且不抛。</summary>
        [Fact]
        public void MissingDataReturnsEmptyWithoutThrowing()
        {
            Assert.Empty(CreationPanelReader.ReadTaskDag(_repositoryRoot, "REQ-9999"));
            Assert.Empty(CreationPanelReader.ReadConflicts(_poolRoot));
            Assert.Empty(CreationPanelReader.ReadPromotions(_poolRoot, 3));
        }

        /// <summary>删除本测试建的临时目录；清理失败不影响测试结论。</summary>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_repositoryRoot))
                {
                    Directory.Delete(_repositoryRoot, true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响测试结论，按契约静默。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }

        private string WorkItemDirectory(string requirementIdentifier)
        {
            return Path.Combine(_repositoryRoot, "_Tasks", requirementIdentifier, "20-工作项");
        }

        private void WriteWorkItem(string directory, string identifier, IReadOnlyList<string> dependencies)
        {
            Directory.CreateDirectory(directory);
            var entryObject = new JsonObject
            {
                ["id"] = identifier,
                ["需求id"] = "REQ-0001",
                ["域"] = "文档",
                ["标题"] = "t",
                ["状态"] = "待执行",
                ["依赖"] = ToJsonArray(dependencies),
                ["验收点"] = "v",
                ["引用需求字段"] = new JsonArray(),
                ["产物"] = new JsonArray()
            };
            File.WriteAllText(Path.Combine(directory, identifier + ".json"), entryObject.ToJsonString(), new UTF8Encoding(false));
        }

        private static JsonArray ToJsonArray(IReadOnlyList<string> values)
        {
            var array = new JsonArray();
            foreach (var value in values)
            {
                array.Add(value);
            }

            return array;
        }

        private void WriteConflicts(string json)
        {
            var directory = Path.Combine(_poolRoot, "Designs");
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, "冲突列表.json"), json);
        }

        private void WriteOpinion(string identifier, string category, string rulability, string moduleName, string quotation)
        {
            var directory = PoolPaths.ReviewOpinionDirectory(_poolRoot);
            Directory.CreateDirectory(directory);
            var content = new JsonObject
            {
                ["id"] = identifier,
                ["问题类别"] = category,
                ["模块"] = moduleName,
                ["可规则化性"] = rulability,
                ["原文引用"] = quotation,
                ["时间"] = "2026-08-20T10:00:00+09:00"
            };
            WriteFile(Path.Combine(directory, identifier + ".json"), content.ToJsonString());
        }

        private static void WriteFile(string path, string json)
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
    }
}
