using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次状态转换判断的结果：是否允许，以及不允许时的原因。</summary>
    public sealed class StateTransitionResult
    {
        private StateTransitionResult(bool isAllowed, string reason)
        {
            IsAllowed = isAllowed;
            Reason = reason;
        }

        /// <summary>是否允许该转换。</summary>
        public bool IsAllowed { get; }

        /// <summary>不允许时的原因，允许时为空串。</summary>
        public string Reason { get; }

        /// <summary>构造一个允许的结果。</summary>
        public static StateTransitionResult Allow()
        {
            return new StateTransitionResult(true, "");
        }

        /// <summary>构造一个拒绝的结果。</summary>
        /// <param name="reason">拒绝原因。</param>
        public static StateTransitionResult Reject(string reason)
        {
            return new StateTransitionResult(false, reason);
        }
    }

    /// <summary>需求状态机的转换合法性判断：按 schema 的转换表核对状态与执行角色。</summary>
    public static class RequirementStateMachine
    {
        /// <summary>
        /// 判断一次状态转换是否合法：状态机缺失、状态未变、无对应转换、角色无权都会拒绝。
        /// </summary>
        /// <param name="schema">实体的 schema，状态机定义取自它。</param>
        /// <param name="fromState">起始状态。</param>
        /// <param name="toState">目标状态。</param>
        /// <param name="actor">发起转换的角色。</param>
        public static StateTransitionResult CanTransfer(PoolSchema schema, string fromState, string toState, string actor)
        {
            if (schema.StateMachine == null)
            {
                return StateTransitionResult.Reject("该实体没有定义状态机");
            }

            if (string.Equals(fromState, toState, StringComparison.Ordinal))
            {
                return StateTransitionResult.Reject("状态未发生变化");
            }

            var candidates = new List<PoolStateTransition>();
            foreach (var transition in schema.StateMachine.Transitions)
            {
                if (string.Equals(transition.To, toState, StringComparison.Ordinal)
                    && (string.Equals(transition.From, fromState, StringComparison.Ordinal)
                        || string.Equals(transition.From, "*", StringComparison.Ordinal)))
                {
                    candidates.Add(transition);
                }
            }

            if (candidates.Count == 0)
            {
                return StateTransitionResult.Reject($"不存在从「{fromState}」到「{toState}」的转换");
            }

            foreach (var transition in candidates)
            {
                if (string.Equals(transition.Actor, actor, StringComparison.Ordinal))
                {
                    return StateTransitionResult.Allow();
                }
            }

            return StateTransitionResult.Reject($"该转换只有「{candidates[0].Actor}」可以执行");
        }
    }
}
