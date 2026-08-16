using UnityEditor;
using UnityEngine;

namespace HSGFrame.Hotfix.Editor
{
    /// <summary>工程打开时检查 HybridCLR 的本地数据是否就位，没装就当场问一句要不要装。</summary>
    /// <remarks>
    /// 刻意不静默自动装：HybridCLR 的本地 il2cpp 数据有 800 MB 上下，
    /// 在别人没预期的时候占满磁盘或跑满带宽，比多点一下按钮讨厌得多。
    /// 无人值守的场景走命令行入口（unity-cmd.ps1 -ExecuteMethod HSGFrame.Hotfix.Editor.HybridClrEnvironmentInstaller.InstallFromCommandLine）。
    /// </remarks>
    [InitializeOnLoad]
    public static class HybridClrBootstrapPrompt
    {
        // 同一次编辑器会话里问过一次就够了，域重载会重跑这个构造函数。
        private const string AskedSessionKey = "Hotfix.HybridCLR安装.本次会话已询问";

        static HybridClrBootstrapPrompt()
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

            var controller = new global::HybridCLR.Editor.Installer.InstallerController();
            if (controller.HasInstalledHybridCLR())
            {
                return;
            }

            var install = EditorUtility.DisplayDialog(
                "开发环境还没装",
                "HybridCLR 的本地 il2cpp 数据还没装（约 800 MB，装在工程内的 HybridCLRData/，不进仓库）。\n\n"
                    + "现在装吗？也可以之后从菜单 工具链/热更/安装 HybridCLR 本地数据 手动装。",
                "现在装",
                "以后再说");

            if (install)
            {
                HybridClrEnvironmentInstaller.InstallFromMenu();
            }
        }
    }
}
