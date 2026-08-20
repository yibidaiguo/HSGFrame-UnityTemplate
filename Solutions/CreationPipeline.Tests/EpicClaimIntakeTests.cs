using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>专项认领入站的测试：拒收、幂等跳过、只写认领与来源、职责白名单、多余键发现。</summary>
    public sealed class EpicClaimIntakeTests
    {
        /// <summary>信封修订 7、认领 美术 open_id_B，用于「修订更大」写入。</summary>
        private const string ClaimEnvelopeJson = """
            {
              "通道": "外部-专项表",
              "专项id": "EP-0003",
              "修订": 7,
              "提交人": "老C",
              "提交时间": "2026-08-20T10:00:00+09:00",
              "认领": { "美术": ["open_id_B"], "程序": ["open_id_C"] }
            }
            """;

        /// <summary>信封修订 5，小于专项文件的来源.修订 6，用于幂等跳过。</summary>
        private const string StaleEnvelopeJson = """
            {
              "通道": "外部-专项表",
              "专项id": "EP-0003",
              "修订": 5,
              "提交人": "老C",
              "提交时间": "2026-08-18T10:00:00+09:00",
              "认领": { "美术": ["open_id_B"] }
            }
            """;

        /// <summary>信封带伪职责「管理员」，用于职责白名单拒收。</summary>
        private const string InvalidDutyEnvelopeJson = """
            {
              "通道": "外部-专项表",
              "专项id": "EP-0003",
              "修订": 7,
              "提交人": "老C",
              "提交时间": "2026-08-20T10:00:00+09:00",
              "认领": { "管理员": ["ou_X"] }
            }
            """;

        /// <summary>信封多带一个「名称」键，用于多余键发现。</summary>
        private const string ExtraFieldEnvelopeJson = """
            {
              "通道": "外部-专项表",
              "专项id": "EP-0003",
              "修订": 7,
              "提交人": "老C",
              "提交时间": "2026-08-20T10:00:00+09:00",
              "认领": { "美术": ["open_id_B"] },
              "名称": "别的名字"
            }
            """;

        /// <summary>专项文件：来源.修订 = 6，含策划端字段 名称/目标/默认锚点。</summary>
        private const string EpicFileJson = """
            {
              "id": "EP-0003",
              "名称": "水下遗迹场景包",
              "目标": "目标文本",
              "状态": "进行中",
              "创建人": "策划小A",
              "默认锚点": { "定稿": "水下遗迹风格@v1" },
              "认领": { "美术": ["open_id_A"] },
              "来源": { "通道": "外部-专项表", "修订": 6, "提交人": "老C", "提交时间": "2026-08-18T10:00:00+09:00" }
            }
            """;

        /// <summary>专项文件不存在 → 一条拒收，理由说清不凭空建专项。</summary>
        [Fact]
        public void MissingEpicFileIsRejected()
        {
            using var workspace = new PoolTestWorkspace();
            WriteClaimInbox(workspace, "claim-1.json", ClaimEnvelopeJson);

            var report = EpicClaimIntake.Process(workspace.Root);

            Assert.Equal(0, report.ProcessedCount);
            Assert.Equal(0, report.SkippedCount);
            var rejection = Assert.Single(report.Rejections);
            Assert.Contains("专项由策划端创建后再同步认领", rejection.Reason);
        }

        /// <summary>修订小于已有 → 跳过，专项文件内容一个字不变。</summary>
        [Fact]
        public void StaleRevisionIsSkippedWithoutTouch()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteEpic("EP-0003.json", EpicFileJson);
            WriteClaimInbox(workspace, "claim-1.json", StaleEnvelopeJson);
            var epicFilePath = EpicFilePath(workspace.Root);
            var before = File.ReadAllText(epicFilePath);

            var report = EpicClaimIntake.Process(workspace.Root);

            Assert.Equal(1, report.SkippedCount);
            Assert.Equal(0, report.ProcessedCount);
            Assert.Empty(report.Rejections);
            Assert.Equal(before, File.ReadAllText(epicFilePath));
        }

        /// <summary>修订更大 → 认领写进去了，且专项的 名称/目标/默认锚点 三个字段没被动过。</summary>
        [Fact]
        public void NewerRevisionWritesClaimsKeepingOtherFields()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteEpic("EP-0003.json", EpicFileJson);
            WriteClaimInbox(workspace, "claim-1.json", ClaimEnvelopeJson);

            var report = EpicClaimIntake.Process(workspace.Root);

            Assert.Equal(1, report.ProcessedCount);
            Assert.Equal(0, report.SkippedCount);
            Assert.Empty(report.Rejections);

            using var document = JsonDocument.Parse(File.ReadAllText(EpicFilePath(workspace.Root)));
            var root = document.RootElement;
            Assert.Equal("水下遗迹场景包", root.GetProperty("名称").GetString());
            Assert.Equal("目标文本", root.GetProperty("目标").GetString());
            Assert.Equal("水下遗迹风格@v1", root.GetProperty("默认锚点").GetProperty("定稿").GetString());
            Assert.Equal(7, root.GetProperty("来源").GetProperty("修订").GetInt32());
            Assert.Equal(new[] { "open_id_B" }, root.GetProperty("认领").GetProperty("美术").EnumerateArray().Select(item => item.GetString()));
        }

        /// <summary>职责写成「管理员」→ 拒收，理由里出现三个合法值。</summary>
        [Fact]
        public void InvalidDutyIsRejectedWithLegalValues()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteEpic("EP-0003.json", EpicFileJson);
            WriteClaimInbox(workspace, "claim-1.json", InvalidDutyEnvelopeJson);

            var report = EpicClaimIntake.Process(workspace.Root);

            Assert.Equal(0, report.ProcessedCount);
            var rejection = Assert.Single(report.Rejections);
            Assert.Contains("美术", rejection.Reason);
            Assert.Contains("程序", rejection.Reason);
            Assert.Contains("策划", rejection.Reason);
        }

        /// <summary>信封多带一个 名称 键 → 出一条 finding 且专项的 名称 没被改。</summary>
        [Fact]
        public void ExtraFieldProducesFindingWithoutChangingEpic()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteEpic("EP-0003.json", EpicFileJson);
            WriteClaimInbox(workspace, "claim-1.json", ExtraFieldEnvelopeJson);

            var report = EpicClaimIntake.Process(workspace.Root);

            Assert.Equal(1, report.ProcessedCount);
            var finding = Assert.Single(report.Findings);
            Assert.Contains("名称", finding.Reason);

            using var document = JsonDocument.Parse(File.ReadAllText(EpicFilePath(workspace.Root)));
            Assert.Equal("水下遗迹场景包", document.RootElement.GetProperty("名称").GetString());
        }

        private static string EpicFilePath(string root)
        {
            return Path.Combine(PoolPaths.EpicsDirectory(root), "EP-0003.json");
        }

        private static void WriteClaimInbox(PoolTestWorkspace workspace, string fileName, string json)
        {
            var directory = PoolPaths.EpicInboxDirectory(workspace.Root);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), json, new UTF8Encoding(false));
        }
    }
}
