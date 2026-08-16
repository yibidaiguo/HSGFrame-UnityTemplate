using HSGFrame.WorldSpaceUI;
using UnityEngine;
using UnityEngine.UI;

namespace Template.View
{
    /// <summary>世界空间名牌：一块贴在 3D 世界里的 UGUI 画布，显示与否、放多大由纯 C# 的策略算，这里只负责照结论摆。</summary>
    [DisallowMultipleComponent]
    public sealed class WorldSpaceLabel : MonoBehaviour
    {
        private Canvas _canvas;
        private Text _text;

        /// <summary>本名牌使用的呈现策略。</summary>
        public WorldAnchorPolicy Policy { get; set; } = new WorldAnchorPolicy();

        /// <summary>名牌跟随的世界坐标。</summary>
        public Vector3 AnchorPosition { get; set; }

        /// <summary>底下那块画布，供验收核对渲染模式。</summary>
        public Canvas Canvas => _canvas;

        /// <summary>建一块世界空间名牌，挂在场景根上。</summary>
        /// <param name="labelText">名牌上的文字。</param>
        /// <param name="anchorPosition">名牌跟随的世界坐标。</param>
        public static WorldSpaceLabel Create(string labelText, Vector3 anchorPosition)
        {
            var host = new GameObject("世界空间名牌");
            var label = host.AddComponent<WorldSpaceLabel>();
            label.AnchorPosition = anchorPosition;
            label.EnsureVisual();
            label.SetText(labelText);
            return label;
        }

        /// <summary>改名牌上的文字。</summary>
        /// <param name="labelText">新文字。</param>
        public void SetText(string labelText)
        {
            EnsureVisual();
            _text.text = labelText;
        }

        /// <summary>按相机位姿刷新一次显示与缩放，返回这一帧的呈现结论。</summary>
        /// <param name="cameraPosition">相机世界坐标。</param>
        /// <param name="cameraForward">相机朝向。</param>
        public WorldAnchorPresentation Refresh(Vector3 cameraPosition, Vector3 cameraForward)
        {
            EnsureVisual();

            var forward = cameraForward.normalized;
            var presentation = Policy.Resolve(
                ToWorldPoint(AnchorPosition),
                ToWorldPoint(cameraPosition),
                ToWorldPoint(forward));

            _canvas.enabled = presentation.IsVisible;
            if (presentation.IsVisible)
            {
                transform.position = AnchorPosition;

                // 名牌始终正对相机：世界空间画布不转向的话，侧面看过去就是一条线。
                transform.rotation = Quaternion.LookRotation(forward);
                transform.localScale = Vector3.one * presentation.Scale;
            }

            return presentation;
        }

        // 画布与文字走懒初始化：编辑模式下 Awake 不会被调用，
        // 而编辑器里的验收脚本同样要用这个组件。
        private void EnsureVisual()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            var rectTransform = (RectTransform)transform;
            rectTransform.sizeDelta = new Vector2(2f, 0.5f);

            var textHost = new GameObject("文字");
            textHost.transform.SetParent(transform, worldPositionStays: false);
            _text = textHost.AddComponent<Text>();
            _text.alignment = TextAnchor.MiddleCenter;
            _text.color = Color.white;

            // 内置字体在编辑器里恒定可取，省得为一块名牌牵进字体资产依赖。
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = 14;

            var textRect = (RectTransform)textHost.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private static WorldPoint ToWorldPoint(Vector3 value)
        {
            return new WorldPoint(value.x, value.y, value.z);
        }
    }
}
