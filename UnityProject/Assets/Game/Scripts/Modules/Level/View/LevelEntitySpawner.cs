using System.Collections;
using System.Collections.Generic;
using HSGFrame.Logging;
using Template.Level.Contracts;
using Template.Level.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

// UnityEngine 里也有一个 Logger，与 HSGFrame.Logging.Logger 同名，直接写 Logger 会 CS0104。
using Logger = HSGFrame.Logging.Logger;

namespace Template.Level.View
{
    /// <summary>
    /// 关卡实体装配器：扫场景里的 <see cref="LogicEntityMarker"/>，按类别查地址、加载预制体、挂到标记物体下。
    /// </summary>
    /// <remarks>
    /// 这是「关卡数据 → 画面」这条链上原先缺的那一环。<c>LevelSceneBuilder</c> 只把类别写进标记组件就收工了，
    /// 于是场景里全是空物体；本组件把类别接到 <c>ResourceArt/Level</c> 的预制体上。
    /// 装配完顺手把名录挂到 <see cref="LevelEntityCatalogRegistry"/>，模块外从那里按接口读实体。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LevelEntitySpawner : MonoBehaviour
    {
        [SerializeField] [Tooltip("实体类别到资源地址的映射资产")]
        private LevelEntityResourceMapAsset _resourceMapAsset;

        [SerializeField] [Tooltip("YooAsset 包裹名，与 BundleCollectorSetting 里的一致")]
        private string _packageName = "DefaultPackage";

        [SerializeField] [Tooltip("勾上则每次世界场景加载完成后自动装配一次")]
        private bool _spawnOnSceneLoaded = true;

        private readonly List<AssetHandle> _handles = new List<AssetHandle>();
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>本次装配出来的实体物体数量。</summary>
        public int SpawnedCount => _spawned.Count;

        private Logger _logger;

        /// <summary>装配用的日志门面，默认取全局挂靠点上那一份（启动装配挂的）。</summary>
        public Logger Logger
        {
            get => _logger ?? LoggerHub.Shared;
            set => _logger = value;
        }

        /// <summary>按场景里的标记装配全部关卡实体。</summary>
        public IEnumerator SpawnAll()
        {
            if (_resourceMapAsset == null)
            {
                Logger.Error("位置：LevelEntitySpawner；原因：没挂实体资源映射资产；修复：把 Assets/Game/Settings/Level/EntityResourceMap.asset 拖到本组件上；参考：《结构规范-资源》第五节");
                yield break;
            }

            ILevelEntityResourceMap resourceMap;
            try
            {
                resourceMap = _resourceMapAsset.ToResourceMap();
            }
            catch (System.ArgumentException exception)
            {
                // 映射配错属于配置错误，不该把整个关卡装配连同启动流程一起崩掉：
                // 报清楚哪一条错了，让关卡以「空物体」状态起来，比黑屏更容易定位。
                Logger.Error(exception.Message);
                yield break;
            }

            Despawn();

            // FindObjectsInactive.Include：标记物体可能被关卡编辑器摆成隐藏，隐藏的实体照样要装配，
            // 否则「在编辑器里临时关掉一个物体」会静默改变出包后的关卡内容。
            var markers = Object.FindObjectsByType<LogicEntityMarker>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            // 没有标记就没有要装配的东西——启动场景就是这种情况。提前返回，
            // 免得为一个本来就没活干的场景去问资源系统要包裹。
            if (markers.Length == 0)
            {
                yield break;
            }

            // YooAssets 没初始化时 TryGetPackage 直接抛，不是返回 false。本组件挂在 sceneLoaded 上，
            // 而 sceneLoaded 对启动场景自己也会触发一次——那一刻启动装配还没跑到初始化资源系统那步，
            // 于是每次进 Play 都先吐一个红异常。判一下 IsInitialized，把它降成一条能看懂的错误。
            if (!YooAssets.IsInitialized)
            {
                Logger.Error($"位置：LevelEntitySpawner；原因：资源系统还没初始化，装配不了 {markers.Length} 个关卡实体；修复：确认关卡场景是在启动装配跑完之后才加载的；参考：Assets/Game/Scripts/Boot/GameBootstrap.cs");
                yield break;
            }

            var package = YooAssets.TryGetPackage(_packageName, out var existing)
                ? existing
                : null;
            if (package == null)
            {
                Logger.Error($"位置：LevelEntitySpawner；原因：找不到资源包裹「{_packageName}」；修复：先由启动装配初始化 YooAsset 再装配关卡；参考：Assets/Game/Scripts/Boot/GameBootstrap.cs");
                yield break;
            }

            var missingKinds = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var marker in markers)
            {
                if (!resourceMap.TryGetResourceAddress(marker.EntityKind, out var address))
                {
                    // 未登记的类别只记一次：一个关卡里同类实体几十个，逐个报会把日志淹了。
                    missingKinds.Add(marker.EntityKind ?? "（空类别）");
                    continue;
                }

                var handle = package.LoadAssetAsync<GameObject>(address);
                yield return handle;

                if (handle.Status != EOperationStatus.Succeeded || handle.AssetObject == null)
                {
                    Logger.Error($"位置：LevelEntitySpawner；原因：按地址 {address} 加载预制体失败（{handle.Error}）；修复：确认 ResourceArt/Level 下有同名预制体且已进收集器；参考：Assets/Game/Settings/Resource/BundleCollectorSetting.asset");
                    handle.Release();
                    continue;
                }

                _handles.Add(handle);

                var visual = Object.Instantiate((GameObject)handle.AssetObject, marker.transform);
                visual.name = address;

                // 局部变换归零：标记物体已经摆在关卡 JSON 给的位置与朝向上，
                // 可视体只负责「长什么样」，位置一律由标记说了算。
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;

                _spawned.Add(visual);
            }

            foreach (var kind in missingKinds)
            {
                Logger.Warning($"位置：LevelEntitySpawner；原因：实体类别「{kind}」没有登记资源地址，这类实体保持空物体；修复：在实体资源映射资产里补一条；参考：Assets/Game/Settings/Level/EntityResourceMap.asset");
            }

            LevelEntityCatalogRegistry.Publish(new LevelEntityCatalog(markers));
            Logger.Success($"关卡实体装配完成：标记 {markers.Length} 个，装出可视体 {_spawned.Count} 个");
        }

        /// <summary>拆掉本组件装出来的可视体并释放资源句柄。</summary>
        public void Despawn()
        {
            foreach (var visual in _spawned)
            {
                if (visual != null)
                {
                    Object.Destroy(visual);
                }
            }

            _spawned.Clear();

            foreach (var handle in _handles)
            {
                handle.Release();
            }

            _handles.Clear();
        }

        private void OnEnable()
        {
            if (_spawnOnSceneLoaded)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // 挂场景加载事件而不是 Start：本组件跨场景常驻在启动场景里，Start 跑的时候
        // 世界场景还没加载，那时扫场景一个标记都找不到。世界场景每换一次就重装一次，
        // 关卡切换因此不需要任何额外接线。
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StartCoroutine(SpawnAll());
        }

        private void OnDestroy()
        {
            Despawn();

            // 名录跟着装配方走：本组件没了还留着一份指向已销毁组件的名录，
            // 模块外读到的就是一堆 Missing 引用。
            LevelEntityCatalogRegistry.Clear();
        }
    }
}
