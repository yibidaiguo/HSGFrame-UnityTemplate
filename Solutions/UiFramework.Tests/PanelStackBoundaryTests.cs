using GameTemplateForAgent.UiFramework;
using Xunit;

namespace Template.UiFramework.Tests
{
    /// <summary>分层面板栈的边界用例：空栈查询、移除、去重与跨层隔离。</summary>
    public class PanelStackBoundaryTests
    {
        [Fact]
        public void CountOfOnEmptyStackReturnsZero()
        {
            var stack = new PanelStack();

            Assert.Equal(0, stack.CountOf(PanelLayer.Normal));
        }

        [Fact]
        public void ListFromTopOnEmptyStackReturnsEmptyListNotnull()
        {
            var stack = new PanelStack();

            var list = stack.ListFromTop(PanelLayer.Dialog);

            Assert.NotNull(list);
            Assert.Empty(list);
        }

        [Fact]
        public void ListFromTopOrdersLastPushedFirst()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "FirstPanel");
            stack.Push(PanelLayer.Normal, "SecondPanel");
            stack.Push(PanelLayer.Normal, "ThirdPanel");

            var list = stack.ListFromTop(PanelLayer.Normal);

            Assert.Equal(3, list.Count);
            Assert.Equal("ThirdPanel", list[0]);
            Assert.Equal("SecondPanel", list[1]);
            Assert.Equal("FirstPanel", list[2]);
        }

        [Fact]
        public void RemoveExistingItemReturnsTrueAndDecrementsCount()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "MainPanel");
            stack.Push(PanelLayer.Normal, "InventoryPanel");

            var removed = stack.Remove(PanelLayer.Normal, "InventoryPanel");

            Assert.True(removed);
            Assert.Equal(1, stack.CountOf(PanelLayer.Normal));
        }

        [Fact]
        public void RemoveMissingItemReturnsFalseAndKeepsCount()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "MainPanel");

            var removed = stack.Remove(PanelLayer.Normal, "MissingPanel");

            Assert.False(removed);
            Assert.Equal(1, stack.CountOf(PanelLayer.Normal));
        }

        [Fact]
        public void RemoveMiddleItemPreservesRelativeOrderOfRemaining()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "BottomPanel");
            stack.Push(PanelLayer.Normal, "MiddlePanel");
            stack.Push(PanelLayer.Normal, "TopPanel");

            stack.Remove(PanelLayer.Normal, "MiddlePanel");

            var list = stack.ListFromTop(PanelLayer.Normal);
            Assert.Equal(2, list.Count);
            Assert.Equal("TopPanel", list[0]);
            Assert.Equal("BottomPanel", list[1]);
        }

        [Fact]
        public void PushingSameIdentifierKeepsSingleEntryAtTop()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "MainPanel");
            stack.Push(PanelLayer.Normal, "InventoryPanel");
            stack.Push(PanelLayer.Normal, "MainPanel");

            Assert.Equal(2, stack.CountOf(PanelLayer.Normal));
            Assert.Equal("MainPanel", stack.ListFromTop(PanelLayer.Normal)[0]);
        }

        [Fact]
        public void RemoveInOneLayerDoesNotAffectAnotherLayer()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "MainPanel");
            stack.Push(PanelLayer.Dialog, "DialogPanel");

            var removed = stack.Remove(PanelLayer.Dialog, "DialogPanel");

            Assert.True(removed);
            Assert.Equal(1, stack.CountOf(PanelLayer.Normal));
            Assert.Equal(0, stack.CountOf(PanelLayer.Dialog));
        }

        [Fact]
        public void RemoveNullOrEmptyIdentifierReturnsFalseWithoutThrowing()
        {
            var stack = new PanelStack();
            stack.Push(PanelLayer.Normal, "MainPanel");

            Assert.False(stack.Remove(PanelLayer.Normal, null));
            Assert.False(stack.Remove(PanelLayer.Normal, string.Empty));
            Assert.Equal(1, stack.CountOf(PanelLayer.Normal));
        }
    }
}
