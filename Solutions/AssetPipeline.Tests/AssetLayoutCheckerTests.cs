using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipeline.Tests
{
    /// <summary>
    /// 资产分层检查器的测试。
    ///
    /// 这一组里最要紧的是 <see cref="MissingAssetsRootIsReportedInsteadOfPassingSilently"/>：
    /// 这道门禁第一版就是**扫描根算错、目录不存在、直接返回空表**，
    /// 于是它报「通过，问题 0 条」，而真实工程里十六个文件正平铺在根上。
    /// 绿得完全错误——比红更坏，因为没人会去查一道绿灯。
    /// </summary>
    public class AssetLayoutCheckerTests
    {
        /// <summary>落到完整深度、门类在词表里、扩展名对得上：放行。</summary>
        [Fact]
        public void WellPlacedAssetPasses()
        {
            using var workspace = new TempAssets();
            workspace.WriteAsset("Game/Art/Model/Vegetation/Grass/M_Grass01.fbx");
            workspace.WriteAsset("Game/Art/Material/Vegetation/Grass/Mat_Grass01.mat");
            workspace.WriteAsset("Game/Art/Audio/Sound/Combat/A_SwordHit.wav");

            var violations = AssetLayoutChecker.Check(workspace.Root, workspace.RuleSet());

            Assert.Empty(violations);
        }

        /// <summary>平铺在类型根上：报深度不够。这是「先平铺以后再分」留下的那一堆。</summary>
        [Fact]
        public void FlatAssetUnderTypeRootIsReported()
        {
            using var workspace = new TempAssets();
            workspace.WriteAsset("Game/Art/Audio/A_BgmVillage.wav");

            var violations = AssetLayoutChecker.Check(workspace.Root, workspace.RuleSet());

            var violation = Assert.Single(violations);
            Assert.Contains("1 层", violation.Reason);
            Assert.Contains("3 层", violation.Reason);
        }

        /// <summary>只分到门类、没开模块夹：一样报深度不够。</summary>
        [Fact]
        public void AssetMissingModuleFolderIsReported()
        {
            using var workspace = new TempAssets();
            workspace.WriteAsset("Game/Art/Material/Character/Mat_Npc.mat");

            var violations = AssetLayoutChecker.Check(workspace.Root, workspace.RuleSet());

            var violation = Assert.Single(violations);
            Assert.Contains("2 层", violation.Reason);
        }

        /// <summary>门类不在词表里：报出来并把词表列给人看。</summary>
        [Fact]
        public void UnknownCategoryIsReportedWithTheVocabulary()
        {
            using var workspace = new TempAssets();
            workspace.WriteAsset("Game/Art/Model/Level/Village/M_House.fbx");

            var violations = AssetLayoutChecker.Check(workspace.Root, workspace.RuleSet());

            var violation = Assert.Single(violations);
            Assert.Contains("Level", violation.Reason);
            Assert.Contains("Vegetation", violation.Reason);
        }

        /// <summary>
        /// 动画夹里躺着模型：报「这棵树只收 .anim…」。
        /// **这一条对着的是真事**——Art/Animation/Character/ 里曾经躺着 A_Idle.fbx、A_Walk.fbx，
        /// 人点开只看到一个模型。
        /// </summary>
        [Fact]
        public void ModelInsideAnimationTreeIsReported()
        {
            using var workspace = new TempAssets();
            workspace.WriteAsset("Game/Art/Animation/Character/Hero/A_Idle.fbx");

            var violations = AssetLayoutChecker.Check(workspace.Root, workspace.RuleSet());

            var violation = Assert.Single(violations);
            Assert.Contains(".anim", violation.Reason);
            Assert.Contains(".fbx", violation.Reason);
            Assert.Contains("Model/", violation.Fix);
        }

        /// <summary>模块层叫 Misc：报出来。这种名字等于没分。</summary>
        [Fact]
        public void BannedModuleFolderNameIsReported()
        {
            using var workspace = new TempAssets();
            workspace.WriteAsset("Game/Art/Model/Prop/Misc/M_Thing.fbx");

            var violations = AssetLayoutChecker.Check(workspace.Root, workspace.RuleSet());

            var violation = Assert.Single(violations);
            Assert.Contains("Misc", violation.Reason);
        }

        /// <summary>.meta 与 import-rules.json 不算资产，不参与分层检查。</summary>
        [Fact]
        public void MetaAndImportRuleFilesAreIgnored()
        {
            using var workspace = new TempAssets();
            workspace.WriteAsset("Game/Art/Audio/import-rules.json");
            workspace.WriteAsset("Game/Art/Audio/Sound/Combat/A_SwordHit.wav");
            workspace.WriteAsset("Game/Art/Audio/Sound/Combat/A_SwordHit.wav.meta");

            var violations = AssetLayoutChecker.Check(workspace.Root, workspace.RuleSet());

            Assert.Empty(violations);
        }

        /// <summary>
        /// **扫描根不存在时必须报出来，不许静默通过。**
        /// 「没扫成」与「没问题」是两件事（决策 42）；合成一件的代价是一道永远绿的门禁。
        /// </summary>
        [Fact]
        public void MissingAssetsRootIsReportedInsteadOfPassingSilently()
        {
            using var workspace = new TempAssets();
            var missing = Path.Combine(workspace.Root, "根本没有这个目录");

            var violations = AssetLayoutChecker.Check(missing, workspace.RuleSet());

            var violation = Assert.Single(violations);
            Assert.Contains("一个文件都没扫", violation.Reason);
        }

        /// <summary>Assets 在、只是还没有 Art/：那确实没东西可查，放行。</summary>
        [Fact]
        public void AssetsRootWithoutArtTreePasses()
        {
            using var workspace = new TempAssets();

            var violations = AssetLayoutChecker.Check(workspace.Root, workspace.RuleSet());

            Assert.Empty(violations);
        }

        /// <summary>词表读不到时报出来，而不是当成「没有类型所以都不合法」刷一屏。</summary>
        [Fact]
        public void UnreadableRuleSetIsReportedOnce()
        {
            using var workspace = new TempAssets();
            workspace.WriteAsset("Game/Art/Audio/A_Flat.wav");
            var broken = AssetLayoutRuleSet.Load(Path.Combine(workspace.Root, "没有.json"), "");

            var violations = AssetLayoutChecker.Check(workspace.Root, broken);

            var violation = Assert.Single(violations);
            Assert.Contains("不存在", violation.Reason);
        }

        /// <summary>用完即删的临时 Assets 目录，外带一份写好的分层词表。</summary>
        private sealed class TempAssets : IDisposable
        {
            private readonly string _baselinePath;

            public TempAssets()
            {
                Root = Path.Combine(Path.GetTempPath(), "资产分层测试-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
                _baselinePath = Path.Combine(Root, "asset-layout.baseline.json");
                File.WriteAllText(_baselinePath, BaselineJson, new UTF8Encoding(false));
            }

            public string Root { get; }

            public AssetLayoutRuleSet RuleSet()
            {
                return AssetLayoutRuleSet.Load(_baselinePath, "");
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

            /// <summary>测试自带一份最小词表：不读仓库里那份正本，免得改正本把测试带红。</summary>
            private const string BaselineJson = @"{
  ""资产根"": ""Game/Art"",
  ""最小层数"": 3,
  ""主题门类"": [""Character"", ""Vegetation"", ""Rock"", ""Prop"", ""Ui"", ""Shared""],
  ""模块层禁用名"": [""Misc"", ""Other"", ""Temp""],
  ""类型"": {
    ""Texture"": { ""门类"": [""Icon""], ""用主题门类"": true, ""允许扩展名"": ["".png"", "".tga""] },
    ""Model"": { ""门类"": [], ""用主题门类"": true, ""允许扩展名"": ["".fbx"", "".glb""] },
    ""Material"": { ""门类"": [], ""用主题门类"": true, ""允许扩展名"": ["".mat""] },
    ""Animation"": { ""门类"": [], ""用主题门类"": true, ""允许扩展名"": ["".anim"", "".controller""] },
    ""Audio"": { ""门类"": [""Music"", ""Sound"", ""Voice""], ""用主题门类"": false, ""允许扩展名"": ["".wav"", "".ogg""] }
  }
}";
        }
    }
}
