using System.Collections.Generic;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>卡片路由四步算法的行为测试：默认表、专项认领、职责池、提出人特判、管理员兜底与文件覆盖。</summary>
    public sealed class CardRouterTests
    {
        /// <summary>覆盖策划/美术/程序/管理员四类职责的成员表 JSON。</summary>
        private const string MembersJson = """
        [
          { "open_id": "ou_A", "姓名": "策划甲", "默认职责": ["策划"], "确认人": true },
          { "open_id": "ou_B", "姓名": "美术乙", "默认职责": ["美术"], "确认人": false },
          { "open_id": "ou_M", "姓名": "美术明", "默认职责": ["美术"], "确认人": false },
          { "open_id": "ou_P", "姓名": "程序丙", "默认职责": ["程序"], "确认人": false },
          { "open_id": "ou_Z", "姓名": "老板", "默认职责": ["管理员"], "确认人": true }
        ]
        """;

        /// <summary>只有管理员职责的成员表 JSON，用于兜底测试。</summary>
        private const string AdministratorsOnlyJson = """
        [ { "open_id": "ou_Z", "姓名": "老板", "默认职责": ["管理员"], "确认人": true } ]
        """;

        /// <summary>把路由四要素加载出来并跑一次路由。</summary>
        private static CardRoutingResult Route(
            PoolTestWorkspace workspace,
            string cardType,
            string epicIdentifier,
            string submitterName)
        {
            return CardRouter.Route(
                cardType,
                epicIdentifier,
                submitterName,
                CardRouteTable.Load(workspace.Root),
                MemberDirectory.Load(workspace.Root),
                EpicClaimBook.Load(workspace.Root));
        }

        /// <summary>默认表里「选片」对应职责「美术」。</summary>
        [Fact]
        public void DefaultTableMapsSelectionToArt()
        {
            using var workspace = new PoolTestWorkspace();
            var table = CardRouteTable.Load(workspace.Root);

            Assert.Equal("美术", table.DutyOf("选片"));
        }

        /// <summary>专项里「美术」有认领人时只推认领人，命中第②步。</summary>
        [Fact]
        public void ClaimedDutyPushesOnlyClaimer()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteMemberDirectory(MembersJson);
            workspace.WriteEpic("EP-0001.json", """
            { "id": "EP-0001", "认领": { "美术": ["ou_M"] } }
            """);

            var result = Route(workspace, "选片", "EP-0001", "");

            Assert.Equal(RoutingStep.ClaimedInEpic, result.Step);
            Assert.Equal(new List<string> { "ou_M" }, result.Recipients);
        }

        /// <summary>专项存在但该职责无人认领时落第③步职责池，收件人是全部美术。</summary>
        [Fact]
        public void UnclaimedDutyFallsBackToDutyPool()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteMemberDirectory(MembersJson);
            workspace.WriteEpic("EP-0001.json", """
            { "id": "EP-0001", "认领": { "程序": ["ou_P"] } }
            """);

            var result = Route(workspace, "选片", "EP-0001", "");

            Assert.Equal(RoutingStep.DutyPool, result.Step);
            Assert.Equal(new List<string> { "ou_B", "ou_M" }, result.Recipients);
        }

        /// <summary>没有专项（传空串）时直接落第③步职责池。</summary>
        [Fact]
        public void NoEpicFallsBackToDutyPool()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteMemberDirectory(MembersJson);

            var result = Route(workspace, "选片", "", "");

            Assert.Equal(RoutingStep.DutyPool, result.Step);
            Assert.Equal(new List<string> { "ou_B", "ou_M" }, result.Recipients);
        }

        /// <summary>「待验收」伪职责命中提出人本人，收件人只有他一个。</summary>
        [Fact]
        public void SubmitterDutyPushesSubmitterItself()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteMemberDirectory(MembersJson);

            var result = Route(workspace, "待验收", "", "策划甲");

            Assert.Equal(RoutingStep.Submitter, result.Step);
            Assert.Equal("提出人", result.Duty);
            Assert.Equal(new List<string> { "ou_A" }, result.Recipients);
        }

        /// <summary>「待验收」提出人不在成员表时退化成策划，理由里写明原因。</summary>
        [Fact]
        public void UnknownSubmitterDegradesToPlanner()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteMemberDirectory(MembersJson);

            var result = Route(workspace, "待验收", "", "路人丁");

            Assert.Equal(RoutingStep.DutyPool, result.Step);
            Assert.Equal("策划", result.Duty);
            Assert.Equal(new List<string> { "ou_A" }, result.Recipients);
            Assert.Contains("提出人不在成员表", result.Reason);
        }

        /// <summary>职责无人时落第④步管理员兜底。</summary>
        [Fact]
        public void EmptyDutyFallsBackToAdministrators()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteMemberDirectory(AdministratorsOnlyJson);

            var result = Route(workspace, "关卡", "", "");

            Assert.Equal(RoutingStep.AdministratorFallback, result.Step);
            Assert.Equal("管理员", result.Duty);
            Assert.Equal(new List<string> { "ou_Z" }, result.Recipients);
        }

        /// <summary>职责与管理员都无人时无人可推，收件人为空。</summary>
        [Fact]
        public void NoDutyAndNoAdministratorPushesNobody()
        {
            using var workspace = new PoolTestWorkspace();

            var result = Route(workspace, "关卡", "", "");

            Assert.Equal(RoutingStep.NoRecipient, result.Step);
            Assert.Empty(result.Recipients);
        }

        /// <summary>卡片路由文件逐键覆盖默认表，其余卡片类型仍是默认值。</summary>
        [Fact]
        public void RouteFileOverridesDefaultTable()
        {
            using var workspace = new PoolTestWorkspace();
            workspace.WriteCardRoute("""
            { "选片": "程序" }
            """);

            var table = CardRouteTable.Load(workspace.Root);

            Assert.Equal("程序", table.DutyOf("选片"));
            Assert.Equal("策划", table.DutyOf("冲突"));
            Assert.Equal("程序", table.DutyOf("关卡"));
            Assert.Equal("管理员", table.DutyOf("喊人"));
        }
    }
}
