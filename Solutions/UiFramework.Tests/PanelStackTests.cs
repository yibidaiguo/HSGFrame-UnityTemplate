using HSGFrame.UiFramework;
using Xunit;

namespace Template.UiFramework.Tests
{
    /// <summary>分层面板栈的压栈、弹栈与跨层查询测试。</summary>
    public class PanelStackTests
    {
        [Fact]
        public void PushThenPeekTopReturnsLastPushedAndCountIsTwo()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "MainPanel");
            stack.Push(PanelLayer.Normal, "InventoryPanel");

            Assert.Equal("InventoryPanel", stack.PeekTop(PanelLayer.Normal));
            Assert.Equal(2, stack.CountIn(PanelLayer.Normal));
        }

        [Fact]
        public void PopFromEmptyStackReturnsNull()
        {
            var stack = new PanelStack();

            Assert.Null(stack.Pop(PanelLayer.Dialog));
        }

        [Fact]
        public void PeekTopmostPrefersHigherLayer()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "MainPanel");
            stack.Push(PanelLayer.Dialog, "DialogPanel");

            Assert.Equal("DialogPanel", stack.PeekTopmost());
        }

        [Fact]
        public void PushSameIdentifierMovesItToTop()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "MainPanel");
            stack.Push(PanelLayer.Normal, "InventoryPanel");
            stack.Push(PanelLayer.Normal, "MainPanel");

            Assert.Equal(2, stack.CountIn(PanelLayer.Normal));
            Assert.Equal("MainPanel", stack.PeekTop(PanelLayer.Normal));
        }

        [Fact]
        public void ClearLayerOnlyClearsThatLayer()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "MainPanel");
            stack.Push(PanelLayer.Dialog, "DialogPanel");

            stack.ClearLayer(PanelLayer.Normal);

            Assert.Equal(0, stack.CountIn(PanelLayer.Normal));
            Assert.Equal(1, stack.CountIn(PanelLayer.Dialog));
        }
    }
}
