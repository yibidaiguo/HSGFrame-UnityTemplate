using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// ConflictDebtView 的纯计算测试：未决判据、需求过滤、TotalPending 不受过滤影响、
    /// 排序确定性与「零未决 / 读不动」两分支，以及不写盘的性质。
    /// </summary>
    public class ConflictDebtViewTests
    {
        /// <summary>冲突列表为 null → Scanned 为 false，原因「冲突列表没加载」，绝不是「零未决」。</summary>
        [Fact]
        public void NullListMeansNotScanned()
        {
            var report = ConflictDebtView.ForRequirement(null, "REQ-0001");

            Assert.False(report.Scanned);
            Assert.Equal("冲突列表没加载", report.LoadFailureReason);
            Assert.Empty(report.Items);
        }

        /// <summary>LoadFailureReason 非空但有条目 → 列表残缺，Scanned 仍为 false。</summary>
        [Fact]
        public void LoadFailureWithEntriesStillNotScanned()
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "DR-0058",
                "新": "REQ-0042",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              { "id": 123 }
            ]
            """);

            var list = ConflictList.Load(workspace.Root);

            Assert.NotEqual("", list.LoadFailureReason);
            Assert.NotEmpty(list.Entries);

            var report = ConflictDebtView.ForRequirement(list, "REQ-0042");

            Assert.False(report.Scanned);
            Assert.Empty(report.Items);
            Assert.Equal(list.LoadFailureReason, report.LoadFailureReason);
        }

        /// <summary>一条已裁决、一条未决 → 只留未决那条。</summary>
        [Fact]
        public void ResolvedExcludedPendingKept()
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "DR-0001",
                "新": "REQ-0001",
                "发现阶段": "入库",
                "状态": "已裁决",
                "裁决": {
                  "人": "策划甲",
                  "选择": "改旧的",
                  "时间": "2026-08-19T10:00:00+08:00"
                }
              },
              {
                "id": "CF-0002",
                "旧": "DR-0058",
                "新": "REQ-0042",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);

            var report = ConflictDebtView.ForRequirement(ConflictList.Load(workspace.Root), "");

            var identifier = Assert.Single(report.Items).Identifier;
            Assert.Equal("CF-0002", identifier);
            Assert.Equal(1, report.TotalPending);
        }

        /// <summary>状态写成已裁决但选择是强制推送 → 算未决（与 PendingCount() 判据一致的历史数据兜底）。</summary>
        [Fact]
        public void ResolvedStateWithForcePushCountsAsPending()
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0003",
                "旧": "DR-0001",
                "新": "REQ-0042",
                "发现阶段": "影响评估",
                "状态": "已裁决",
                "裁决": {
                  "人": "张三",
                  "选择": "强制推送",
                  "时间": "2026-08-19T10:00:00+08:00"
                }
              }
            ]
            """);

            var list = ConflictList.Load(workspace.Root);
            var report = ConflictDebtView.ForRequirement(list, "");

            var item = Assert.Single(report.Items);
            Assert.Equal("CF-0003", item.Identifier);
            Assert.True(item.IsForcePushed);
            Assert.Equal("张三", item.ForcePusherName);
            Assert.Contains("张三强制推送挂账", item.Summary);
            Assert.Equal(1, list.PendingCount());
            Assert.Equal(list.PendingCount(), report.TotalPending);
        }

        /// <summary>按需求 id 过滤：旧命中、新命中都要留，都不命中的不留。</summary>
        [Fact]
        public void FilterMatchesOldOrNewIdentifier()
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "REQ-0001",
                "新": "REQ-0100",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0002",
                "旧": "DR-0001",
                "新": "REQ-0001",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0003",
                "旧": "DR-0001",
                "新": "REQ-0099",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);

            var report = ConflictDebtView.ForRequirement(ConflictList.Load(workspace.Root), "REQ-0001");

            var identifiers = report.Items.Select(item => item.Identifier).ToList();
            Assert.Equal(new[] { "CF-0001", "CF-0002" }, identifiers);
        }

        /// <summary>需求 id 传空白 → 留全部未决。</summary>
        [Fact]
        public void BlankRequirementKeepsAllPending()
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "DR-0001",
                "新": "REQ-0001",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0002",
                "旧": "DR-0002",
                "新": "REQ-0002",
                "发现阶段": "影响评估",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);

            var report = ConflictDebtView.ForRequirement(ConflictList.Load(workspace.Root), "   ");

            Assert.Equal(2, report.Items.Count);
            Assert.Equal(2, report.TotalPending);
        }

        /// <summary>TotalPending 不受需求过滤影响：过滤后 1 条，TotalPending 仍是 3。</summary>
        [Fact]
        public void TotalPendingIgnoresRequirementFilter()
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "DR-0001",
                "新": "REQ-0001",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0002",
                "旧": "DR-0002",
                "新": "REQ-0002",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0003",
                "旧": "DR-0003",
                "新": "REQ-0001",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);

            var report = ConflictDebtView.ForRequirement(ConflictList.Load(workspace.Root), "REQ-0001");

            Assert.Equal(2, report.Items.Count);
            Assert.Equal(3, report.TotalPending);
        }

        /// <summary>排序确定性：同一批条目乱序写两次，Items 的 id 序列相同且按序数序。</summary>
        [Fact]
        public void SortingIsDeterministic()
        {
            var firstReport = LoadReportFromJson("""
            [
              {
                "id": "CF-0003",
                "旧": "DR-0003",
                "新": "REQ-0003",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0001",
                "旧": "DR-0001",
                "新": "REQ-0001",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0002",
                "旧": "DR-0002",
                "新": "REQ-0002",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);

            var secondReport = LoadReportFromJson("""
            [
              {
                "id": "CF-0002",
                "旧": "DR-0002",
                "新": "REQ-0002",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0003",
                "旧": "DR-0003",
                "新": "REQ-0003",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0001",
                "旧": "DR-0001",
                "新": "REQ-0001",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);

            Assert.Equal(
                firstReport.Items.Select(item => item.Identifier),
                secondReport.Items.Select(item => item.Identifier));
            Assert.Equal(new[] { "CF-0001", "CF-0002", "CF-0003" }, firstReport.Items.Select(item => item.Identifier));
        }

        /// <summary>AffectedIdentifiers 去重且按序数序排列。</summary>
        [Fact]
        public void AffectedIdentifiersAreDistinctAndOrdinal()
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "DR-0005",
                "新": "REQ-0001",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0002",
                "旧": "DR-0001",
                "新": "REQ-0001",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              },
              {
                "id": "CF-0003",
                "旧": "DR-0001",
                "新": "REQ-0010",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);

            var report = ConflictDebtView.ForRequirement(ConflictList.Load(workspace.Root), "");

            Assert.Equal(
                new[] { "DR-0001", "DR-0005", "REQ-0001", "REQ-0010" },
                ConflictDebtView.AffectedIdentifiers(report));
        }

        /// <summary>不写盘：跑完视图后，临时池子目录里的文件数与内容与跑之前完全一致。</summary>
        [Fact]
        public void ViewDoesNotWriteToDisk()
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, """
            [
              {
                "id": "CF-0001",
                "旧": "DR-0058",
                "新": "REQ-0042",
                "发现阶段": "入库",
                "状态": "未决",
                "裁决": null
              }
            ]
            """);
            var before = SnapshotFiles(workspace.Root);

            ConflictDebtView.ForRequirement(ConflictList.Load(workspace.Root), "REQ-0042");
            ConflictDebtView.All(ConflictList.Load(workspace.Root));
            ConflictDebtView.AffectedIdentifiers(ConflictDebtView.All(ConflictList.Load(workspace.Root)));

            var after = SnapshotFiles(workspace.Root);
            Assert.Equal(before, after);
        }

        /// <summary>把同一段冲突列表 JSON 写进一个临时池子，Load 后查全池未决。</summary>
        private static ConflictDebtReport LoadReportFromJson(string json)
        {
            using var workspace = new PoolTestWorkspace();
            WriteConflictList(workspace.Root, json);
            return ConflictDebtView.All(ConflictList.Load(workspace.Root));
        }

        /// <summary>把冲突列表 JSON 写到池子的 Designs/冲突列表.json。</summary>
        private static void WriteConflictList(string poolRoot, string json)
        {
            var filePath = PoolPaths.ConflictListFile(poolRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, json, new UTF8Encoding(false));
        }

        /// <summary>列池子目录下全部文件的相对路径与内容，按路径序数序排列。</summary>
        private static IReadOnlyList<string> SnapshotFiles(string root)
        {
            var snapshots = new List<string>();
            foreach (var filePath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                snapshots.Add($"{Path.GetRelativePath(root, filePath)}:{File.ReadAllText(filePath)}");
            }

            snapshots.Sort(StringComparer.Ordinal);
            return snapshots;
        }
    }
}
