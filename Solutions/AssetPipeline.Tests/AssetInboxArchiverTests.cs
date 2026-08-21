using System;
using System.IO;
using System.Linq;
using Template.Toolkit.AssetPipeline;
using Xunit;

namespace Template.Toolkit.AssetPipelineTests
{
    /// <summary>收件箱归档器测试。</summary>
    public class AssetInboxArchiverTests
    {
        private const string RoutingJson =
            "{\"说明\":\"测试\",\"路由\":[" +
            "{\"扩展名\":[\".png\",\".tga\"],\"目标目录\":\"Game/Art/Texture\"}," +
            "{\"扩展名\":[\".wav\"],\"目标目录\":\"Game/Art/Audio\"}," +
            "{\"扩展名\":[\".fbx\"],\"目标目录\":\"Game/Art/Model\"}" +
            "]}";

        [Fact]
        public void PlanRoutesThreeExtensionsToTheirTargetDirectories()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteRule("Game/Art/Audio", "A_", new[] { ".wav" });
            fixture.WriteRule("Game/Art/Model", "M_", new[] { ".fbx" });
            fixture.WriteAsset("a.png");
            fixture.WriteAsset("b.wav");
            fixture.WriteAsset("c.fbx");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            Assert.Equal(3, plans.Count);
            Assert.Equal(TargetDirectory(fixture, "Game/Art/Texture"), plans[0].TargetDirectory);
            Assert.Equal(TargetDirectory(fixture, "Game/Art/Audio"), plans[1].TargetDirectory);
            Assert.Equal(TargetDirectory(fixture, "Game/Art/Model"), plans[2].TargetDirectory);
        }

