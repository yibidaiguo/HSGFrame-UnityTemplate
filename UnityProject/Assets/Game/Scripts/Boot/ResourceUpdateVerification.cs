using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace Template.Boot
{
    /// <summary>把所有远端文件都指到同一个基地址下的最简实现，本机验收用。</summary>
    public sealed class FlatRemoteService : IRemoteService
    {
        private readonly string _baseUrl;

        /// <summary>基地址形如 http://127.0.0.1:8123/，结尾没有斜杠时自动补上。</summary>
        /// <param name="baseUrl">远端资源的基地址。</param>
        public FlatRemoteService(string baseUrl)
        {
            _baseUrl = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/";
        }

        /// <summary>返回这个文件的唯一候选地址。</summary>
        /// <param name="fileName">请求的文件名。</param>
        public IReadOnlyList<string> GetRemoteUrls(string fileName)
        {
            return new[] { _baseUrl + fileName };
        }
    }

    /// <summary>
    /// 资源热更验收：在真包里以联机模式初始化 YooAsset，从本地服务器取版本、取清单、下载资源包，
    /// 再按地址加载出一个资源。这一条走的是与代码热更并列的另一半链路。
    /// </summary>
    public static class ResourceUpdateVerification
    {
        private const string PackageName = "DefaultPackage";
        // 探针取 ResourceArt/Level 里的一个预制体：收集入口只有 ResourceArt/ 与 Scenes/World/
        //（《结构规范-资源》第二节），Art/ 下的贴图作为依赖进包、没有自己的寻址地址。
        private const string ProbeAssetAddress = "P_Npc";

        /// <summary>按远端基地址跑一遍资源热更，把每一步的结论写进 reportLines。</summary>
        /// <param name="remoteBaseUrl">本地资源服务器的基地址。</param>
        /// <param name="reportLines">结论收集器。</param>
        public static IEnumerator Run(string remoteBaseUrl, List<string> reportLines)
        {
            if (!YooAssets.IsInitialized)
            {
                YooAssets.Initialize();
            }

            if (!YooAssets.TryGetPackage(PackageName, out var package))
            {
                package = YooAssets.CreatePackage(PackageName);
            }

            // 内置文件系统留空：这一条验的是「包里什么资源都没有，全靠从服务器下」这个场景，
            // 挂上内置文件系统反而会去找随包发的首包目录，那个目录本来就不存在。
            var options = new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = null,
                CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(
                    new FlatRemoteService(remoteBaseUrl)),
            };

            var initializeOperation = package.InitializePackageAsync(options);
            yield return initializeOperation;
            if (initializeOperation.Status != EOperationStatus.Succeeded)
            {
                reportLines.Add($"未通过：联机模式初始化失败，{initializeOperation.Error}");
                yield break;
            }

            var versionOperation = package.RequestPackageVersionAsync();
            yield return versionOperation;
            if (versionOperation.Status != EOperationStatus.Succeeded)
            {
                reportLines.Add($"未通过：从 {remoteBaseUrl} 取包裹版本失败，{versionOperation.Error}");
                yield break;
            }

            var packageVersion = versionOperation.PackageVersion;
            reportLines.Add($"远端包裹版本：{packageVersion}");

            var manifestOperation = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(packageVersion, 60));
            yield return manifestOperation;
            if (manifestOperation.Status != EOperationStatus.Succeeded)
            {
                reportLines.Add($"未通过：加载包裹清单失败，{manifestOperation.Error}");
                yield break;
            }

            var downloader = package.CreateResourceDownloader(new ResourceDownloaderOptions(10, 3));
            reportLines.Add($"待下载资源包 {downloader.TotalDownloadCount} 个，合计 {downloader.TotalDownloadBytes} 字节");
            if (downloader.TotalDownloadCount > 0)
            {
                downloader.StartDownload();
                yield return downloader;
                if (downloader.Status != EOperationStatus.Succeeded)
                {
                    reportLines.Add($"未通过：下载资源包失败，{downloader.Error}");
                    yield break;
                }
            }

            var handle = package.LoadAssetAsync<UnityEngine.Object>(ProbeAssetAddress);
            yield return handle;
            if (handle.Status != EOperationStatus.Succeeded || handle.AssetObject == null)
            {
                reportLines.Add($"未通过：按地址 {ProbeAssetAddress} 加载资源失败，{handle.Error}");
                yield break;
            }

            var loadedName = handle.AssetObject.name;

            // 顺带把内容特征读出来：换一版资源之后靠它证明客户端拿到的是新内容而不是缓存里的旧的。
            string contentMark;
            switch (handle.AssetObject)
            {
                case Texture2D texture:
                    contentMark = $"贴图 {texture.width}×{texture.height}";
                    break;
                case GameObject prefab:
                    contentMark = $"预制体 组件 {prefab.GetComponents<Component>().Length} 个";
                    break;
                default:
                    contentMark = handle.AssetObject.GetType().Name;
                    break;
            }

            handle.Release();

            if (loadedName != ProbeAssetAddress)
            {
                reportLines.Add($"未通过：加载到的资源名是 {loadedName}，与地址 {ProbeAssetAddress} 对不上");
                yield break;
            }

            reportLines.Add($"通过：版本 {packageVersion} 的资源包已从 {remoteBaseUrl} 下载并按地址加载出 {loadedName}（{contentMark}）");
        }
    }
}
