using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HybridCLR;
using Template.Boot;
using UnityEngine;

// 命名空间刻意不叫 HSGFrame.Hotfix.HybridCLR：那样一来「HybridCLR」在本文件里会先解析成
// 自己这层命名空间，第三方的 HybridCLR.RuntimeApi 反而找不着。程序集名叫那个没问题，命名空间不行。
namespace HSGFrame.Hotfix
{
    /// <summary>
    /// 热更这一项出包验收：把随包发出去的热更程序集加载起来、补上 AOT 元数据，再反射调探针。
    /// 这里是全仓库**唯一**允许引用 <c>HybridCLR.Runtime</c> 的地方——它自己挂进 Boot 的注册表，
    /// 所以摘掉这个包时验收入口一行都不用改，少的就是这一项。
    /// </summary>
    public sealed class HotfixBuildVerification : IBuildVerification
    {
        /// <summary>热更排在存档之后：存档不依赖随包资产，先跑完它再碰这些会缺失的东西。</summary>
        public const int VerificationOrder = 20;

        private const string ShipDirectoryName = "HotfixShip";
        private const string AotMetadataDirectoryName = "AotMetadata";
        private const string ProbeAssemblyName = "HSGFrame.Hotfix.Probe";
        private const string VerificationTypeFullName = "Template.Hotfix.HotfixVerification";

        /// <summary>这一项的名字。</summary>
        public string Name => "热更";

        /// <summary>排序键。</summary>
        public int Order => VerificationOrder;

        // 挂表要先于跑表：Boot 的验收入口挂在 AfterSceneLoad，这里用 BeforeSceneLoad。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSelf()
        {
            BuildVerificationRegistry.Register(new HotfixBuildVerification());
        }

        /// <summary>跑热更这一项验收，把结论逐行追加进 reportLines。</summary>
        /// <param name="reportLines">结论收集器。</param>
        public void Collect(List<string> reportLines)
        {
            var shipDirectory = Path.Combine(Application.streamingAssetsPath, ShipDirectoryName);
            if (!Directory.Exists(shipDirectory))
            {
                reportLines.Add($"未通过：包里没有 {shipDirectory}，" +
                                "说明出包前没跑热更资产随包（HSGFrame.Hotfix.Editor.HotfixShipStaging.StageFromCommandLine）");
                return;
            }

            reportLines.Add(LoadAotMetadata(Path.Combine(shipDirectory, AotMetadataDirectoryName)));

            // 刻意不写死程序集名：热更程序集清单是宿主自己在 HybridCLR 设置里维护的，
            // 包不该知道宿主把哪几个程序集划成了热更。
            var shippedPaths = Directory.GetFiles(shipDirectory, "*.dll.bytes");
            if (shippedPaths.Length == 0)
            {
                reportLines.Add($"未通过：{shipDirectory} 下一个 .dll.bytes 都没有");
                return;
            }

            // 先把全部程序集加载进来，再回头取类型数。热更程序集之间有依赖，
            // 边加载边 GetTypes() 会在被依赖的那个还没进来时抛。
            var loadedAssemblies = new List<Assembly>();
            foreach (var shippedPath in shippedPaths)
            {
                try
                {
                    loadedAssemblies.Add(Assembly.Load(File.ReadAllBytes(shippedPath)));
                }
                catch (Exception exception)
                {
                    reportLines.Add($"未通过：加载热更程序集 {Path.GetFileName(shippedPath)} 失败，" +
                                    $"{exception.GetType().Name}：{exception.Message}");
                    return;
                }
            }

            Assembly probeAssembly = null;
            foreach (var loadedAssembly in loadedAssemblies)
            {
                var loadedName = loadedAssembly.GetName().Name;
                reportLines.Add($"热更程序集已加载：{loadedName}，{DescribeTypeCount(loadedAssembly)}");

                if (loadedName == ProbeAssemblyName)
                {
                    probeAssembly = loadedAssembly;
                }
            }

            if (probeAssembly == null)
            {
                reportLines.Add($"未通过：随包的热更程序集里没有 {ProbeAssemblyName}");
                return;
            }

            var verificationType = probeAssembly.GetType(VerificationTypeFullName, throwOnError: false);
            if (verificationType == null)
            {
                reportLines.Add($"未通过：热更程序集 {ProbeAssemblyName} 里找不到 {VerificationTypeFullName}");
                return;
            }

            reportLines.Add("验证 3 · 源生成器产物在热更程序集内 —— " + Invoke(verificationType, "ProbeSourceGenerator"));
            reportLines.Add("验证 7 · System.Text.Json 源生成 × IL2CPP × HybridCLR —— " + Invoke(verificationType, "ProbeJsonSourceGeneration"));
            reportLines.Add(Invoke(verificationType, "DescribeReflectionFallback"));
        }

        // 类型数只是给人看的旁证，取不到不该把整条验收判黑：热更程序集互相依赖时，
        // 谁先谁后由文件名顺序决定，取类型数这一步可能撞上还没解析的引用。
        private static string DescribeTypeCount(Assembly assembly)
        {
            try
            {
                return $"类型 {assembly.GetTypes().Length} 个";
            }
            catch (ReflectionTypeLoadException exception)
            {
                return $"类型数取不到（{exception.GetType().Name}）";
            }
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
    }
}
