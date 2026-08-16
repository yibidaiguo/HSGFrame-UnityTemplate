using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HSGFrame.Hotfix.Editor
{
    /// <summary>装 HybridCLR 的本地 il2cpp 数据（几百 MB，从 il2cpp_plus 仓库克隆）。</summary>
    /// <remarks>
    /// UPM 包本身在 manifest.json 里写死，Unity 打开工程时自己会拉；真正需要「安装」的只有本地数据。
    /// 这一步随热更这个可选功能走：摘掉本包，这个菜单与命令行入口一起消失，
    /// 常驻的 <c>工具链/初始化开发环境</c> 只剩 YooAsset 核对。
    /// </remarks>
    public static class HybridClrEnvironmentInstaller
    {
        private const string MenuPath = "工具链/热更/安装 HybridCLR 本地数据";
        private const string DialogTitle = "安装 HybridCLR 本地数据";

        /// <summary>菜单入口：人点一下就装。</summary>
        [MenuItem(MenuPath)]
        public static void InstallFromMenu()
        {
            var report = Install();
            Debug.Log(report.ToDisplayText());
            EditorUtility.DisplayDialog(DialogTitle, report.ToDisplayText(), report.IsSuccess ? "好" : "知道了");
        }

        /// <summary>命令行入口：由 unity-cmd.ps1 通过 -executeMethod 调，失败时以非零码退出。</summary>
        public static void InstallFromCommandLine()
        {
            var report = Install();
            Debug.Log(report.ToDisplayText());
            EditorApplication.Exit(report.IsSuccess ? 0 : 1);
        }

        /// <summary>执行安装，返回一份可直接打印的报告。</summary>
        public static HybridClrInstallReport Install()
        {
            var report = new HybridClrInstallReport();

            try
            {
                InstallHybridClr(report);
            }
            catch (Exception exception)
            {
                report.Fail($"HybridCLR 安装抛出 {exception.GetType().Name}：{exception.Message}");
            }

            return report;
        }

        private static void InstallHybridClr(HybridClrInstallReport report)
        {
            var controller = new global::HybridCLR.Editor.Installer.InstallerController();

            if (controller.GetCompatibleType() != global::HybridCLR.Editor.Installer.InstallerController.CompatibleType.Compatible)
            {
                report.Fail($"HybridCLR 判定当前编辑器版本 {Application.unityVersion} 不兼容，装不了");
                return;
            }

            if (controller.HasInstalledHybridCLR())
            {
                report.Note($"HybridCLR 已装过，跳过（本地 il2cpp 版本 {controller.Il2cppPlusLocalVersion}）");
                return;
            }

            // 这一步会从 il2cpp_plus 仓库克隆对应分支，几百 MB，首次跑几分钟很正常。
            report.Note($"HybridCLR 开始安装，拉取分支 {controller.Il2cppPlusLocalVersion}（首次会下几百 MB）");
            controller.InstallDefaultHybridCLR();

            if (controller.HasInstalledHybridCLR())
            {
                report.Note("HybridCLR 安装完成");
            }
            else
            {
                report.Fail("HybridCLR 安装跑完了，但本地 il2cpp 目录仍然不存在，需要人看日志");
            }
        }
    }

    /// <summary>一次 HybridCLR 本地数据安装的结果：是否成功，以及逐条过程说明。</summary>
    public sealed class HybridClrInstallReport
    {
        private readonly StringBuilder _lines = new StringBuilder();

        /// <summary>是否全部成功。</summary>
        public bool IsSuccess { get; private set; } = true;

        /// <summary>记一条过程说明。</summary>
        /// <param name="line">说明文本。</param>
        public void Note(string line)
        {
            _lines.AppendLine("  · " + line);
        }

        /// <summary>记一条失败原因，并把整体结果标成失败。</summary>
        /// <param name="line">失败原因。</param>
        public void Fail(string line)
        {
            IsSuccess = false;
            _lines.AppendLine("  × " + line);
        }

        /// <summary>拼成一段可直接打印给人看的文本。</summary>
        public string ToDisplayText()
        {
            var headline = IsSuccess ? "[HybridCLR 安装] 完成" : "[HybridCLR 安装] 有问题";
            return headline + Environment.NewLine + _lines.ToString().TrimEnd();
        }
    }
}
