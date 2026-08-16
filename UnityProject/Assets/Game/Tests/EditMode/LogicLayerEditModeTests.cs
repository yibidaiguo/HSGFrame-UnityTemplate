using NUnit.Framework;
using Template.Logic.Data.Level;
using Template.Logic.Service;
using Template.Logic.State;

namespace Template.Logic.Tests.EditMode
{
    /// <summary>Unity 侧的 EditMode 冒烟测试：确认零依赖 Logic 层在编辑器里同样可用。</summary>
    public class LogicLayerEditModeTests
    {
        [Test]
        public void DamageReducesHealthInsideEditor()
        {
            var state = new UnitState { Health = 100 };
            DamageService.Apply(state, 30);
            Assert.AreEqual(70, state.Health);
        }

        [Test]
        public void LevelValidatorReportsMissingChunkInsideEditor()
        {
            var level = new LevelDefinition { LevelName = "村庄" };
            level.ChunkNames.Add("区块_广场");

            var errors = LevelValidator.Validate(level, new System.Collections.Generic.Dictionary<string, LevelChunk>());

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("区块_广场", errors[0]);
        }
    }
}
