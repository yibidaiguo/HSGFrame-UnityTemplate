using UnityEditor;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>工程打开时检查开发环境是否就位，没装就当场问一句要不要装。</summary>
    /// <remarks>
    /// 刻意不静默自动装：HybridCLR 的本地 il2cpp 数据有 800 MB 上下，
    /// 在别人没预期的时候占满磁盘或跑满带宽，比多点一下按钮讨厌得多。
    /// 无人值守的场景走命令行入口（unity-cmd.ps1 -ExecuteMethod ...EnvironmentInstaller.InstallFromCommandLine）。
    /// </remarks>
    [InitializeOnLoad]
    public static class EnvironmentBootstrapPrompt
    {
        // 同一次编辑器会话里问过一次就够了，域重载会重跑这个构造函数。
        private const string AskedSessionKey = "Toolkit.环境初始化.本次会话已询问";

        static EnvironmentBootstrapPrompt()
        {
            EditorApplication.delayCall += PromptWhenEnvironmentIsMissing;
        }

        private static void PromptWhenEnvironmentIsMissing()
        {
            if (Application.isBatchMode || SessionState.GetBool(AskedSessionKey, false))
            {
                return;
            }

            SessionState.SetBool(AskedSessionKey, true);

            var controller = new HybridCLR.Editor.Installer.InstallerController();
            if (controller.HasInstalledHybridCLR())
            {
                return;
            }

            var install = EditorUtility.DisplayDialog(
                "开发环境还没装",
                "HybridCLR 的本地 il2cpp 数据还没装（约 800 MB，装在工程内的 HybridCLRData/，不进仓库）。\n\n"
                    + "现在装吗？也可以之后从菜单 工具链/初始化开发环境 手动装。",
                "现在装",
                "以后再说");

            if (install)
            {
                EnvironmentInstaller.InstallFromMenu();
            }
        }
    }
}
