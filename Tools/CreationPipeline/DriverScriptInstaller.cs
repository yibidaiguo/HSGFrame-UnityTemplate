using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 把 <c>Bridges/&lt;driver&gt;/scripts/&lt;包名&gt;/</c> 这样一个脚本包装进下游宿主的安装目录。
    ///
    /// 三条纪律：
    /// 1. **安装目录只来自本机配置，永远不猜。**没配就明说没配、指路去填，
    ///    绝不拿「常见路径」试探——猜中了是运气，猜错了是往一个陌生目录里写文件。
    /// 2. **删除只在认出来之后做。**覆盖装要先确认目标目录里躺着的就是本包的 plugin.json；
    ///    认不出来一律拒绝，把判断交回给人。递归删一个没认出来的路径是这条链路上最贵的错。
    /// 3. **软链失败不静默回落成拷贝。**两种模式的语义天差地别——软链改了源码就生效，
    ///    拷贝必须重装。静默回落会让人以为改动生效了，其实一直在跑旧代码。
    /// </summary>
    public static class DriverScriptInstaller
    {
        /// <summary>拷贝时要跳过的目录名：Python 的字节码缓存，装过去没意义还会带上别的机器的路径。</summary>
        private static readonly string[] SkippedDirectoryNames = { "__pycache__" };

        /// <summary>拷贝时要跳过的扩展名。</summary>
        private static readonly string[] SkippedFileExtensions = { ".pyc", ".pyo" };

        /// <summary>
        /// 装一个包。任何一步不过都当场返回失败，且失败文案里必须有「下一步该干什么」。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称，对应 Bridges/&lt;名&gt;/ 目录。</param>
        /// <param name="packageName">包名，对应 scripts/&lt;名&gt;/ 目录。</param>
        /// <param name="useSymlink">true 建目录符号链接，false 递归拷贝。</param>
        /// <param name="force">目标已存在时是否覆盖；覆盖仍要先认出那是本包。</param>
        public static ScriptInstallOutcome Install(
            string repositoryRoot,
            string driverName,
            string packageName,
            bool useSymlink,
            bool force)
        {
            var driver = (driverName ?? "").Trim();
            var wantedPackage = (packageName ?? "").Trim();

            if (driver.Length == 0)
            {
                return ScriptInstallOutcome.Failure("必须指定 driver 名，值取 Bridges/ 下的目录名");
            }

            if (wantedPackage.Length == 0)
            {
                return ScriptInstallOutcome.Failure($"必须指定包名，值取 Bridges/{driver}/scripts/ 下的目录名");
            }

            // 一、driver 自述读得出来吗。
            BridgeDriverDescriptor descriptor;
            try
            {
                descriptor = BridgeDriverDescriptor.Load(repositoryRoot, driver);
            }
            catch (InvalidOperationException exception)
            {
                return ScriptInstallOutcome.Failure($"读不出 driver「{driver}」的自述：{exception.Message}");
            }

            // 二、这个 driver 允许装脚本包吗——判据是它自述里有没有「安装目录」这一格。
            if (!descriptor.ConfigurationFieldNames.Contains(DriverScriptPackage.InstallRootFieldName, StringComparer.Ordinal))
            {
                return ScriptInstallOutcome.Failure(
                    $"driver「{driver}」的自述里没有「{DriverScriptPackage.InstallRootFieldName}」这一格，装不了脚本包。"
                    + $"要装得先在 Bridges/{driver}/driver.json 的「配置schema」里加上它。");
            }

            // 三、本机配了安装目录吗。没配就指路，绝不猜。
            var installRoot = ReadInstallRoot(repositoryRoot, driver);
            if (installRoot.Length == 0)
            {
                return ScriptInstallOutcome.Failure(
                    $"本机还没配 {driver} 的「{DriverScriptPackage.InstallRootFieldName}」，不知道该往哪装。",
                    new[]
                    {
                        $"在面板的 {driver} 卡里填「{DriverScriptPackage.InstallRootFieldName}」，",
                        $"或跑：bridge.config.set --Driver {driver} --Field {DriverScriptPackage.InstallRootFieldName} --Value <宿主根目录>",
                        "这一格要的是**本机磁盘路径**——宿主装在别的机器上时装不了，那台机器得自己装一次。"
                    });
            }

            string fullInstallRoot;
            try
            {
                fullInstallRoot = Path.GetFullPath(installRoot);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                return ScriptInstallOutcome.Failure(
                    $"{driver} 的「{DriverScriptPackage.InstallRootFieldName}」不是一个能解析的路径：{exception.Message}");
            }

            // 四、安装目录必须已经在那儿。**不替人建**——建出来就等于替人认定「宿主装在这」，
            // 而它更可能只是打错了一个字。
            if (!Directory.Exists(fullInstallRoot))
            {
                return ScriptInstallOutcome.Failure(
                    $"{driver} 的「{DriverScriptPackage.InstallRootFieldName}」指向的目录不存在：{fullInstallRoot}",
                    new[] { "这个目录我不会替你建——先确认宿主到底装在哪，再把这一格改对。" });
            }

            // 五、包在不在、坏没坏。
            var package = DriverScriptPackage.Find(repositoryRoot, driver, wantedPackage);
            if (package == null)
            {
                var available = DriverScriptPackage.LoadAll(repositoryRoot, driver).Select(item => item.Name).ToList();
                return ScriptInstallOutcome.Failure(
                    $"Bridges/{driver}/scripts/ 下没有名叫「{wantedPackage}」的包。",
                    new[]
                    {
                        available.Count == 0
                            ? $"这个 driver 底下现在一个脚本包都没有。"
                            : $"现在有的是：{string.Join("、", available)}"
                    });
            }

            if (!package.Loaded)
            {
                return ScriptInstallOutcome.Failure(
                    $"包「{package.Name}」的自述有问题，装不了：{package.LoadFailureReason}",
                    new[] { $"改 Bridges/{driver}/scripts/{package.Name}/{DriverScriptPackage.ManifestFileName}" });
            }

            // 六、算落点，并且**再验一次它确实在安装目录之下**。读包时已经拦过 `..` 与绝对路径，
            // 这是第二道：符号链接、盘符差异这些拼完才看得出来的情况，只有比完整路径才拦得住。
            string targetDirectory;
            try
            {
                targetDirectory = Path.GetFullPath(package.TargetDirectoryUnder(fullInstallRoot));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                return ScriptInstallOutcome.Failure($"落点路径解析不了：{exception.Message}");
            }

            if (!IsUnder(targetDirectory, fullInstallRoot))
            {
                return ScriptInstallOutcome.Failure(
                    $"包「{package.Name}」的落点算出来是 {targetDirectory}，它不在安装目录 {fullInstallRoot} 之下，拒绝安装。");
            }

            // 七、目标已存在时的两支。
            if (Directory.Exists(targetDirectory))
            {
                if (!force)
                {
                    return ScriptInstallOutcome.Failure(
                        $"{targetDirectory} 已经存在。",
                        new[] { "要覆盖装，带 --Force true。覆盖前我仍会先确认那里面装的就是这个包。" });
                }

                var recognizeFailure = RecognizeInstalledPackage(targetDirectory, package.Name);
                if (recognizeFailure.Length > 0)
                {
                    return ScriptInstallOutcome.Failure(
                        $"{targetDirectory} 已经存在，但我认不出那是本包：{recognizeFailure}",
                        new[] { "我不动它——请自己确认那个目录是什么，再决定要不要手工挪开。" });
                }

                try
                {
                    DeleteInstalledDirectory(targetDirectory);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    return ScriptInstallOutcome.Failure(
                        $"删旧的落点失败：{targetDirectory}：{exception.Message}",
                        new[] { "多半是宿主正开着占用了文件——把宿主关掉再装一次。" });
                }
            }

            // 八、装。
            var lines = new List<string>();
            int fileCount;
            try
            {
                if (useSymlink)
                {
                    var parent = Path.GetDirectoryName(targetDirectory);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    Directory.CreateSymbolicLink(targetDirectory, package.SourceDirectory);
                    fileCount = 0;
                    lines.Add($"模式：软链（改了 Bridges/{driver}/scripts/{package.Name}/ 里的源码立刻生效，不用重装）");
                }
                else
                {
                    fileCount = CopyDirectory(package.SourceDirectory, targetDirectory);
                    lines.Add($"模式：拷贝，共 {fileCount} 个文件（**改了源码要重装一次才生效**）");
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                return ScriptInstallOutcome.Failure(
                    useSymlink
                        ? $"建软链被拒：{exception.Message}"
                        : $"拷贝被拒：{exception.Message}",
                    useSymlink
                        ? new[] { "Windows 上建软链要开发者模式或管理员权限。不想开就用默认的拷贝模式（去掉 --Mode 软链）。" }
                        : new[] { "检查一下安装目录的写权限，或者宿主是不是正开着占用文件。" });
            }
            catch (IOException exception)
            {
                return ScriptInstallOutcome.Failure(
                    useSymlink
                        ? $"建软链失败：{exception.Message}"
                        : $"拷贝失败：{exception.Message}",
                    useSymlink
                        ? new[] { "Windows 上建软链要开发者模式或管理员权限。不想开就用默认的拷贝模式（去掉 --Mode 软链）。" }
                        : Array.Empty<string>());
            }

            // 九、写回仓库的路。**这里面只许有仓库根路径**——节点靠它找回本机配置，
            // 密钥、地址一个字都不落在宿主目录里（决策 5）。
            var linkFailure = WriteLinkFile(targetDirectory, repositoryRoot);
            if (linkFailure.Length > 0)
            {
                lines.Add($"提醒：{DriverScriptPackage.LinkFileName} 没写成（{linkFailure}），包本身已经装好，但它可能找不回仓库配置。");
            }

            lines.Add($"落点：{targetDirectory}");
            lines.Add("这一步的产物在仓库外，git diff 看不见；判据是标志文件 "
                + Path.Combine(package.HostRelativePath.Replace('/', Path.DirectorySeparatorChar), package.MarkerFileName)
                + " 在不在。");
            if (package.ActivationNote.Length > 0)
            {
                lines.Add(package.ActivationNote);
            }

            return ScriptInstallOutcome.Success(
                $"包「{package.Name}」已装进 {driver}：{targetDirectory}",
                targetDirectory,
                lines);
        }

        /// <summary>
        /// 读本机配置里某 driver 的「安装目录」。没配、读不出来一律给空串——
        /// 「读不出来」与「配了个空串」在这里是同一个结论：不知道往哪装。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        private static string ReadInstallRoot(string repositoryRoot, string driverName)
        {
            var settings = LocalBridgeSettings.Load(repositoryRoot);
            if (!settings.TryGetDriverConfiguration(driverName, out var configuration)
                || configuration.ValueKind != JsonValueKind.Object)
            {
                return "";
            }

            return configuration.TryGetProperty(DriverScriptPackage.InstallRootFieldName, out var value)
                && value.ValueKind == JsonValueKind.String
                ? (value.GetString() ?? "").Trim()
                : "";
        }

        /// <summary>
        /// 认一认目标目录里装的是不是本包：里面要有 plugin.json，且「名称」与本包同名。
        /// 认不出来时返回一句原因（调用方据此拒绝动手）；认出来了返回空串。
        /// </summary>
        /// <param name="targetDirectory">宿主里的落点。</param>
        /// <param name="expectedName">本包名。</param>
        private static string RecognizeInstalledPackage(string targetDirectory, string expectedName)
        {
            var manifestFile = Path.Combine(targetDirectory, DriverScriptPackage.ManifestFileName);
            if (!File.Exists(manifestFile))
            {
                return $"那个目录里没有 {DriverScriptPackage.ManifestFileName}";
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(manifestFile));
            }
            catch (JsonException exception)
            {
                return $"那个目录里的 {DriverScriptPackage.ManifestFileName} 不是合法 JSON：{exception.Message}";
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return $"那个目录里的 {DriverScriptPackage.ManifestFileName} 读不出来：{exception.Message}";
            }

            using (document)
            {
                var root = document.RootElement;
                var installedName = root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("名称", out var value)
                    && value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? ""
                        : "";

                return string.Equals(installedName, expectedName, StringComparison.Ordinal)
                    ? ""
                    : $"那里面的 {DriverScriptPackage.ManifestFileName} 写的是「{installedName}」，不是「{expectedName}」";
            }
        }

        /// <summary>
        /// 删一个已经认出来的落点。目标本身是符号链接时**只删链接、不碰它指向的源目录**——
        /// 上一次用软链装的，这一次改成拷贝装，源码在仓库里，删错了就是删仓库。
        /// </summary>
        /// <param name="targetDirectory">已经认出来的落点。</param>
        private static void DeleteInstalledDirectory(string targetDirectory)
        {
            var info = new DirectoryInfo(targetDirectory);
            if (info.LinkTarget != null)
            {
                info.Delete();
                return;
            }

            Directory.Delete(targetDirectory, true);
        }

        /// <summary>
        /// 递归拷贝，跳过字节码缓存。返回拷了多少个文件。
        /// </summary>
        /// <param name="sourceDirectory">源目录。</param>
        /// <param name="targetDirectory">目标目录。</param>
        private static int CopyDirectory(string sourceDirectory, string targetDirectory)
        {
            Directory.CreateDirectory(targetDirectory);
            var count = 0;

            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                var extension = Path.GetExtension(file);
                if (SkippedFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)), true);
                count++;
            }

            foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            {
                var name = Path.GetFileName(directory);
                if (SkippedDirectoryNames.Contains(name, StringComparer.Ordinal))
                {
                    continue;
                }

                count += CopyDirectory(directory, Path.Combine(targetDirectory, name));
            }

            return count;
        }

        /// <summary>
        /// 往落点写 link.json：**只有仓库根路径这一项**。
        /// 装好的包靠它回头找本机配置（地址、密钥都留在仓库里那一份 local.json，不复制过来）。
        /// 写失败不算安装失败，只回一句原因——包已经在那儿了，谎报成失败更糟。
        /// </summary>
        /// <param name="targetDirectory">落点。</param>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <returns>失败原因；成功给空串。</returns>
        private static string WriteLinkFile(string targetDirectory, string repositoryRoot)
        {
            try
            {
                var payload = new JsonObject
                {
                    ["契约版本"] = "1.0.0",
                    ["仓库根"] = Path.GetFullPath(repositoryRoot).Replace('\\', '/')
                };

                File.WriteAllText(
                    Path.Combine(targetDirectory, DriverScriptPackage.LinkFileName),
                    payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                return "";
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return exception.Message;
            }
        }

        /// <summary>
        /// 判断 <paramref name="candidate"/> 是不是落在 <paramref name="root"/> 之下。
        /// 两边都补上分隔符再比，否则 <c>C:/A/Bee</c> 会被判成在 <c>C:/A/B</c> 之下。
        /// </summary>
        /// <param name="candidate">完整路径。</param>
        /// <param name="root">根目录完整路径。</param>
        private static bool IsUnder(string candidate, string root)
        {
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
    }
}
