using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>专项认领写盘的测试：显式可跨默认职责、隐式两条硬前提、只改认领字段。</summary>
    public sealed class EpicClaimWriterTests
    {
        /// <summary>专项文件：美术已认领 ou_art，含策划端字段 名称/目标/默认锚点。</summary>
        private const string EpicFileJson = """
            {
              "id": "EP-0003",
              "名称": "水下遗迹场景包",
              "目标": "目标文本",
              "状态": "进行中",
              "创建人": "策划小A",
              "默认锚点": { "定稿": "水下遗迹风格@v1" },
              "认领": { "美术": ["ou_art"] },
              "来源": { "通道": "外部-专项表", "修订": 3, "提交人": "老C", "提交时间": "2026-08-18T10:00:00+09:00" }
            }
            """;

        /// <summary>专项文件：美术无人认领，程序已认领 ou_prog。</summary>
        private const string EpicWithoutArtJson = """
            {
              "id": "EP-0003",
              "名称": "水下遗迹场景包",
              "目标": "目标文本",
              "状态": "进行中",
              "创建人": "策划小A",
              "默认锚点": { "定稿": "水下遗迹风格@v1" },
              "认领": { "程序": ["ou_prog"] },
              "来源": { "通道": "外部-专项表", "修订": 3, "提交人": "老C", "提交时间": "2026-08-18T10:00:00+09:00" }
            }
            """;

        /// <summary>成员表：ou_art 默认美术、ou_prog 默认程序。</summary>
        private const string MembersJson = """
            [
              { "open_id": "ou_art", "姓名": "美术乙", "默认职责": ["美术"], "确认人": false },
              { "open_id": "ou_prog", "姓名": "程序丙", "默认职责": ["程序"], "确认人": false }
            ]
            """;

        /// <summary>显式认领可跨默认职责：程序职责的 ou_prog 显式认领美术，写成功。</summary>
        [Fact]
        public void ExplicitClaimMayCrossDefaultDuty()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteEpic("EP-0003.json", EpicFileJson);
            workspace.WriteMemberDirectory(MembersJson);

            var result = EpicClaimWriter.RecordExplicitClaim(workspace.Root, "EP-0003", "美术", "ou_prog");

            Assert.True(result.Written);
            Assert.Equal(new[] { "ou_art", "ou_prog" }, ArtClaimers(workspace.Root));
        }

        /// <summary>隐式认领、该职责已有人 → 不写，Reason 说清。</summary>
        [Fact]
        public void ImplicitClaimSkipsWhenDutyAlreadyClaimed()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteEpic("EP-0003.json", EpicFileJson);
            workspace.WriteMemberDirectory(MembersJson);

            var result = EpicClaimWriter.RecordImplicitClaim(workspace.Root, "EP-0003", "美术", "ou_art");

            Assert.False(result.Written);
            Assert.Contains("已有认领人", result.Reason);
        }

        /// <summary>隐式认领、该职责无人但不在默认职责内 → 不写，Reason 说清。</summary>
        [Fact]
        public void ImplicitClaimSkipsWhenNotInDefaultDuty()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteEpic("EP-0003.json", EpicWithoutArtJson);
            workspace.WriteMemberDirectory(MembersJson);

            var result = EpicClaimWriter.RecordImplicitClaim(workspace.Root, "EP-0003", "美术", "ou_prog");

            Assert.False(result.Written);
            Assert.Contains("不许跨默认职责", result.Reason);
        }

        /// <summary>隐式认领、该职责无人且在默认职责内 → 写成功。</summary>
        [Fact]
        public void ImplicitClaimWritesWhenDutyUnclaimedAndInDefault()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteEpic("EP-0003.json", EpicWithoutArtJson);
            workspace.WriteMemberDirectory(MembersJson);

            var result = EpicClaimWriter.RecordImplicitClaim(workspace.Root, "EP-0003", "美术", "ou_art");

            Assert.True(result.Written);
            Assert.Equal(new[] { "ou_art" }, ArtClaimers(workspace.Root));
            Assert.Equal(new[] { "ou_prog" }, ProgramClaimers(workspace.Root));
        }

        /// <summary>显式认领只改认领字段，其余字段不变。</summary>
        [Fact]
        public void ExplicitClaimKeepsOtherFieldsUntouched()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteEpic("EP-0003.json", EpicFileJson);

            EpicClaimWriter.RecordExplicitClaim(workspace.Root, "EP-0003", "程序", "ou_prog");

            AssertOtherFieldsUntouched(workspace.Root);
        }

        /// <summary>隐式认领只改认领字段，其余字段不变。</summary>
        [Fact]
        public void ImplicitClaimKeepsOtherFieldsUntouched()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteEpic("EP-0003.json", EpicWithoutArtJson);
            workspace.WriteMemberDirectory(MembersJson);

            EpicClaimWriter.RecordImplicitClaim(workspace.Root, "EP-0003", "美术", "ou_art");

            AssertOtherFieldsUntouched(workspace.Root);
        }

        private static void AssertOtherFieldsUntouched(string root)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(EpicFilePath(root)));
            var epic = document.RootElement;
            Assert.Equal("EP-0003", epic.GetProperty("id").GetString());
            Assert.Equal("水下遗迹场景包", epic.GetProperty("名称").GetString());
            Assert.Equal("目标文本", epic.GetProperty("目标").GetString());
            Assert.Equal("进行中", epic.GetProperty("状态").GetString());
            Assert.Equal("策划小A", epic.GetProperty("创建人").GetString());
            Assert.Equal("水下遗迹风格@v1", epic.GetProperty("默认锚点").GetProperty("定稿").GetString());
        }

        private static string EpicFilePath(string root)
        {
            return Path.Combine(PoolPaths.EpicsDirectory(root), "EP-0003.json");
        }

        private static string[] ArtClaimers(string root)
        {
            return ClaimersOf(root, "美术");
        }

        private static string[] ProgramClaimers(string root)
        {
            return ClaimersOf(root, "程序");
        }

        private static string[] ClaimersOf(string root, string duty)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(EpicFilePath(root)));
            return document.RootElement
                .GetProperty("认领")
                .GetProperty(duty)
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray();
        }
    }
}
