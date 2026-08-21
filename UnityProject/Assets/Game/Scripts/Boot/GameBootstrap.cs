using System;
using System.Collections;
using System.IO;
using System.Text;
using HSGFrame.Logging;
using HSGFrame.Save;

// UnityEngine 里也有一个 Logger，与 HSGFrame.Logging.Logger 同名，直接写 Logger 会 CS0104。
// 起别名而不是处处写全名：这一层里「Logger」只可能指日志门面那一个。
using Logger = HSGFrame.Logging.Logger;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace Template.Boot
{
    /// <summary>资源系统的运行模式。</summary>
    public enum ResourcePlayMode
    {
        /// <summary>编辑器模拟：不打包直接按资产路径加载，编辑器里按 Play 就能跑。</summary>
        EditorSimulate = 0,

        /// <summary>单机离线：只读随包发布的内置资源，不连服务器。</summary>
        Offline = 1,

        /// <summary>联机：从远端取版本与清单，边下边玩。</summary>
        Host = 2
    }

    /// <summary>
    /// 游戏启动装配：整个运行时唯一的装配入口，按固定顺序把框架各件立起来，最后交给首个世界场景。
    /// </summary>
    /// <remarks>
    /// 顺序是硬的，后一步依赖前一步：
    /// 日志落点 → 帧驱动 → 资源系统 → 存档 → 首个世界场景。
    /// 日志排第一，是为了让后面每一步的失败都有地方可写；帧驱动排第二，
    /// 因为资源系统与场景加载的进度回调都靠它推进。
    /// 本类型属 AOT 的 <c>Game.Boot</c>，引用不到热更的 <c>Game.View</c>／<c>Game.Logic</c>，
    /// 所以它只装配框架，业务侧的东西由世界场景自带的组件接手。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        private const string LogDirectoryName = "日志";
        private const string LogFileName = "运行.log";
        private const string SaveDirectoryName = "存档";
        private const string SaveFileName = "存档.json";

        [SerializeField] [Tooltip("装配完成后加载的首个世界场景名，要在 Build Settings 里")]
        private string _firstWorldSceneName = "村庄";

        [SerializeField] [Tooltip("YooAsset 包裹名，与 BundleCollectorSetting 里的一致")]
        private string _packageName = "DefaultPackage";

        [SerializeField] [Tooltip("资源系统的运行模式；编辑器里一般用编辑器模拟")]
        private ResourcePlayMode _resourcePlayMode = ResourcePlayMode.EditorSimulate;

        [SerializeField] [Tooltip("联机模式下的远端基地址，其余模式忽略")]
        private string _remoteBaseUrl = "http://127.0.0.1:8123/";

        [SerializeField] [Tooltip("低于这个等级的日志不写")]
        private LogLevel _minimumLogLevel = LogLevel.Information;

        /// <summary>装配是否已经整条跑完。</summary>
        public bool IsReady { get; private set; }

        /// <summary>本次装配读出来的存档，装配未完成时为 null。</summary>
        public SaveDocument Save { get; private set; }

        /// <summary>按固定顺序跑完整条装配。</summary>
        public IEnumerator RunBootSequence()
        {
            var logger = BuildLogger();
            LoggerHub.Publish(logger);
            logger.Information("启动装配 1/5：日志落点就绪");

            FrameworkDriverHost.Create();
            logger.Information("启动装配 2/5：帧驱动就绪");

            var resourceReady = false;
            yield return InitializeResourceSystem(logger, succeeded => resourceReady = succeeded);
            if (!resourceReady)
            {
                logger.Error("启动装配中断：资源系统没起来，后面两步跳过");
                yield break;
            }

            logger.Information("启动装配 3/5：资源系统就绪");

            Save = LoadOrCreateSave(logger);
            logger.Information($"启动装配 4/5：存档就绪（版本 {Save.Version}，分节 {Save.Sections.Count} 个）");

            if (string.IsNullOrWhiteSpace(_firstWorldSceneName))
            {
                logger.Warning("位置：GameBootstrap；原因：没配首个世界场景名，装配停在这一步；修复：在启动场景的 GameBootstrap 上填写场景名；参考：Assets/Game/Scenes/World/");
                IsReady = true;
                yield break;
            }

            var loadOperation = SceneManager.LoadSceneAsync(_firstWorldSceneName, LoadSceneMode.Single);
            if (loadOperation == null)
            {
                // 场景没进 Build Settings 时引擎返回 null，且不抛异常——不显式判一下就是一个静默的黑屏。
                logger.Error($"位置：GameBootstrap；原因：场景「{_firstWorldSceneName}」加载不了，多半没进 Build Settings；修复：把它加进 Build Settings 的场景清单；参考：Assets/Game/Scenes/World/");
                yield break;
            }

            yield return loadOperation;

            IsReady = true;
            logger.Success($"启动装配 5/5：世界场景「{_firstWorldSceneName}」已加载，装配完成");
        }

        private Logger BuildLogger()
        {
            var options = new LogFormatOptions { WriteTimestamp = true, WriteLevel = true };
            var logger = new Logger(options) { MinimumLevel = _minimumLogLevel };

            logger.AddSink(new BootConsoleLogSink(options));

            var logPath = Path.Combine(Application.persistentDataPath, LogDirectoryName, LogFileName);
            logger.AddSink(new FileLogSink(logPath, options));

            return logger;
        }

        private IEnumerator InitializeResourceSystem(Logger logger, Action<bool> onFinished)
        {
            if (!YooAssets.IsInitialized)
            {
                YooAssets.Initialize();
            }

            if (!YooAssets.TryGetPackage(_packageName, out var package))
            {
                package = YooAssets.CreatePackage(_packageName);
            }

            InitializePackageOptions options;
            switch (_resourcePlayMode)
            {
                case ResourcePlayMode.EditorSimulate:
                    options = BuildEditorSimulateOptions(logger);
                    break;
                case ResourcePlayMode.Offline:
                    options = new OfflinePlayModeOptions
                    {
                        BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(null),
                    };
                    break;
                default:
                    options = new HostPlayModeOptions
                    {
                        BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(null),
                        CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(
                            new FlatRemoteService(_remoteBaseUrl)),
                    };
                    break;
            }

            if (options == null)
            {
                onFinished(false);
                yield break;
            }

            var initializeOperation = package.InitializePackageAsync(options);
            yield return initializeOperation;
            if (initializeOperation.Status != EOperationStatus.Succeeded)
            {
                logger.Error($"位置：GameBootstrap；原因：资源包裹「{_packageName}」初始化失败（{initializeOperation.Error}）；修复：按运行模式检查资源产物是否就位；参考：Assets/Game/Settings/Resource/BundleCollectorSetting.asset");
                onFinished(false);
                yield break;
            }

            var versionOperation = package.RequestPackageVersionAsync();
            yield return versionOperation;
            if (versionOperation.Status != EOperationStatus.Succeeded)
            {
                logger.Error($"位置：GameBootstrap；原因：取资源包裹版本失败（{versionOperation.Error}）；修复：联机模式确认远端可达，离线模式确认首包已随包发布；参考：Tools/Gates/gate-full.ps1");
                onFinished(false);
                yield break;
            }

            var manifestOperation = package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(versionOperation.PackageVersion, 60));
            yield return manifestOperation;
            if (manifestOperation.Status != EOperationStatus.Succeeded)
            {
                logger.Error($"位置：GameBootstrap；原因：加载资源清单失败（{manifestOperation.Error}）；修复：确认包裹版本 {versionOperation.PackageVersion} 的清单文件存在；参考：Tools/Gates/gate-full.ps1");
                onFinished(false);
                yield break;
            }

            onFinished(true);
        }

        private InitializePackageOptions BuildEditorSimulateOptions(Logger logger)
        {
#if UNITY_EDITOR
            // 模拟构建只在编辑器里成立，出包后这条分支根本编不进来，所以整段用宏圈起来。
            var buildResult = EditorSimulateBuildInvoker.Build(_packageName, (int)EBundleType.VirtualAssetBundle);
            return new EditorSimulateModeOptions
            {
                EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory),
            };
#else
            logger.Error("位置：GameBootstrap；原因：真包里选了编辑器模拟模式，这个模式只在编辑器里成立；修复：把运行模式改成离线或联机再出包；参考：Assets/Game/Scenes/Boot/Boot.unity");
            return null;
#endif
        }

        private SaveDocument LoadOrCreateSave(Logger logger)
        {
            var directory = Path.Combine(Application.persistentDataPath, SaveDirectoryName);
            var filePath = Path.Combine(directory, SaveFileName);

            try
            {
                Directory.CreateDirectory(directory);
                if (!File.Exists(filePath))
                {
                    return new SaveDocument { Version = 1 };
                }

                var json = File.ReadAllText(filePath, Encoding.UTF8);
                return SaveSerializer.FromJson(json) ?? new SaveDocument { Version = 1 };
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 存档读不出来不该把启动挡死：报清楚再以空档继续，玩家至少进得去。
                logger.Error($"位置：GameBootstrap；原因：存档读取失败（{exception.Message}）；修复：检查 {filePath} 是否可读；参考：Assets/Game/Scripts/Boot/SaveVerification.cs");
                return new SaveDocument { Version = 1 };
            }
        }

        private IEnumerator Start()
        {
            yield return RunBootSequence();
        }
    }
}
