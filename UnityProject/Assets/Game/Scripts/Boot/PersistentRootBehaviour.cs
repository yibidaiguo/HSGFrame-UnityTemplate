using UnityEngine;

namespace Template.Boot
{
    /// <summary>跨场景常驻标记：挂在启动场景的常驻根上，让它不被后续的单场景加载销毁。</summary>
    /// <remarks>
    /// 世界场景按 <c>LoadSceneMode.Single</c> 加载，会把启动场景里的一切扫掉——
    /// 相机、灯光、UI 根、关卡装配器都在那里，不常驻就等于装配完立刻全没。
    /// 世界场景自己保持「恰好一个关卡根物体」的形状不变，<c>scene.export</c> 才导得回去，
    /// 所以这些运行时家具一律住在启动场景，而不是塞进世界场景。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PersistentRootBehaviour : MonoBehaviour
    {
        private void Awake()
        {
            // DontDestroyOnLoad 只在运行模式下有效，编辑模式下调它会抛异常。
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }
    }
}
