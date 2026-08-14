using Template.Logic.State;

namespace Template.Logic.Service
{
    // 占位服务：伤害结算
    public static class DamageService
    {
        public static void Apply(UnitState 目标, int 伤害值)
        {
            目标.血量 -= 伤害值;
            if (目标.血量 < 0)
            {
                目标.血量 = 0;
            }
        }
    }
}
