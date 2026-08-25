using System;
using System.IO;
using System.Linq;
using System.Text;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>
    /// 资产搬迁计划的测试。
    ///
    /// 这一组的重点是**「认不出来就不猜」**：猜错一个资产的归属不会报错，
    /// 它只是安静地待在一个不该待的夹子里，而半年后没人找得到它。
    /// 所以「不认识的名字进要人定」比「多认出几个」重要得多。
    /// </summary>
    public class AssetRelayoutPlannerTests
    {
        /// <summary>平铺的资产按关键词认出门类与模块，提议搬到完整深度。</summary>
        [Fact]
        public void FlatAssetGetsAMoveProposal()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteAsset("Game/Art/Model/M_BigRock.fbx");

            var plan = workspace.Plan();

            var move = Assert.Single(plan.Moves);
            Assert.Equal("Game/Art/Model/M_BigRock.fbx", move.FromPath);
            Assert.Equal("Game/Art/Model/Rock/Boulder/M_BigRock.fbx", move.ToPath);
            Assert.Empty(plan.Undecided);
        }

        /// <summary>
        /// 长关键词先试：GrassPatch 要落 Grass 夹，不能被别的短词抢走。
        /// 这条钉的是排序，不是某一个词——排序错了症状是「大部分对、少数莫名其妙」。
        /// </summary>
        [Fact]
        public void LongerKeywordWinsOverShorterOne()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteAsset("Game/Art/Model/M_GrassPatch_3_4.fbx");

            var plan = workspace.Plan();

            var move = Assert.Single(plan.Moves);
            Assert.Equal("Game/Art/Model/Vegetation/Grass/M_GrassPatch_3_4.fbx", move.ToPath);
            Assert.Equal("grasspatch", move.MatchedKeyword);
        }

        /// <summary>前缀先剥掉再认：Mat_ 与 M_ 都不该影响判断。</summary>
        [Fact]
        public void NamePrefixIsStrippedBeforeMatching()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteAsset("Game/Art/Material/Mat_PineSapling.mat");

            var plan = workspace.Plan();

            var move = Assert.Single(plan.Moves);
            Assert.Equal("Game/Art/Material/Vegetation/Tree/Mat_PineSapling.mat", move.ToPath);
        }

        /// <summary>名字里没有能对上的词：进「要人定」，一个字都不动。</summary>
        [Fact]
        public void UnrecognisedNameGoesToUndecidedInsteadOfGuessing()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteAsset("Game/Art/Model/M_Zzzyx.fbx");

            var plan = workspace.Plan();

            Assert.Empty(plan.Moves);
            Assert.Equal("Game/Art/Model/M_Zzzyx.fbx", Assert.Single(plan.Undecided));
        }

        /// <summary>
        /// 放错了树的（动画夹里的 fbx）进「要人定」，不自动搬。
        /// 该搬去哪要人定：它可能该进 Model/，也可能该被提成 clip 之后删掉——那是两件事。
        /// </summary>
        [Fact]
        public void WrongTreeGoesToUndecided()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteAsset("Game/Art/Animation/Character/Hero/A_Idle.fbx");

            var plan = workspace.Plan();

            Assert.Empty(plan.Moves);
            Assert.Single(plan.Undecided);
        }

        /// <summary>认出来的门类不在这一类的词表里时也不搬——Audio 没有 Rock 这一档。</summary>
        [Fact]
        public void CategoryOutsideTheTypeVocabularyIsNotMoved()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteAsset("Game/Art/Audio/A_BigRockRumble.wav");

            var plan = workspace.Plan();

            Assert.Empty(plan.Moves);
            Assert.Single(plan.Undecided);
        }

        /// <summary>已经落到位的资产不出现在计划里。</summary>
        [Fact]
        public void AlreadyPlacedAssetProducesNoMove()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteAsset("Game/Art/Model/Rock/Boulder/M_BigRock.fbx");

            var plan = workspace.Plan();

            Assert.Empty(plan.Moves);
            Assert.Empty(plan.Undecided);
        }

        /// <summary>关键词表不在：整份计划算不出来，且说得出原因——不是静默给空计划。</summary>
        [Fact]
        public void MissingKeywordTableIsReported()
        {
            using var workspace = new TempWorkspace();
            workspace.WriteAsset("Game/Art/Model/M_BigRock.fbx");

            var plan = AssetRelayoutPlanner.Plan(
                workspace.Root, workspace.RuleSet(), Path.Combine(workspace.Root, "没有这份.json"));

            Assert.NotEqual("", plan.FailureReason);
            Assert.Empty(plan.Moves);
        }

        /// <summary>用完即删的临时工作区：一份 Assets 根 + 一份词表 + 一份关键词表。</summary>
        private sealed class TempWorkspace : IDisposable
        {
            private readonly string _baselinePath;
            private readonly string _keywordPath;

            public TempWorkspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "资产搬迁测试-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
                _baselinePath = Path.Combine(Root, "asset-layout.baseline.json");
                _keywordPath = Path.Combine(Root, "relayout-keywords.json");
                File.WriteAllText(_baselinePath, BaselineJson, new UTF8Encoding(false));
                File.WriteAllText(_keywordPath, KeywordJson, new UTF8Encoding(false));
            }

            public string Root { get; }

            public AssetLayoutRuleSet RuleSet()
            {
                return AssetLayoutRuleSet.Load(_baselinePath, "");
            }

            public AssetRelayoutPlan Plan()
            {
                return AssetRelayoutPlanner.Plan(Root, RuleSet(), _keywordPath);
            }

            public void WriteAsset(string relativePath)
            {
                var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "占位", new UTF8Encoding(false));
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Root, recursive: true);
                }
                catch (IOException)
                {
                }
            }

            private const string BaselineJson = @"{
  ""资产根"": ""Game/Art"",
  ""最小层数"": 3,
  ""主题门类"": [""Character"", ""Vegetation"", ""Rock"", ""Prop"", ""Ui"", ""Shared""],
  ""模块层禁用名"": [""Misc""],
  ""类型"": {
    ""Model"": { ""门类"": [], ""用主题门类"": true, ""允许扩展名"": ["".fbx""] },
    ""Material"": { ""门类"": [], ""用主题门类"": true, ""允许扩展名"": ["".mat""] },
    ""Animation"": { ""门类"": [], ""用主题门类"": true, ""允许扩展名"": ["".anim""] },
    ""Audio"": { ""门类"": [""Music"", ""Sound""], ""用主题门类"": false, ""允许扩展名"": ["".wav""] }
  }
}";

            private const string KeywordJson = @"{
  ""规则"": [
    { ""关键词"": ""grasspatch"", ""门类"": ""Vegetation"", ""模块"": ""Grass"" },
    { ""关键词"": ""grass"",      ""门类"": ""Vegetation"", ""模块"": ""Undergrowth"" },
    { ""关键词"": ""pinesapling"",""门类"": ""Vegetation"", ""模块"": ""Tree"" },
    { ""关键词"": ""bigrock"",    ""门类"": ""Rock"",       ""模块"": ""Boulder"" },
    { ""关键词"": ""rock"",       ""门类"": ""Rock"",       ""模块"": ""Boulder"" }
  ]
}";
        }
    }
}
