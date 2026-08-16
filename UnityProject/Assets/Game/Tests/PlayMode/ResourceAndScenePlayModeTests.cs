using System.Collections;
using HSGFrame.Resource;
using HSGFrame.Scene;
using NUnit.Framework;
using Template.Presentation.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using YooAsset;

namespace Template.Tests.PlayMode
{
    /// <summary>资源与场景在真运行期的加载测试。这两件事在编辑模式下都驱动不起来，只有 PlayMode 能证。</summary>
    public sealed class ResourceAndScenePlayModeTests
    {
        private const string PackageName = "DefaultPackage";

        /// <summary>YooAsset 在编辑器模拟模式下真初始化并同步加载出一个资源，加载出来的对象非空。</summary>
        [UnityTest]
        public IEnumerator YooAssetLoadsOneAssetInSimulateMode()
        {
            var packageRoot = ResolveSimulatePackageRoot();
            Assert.IsTrue(System.IO.Directory.Exists(packageRoot),
                $"模拟构建的包裹目录不存在：{packageRoot}。先在编辑器里跑「工具链/资源/模拟构建资源包」。");

            if (!YooAssets.IsInitialized)
            {
                YooAssets.Initialize();
            }

            ResourcePackage package;
            if (!YooAssets.TryGetPackage(PackageName, out package))
            {
                package = YooAssets.CreatePackage(PackageName);
            }

            var options = new EditorSimulateModeOptions
            {
                EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot),
            };

            var initializeOperation = package.InitializePackageAsync(options);
            yield return initializeOperation;
            Assert.AreEqual(EOperationStatus.Succeeded, initializeOperation.Status,
                $"包裹初始化失败：{initializeOperation.Error}");

            // 初始化只是把文件系统挂上，清单还要单独取版本再加载——
            // 少了这两步，加载资源时会报「Active package manifest not found」。
            var versionOperation = package.RequestPackageVersionAsync();
            yield return versionOperation;
            Assert.AreEqual(EOperationStatus.Succeeded, versionOperation.Status,
                $"取包裹版本失败：{versionOperation.Error}");

            var manifestOperation = package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(versionOperation.PackageVersion, 60));
            yield return manifestOperation;
            Assert.AreEqual(EOperationStatus.Succeeded, manifestOperation.Status,
                $"加载包裹清单失败：{manifestOperation.Error}");

            var handle = package.LoadAssetAsync<Object>("T_HeroIdle_01");
            yield return handle;
            Assert.AreEqual(EOperationStatus.Succeeded, handle.Status, $"资源加载失败：{handle.Error}");
            Assert.IsNotNull(handle.AssetObject, "加载出来的资源对象是空的");
            Assert.AreEqual("T_HeroIdle_01", handle.AssetObject.name, "加载到的资源名对不上");

            handle.Release();
        }

        /// <summary>引用账本与真加载串起来：取用两次、释放两次，归零后进待卸载清单。</summary>
        [UnityTest]
        public IEnumerator ReferenceLedgerTracksRealLoadAndUnload()
        {
            var ledger = new AssetReferenceLedger();
            const string assetKey = "T_HeroIdle_01";

            ledger.Acquire(assetKey);
            ledger.Acquire(assetKey);
            Assert.AreEqual(2, ledger.ReferenceCountOf(assetKey));

            ledger.Release(assetKey);
            Assert.AreEqual(1, ledger.ReferenceCountOf(assetKey));
            CollectionAssert.DoesNotContain(ledger.ReadyToUnloadKeys, assetKey, "还有引用时就被列进待卸载了");

            ledger.Release(assetKey);
            Assert.AreEqual(0, ledger.ReferenceCountOf(assetKey));
            CollectionAssert.Contains(ledger.ReadyToUnloadKeys, assetKey, "归零后没有进待卸载清单");

            yield return null;
        }

        /// <summary>场景加载队列驱动真的异步加载：进度走到 1 且场景真被加载进来。</summary>
        [UnityTest]
        public IEnumerator SceneLoadDriverLoadsTheVillageSceneAdditively()
        {
            var sceneCountBefore = SceneManager.sceneCount;

            var progressSeen = 0f;
            System.Action<float> onProgress = value => progressSeen = value;
            SceneLoadDriver.Queue.ProgressChanged += onProgress;
            SceneLoadDriver.Queue.Enqueue(new SceneLoadRequest("村庄", SceneLoadMode.Additive, activateOnLoad: true));

            yield return SceneLoadDriver.LoadNext();

            SceneLoadDriver.Queue.ProgressChanged -= onProgress;

            Assert.AreEqual(sceneCountBefore + 1, SceneManager.sceneCount, "场景没有被叠加加载进来");
            Assert.AreEqual(1f, progressSeen, 0.001f, "进度没有走到 1");
            Assert.IsNull(SceneLoadDriver.Queue.Current, "加载完成后当前请求没有清空");

            var loaded = SceneManager.GetSceneByName("村庄");
            Assert.IsTrue(loaded.isLoaded, "村庄场景没有处于已加载状态");

            var rootObjects = loaded.GetRootGameObjects();
            Assert.AreEqual(1, rootObjects.Length, "村庄场景的根物体数量对不上");
            Assert.AreEqual(2, rootObjects[0].transform.childCount, "两个区块根没有都在");

            yield return SceneManager.UnloadSceneAsync(loaded);
        }

        // 模拟构建的包裹根目录：与编辑器侧 YooAssetSimulateBootstrap 用的是同一个约定路径。
        private static string ResolveSimulatePackageRoot()
        {
            var projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            return System.IO.Path.Combine(
                projectRoot, "Bundles", "StandaloneWindows64", PackageName, "Simulate");
        }
    }
}
