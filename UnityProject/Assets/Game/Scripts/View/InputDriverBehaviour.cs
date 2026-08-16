using System.Collections.Generic;
using HSGFrame.Input;
using HSGFrame.Logging;
using HSGFrame.MonoDriver;
using UnityEngine;
using UnityEngine.InputSystem;

// 起别名而不是处处写全名：`HSGFrame.Input` 与 `UnityEngine.InputSystem` 各有一个
// `InputActionPhase`，同时 using 两边必然 CS0104。业务读到的相位一律是纯 C# 那一个，
// 引擎那个只在本文件内经 `InputAction.IsPressed()` 间接用到，不需要出现在签名里。
using InputActionPhase = HSGFrame.Input.InputActionPhase;

namespace Template.View
{
    /// <summary>
    /// 输入驱动壳：把新版 Input System 的动作状态按帧喂给 <see cref="InputActionTracker"/>，业务读的是「动作」而不是按键。
    /// </summary>
    /// <remarks>
    /// 工程走的是**新版 Input System**（`activeInputHandler: 1`，旧输入管理器已停用），
    /// 绑定的唯一事实源是 <c>Assets/Game/Settings/Input/IA_默认输入.inputactions</c>——
    /// 改键、手柄、重映射全部由那份资产负责，本类型不认识任何一个具体按键。
    /// 留这一层的意义在于业务侧读到的仍然是「前进」这样的动作名与
    /// <see cref="InputActionPhase"/> 这样的纯 C# 相位：相位判定留在
    /// <see cref="InputActionTracker"/> 里能在 <c>dotnet test</c> 秒级验，
    /// 而引擎那一半被隔在本文件之内。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class InputDriverBehaviour : MonoBehaviour
    {
        [SerializeField] [Tooltip("输入动作资产，缺省时本组件不产生任何输入")]
        private InputActionAsset _actionAsset;

        [SerializeField] [Tooltip("要启用的 Action Map 名")]
        private string _actionMapName = "游戏";

        private readonly List<string> _activeActions = new List<string>();
        private InputActionMap _actionMap;
        private System.IDisposable _updateSubscription;

        /// <summary>当前的动作跟踪器，唤醒后可用。</summary>
        public InputActionTracker Tracker { get; private set; }

        /// <summary>取某个动作在当前帧的状态；没装配起来时返回 Idle。</summary>
        /// <param name="actionName">动作名。</param>
        public InputActionPhase GetPhase(string actionName)
        {
            return Tracker == null ? InputActionPhase.Idle : Tracker.GetPhase(actionName);
        }

        private void Awake()
        {
            // 无参构造：按键到动作的翻译由 .inputactions 资产做完了，
            // 再挂一张绑定表就是第二个事实源。
            Tracker = new InputActionTracker();
            _actionMap = ResolveActionMap();
        }

        private void OnEnable()
        {
            // 先 Enable 再订阅：反过来的话订阅后的第一帧会读到还没启用的 action，
            // 表现为「进场景头一帧输入全丢」。
            _actionMap?.Enable();

            // 走 MonoDriver 而不是自己写 Update：逐帧逻辑集中经帧驱动走是既有约定
            //（《结构规范-代码》第六节），也让输入在暂停时能整体停掉。
            _updateSubscription = MonoDriverHub.Shared.AddUpdateListener(TickInput);
        }

        private void OnDisable()
        {
            // 先退订再 Disable：反过来的话退订前的最后一帧会读到已经 Disable 的 action。
            _updateSubscription?.Dispose();
            _updateSubscription = null;
            _actionMap?.Disable();
        }

        private void TickInput()
        {
            _activeActions.Clear();

            if (_actionMap != null)
            {
                foreach (var action in _actionMap.actions)
                {
                    if (action.IsPressed())
                    {
                        _activeActions.Add(action.name);
                    }
                }
            }

            // map 为 null 时也照样推进一帧：否则最后一次按下会永远停在 Held，再也等不到 Released。
            Tracker.TickActions(_activeActions);
        }

        private InputActionMap ResolveActionMap()
        {
            if (_actionAsset == null)
            {
                LoggerHub.Shared.Warning("位置：InputDriverBehaviour；原因：没挂输入动作资产，本组件不会产生任何输入；修复：把 Assets/Game/Settings/Input/IA_默认输入.inputactions 拖到本组件上；参考：《结构规范-资源》第五节");
                return null;
            }

            var map = _actionAsset.FindActionMap(_actionMapName, throwIfNotFound: false);
            if (map == null)
            {
                LoggerHub.Shared.Error($"位置：{_actionAsset.name}；原因：找不到名为「{_actionMapName}」的 Action Map，本组件不会产生任何输入；修复：把组件上的 Action Map 名改成资产里真有的那个；参考：Assets/Game/Settings/Input/IA_默认输入.inputactions");
            }

            return map;
        }
    }
}
