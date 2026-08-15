using System;
using System.Collections.Generic;

namespace GameTemplateForAgent.UiFramework
{
    /// <summary>分层面板栈：按层各自维护一个栈，提供压入、弹出与跨层查询。</summary>
    public sealed class PanelStack
    {
        private static readonly PanelLayer[] LayersFromTopToBottom = BuildLayersFromTopToBottom();

        private readonly Dictionary<PanelLayer, Stack<string>> _stacks = new Dictionary<PanelLayer, Stack<string>>();

        /// <summary>把面板标识名压入指定层的栈顶。</summary>
        /// <param name="layer">目标层。</param>
        /// <param name="panelIdentifierName">面板标识名。</param>
        public void Push(PanelLayer layer, string panelIdentifierName)
        {
            // 同一面板重复打开应当置顶而不是叠两份：先把旧的那份从该层移除再压栈。
            RemoveFromLayer(layer, panelIdentifierName);
            GetOrCreateStack(layer).Push(panelIdentifierName);
        }

        /// <summary>弹出指定层的栈顶面板标识名；空栈返回 null。</summary>
        /// <param name="layer">目标层。</param>
        public string Pop(PanelLayer layer)
        {
            if (_stacks.TryGetValue(layer, out var stack) && stack.Count > 0)
            {
                return stack.Pop();
            }

            return null;
        }

        /// <summary>查看指定层的栈顶面板标识名；空栈返回 null。</summary>
        /// <param name="layer">目标层。</param>
        public string PeekTop(PanelLayer layer)
        {
            if (_stacks.TryGetValue(layer, out var stack) && stack.Count > 0)
            {
                return stack.Peek();
            }

            return null;
        }

        /// <summary>从最高层往下找第一个非空栈的栈顶；全空返回 null。</summary>
        public string PeekTopmost()
        {
            foreach (var layer in LayersFromTopToBottom)
            {
                if (_stacks.TryGetValue(layer, out var stack) && stack.Count > 0)
                {
                    return stack.Peek();
                }
            }

            return null;
        }

        /// <summary>返回指定层当前压入的面板数量。</summary>
        /// <param name="layer">目标层。</param>
        public int CountIn(PanelLayer layer)
        {
            return _stacks.TryGetValue(layer, out var stack) ? stack.Count : 0;
        }

        /// <summary>指定层当前的面板数量。</summary>
        /// <param name="layer">目标层。</param>
        public int CountOf(PanelLayer layer)
        {
            return CountIn(layer);
        }

        /// <summary>从栈顶到栈底列出指定层的全部面板标识名。</summary>
        /// <param name="layer">目标层。</param>
        public IReadOnlyList<string> ListFromTop(PanelLayer layer)
        {
            if (!_stacks.TryGetValue(layer, out var stack))
            {
                return Array.Empty<string>();
            }

            return new List<string>(stack);
        }

        /// <summary>清空指定层的全部面板，不影响其它层。</summary>
        /// <param name="layer">目标层。</param>
        public void ClearLayer(PanelLayer layer)
        {
            if (_stacks.TryGetValue(layer, out var stack))
            {
                stack.Clear();
            }
        }

        /// <summary>判断某个面板标识名是否已压入任意层。</summary>
        /// <param name="panelIdentifierName">面板标识名。</param>
        public bool Contains(string panelIdentifierName)
        {
            foreach (var stack in _stacks.Values)
            {
                if (stack.Contains(panelIdentifierName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>从指定层移除一个面板标识名，移除成功返回 true。</summary>
        /// <param name="layer">目标层。</param>
        /// <param name="panelIdentifierName">面板标识名。</param>
        public bool Remove(PanelLayer layer, string panelIdentifierName)
        {
            if (string.IsNullOrEmpty(panelIdentifierName))
            {
                return false;
            }

            return RemoveFromLayer(layer, panelIdentifierName);
        }

        private static PanelLayer[] BuildLayersFromTopToBottom()
        {
            var values = (PanelLayer[])Enum.GetValues(typeof(PanelLayer));
            Array.Reverse(values);
            return values;
        }

        private Stack<string> GetOrCreateStack(PanelLayer layer)
        {
            if (!_stacks.TryGetValue(layer, out var stack))
            {
                stack = new Stack<string>();
                _stacks[layer] = stack;
            }

            return stack;
        }

        private bool RemoveFromLayer(PanelLayer layer, string panelIdentifierName)
        {
            if (!_stacks.TryGetValue(layer, out var stack) || stack.Count == 0)
            {
                return false;
            }

            // Stack 枚举自顶向底，先收集去掉命中项后的剩余项，再从底到顶重建，保持相对顺序不变。
            var kept = new List<string>();
            foreach (var item in stack)
            {
                if (!string.Equals(item, panelIdentifierName, StringComparison.Ordinal))
                {
                    kept.Add(item);
                }
            }

            if (kept.Count == stack.Count)
            {
                return false;
            }

            var rebuilt = new Stack<string>();
            for (var index = kept.Count - 1; index >= 0; index--)
            {
                rebuilt.Push(kept[index]);
            }

            _stacks[layer] = rebuilt;
            return true;
        }
    }
}
