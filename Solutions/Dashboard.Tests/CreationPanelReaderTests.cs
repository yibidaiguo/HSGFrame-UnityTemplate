using System;
using System.IO;
using System.Text;
using Template.Toolkit.Dashboard;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>面板五页数据读取器测试：全部用系统临时目录建仓库，跑完自删。</summary>
    public sealed class CreationPanelReaderTests : IDisposable
    {
        private readonly string _repositoryRoot;
        private readonly string _poolRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根与池根。</summary>
        public CreationPanelReaderTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板读取器测试-" + Guid.NewGuid().ToString("N"));
            _poolRoot = Path.Combine(_repositoryRoot, "Pools");
            Directory.CreateDirectory(PoolPaths.RequirementsDirectory(_poolRoot));
        }

        /// <summary>空仓库时五个读取器都返回空或默认值，且都不抛。</summary>
        [Fact]
        public void EmptyRepositoryReturnsDefaultsWithoutThrowing()
        {
            Assert.Empty(CreationPanelReader.ReadRequirements(_repositoryRoot, _poolRoot));
            Assert.Empty(CreationPanelReader.ReadTasks(_repositoryRoot, _poolRoot));

            var gateReport = CreationPanelReader.ReadGateReport(_repositoryRoot);
            Assert.Equal("未跑", gateReport.Status);
            Assert.Empty(gateReport.Entries);

            var engine = CreationPanelReader.ReadEngine(_repositoryRoot, _poolRoot);
            Assert.Equal("值守", engine.Mode);
            Assert.Empty(engine.Confirmers);
            Assert.Empty(engine.QueueEntries);

            var overview = CreationPanelReader.ReadOverview(_repositoryRoot, _poolRoot);
            Assert.Equal(0, overview.RunningTaskCount);
            Assert.Equal(0, overview.WaitingGateCount);
            Assert.Equal(0, overview.DraftRequirementCount);
            Assert.Equal(0, overview.QueueLength);
            Assert.Equal("未跑", overview.GateStatus);
            Assert.Equal(0, overview.DriverCount);
            Assert.Equal(0, overview.ProvisionedDriverCount);
        }

        /// <summary>放两份需求 JSON 后读到两条，字段对得上，且按文件名序数序。</summary>
        [Fact]
        public void ReadRequirementsReadsTwoFilesInOrdinalOrder()
        {
            WriteRequirement("REQ-0002", """
                {
                  "id": "REQ-0002",
                  "标题": "需求二",
                  "类型": "修改",
                  "状态": "已确认",
                  "专项": "专项乙",
                  "锁定": false
                }
                """);
            WriteRequirement("REQ-0001", """
                {
                  "id": "REQ-0001",
                  "标题": "需求一",
                  "类型": "系统",
                  "状态": "草稿",
                  "专项": "专项甲",
                  "锁定": true
                }
                """);

            var rows = CreationPanelReader.ReadRequirements(_repositoryRoot, _poolRoot);

            Assert.Equal(2, rows.Count);
            Assert.Equal("REQ-0001", rows[0].Identifier);
            Assert.Equal("需求一", rows[0].Title);
            Assert.Equal("系统", rows[0].RequirementType);
            Assert.Equal("草稿", rows[0].State);
            Assert.Equal("专项甲", rows[0].Epic);
            Assert.True(rows[0].IsLocked);
            Assert.Equal("REQ-0002", rows[1].Identifier);
            Assert.False(rows[1].IsLocked);
        }

        /// <summary>一份需求 JSON 故意写坏，只跳过它，另一份仍读得到。</summary>
        [Fact]
        public void BrokenRequirementFileIsSkipped()
        {
            // 坏 JSON 的内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据，
            // 裸中文写在这里会被当成「标识符含中文」判红。
            WriteRequirement("REQ-0001", """
                {
                  not valid json at all
                """);
            WriteRequirement("REQ-0002", """
                {
                  "id": "REQ-0002",
                  "标题": "完好需求"
                }
                """);

            var rows = CreationPanelReader.ReadRequirements(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.Equal("REQ-0002", row.Identifier);
        }

        /// <summary>_Tasks/REQ-0001/状态.json 存在时读出一条任务，阶段与标题对得上。</summary>
        [Fact]
        public void ReadTasksReadsStateFileAndStage()
        {
            WriteRequirement("REQ-0001", """
                {
                  "id": "REQ-0001",
                  "标题": "需求一"
                }
                """);
            WriteTaskState("REQ-0001", """
                {
                  "阶段": "方案",
                  "子状态": "起草中",
                  "当前工作项": "work-item-1",
                  "关卡待审": null
                }
                """);

            var rows = CreationPanelReader.ReadTasks(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.Equal("REQ-0001", row.RequirementIdentifier);
            Assert.Equal("需求一", row.Title);
            Assert.Equal("方案", row.Stage);
            Assert.Equal("起草中", row.SubState);
            Assert.Equal("work-item-1", row.CurrentWorkItem);
            Assert.Equal("", row.PendingGate);
        }

        /// <summary>门禁报告文件不存在时状态是「未跑」、条目为空。</summary>
        [Fact]
        public void MissingGateReportIsUntested()
        {
            var report = CreationPanelReader.ReadGateReport(_repositoryRoot);

            Assert.Equal("未跑", report.Status);
            Assert.Empty(report.Entries);
        }

        /// <summary>门禁报告里有一条结果不是「成功」时整份是红。</summary>
        [Fact]
        public void GateReportWithFailureIsRed()
        {
            WriteGateReport("""
                {
                  "条目": [
                    { "名称": "需求校验", "结果": "成功", "问题数": 0 },
                    { "名称": "下游边界", "结果": "失败", "问题数": 3 }
                  ]
                }
                """);

            var report = CreationPanelReader.ReadGateReport(_repositoryRoot);

            Assert.Equal("红", report.Status);
            Assert.Equal(2, report.Entries.Count);
            Assert.Equal("下游边界", report.Entries[1].Name);
            Assert.Equal(3, report.Entries[1].FindingCount);
        }

        /// <summary>门禁报告全部成功时整份是绿。</summary>
        [Fact]
        public void GateReportAllSucceededIsGreen()
        {
            WriteGateReport("""
                {
                  "条目": [
                    { "名称": "需求校验", "结果": "成功", "问题数": 0 },
                    { "名称": "供给对账", "结果": "成功", "问题数": 0 }
                  ]
                }
                """);

            var report = CreationPanelReader.ReadGateReport(_repositoryRoot);

            Assert.Equal("绿", report.Status);
        }

        /// <summary>没有引擎配置时 ReadEngine 的模式是「值守」。</summary>
        [Fact]
        public void ReadEngineDefaultsToStandbyWithoutConfig()
        {
            var engine = CreationPanelReader.ReadEngine(_repositoryRoot, _poolRoot);

            Assert.Equal("值守", engine.Mode);
            Assert.Empty(engine.Confirmers);
            Assert.Empty(engine.QueueEntries);
        }

        /// <summary>总览聚合：进行中任务、停在关卡、待确认需求与队列长度都算对。</summary>
        [Fact]
        public void OverviewAggregatesCounts()
        {
            WriteRequirement("REQ-0001", """
                {
                  "id": "REQ-0001",
                  "状态": "草稿"
                }
                """);
            WriteRequirement("REQ-0002", """
                {
                  "id": "REQ-0002",
                  "状态": "已确认"
                }
                """);
            WriteTaskState("REQ-0001", """
                {
                  "阶段": "方案",
                  "关卡待审": "方案审"
                }
                """);
            WriteQueue("""
                {
                  "条目": [
                    { "需求id": "REQ-0002", "入队时间": "2026-01-01T00:00:00", "理由": "测试入队" }
                  ]
                }
                """);

            var overview = CreationPanelReader.ReadOverview(_repositoryRoot, _poolRoot);

            Assert.Equal(1, overview.RunningTaskCount);
            Assert.Equal(1, overview.WaitingGateCount);
            Assert.Equal(1, overview.DraftRequirementCount);
            Assert.Equal(1, overview.QueueLength);
            Assert.Equal("未跑", overview.GateStatus);
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

        private void WriteRequirement(string identifier, string json)
        {
            WriteFile(PoolPaths.RequirementFile(_poolRoot, identifier), json);
        }

        private void WriteTaskState(string requirementIdentifier, string json)
        {
            var directory = PipelinePaths.TaskDirectory(_repositoryRoot, requirementIdentifier);
            Directory.CreateDirectory(directory);
            WriteFile(PipelinePaths.TaskStateFile(_repositoryRoot, requirementIdentifier), json);
        }

        private void WriteGateReport(string json)
        {
            var directory = Path.Combine(_repositoryRoot, "_Generated");
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, "gate-report.json"), json);
        }

        private void WriteQueue(string json)
        {
            WriteFile(PoolPaths.QueueFile(_poolRoot), json);
        }

        private static void WriteFile(string path, string json)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
    }
}
