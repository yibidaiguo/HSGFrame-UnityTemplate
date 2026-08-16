using System.Collections.Generic;
using System.Linq;

namespace Template.Boot
{
    /// <summary>
    /// 出包验收项的静态注册表。各项自己挂进来，入口按这张表跑、不引用任何一项的实现——
    /// 这正是这张表存在的理由：可选功能（比如热更）能整块摘掉而不动入口。
    /// </summary>
    public static class BuildVerificationRegistry
    {
        private static readonly List<IBuildVerification> _verifications = new List<IBuildVerification>();

        /// <summary>
        /// 挂一项验收进来。传 null 直接忽略；同名时保留先挂进来的那个、直接返回——
        /// 域重载会把注册入口重跑一遍，不去重会挂出重复项。
        /// </summary>
        /// <param name="verification">要挂进来的验收项。</param>
        public static void Register(IBuildVerification verification)
        {
            if (verification == null)
            {
                return;
            }

            foreach (var existing in _verifications)
            {
                if (existing.Name == verification.Name)
                {
                    return;
                }
            }

            _verifications.Add(verification);
        }

        /// <summary>按 Order 升序返回已挂的项；Order 相同时保持挂进来的先后。</summary>
        /// <returns>排好序的验收项快照。</returns>
        public static IReadOnlyList<IBuildVerification> ListOrdered()
        {
            return _verifications.OrderBy(v => v.Order).ToList();
        }
    }
}
