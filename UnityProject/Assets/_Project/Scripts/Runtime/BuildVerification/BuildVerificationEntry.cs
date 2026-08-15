using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HybridCLR;
using UnityEngine;

namespace Template.Presentation.BuildVerification
{
    /// <summary>
    /// 出包验收入口：只有命令行带 <c>-buildVerification</c> 时才跑。
    /// 它在真包里把热更程序集加载起来，调热更侧的两条探针，把结论打进日志并落一份报告，然后退出。
    /// 平时启动游戏这段代码不做任何事。
    /// </summary>
    public static class BuildVerificationEntry
    {
        /// <summary>报告与日志里每一行的前缀，外面靠它把结论从满屏引擎日志里捞出来。</summary>
        public const string LinePrefix = "[出包验收]";

        private const string SwitchArgumentName = "-buildVerification";
        private const string ResourceVerificationArgumentName = "-resourceVerification";
        private const string ReportPathArgumentName = "-verificationReport";
        private const string ShipDirectoryName = "HotfixShip";
        private const string HotfixAssemblyFileName = "Hotfix.Logic.dll.bytes";
        private const string AotMetadataDirectoryName = "AotMetadata";
        private const string VerificationTypeFullName = "Template.Hotfix.HotfixVerification";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RunWhenRequested()
        {
            var wantsHotfixVerification = HasSwitch(SwitchArgumentName);
            var resourceBaseUrl = ReadArgument(ResourceVerificationArgumentName);
            if (!wantsHotfixVerification && string.IsNullOrEmpty(resourceBaseUrl))
            {
                return;
            }

            var reportLines = new List<string>();
            if (wantsHotfixVerification)
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

            var shipDirectory = Path.Combine(Application.streamingAssetsPath, ShipDirectoryName);
            if (!Directory.Exists(shipDirectory))
            {
                reportLines.Add($"未通过：包里没有 {shipDirectory}，" +
                                "说明出包前没跑热更资产随包（Template.Toolkit.Editor.HotfixShipStaging.StageFromCommandLine）");
                return;
            }

            reportLines.Add(LoadAotMetadata(Path.Combine(shipDirectory, AotMetadataDirectoryName)));

            var hotfixAssemblyPath = Path.Combine(shipDirectory, HotfixAssemblyFileName);
            if (!File.Exists(hotfixAssemblyPath))
            {
                reportLines.Add($"未通过：包里没有热更程序集 {hotfixAssemblyPath}");
                return;
            }

            Assembly hotfixAssembly;
            try
            {
                hotfixAssembly = Assembly.Load(File.ReadAllBytes(hotfixAssemblyPath));
            }
            catch (Exception exception)
            {
                reportLines.Add($"未通过：加载热更程序集失败，{exception.GetType().Name}：{exception.Message}");
                return;
            }

            reportLines.Add($"热更程序集已加载：{hotfixAssembly.GetName().Name}，类型 {hotfixAssembly.GetTypes().Length} 个");

            var verificationType = hotfixAssembly.GetType(VerificationTypeFullName, throwOnError: false);
            if (verificationType == null)
            {
                reportLines.Add($"未通过：热更程序集里找不到 {VerificationTypeFullName}");
                return;
            }

            reportLines.Add("验证 3 · 源生成器产物在热更程序集内 —— " + Invoke(verificationType, "ProbeSourceGenerator"));
            reportLines.Add("验证 7 · System.Text.Json 源生成 × IL2CPP × HybridCLR —— " + Invoke(verificationType, "ProbeJsonSourceGeneration"));
            reportLines.Add(Invoke(verificationType, "DescribeReflectionFallback"));
        }

        // 补充 AOT 元数据是热更代码用到 AOT 泛型时的前置：漏了这一步，
        // 症状是运行到那一行才抛 ExecutionEngineException，而不是加载时就报错。
        private static string LoadAotMetadata(string metadataDirectory)
        {
            if (!Directory.Exists(metadataDirectory))
            {
                return $"AOT 补充元数据：目录 {metadataDirectory} 不存在，跳过";
            }

            var results = new List<string>();
            foreach (var filePath in Directory.GetFiles(metadataDirectory, "*.bytes"))
            {
                var assemblyFileName = Path.GetFileNameWithoutExtension(filePath);
                try
                {
                    var errorCode = RuntimeApi.LoadMetadataForAOTAssembly(File.ReadAllBytes(filePath), HomologousImageMode.SuperSet);
                    results.Add($"{assemblyFileName}={errorCode}");
                }
                catch (Exception exception)
                {
                    results.Add($"{assemblyFileName}=抛 {exception.GetType().Name}");
                }
            }

            return results.Count == 0
                ? $"AOT 补充元数据：目录 {metadataDirectory} 下没有 .bytes 文件"
                : $"AOT 补充元数据 {results.Count} 个：{string.Join("、", results)}";
        }

        private static string Invoke(Type verificationType, string methodName)
        {
            var method = verificationType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                return $"未通过：热更程序集里找不到 {methodName}()";
            }

            try
            {
                return method.Invoke(null, null) as string ?? "未通过：探针返回了 null";
            }
            catch (TargetInvocationException exception)
            {
                var inner = exception.InnerException ?? exception;
                return $"未通过：探针抛了 {inner.GetType().Name}：{inner.Message}";
            }
            catch (Exception exception)
            {
                return $"未通过：调用探针失败，{exception.GetType().Name}：{exception.Message}";
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
