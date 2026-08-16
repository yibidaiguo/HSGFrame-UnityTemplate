using HSGFrame.UiFramework;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Template.Presentation.UI.Tests.EditMode
{
    /// <summary>UI 框架在 Unity 编辑器内的联调测试：面板分层、打开关闭生命周期与主题加载。</summary>
    public class UiFrameworkEditModeTests
    {
        [Test]
        public void RootCreatesFiveLayerContainersInLayerOrder()
        {
            var host = new VisualElement();
            var root = new PanelRoot(host);

            Assert.AreEqual(5, host.childCount);
            Assert.AreEqual(0, host.IndexOf(root.GetLayerContainer(PanelLayer.Hud)));
            Assert.AreEqual(1, host.IndexOf(root.GetLayerContainer(PanelLayer.Normal)));
            Assert.AreEqual(2, host.IndexOf(root.GetLayerContainer(PanelLayer.Dialog)));
            Assert.AreEqual(3, host.IndexOf(root.GetLayerContainer(PanelLayer.Tip)));
            Assert.AreEqual(4, host.IndexOf(root.GetLayerContainer(PanelLayer.Loading)));
        }

        [Test]
        public void OpeningPanelsAttachesToOwnLayerContainer()
        {
            var host = new VisualElement();
            var root = new PanelRoot(host);
            var normal = new NormalTestPanel();
            var dialog = new DialogTestPanel();

            root.Open(normal);
            root.Open(dialog);

            Assert.AreSame(root.GetLayerContainer(PanelLayer.Normal), normal.parent);
            Assert.AreSame(root.GetLayerContainer(PanelLayer.Dialog), dialog.parent);
        }

        [Test]
        public void DialogLayerRendersAboveNormalLayer()
        {
            var host = new VisualElement();
            var root = new PanelRoot(host);
            var normal = new NormalTestPanel();
            var dialog = new DialogTestPanel();

            root.Open(normal);
            root.Open(dialog);

            var normalIndex = host.IndexOf(root.GetLayerContainer(PanelLayer.Normal));
            var dialogIndex = host.IndexOf(root.GetLayerContainer(PanelLayer.Dialog));

            Assert.Greater(dialogIndex, normalIndex);
        }

        [Test]
        public void OpeningPanelsSetsIsOpenAndInvokesOnOpenOnce()
        {
            var host = new VisualElement();
            var root = new PanelRoot(host);
            var normal = new NormalTestPanel();
            var dialog = new DialogTestPanel();

            root.Open(normal);
            root.Open(dialog);

            Assert.IsTrue(normal.IsOpen);
            Assert.IsTrue(dialog.IsOpen);
            Assert.AreEqual(1, normal.OpenCount);
            Assert.AreEqual(1, dialog.OpenCount);
        }

        [Test]
        public void StackPeekTopMatchesPanelIdentifiers()
        {
            var host = new VisualElement();
            var root = new PanelRoot(host);
            var normal = new NormalTestPanel();
            var dialog = new DialogTestPanel();

            root.Open(normal);
            root.Open(dialog);

            Assert.AreEqual(normal.PanelIdentifierName, root.Stack.PeekTop(PanelLayer.Normal));
            Assert.AreEqual(dialog.PanelIdentifierName, root.Stack.PeekTop(PanelLayer.Dialog));
        }

        [Test]
        public void ClosingDialogPanelRestoresState()
        {
            var host = new VisualElement();
            var root = new PanelRoot(host);
            var normal = new NormalTestPanel();
            var dialog = new DialogTestPanel();

            root.Open(normal);
            root.Open(dialog);
            root.Close(dialog);

            Assert.IsFalse(dialog.IsOpen);
            Assert.AreEqual(1, dialog.CloseCount);
            Assert.AreEqual(0, root.Stack.CountOf(PanelLayer.Dialog));
            Assert.IsNull(dialog.parent);
            Assert.IsTrue(normal.IsOpen);
            Assert.AreEqual(1, root.Stack.CountOf(PanelLayer.Normal));
        }

        [Test]
        public void OpeningTwoPanelsInSameLayerStacksLastOpenedFirst()
        {
            var host = new VisualElement();
            var root = new PanelRoot(host);
            var first = new NormalTestPanel();
            var second = new SecondaryNormalTestPanel();

            root.Open(first);
            root.Open(second);

            var list = root.Stack.ListFromTop(PanelLayer.Normal);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(second.PanelIdentifierName, list[0]);
            Assert.AreEqual(first.PanelIdentifierName, list[1]);
        }

        [Test]
        public void ThemeVariablesResolveAfterApplyTheme()
        {
            var host = new VisualElement();
            var root = new PanelRoot(host);

            var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.hsgframe.uiframework/Theme/主题变量.uss");
            Assert.IsNotNull(theme, "包没接进 manifest.json：找不到主题样式表 Packages/com.hsgframe.uiframework/Theme/主题变量.uss");

            Assert.AreEqual(0, host.styleSheets.count);
            root.ApplyTheme(theme);
            Assert.AreEqual(1, host.styleSheets.count);
        }

        private sealed class NormalTestPanel : PanelBase
        {
            public int OpenCount;
            public int CloseCount;

            public override PanelLayer Layer => PanelLayer.Normal;

            public override void OnOpen()
            {
                OpenCount++;
            }

            public override void OnClose()
            {
                CloseCount++;
            }
        }

        private sealed class SecondaryNormalTestPanel : PanelBase
        {
            public override PanelLayer Layer => PanelLayer.Normal;

            public override void OnOpen()
            {
            }

            public override void OnClose()
            {
            }
        }

        private sealed class DialogTestPanel : PanelBase
        {
            public int OpenCount;
            public int CloseCount;

            public override PanelLayer Layer => PanelLayer.Dialog;

            public override void OnOpen()
            {
                OpenCount++;
            }

            public override void OnClose()
            {
                CloseCount++;
            }
        }
    }
}
