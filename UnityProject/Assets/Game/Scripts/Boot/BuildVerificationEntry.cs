using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Template.Boot
{
    /// <summary>
    /// 出包验收入口：只有命令行带 <c>-buildVerification</c> 时才跑。
    /// 它按注册表跑全部验收项，把结论打进日志并落一份报告，然后退出。
    /// 平时启动游戏这段代码不做任何事。
    /// </summary>
    public static class BuildVerificationEntry
    {
        /// <summary>报告与日志里每一行的前缀，外面靠它把结论从满屏引擎日志里捞出来。</summary>
        public const string LinePrefix = "[出包验收]";

        private const string SwitchArgumentName = "-buildVerification";
        private const string ResourceVerificationArgumentName = "-resourceVerification";
        private const string ReportPathArgumentName = "-verificationReport";

        // Boot 自带的验收项在这里挂。可选功能包在自己的程序集里挂自己的那一项，
        // 所以这个入口里不许出现任何可选功能的名字。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterBuiltInVerifications()
        {
            BuildVerificationRegistry.Register(new SaveBuildVerification());
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RunWhenRequested()
        {
            var wantsBuildVerification = HasSwitch(SwitchArgumentName);
            var resourceBaseUrl = ReadArgument(ResourceVerificationArgumentName);
            if (!wantsBuildVerification && string.IsNullOrEmpty(resourceBaseUrl))
            {
                return;
            }

            var reportLines = new List<string>();
            if (wantsBuildVerification)
            {
                try
                {
                    Collect(reportLines);
                }
                catch (Exception exception)
                {
                    reportLines.Add($"未通过：验收入口自身抛了 {exception.GetType().Name}：{exception.Message}");
                }
            }

            // 资源热更那一条全是跨帧的异步操作，编不进同步流程，只能挂个驱动跑协程。
            if (!string.IsNullOrEmpty(resourceBaseUrl))
            {
                VerificationDriver.Create().Run(resourceBaseUrl, reportLines, FinishAndQuit);
                return;
            }

            FinishAndQuit(reportLines);
        }

        private static void FinishAndQuit(List<string> reportLines)
        {
            foreach (var line in reportLines)
            {
                Debug.Log($"{LinePrefix} {line}");
            }

            WriteReport(reportLines);
            Application.Quit(0);
        }

        /// <summary>协程驱动：真包里没有测试运行器，异步操作要靠一个常驻物体推。</summary>
        private sealed class VerificationDriver : MonoBehaviour
        {
            /// <summary>建一个不随场景卸载的驱动物体。</summary>
            public static VerificationDriver Create()
            {
                var host = new GameObject("出包验收驱动");
                DontDestroyOnLoad(host);
                return host.AddComponent<VerificationDriver>();
            }

            /// <summary>跑资源热更验收，跑完把结论交回给回调。</summary>
            /// <param name="remoteBaseUrl">本地资源服务器基地址。</param>
            /// <param name="reportLines">结论收集器。</param>
            /// <param name="onFinished">跑完之后的收尾动作。</param>
            public void Run(string remoteBaseUrl, List<string> reportLines, Action<List<string>> onFinished)
            {
                StartCoroutine(RunCoroutine(remoteBaseUrl, reportLines, onFinished));
            }

            private System.Collections.IEnumerator RunCoroutine(string remoteBaseUrl, List<string> reportLines, Action<List<string>> onFinished)
            {
                var innerLines = new List<string>();
                yield return ResourceUpdateVerification.Run(remoteBaseUrl, innerLines);
                foreach (var line in innerLines)
                {
                    reportLines.Add("资源热更 · " + line);
                }

                onFinished(reportLines);
            }
        }

        private static void Collect(List<string> reportLines)
        {
            reportLines.Add($"引擎 {Application.unityVersion}，平台 {Application.platform}，脚本后端 {ScriptingBackendName()}");

            var verifications = BuildVerificationRegistry.ListOrdered();
            if (verifications.Count == 0)
            {
                reportLines.Add("知会：注册表里一项验收都没有，本次只有上面这行环境信息");
                return;
            }

            // 一项抛异常不该把后面的项一起断掉：报告要能一次看完全部结论，而不是修一条跑一次。
            foreach (var verification in verifications)
            {
                try
                {
                    verification.Collect(reportLines);
                }
                catch (Exception exception)
                {
                    reportLines.Add($"未通过：验收项「{verification.Name}」抛了 {exception.GetType().Name}：{exception.Message}");
                }
            }
        }

        private static string ScriptingBackendName()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#else
            return "Mono";
#endif
        }

        private static void WriteReport(List<string> reportLines)
        {
            var reportPath = ReadArgument(ReportPathArgumentName)
                             ?? Path.Combine(Application.persistentDataPath, "出包验收报告.txt");
            try
            {
                var directory = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(reportPath, string.Join(Environment.NewLine, reportLines), new UTF8Encoding(false));
                Debug.Log($"{LinePrefix} 报告已写到 {reportPath}");
            }
            catch (Exception exception)
            {
                Debug.Log($"{LinePrefix} 报告写不出去（{exception.GetType().Name}：{exception.Message}），只有日志这一份");
            }
        }

        private static bool HasSwitch(string switchName)
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (argument == switchName)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadArgument(string argumentName)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (arguments[index] == argumentName)
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }
    }
}
