using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>
    /// 提交资产的推断测试：能推的推出来、推不出来的才问、一轮最多问两条，
    /// 以及落点与命名一律照资产规格来（那两样从来不该问人）。
    /// </summary>
    public class AssetSubmissionTests
    {
        /// <summary>扩展名分族：图 / 模型 / 音频，别的给空串。</summary>
        [Theory]
        [InlineData(".png", "图")]
        [InlineData(".PSD", "图")]
        [InlineData(".fbx", "模型")]
        [InlineData(".glb", "模型")]
        [InlineData(".wav", "音频")]
        [InlineData(".txt", "")]
        public void FamilyOfClassifiesByExtension(string extension, string expected)
        {
            Assert.Equal(expected, AssetSubmission.FamilyOf(extension));
        }

        /// <summary>源文件不在就直接拦下，不去猜类型。</summary>
        [Fact]
        public void MissingSourceIsBlocked()
        {
            using var workspace = new PoolTestWorkspace();

            var plan = AssetSubmission.Plan(workspace.RepositoryRoot, Path.Combine(workspace.Root, "没有.png"), "图标", "Inventory", "T_Bag");

            Assert.Single(plan.Blockers);
            Assert.Contains("源文件不在", plan.Blockers[0]);
        }

        /// <summary>不认识的扩展名当场拦下，并把收得了的三族说出来。</summary>
        [Fact]
        public void UnknownExtensionIsBlockedWithWhatIsAccepted()
        {
            using var workspace = new PoolTestWorkspace();
            var source = Path.Combine(workspace.Root, "说明.txt");
            File.WriteAllText(source, "不是资产");

            var plan = AssetSubmission.Plan(workspace.RepositoryRoot, source, "", "", "");

            Assert.Contains(plan.Blockers, blocker => blocker.Contains("不认识的文件类型"));
            Assert.False(plan.CanProceed);
        }

        /// <summary>
        /// 类型与模块都没推出来时问两条，**且只问两条**——一轮最多两条是助手那三条形状里的第一条。
        /// </summary>
        [Fact]
        public void AsksAtMostTwoQuestions()
        {
            using var workspace = new PoolTestWorkspace();
            var source = Path.Combine(workspace.Root, "a.png");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3 });

            var plan = AssetSubmission.Plan(workspace.RepositoryRoot, source, "", "", "");

            Assert.True(plan.Questions.Count <= AssetSubmission.MaximumQuestionCount);
            Assert.Contains(plan.Questions, question => question.Contains("哪一类资产"));
            Assert.Contains(plan.Questions, question => question.Contains("哪个模块"));
        }

        /// <summary>模块给了就不再问模块，只剩类型那一条。</summary>
        [Fact]
        public void KnownModuleRemovesThatQuestion()
        {
            using var workspace = new PoolTestWorkspace();
            var source = Path.Combine(workspace.Root, "a.png");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3 });

            var plan = AssetSubmission.Plan(workspace.RepositoryRoot, source, "", "Inventory", "");

            Assert.DoesNotContain(plan.Questions, question => question.Contains("哪个模块"));
        }

        /// <summary>
        /// 命名推不出来时**不问人**，是拦下来让上游按模式现拟一个：
        /// 问「你想叫什么」等于把命名规范背给人听。
        /// </summary>
        [Fact]
        public void MissingNamingIsBlockedNotAsked()
        {
            using var workspace = new PoolTestWorkspace();
            var source = Path.Combine(workspace.Root, "a.png");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
            WriteMinimalSpec(workspace.RepositoryRoot);

            var plan = AssetSubmission.Plan(workspace.RepositoryRoot, source, "图标", "Inventory", "");

            Assert.DoesNotContain(plan.Questions, question => question.Contains("叫什么"));
            Assert.Contains(plan.Blockers, blocker => blocker.Contains("还没有命名"));
        }

        /// <summary>命名不匹配这一类的模式时拦下，并把模式原样摆出来。</summary>
        [Fact]
        public void NamingAgainstPatternIsBlocked()
        {
            using var workspace = new PoolTestWorkspace();
            var source = Path.Combine(workspace.Root, "a.png");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
            WriteMinimalSpec(workspace.RepositoryRoot);

            var plan = AssetSubmission.Plan(workspace.RepositoryRoot, source, "图标", "Inventory", "bag_icon");

            Assert.Contains(plan.Blockers, blocker => blocker.Contains("不匹配"));
            Assert.Contains(plan.Blockers, blocker => blocker.Contains("^T_"));
        }

        /// <summary>推全了：落点与文件名照资产规格算出来，没有要问的也没有拦下的。</summary>
        [Fact]
        public void CompletePlanComputesDestinationFromSpecification()
        {
            using var workspace = new PoolTestWorkspace();
            var source = Path.Combine(workspace.Root, "a.png");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
            WriteMinimalSpec(workspace.RepositoryRoot);

            var plan = AssetSubmission.Plan(workspace.RepositoryRoot, source, "图标", "Inventory", "T_Bag");

            Assert.True(plan.CanProceed);
            Assert.Equal("图标", plan.AssetType);
            Assert.Equal("Assets/Game/Art/Texture/Icon/T_Bag.png", plan.DestinationPath);
        }

        /// <summary>
        /// 提议的类型只给**收得了这一族**的那几个：一张 PNG 不该被提议成角色模型。
        /// </summary>
        [Fact]
        public void CandidateTypesAreFilteredByFamily()
        {
            using var workspace = new PoolTestWorkspace();
            var source = Path.Combine(workspace.Root, "a.png");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
            WriteMinimalSpec(workspace.RepositoryRoot);

            var plan = AssetSubmission.Plan(workspace.RepositoryRoot, source, "", "Inventory", "T_Bag");

            var question = Assert.Single(plan.Questions);
            Assert.Contains("图标", question);
            Assert.DoesNotContain("角色模型", question);
        }

        /// <summary>写一份够用的资产规格：一个图类型、一个模型类型。</summary>
        private static void WriteMinimalSpec(string repositoryRoot)
        {
            var path = Path.Combine(repositoryRoot, "Specifications", "Baseline", "asset-spec.baseline.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, """
            {
              "版本": "1.0.0",
              "资产类型": {
                "图标": {
                  "域": "资产.生图",
                  "规格": { "宽": 256, "高": 256, "格式": "PNG", "需要透明": true, "二次幂": true },
                  "落点": "Assets/Game/Art/Texture/Icon/",
                  "命名模式": "^T_[A-Za-z0-9]+$",
                  "可覆盖": ["规格"]
                },
                "角色模型": {
                  "域": "资产.建模",
                  "规格": { "格式": "FBX" },
                  "落点": "Assets/Game/Art/Model/Character/",
                  "命名模式": "^M_[A-Za-z0-9]+$",
                  "可覆盖": []
                }
              }
            }
            """);
        }
    }
}
