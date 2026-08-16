using UnityEditor;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>运行时资产脚手架的两个入口：batchmode 命令行，以及编辑器菜单。</summary>
    public static class RuntimeAssetScaffoldCommandLine
    {
        private const string MenuPath = "工具链/运行时/生成运行时资产";

        /// <summary>batchmode 入口：落一遍全部运行时资产，逐行打出摘要。</summary>
        public static void ScaffoldFromCommandLine()
        {
            foreach (var line in RuntimeAssetScaffold.ScaffoldAll())
            {
                Debug.Log(line);
            }
        }

        /// <summary>菜单入口：落一遍全部运行时资产。</summary>
        [MenuItem(MenuPath)]
        public static void ScaffoldFromMenu()
        {
            ScaffoldFromCommandLine();
        }
    }
}
