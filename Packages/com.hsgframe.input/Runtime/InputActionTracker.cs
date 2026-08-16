using System;
using System.Collections.Generic;
using System.Linq;

namespace HSGFrame.Input
{
    /// <summary>一个动作在某一帧的状态。</summary>
    public enum InputActionPhase
    {
        /// <summary>本帧没按。</summary>
        Idle,

        /// <summary>本帧刚按下。</summary>
        Pressed,

        /// <summary>按住不放。</summary>
        Held,

        /// <summary>本帧刚抬起。</summary>
        Released,
    }

    /// <summary>动作状态跟踪器：按帧喂入按下的键集合，把它翻译成每个动作的按下/按住/抬起。</summary>
    public sealed class InputActionTracker
    {
        private readonly InputBindingTable _bindingTable;

        // 本帧处于按下状态的全部动作（含刚按下与按住）。
        private HashSet<string> _downActions = new HashSet<string>(StringComparer.Ordinal);

        // 本帧刚按下的动作（上一帧没按、这一帧按了）。
        private HashSet<string> _pressedThisFrame = new HashSet<string>(StringComparer.Ordinal);

        // 本帧刚抬起的动作（上一帧按了、这一帧没按）。
        private HashSet<string> _releasedThisFrame = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>用一张绑定表构造，喂入的是按键名（旧输入管理器那条路径）。</summary>
        /// <param name="bindingTable">绑定表。</param>
        public InputActionTracker(InputBindingTable bindingTable)
        {
            _bindingTable = bindingTable ?? throw new ArgumentNullException(nameof(bindingTable));
        }

        /// <summary>不带绑定表构造，喂入的直接是动作名。</summary>
        /// <remarks>
        /// 给「按键到动作的映射由外部负责」的场景用——例如新版 Input System，
        /// 那边 <c>.inputactions</c> 资产已经把按键翻译成动作了，绑定表这一层是多余的第二事实源。
        /// 这么构造出来的跟踪器只能走 <see cref="TickActions"/>，调 <see cref="Tick"/> 会抛。
        /// </remarks>
        public InputActionTracker()
        {
            _bindingTable = null;
        }

        /// <summary>推进一帧，pressedKeys 是这一帧处于按下状态的全部按键。</summary>
        /// <param name="pressedKeys">本帧按下的按键集合，传 null 按空集合处理。</param>
        public void Tick(IEnumerable<string> pressedKeys)
        {
            if (_bindingTable == null)
            {
                throw new InvalidOperationException(
                    "这个 InputActionTracker 是用无参构造建的，没有绑定表，按键名翻不成动作名；请改调 TickActions 直接喂动作名。");
            }

            var pressed = pressedKeys == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(pressedKeys.Where(key => key != null), StringComparer.Ordinal);

            var currentDown = new List<string>();
            foreach (var actionName in _bindingTable.ActionNames)
            {
                var entry = _bindingTable.Find(actionName);
                if (entry == null)
                {
                    continue;
                }

                if (IsDown(entry.PrimaryKey, pressed) || IsDown(entry.SecondaryKey, pressed))
                {
                    currentDown.Add(actionName);
                }
            }

            TickActions(currentDown);
        }

        /// <summary>推进一帧，activeActions 是这一帧处于按下状态的全部动作名。</summary>
        /// <param name="activeActions">本帧按下的动作名集合，传 null 按空集合处理。</param>
        public void TickActions(IEnumerable<string> activeActions)
        {
            var currentDown = activeActions == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(activeActions.Where(action => !string.IsNullOrEmpty(action)), StringComparer.Ordinal);

            _pressedThisFrame = new HashSet<string>(currentDown.Except(_downActions), StringComparer.Ordinal);
            _releasedThisFrame = new HashSet<string>(_downActions.Except(currentDown), StringComparer.Ordinal);
            _downActions = currentDown;
        }

        /// <summary>取某个动作在当前帧的状态；动作名不存在时返回 Idle。</summary>
        /// <param name="actionName">动作名。</param>
        public InputActionPhase GetPhase(string actionName)
        {
            if (_pressedThisFrame.Contains(actionName))
            {
                return InputActionPhase.Pressed;
            }

            if (_releasedThisFrame.Contains(actionName))
            {
                return InputActionPhase.Released;
            }

            if (_downActions.Contains(actionName))
            {
                return InputActionPhase.Held;
            }

            return InputActionPhase.Idle;
        }

        /// <summary>本帧刚按下的动作名，按序数序排列。</summary>
        public IReadOnlyList<string> PressedActions =>
            _pressedThisFrame.OrderBy(name => name, StringComparer.Ordinal).ToList();

        private static bool IsDown(string key, HashSet<string> pressed) =>
            !string.IsNullOrEmpty(key) && pressed.Contains(key);
    }
}
