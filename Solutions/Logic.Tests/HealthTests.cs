using Template.Combat;
using Unity.Mathematics;
using Xunit;

namespace Template.Tests
{
    /// <summary>血量结算与 Unity.Mathematics 可用性的占位测试。</summary>
    public class HealthTests
    {
        [Fact]
        public void DamageReducesHealth()
        {
            var state = new UnitState { Health = 100 };
            DamageService.Apply(state, 30);
            Assert.Equal(70, state.Health);
        }

        [Fact]
        public void DamageBeyondHealthStopsAtZero()
        {
            var state = new UnitState { Health = 100 };
            DamageService.Apply(state, 200);
            Assert.Equal(0, state.Health);
        }

        [Fact]
        public void UnityMathematicsIsUsable()
        {
            var distance = math.distance(new float3(0, 0, 0), new float3(3, 4, 0));
            Assert.Equal(5f, distance);
        }
    }
}
