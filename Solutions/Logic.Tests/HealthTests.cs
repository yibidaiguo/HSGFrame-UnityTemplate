using Template.Logic.Service;
using Template.Logic.State;
using Unity.Mathematics;
using Xunit;

namespace Template.Logic.Tests
{
    public class HealthTests
    {
        [Fact]
        public void 受到伤害后血量下降()
        {
            var state = new UnitState { 血量 = 100 };
            DamageService.Apply(state, 30);
            Assert.Equal(70, state.血量);
        }

        [Fact]
        public void 伤害超过血量时血量停在零()
        {
            var state = new UnitState { 血量 = 100 };
            DamageService.Apply(state, 200);
            Assert.Equal(0, state.血量);
        }

        [Fact]
        public void UnityMathematics可用()
        {
            var distance = math.distance(new float3(0, 0, 0), new float3(3, 4, 0));
            Assert.Equal(5f, distance);
        }
    }
}
