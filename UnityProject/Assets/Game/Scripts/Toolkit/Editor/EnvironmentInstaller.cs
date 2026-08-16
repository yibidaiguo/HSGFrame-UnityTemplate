using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>一键部署开发环境：核对常驻依赖是否真的解析进来了。</summary>
    /// <remarks>
    /// UPM 包在 manifest.json 里写死，clone 后 Unity 打开工程时自己会拉，所以常驻这边没有要「装」的东西，
    /// 只需确认它们真的到位——manifest 写了却因为网络没拉到时，人往往以为环境是好的。
    /// 可选功能自带的安装步骤跟着各自的包走：例如热更的本地 il2cpp 数据在
    /// 菜单「工具链/热更/安装 HybridCLR 本地数据」，摘掉那个包时这一步一并消失。
    /// </remarks>
    public static class EnvironmentInstaller
    {
        // 菜单根名保持通用：模板会被生成成任意项目名，把模板自己的名字焊在这里，
        // 新项目的菜单就会顶着一个不属于它的名字（MenuItem 路径是编译期常量，运行时换不掉）。
        private const string MenuPath = "工具链/初始化开发环境";

        /// <summary>菜单入口：人点一下就装。</summary>
        [MenuItem(MenuPath)]
        public static void InstallFromMenu()
        {
            var report = Install();
            Debug.Log(report.ToDisplayText());

            if (!report.IsSuccess)
            {
                EditorUtility.DisplayDialog("初始化开发环境", report.ToDisplayText(), "知道了");
                return;
            }

            EditorUtility.DisplayDialog("初始化开发环境", report.ToDisplayText(), "好");
        }

        /// <summary>命令行入口：由 unity-cmd.ps1 通过 -executeMethod 调，失败时以非零码退出。</summary>
        public static void InstallFromCommandLine()
        {
            var report = Install();
            Debug.Log(report.ToDisplayText());
            EditorApplication.Exit(report.IsSuccess ? 0 : 1);
        }

        /// <summary>执行安装，返回一份可直接打印的报告。</summary>
        public static EnvironmentInstallReport Install()
        {
            var report = new EnvironmentInstallReport();

            try
            {
                CheckYooAsset(report);
            }
            catch (Exception exception)
            {
                report.Fail($"YooAsset 核对抛出 {exception.GetType().Name}：{exception.Message}");
            }

            return report;
        }

        private static void CheckYooAsset(EnvironmentInstallReport report)
        {
            // YooAsset 是纯 UPM 包，没有额外安装步骤；这里只确认它真的被解析进来了，
            // 免得 manifest 写了却因为网络问题没拉到，人却以为环境是好的。
            var yooAssetType = Type.GetType("YooAsset.YooAssets, YooAsset");
            if (yooAssetType == null)
            {
                report.Fail("YooAsset 程序集没找到，检查 manifest.json 与网络后重新打开工程");
                return;
            }

            report.Note("YooAsset 已就位");
        }
    }

    /// <summary>一次环境初始化的结果：是否成功，以及逐条过程说明。</summary>
    public sealed class EnvironmentInstallReport
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
            var headline = IsSuccess ? "[环境初始化] 完成" : "[环境初始化] 有问题";
            return headline + Environment.NewLine + _lines.ToString().TrimEnd();
        }
    }
}
