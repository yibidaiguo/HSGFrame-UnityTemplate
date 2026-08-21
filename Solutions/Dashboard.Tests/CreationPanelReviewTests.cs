using System;
using System.IO;
using System.Text;
using Template.Toolkit.Dashboard;
using Template.Toolkit.CreationPipeline;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>审查页（终审队列 / 放行流水 / 晋升提案待批）三个读取器测试：全部用系统临时目录建仓库，跑完自删。</summary>
    public sealed class CreationPanelReviewTests : IDisposable
    {
        private readonly string _repositoryRoot;
        private readonly string _poolRoot;

        /// <summary>构造：在系统临时目录下建一个空仓库根与池根。</summary>
        public CreationPanelReviewTests()
        {
            _repositoryRoot = Path.Combine(Path.GetTempPath(), "面板审查测试-" + Guid.NewGuid().ToString("N"));
            _poolRoot = Path.Combine(_repositoryRoot, "Pools");
            Directory.CreateDirectory(_poolRoot);
        }

        /// <summary>_Tasks 目录不存在时终审队列返回空列表，不抛异常。</summary>
        [Fact]
        public void MissingTaskDirectoryReturnsEmptyQueue()
        {
            var rows = CreationPanelReader.ReadReviewQueue(_repositoryRoot, _poolRoot);

            Assert.Empty(rows);
        }

        /// <summary>两个任务，一个关卡待审为空、一个非空，只出一行。</summary>
        [Fact]
        public void OnlyTasksWithPendingGateAppear()
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
                  "关卡待审": ""
                }
                """);
            WriteTaskState("REQ-0002", """
                {
                  "阶段": "方案",
                  "子状态": "等审",
                  "关卡待审": "方案审"
                }
                """);

            var rows = CreationPanelReader.ReadReviewQueue(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.Equal("REQ-0002", row.RequirementIdentifier);
            Assert.Equal("方案审", row.PendingGate);
        }

        /// <summary>标题从需求文件取到；需求文件缺失时标题空串但行还在。</summary>
        [Fact]
        public void TitleComesFromRequirementFileOrFallsBackToEmpty()
        {
            WriteRequirement("REQ-0001", """
                {
                  "id": "REQ-0001",
                  "标题": "有标题的需求"
                }
                """);
            WriteTaskState("REQ-0001", """
                {
                  "阶段": "方案",
                  "关卡待审": "方案审"
                }
                """);
            // REQ-0002 没有需求文件，只有任务状态文件。
            WriteTaskState("REQ-0002", """
                {
                  "阶段": "方案",
                  "关卡待审": "方案审"
                }
                """);

            var rows = CreationPanelReader.ReadReviewQueue(_repositoryRoot, _poolRoot);

            Assert.Equal(2, rows.Count);
            Assert.Equal("有标题的需求", rows[0].Title);
            Assert.Equal("REQ-0002", rows[1].RequirementIdentifier);
            Assert.Equal("", rows[1].Title);
        }

        /// <summary>风险级从放行流水搬：流水里有该需求给最近一条的 Grade，没有给空串。</summary>
        [Fact]
        public void GradeComesFromReleaseLedger()
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
                  "关卡待审": "方案审"
                }
                """);
            WriteTaskState("REQ-0002", """
                {
                  "阶段": "方案",
                  "关卡待审": "方案审"
                }
                """);
            WriteReleases("""
                {
                  "条目": [
                    { "id": "RL-0001", "需求id": "REQ-0001", "风险级": "常规", "范围": ["A"], "放行时间": "2026-01-01T00:00:00Z", "合并提交": "abc", "抽查状态": "未抽查", "抽查结论": "", "回滚提交": "" },
                    { "id": "RL-0002", "需求id": "REQ-0001", "风险级": "高", "范围": ["B"], "放行时间": "2026-01-02T00:00:00Z", "合并提交": "def", "抽查状态": "未抽查", "抽查结论": "", "回滚提交": "" }
                  ]
                }
                """);

            var rows = CreationPanelReader.ReadReviewQueue(_repositoryRoot, _poolRoot);

            // REQ-0001 有两条流水，取最近一条（RL-0002）的高；REQ-0002 流水里没有，空串。
            var row0001 = Assert.Single(rows, row => row.RequirementIdentifier == "REQ-0001");
            Assert.Equal("高", row0001.Grade);
            var row0002 = Assert.Single(rows, row => row.RequirementIdentifier == "REQ-0002");
            Assert.Equal("", row0002.Grade);
        }

        /// <summary>状态文件是坏 JSON 时该行仍然产出，HasStateFailure 为 true 且原因非空。</summary>
        [Fact]
        public void BrokenStateFileStillProducesRowWithFailure()
        {
            // 坏 JSON 的内容刻意只用 ASCII：命名门禁看不出这是字符串里的数据。
            WriteTaskState("REQ-0001", """
                {
                  not valid json at all
                """);

            var rows = CreationPanelReader.ReadReviewQueue(_repositoryRoot, _poolRoot);

            var row = Assert.Single(rows);
            Assert.True(row.HasStateFailure);
            Assert.False(string.IsNullOrEmpty(row.StateFailureReason));
        }

        /// <summary>排序：状态文件修改时间早的排前；时间缺失（1601 默认值）的排最后。</summary>
        [Fact]
        public void QueueSortsByLastTouchedTimeThenMissingLast()
        {
            WriteRequirement("REQ-0001", """
                {
                  "id": "REQ-0001",
                  "标题": "需求一"
                }
                """);
            var state0001 = WriteTaskState("REQ-0001", """
                {
                  "阶段": "方案",
                  "关卡待审": "方案审"
                }
                """);
            var state0002 = WriteTaskState("REQ-0002", """
                {
                  "阶段": "方案",
                  "关卡待审": "方案审"
                }
                """);
            var state0003 = WriteTaskState("REQ-0003", """
                {
                  "阶段": "方案",
                  "关卡待审": "方案审"
                }
                """);

            File.SetLastWriteTimeUtc(state0002, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(state0001, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            // REQ-0003 的修改时间刻意设成 1969 年（Unix 纪元之前），模拟「时间缺失」。
            File.SetLastWriteTimeUtc(state0003, new DateTime(1969, 12, 31, 0, 0, 0, DateTimeKind.Utc));

            var rows = CreationPanelReader.ReadReviewQueue(_repositoryRoot, _poolRoot);

            Assert.Equal(3, rows.Count);
            Assert.Equal("REQ-0001", rows[0].RequirementIdentifier);
            Assert.Equal("REQ-0002", rows[1].RequirementIdentifier);
            Assert.Equal("REQ-0003", rows[2].RequirementIdentifier);
            Assert.Equal("", rows[2].LastTouchedMoment);
            Assert.Equal("", rows[2].WaitingLabel);
            Assert.StartsWith("2026-01-01", rows[0].LastTouchedMoment);
        }

        /// <summary>放行流水文件不存在时 Loaded 为 true、TotalCount 为 0（空流水是正常状态，不是读不成）。</summary>
        [Fact]
        public void MissingReleaseLedgerIsLoadedEmpty()
        {
            var summary = CreationPanelReader.ReadReleases(_poolRoot);

            Assert.True(summary.Loaded);
            Assert.Equal("", summary.LoadFailureReason);
            Assert.Equal(0, summary.TotalCount);
            Assert.Empty(summary.Rows);
        }

        /// <summary>放行流水顶层不是对象时 Loaded 为 false、原因非空。</summary>
        [Fact]
        public void NonObjectReleaseLedgerIsNotLoaded()
        {
            WriteReleases("""
                [ 1, 2, 3 ]
                """);

            var summary = CreationPanelReader.ReadReleases(_poolRoot);

            Assert.False(summary.Loaded);
            Assert.False(string.IsNullOrEmpty(summary.LoadFailureReason));
        }

        /// <summary>三条流水（未抽查 / 合格 / 发现问题）：三个计数都对，HasProblem 只在那一条为 true。</summary>
        [Fact]
        public void ReleaseLedgerCountsAreCorrect()
        {
            WriteReleases("""
                {
                  "条目": [
                    { "id": "RL-0001", "需求id": "REQ-0001", "风险级": "低", "范围": ["A", "B"], "放行时间": "2026-01-01T00:00:00Z", "合并提交": "abc", "抽查状态": "未抽查", "抽查结论": "", "回滚提交": "" },
                    { "id": "RL-0002", "需求id": "REQ-0002", "风险级": "常规", "范围": ["C"], "放行时间": "2026-01-02T00:00:00Z", "合并提交": "def", "抽查状态": "合格", "抽查结论": "没问题", "回滚提交": "" },
                    { "id": "RL-0003", "需求id": "REQ-0003", "风险级": "高", "范围": ["D"], "放行时间": "2026-01-03T00:00:00Z", "合并提交": "ghi", "抽查状态": "发现问题", "抽查结论": "风格偏离", "回滚提交": "jkl" }
                  ]
                }
                """);

            var summary = CreationPanelReader.ReadReleases(_poolRoot);

            Assert.True(summary.Loaded);
            Assert.Equal(3, summary.TotalCount);
            Assert.Equal(1, summary.UncheckedCount);
            Assert.Equal(1, summary.ProblemCount);
            Assert.Equal("A、B", summary.Rows[0].ScopeText);
            Assert.False(summary.Rows[0].HasProblem);
            Assert.False(summary.Rows[0].IsSpotChecked);
            Assert.True(summary.Rows[1].IsSpotChecked);
            Assert.False(summary.Rows[1].HasProblem);
            Assert.True(summary.Rows[2].HasProblem);
            Assert.True(summary.Rows[2].IsSpotChecked);
            Assert.Equal("jkl", summary.Rows[2].RevertCommit);
        }

        /// <summary>晋升提案四种状态各一条：PendingCount 为 1、OpenCount 为 2、TotalCount 为 4。</summary>
        [Fact]
        public void PromotionProposalCountsAcrossFourStates()
        {
            WritePromotion("PR-0001.json", """
                {
                  "id": "PR-0001",
                  "问题类别": "类别一",
                  "同类条数": 3,
                  "可规则化性": "可代码化",
                  "晋升去向": "检查器",
                  "涉及模块": ["模块A"],
                  "原文引用": ["引用一"],
                  "状态": "待批",
                  "提出时间": "2026-01-01T00:00:00Z",
                  "裁决人": "",
                  "裁决时间": "",
                  "落地产物": ""
                }
                """);
            WritePromotion("PR-0002.json", """
                {
                  "id": "PR-0002",
                  "问题类别": "类别二",
                  "同类条数": 4,
                  "可规则化性": "可提示词化",
                  "晋升去向": "预审规则",
                  "涉及模块": ["模块B"],
                  "原文引用": ["引用二"],
                  "状态": "已批准",
                  "提出时间": "2026-01-02T00:00:00Z",
                  "裁决人": "张三",
                  "裁决时间": "2026-01-03T00:00:00Z",
                  "落地产物": ""
                }
                """);
            WritePromotion("PR-0003.json", """
                {
                  "id": "PR-0003",
                  "问题类别": "类别三",
                  "同类条数": 5,
                  "可规则化性": "不可规则化",
                  "晋升去向": "无",
                  "涉及模块": [],
                  "原文引用": [],
                  "状态": "已拒绝",
                  "提出时间": "2026-01-04T00:00:00Z",
                  "裁决人": "李四",
                  "裁决时间": "2026-01-05T00:00:00Z",
                  "落地产物": ""
                }
                """);
            WritePromotion("PR-0004.json", """
                {
                  "id": "PR-0004",
                  "问题类别": "类别四",
                  "同类条数": 6,
                  "可规则化性": "可代码化",
                  "晋升去向": "检查器",
                  "涉及模块": ["模块C"],
                  "原文引用": ["引用四"],
                  "状态": "已落地",
                  "提出时间": "2026-01-06T00:00:00Z",
                  "裁决人": "王五",
                  "裁决时间": "2026-01-07T00:00:00Z",
                  "落地产物": "Specifications/Project/预审规则.json"
                }
                """);

            var summary = CreationPanelReader.ReadPromotionProposals(_poolRoot);

            Assert.True(summary.Loaded);
            Assert.Equal(4, summary.TotalCount);
            Assert.Equal(1, summary.PendingCount);
            Assert.Equal(2, summary.OpenCount);
            Assert.Equal("PR-0001", summary.Rows[0].Identifier);
            Assert.Equal("模块A", summary.Rows[0].ModuleText);
            Assert.True(summary.Rows[0].IsPending);
            Assert.False(summary.Rows[2].IsOpen);
        }

        /// <summary>晋升提案目录不存在时 Loaded 为 true、TotalCount 为 0（空账本是正常状态）。</summary>
        [Fact]
        public void MissingPromotionDirectoryIsLoadedEmpty()
        {
            var summary = CreationPanelReader.ReadPromotionProposals(_poolRoot);

            Assert.True(summary.Loaded);
            Assert.Equal("", summary.LoadFailureReason);
            Assert.Equal(0, summary.TotalCount);
            Assert.Empty(summary.Rows);
        }

        /// <summary>原文引用超过三条时只留前三条。</summary>
        [Fact]
        public void QuotationsAreTruncatedToThree()
        {
            WritePromotion("PR-0001.json", """
                {
                  "id": "PR-0001",
                  "问题类别": "类别一",
                  "同类条数": 3,
                  "可规则化性": "可代码化",
                  "晋升去向": "检查器",
                  "涉及模块": [],
                  "原文引用": ["引用一", "引用二", "引用三", "引用四", "引用五"],
                  "状态": "待批",
                  "提出时间": "2026-01-01T00:00:00Z",
                  "裁决人": "",
                  "裁决时间": "",
                  "落地产物": ""
                }
                """);

            var summary = CreationPanelReader.ReadPromotionProposals(_poolRoot);

            var row = Assert.Single(summary.Rows);
            Assert.Equal(3, row.Quotations.Count);
            Assert.Equal("引用一", row.Quotations[0]);
            Assert.Equal("引用三", row.Quotations[2]);
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
            Directory.CreateDirectory(PoolPaths.RequirementDirectory(_poolRoot, identifier));
            WriteFile(PoolPaths.RequirementFile(_poolRoot, identifier), json);
        }

        private string WriteTaskState(string requirementIdentifier, string json)
        {
            var directory = PipelinePaths.TaskDirectory(_repositoryRoot, requirementIdentifier);
            Directory.CreateDirectory(directory);
            var filePath = PipelinePaths.TaskStateFile(_repositoryRoot, requirementIdentifier);
            WriteFile(filePath, json);
            return filePath;
        }

        private void WriteReleases(string json)
        {
            WriteFile(PoolPaths.ReleaseLedgerFile(_poolRoot), json);
        }

        private void WritePromotion(string fileName, string json)
        {
            var directory = PoolPaths.PromotionProposalDirectory(_poolRoot);
            Directory.CreateDirectory(directory);
            WriteFile(Path.Combine(directory, fileName), json);
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
