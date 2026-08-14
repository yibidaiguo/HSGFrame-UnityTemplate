using Template.Logic.State;

namespace Template.Logic.Service
{
    /// <summary>伤害结算服务。</summary>
    public static class DamageService
    {
        /// <summary>对目标结算一次伤害，血量下限为零。</summary>
        public static void Apply(UnitState target, int damageAmount)
        {
            target.Health -= damageAmount;
            if (target.Health < 0)
            {
                target.Health = 0;
            }
        }
    }
}
