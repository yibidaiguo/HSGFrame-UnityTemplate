namespace HSGFrame.Hotfix
{
    /// <summary>热更启动器：比对远端与本地版本，判定是否需要更新，校验失败时回滚。下载与落盘走 IHotfixStorage，本轮不做真实文件操作。</summary>
    public sealed class HotfixLauncher
    {
        private readonly IHotfixStorage _storage;

        /// <summary>以本地存储构造启动器。</summary>
        public HotfixLauncher(IHotfixStorage storage)
        {
            _storage = storage;
        }

        /// <summary>按远端清单做一次启动：比版本、校验包、写回新版本或回滚。</summary>
        public HotfixLaunchResult Launch(HotfixManifest remoteManifest)
        {
            if (!remoteManifest.TryGetVersion(out var remoteVersion))
            {
                return HotfixLaunchResult.Fail($"远端清单版本号无法解析：{remoteManifest.VersionText}", _storage.ReadInstalledVersionText(), string.Empty);
            }

            var installedText = _storage.ReadInstalledVersionText();

            // 首次安装本地版本号是空字符串，解析失败时按 0.0.0 处理。
            if (!HotfixVersion.TryParse(installedText, out var installedVersion))
            {
                installedVersion = default;
            }

            if (installedVersion >= remoteVersion)
            {
                return HotfixLaunchResult.Success(false, "已是最新", installedText);
            }

            // 逐个包校验。校验失败要保持已装版本不变：热更半途失败时，写了新版本号但包不完整会让下次启动直接崩，比停在旧版本严重得多。
            foreach (var package in remoteManifest.Packages)
            {
                if (!_storage.HasPackage(remoteManifest.VersionText, package.FileName))
                {
                    return HotfixLaunchResult.Fail($"更新失败：包 {package.FileName} 缺失", installedText, installedText);
                }

                if (_storage.ComputePackageHash(remoteManifest.VersionText, package.FileName) != package.ContentHash)
                {
                    return HotfixLaunchResult.Fail($"更新失败：包 {package.FileName} 哈希校验不通过", installedText, installedText);
                }
            }

            _storage.WriteInstalledVersionText(remoteManifest.VersionText);
            return HotfixLaunchResult.Success(true, "更新完成", remoteManifest.VersionText);
        }

        /// <summary>回滚到小于当前已装版本的最大历史版本，并移除当前坏版本。</summary>
        public HotfixLaunchResult Rollback()
        {
            var currentText = _storage.ReadInstalledVersionText();
            if (!HotfixVersion.TryParse(currentText, out var currentVersion))
            {
                return HotfixLaunchResult.Fail("没有可回退的历史版本，需要整包更新", currentText, string.Empty);
            }

            var found = false;
            var bestText = string.Empty;
            var bestVersion = default(HotfixVersion);

            foreach (var versionText in _storage.ListInstalledVersions())
            {
                if (!HotfixVersion.TryParse(versionText, out var candidate))
                {
                    continue;
                }

                if (candidate >= currentVersion)
                {
                    continue;
                }

                if (!found || candidate > bestVersion)
                {
                    bestVersion = candidate;
                    bestText = versionText;
                    found = true;
                }
            }

            if (!found)
            {
                return HotfixLaunchResult.Fail("没有可回退的历史版本，需要整包更新", currentText, string.Empty);
            }

            _storage.WriteInstalledVersionText(bestText);
            _storage.RemoveVersion(currentText);
            return HotfixLaunchResult.Success(false, $"已回退到 {bestText}", bestText, bestText);
        }
    }
}