        [Fact]
        public void PlanSkipsUnknownExtensionWithoutThrowing()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteAsset("readme.txt");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            Assert.Empty(plans);
        }

        [Fact]
        public void PlanSkipsRuleAndRoutingJsonFiles()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteAsset("hero.png");
            File.WriteAllText(Path.Combine(fixture.Inbox, "import-rules.json"), "{}");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            Assert.Single(plans);
            Assert.Equal("hero.png", Path.GetFileName(plans[0].SourcePath));
        }

        [Fact]
        public void PlanDoesNotEmitSeparatePlanForMetaFile()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteAssetWithMeta("hero.png");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            Assert.Single(plans);
            Assert.Equal("hero.png", Path.GetFileName(plans[0].SourcePath));
        }

        [Fact]
        public void PlanAppliesTargetDirectoryPrefix()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteRule("Game/Art/Audio", "A_", new[] { ".wav" });
            fixture.WriteAsset("rock.png");
            fixture.WriteAsset("music.wav");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            Assert.Equal("A_Music.wav", plans[0].TargetFileName);
            Assert.Equal("T_Rock.png", plans[1].TargetFileName);
        }

        [Fact]
        public void PlanNormalizesSpaceHyphenAndDuplicateUnderscores()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteAsset("hero idle 01.png");
            fixture.WriteAsset("rock-cliff-albedo.tga");
            fixture.WriteAsset("ui__button___normal.png");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            var names = plans.Select(plan => plan.TargetFileName).ToHashSet();
            Assert.Contains("T_HeroIdle_01.png", names);
            Assert.Contains("T_RockCliffAlbedo.tga", names);
            Assert.Contains("T_UiButtonNormal.png", names);
        }

        [Fact]
        public void PlanLowercasesUppercaseExtension()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteAsset("logo.PNG");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            Assert.Equal("T_Logo.png", plans[0].TargetFileName);
        }

        [Fact]
        public void PlanKeepsChineseWordsInStem()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteAsset("村庄 地面 贴图.png");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            Assert.Equal("T_村庄_地面_贴图.png", plans[0].TargetFileName);
        }

        [Fact]
        public void PlanSuffixesCollidingNormalizedNamesWithinBatch()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteAsset("a b.png");
            fixture.WriteAsset("a_b.png");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            Assert.Equal(2, plans.Count);
            var names = plans.Select(plan => plan.TargetFileName).ToHashSet();
            Assert.Contains("T_AB.png", names);
            Assert.Contains("T_AB_2.png", names);
        }

        [Fact]
        public void PlanSuffixesWhenTargetDirectoryAlreadyHasSameName()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            File.WriteAllText(Path.Combine(TargetDirectory(fixture, "Game/Art/Texture"), "T_Hero.png"), string.Empty);
            fixture.WriteAsset("hero.png");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            Assert.Equal("T_Hero_2.png", plans[0].TargetFileName);
        }

        [Fact]
        public void ApplyMovesAssetOutOfInboxIntoTargetDirectory()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteAsset("hero.png");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());
            var movedCount = AssetInboxArchiver.Apply(plans);

            Assert.Equal(1, movedCount);
            Assert.False(File.Exists(Path.Combine(fixture.Inbox, "hero.png")));
            Assert.True(File.Exists(plans[0].TargetPath));
        }

        [Fact]
        public void ApplyMovesMetaAlongsideAsset()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteAssetWithMeta("hero.png");

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());
            AssetInboxArchiver.Apply(plans);

            Assert.False(File.Exists(Path.Combine(fixture.Inbox, "hero.png.meta")));
            Assert.True(File.Exists(plans[0].TargetPath + ".meta"));
        }

        [Fact]
        public void PlanThrowsRoutingExceptionWhenTargetDirectoryHasNoRule()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            Directory.CreateDirectory(TargetDirectory(fixture, "Game/Art/Texture"));
            fixture.WriteAsset("hero.png");

            var exception = Assert.Throws<AssetRoutingException>(() =>
                AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting()));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
            Assert.Contains("Texture", exception.Message);
        }

        [Fact]
        public void PlanOrderIsStableAcrossRuns()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });
            fixture.WriteAsset("z.png");
            fixture.WriteAsset("a.png");
            fixture.WriteAsset("m.png");

            var routing = fixture.LoadRouting();
            var first = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, routing);
            var second = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, routing);

            Assert.Equal(
                first.Select(plan => plan.TargetFileName).ToList(),
                second.Select(plan => plan.TargetFileName).ToList());
        }

        [Fact]
        public void PlanReturnsEmptyListForEmptyInbox()
        {
            using var fixture = new Fixture();
            fixture.WriteRoutingTable();
            fixture.WriteRule("Game/Art/Texture", "T_", new[] { ".png", ".tga" });

            var plans = AssetInboxArchiver.Plan(fixture.Inbox, fixture.Assets, fixture.LoadRouting());

            Assert.Empty(plans);
        }

        private static string TargetDirectory(Fixture fixture, string relativeDirectory)
        {
            return Path.GetFullPath(Path.Combine(fixture.Assets, relativeDirectory));
        }

        private static string RuleJson(string prefix, string[] extensions)
        {
            var quoted = string.Join(",", extensions.Select(extension => "\"" + extension + "\""));
            return "{\"目录用途\":\"测试\",\"文件名前缀\":\"" + prefix
                + "\",\"允许扩展名\":[" + quoted
                + "],\"命名风格\":\"PascalCase\",\"最大文件字节\":8388608}";
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture()
            {
                Root = Path.Combine(Path.GetTempPath(), "AssetInboxArchiverTests_" + Guid.NewGuid().ToString("N"));
                Assets = Path.Combine(Root, "Assets");
                Inbox = Path.Combine(Assets, "_Inbox");
                Directory.CreateDirectory(Inbox);
            }

            public string Root { get; }

            public string Assets { get; }

            public string Inbox { get; }

            public void WriteRoutingTable()
            {
                File.WriteAllText(Path.Combine(Inbox, "archive-routes.json"), RoutingJson);
            }

            public void WriteRule(string relativeDirectory, string prefix, string[] extensions)
            {
                var directory = Path.Combine(Assets, relativeDirectory);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "import-rules.json"), RuleJson(prefix, extensions));
            }

            public void WriteAsset(string fileName)
            {
                File.WriteAllText(Path.Combine(Inbox, fileName), string.Empty);
            }

            public void WriteAssetWithMeta(string fileName)
            {
                WriteAsset(fileName);
                File.WriteAllText(Path.Combine(Inbox, fileName + ".meta"), string.Empty);
            }

            public AssetRoutingTable LoadRouting()
            {
                return AssetRoutingTable.LoadFromFile(Path.Combine(Inbox, "archive-routes.json"));
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Root, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
