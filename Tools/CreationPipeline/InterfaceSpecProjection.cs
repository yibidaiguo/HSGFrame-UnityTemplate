using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 把界面规格投影成 uidef 的元素形状。
    ///
    /// **两份分开是因为写入者不同**（总纲「写入者唯一」纪律）：
    /// 界面规格归策划（行为、成败、验收），uidef 归工程（贴图、布局、控件类型）。
    /// 美术换一张贴图不该动策划写的行为规格，所以不能合成一份。
    ///
    /// 投影是**单向**的：规格 → uidef。反过来从 uidef 倒推规格没有意义——
    /// uidef 里没有行为，倒推只会把行为丢掉。
    /// </summary>
    public static class InterfaceSpecProjection
    {
        /// <summary>
        /// 界面规格里的元素类型 → UI Toolkit 控件名。
        ///
        /// 从前这一步是**按层名猜**的（btn_ 开头就当 Button），
        /// 因为那时的元素来自「看图猜出来的层」，除了名字什么都没有。
        /// 现在类型是规格里明写的，不用再猜——猜错一次，程序拿到的控件类型就是错的。
        /// </summary>
        private static readonly Dictionary<string, string> ControlByElementType = new(StringComparer.Ordinal)
        {
            ["Button"] = "Button",
            ["Toggle"] = "Toggle",
            ["Label"] = "Label",
            ["Image"] = "VisualElement",
            ["ProgressBar"] = "ProgressBar",
            ["Container"] = "VisualElement",
            ["Decoration"] = "VisualElement",
            ["Background"] = "VisualElement"
        };

        /// <summary>没登记的类型退回 VisualElement——它是 UI Toolkit 里什么都能当的那个基类。</summary>
        private const string DefaultControl = "VisualElement";

        /// <summary>
        /// 把一份界面规格投影成 uidef 元素表。
        /// </summary>
        /// <param name="spec">界面规格。</param>
        /// <param name="manifest">资产清单，用来取每个元素的贴图路径；给 null 则贴图留空。</param>
        public static IReadOnlyList<UiPanelElement> ToPanelElements(
            InterfaceSpec spec, IReadOnlyList<InterfaceAssetEntry> manifest)
        {
            var elements = new List<UiPanelElement>();
            if (spec == null)
            {
                return elements;
            }

            var textureByElement = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in manifest ?? Array.Empty<InterfaceAssetEntry>())
            {
                // 「不出图」的那些没有贴图路径——包括 Label 与 Container。
                // 给它们编一个路径的话，ui.scaffold 会写出一条指向不存在文件的 background-image。
                if (entry.Naming.Length > 0 && entry.Destination.Length > 0)
                {
                    textureByElement[entry.ElementIdentifier] = entry.Destination + entry.Naming + ".png";
                }
            }

            foreach (var element in spec.Elements)
            {
                element.ReadLayout(out var x, out var y, out var width, out var height);

                elements.Add(new UiPanelElement(
                    element.DisplayName.Length > 0 ? element.DisplayName : element.Identifier,
                    element.Identifier,
                    ControlByElementType.TryGetValue(element.ElementType, out var control) ? control : DefaultControl,
                    textureByElement.TryGetValue(element.Identifier, out var texture) ? texture : "",
                    x,
                    y,
                    width,
                    height));
            }

            return elements;
        }

        /// <summary>这一屏的面板标识名：面板名 + Panel，与拆图那条路生成的名字保持同一套。</summary>
        /// <param name="spec">界面规格。</param>
        public static string PanelIdentifier(InterfaceSpec spec)
        {
            var panel = spec?.PanelName ?? "";
            return panel.Length == 0
                ? "Panel"
                : (panel.EndsWith("Panel", StringComparison.Ordinal) ? panel : panel + "Panel");
        }
    }
}
