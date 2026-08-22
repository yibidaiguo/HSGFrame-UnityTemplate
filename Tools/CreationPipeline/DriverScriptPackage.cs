using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 一个**要装进下游宿主**的脚本包：<c>Bridges/&lt;driver&gt;/scripts/&lt;包名&gt;/</c> 这样一个目录，
    /// 目录里有一份 <c>plugin.json</c> 自述。
    ///
    /// 它与 <c>scripts/</c> 下那些**散落文件**是两回事，这个区分是本类型存在的全部理由：
    /// 散落文件是加工站在调用时以命令行参数现喂进去的（<c>--python &lt;脚本&gt;</c> 那一类），
    /// 从不落进宿主目录，状态恒为「无需安装」；而有些宿主的扩展**必须先拷进它自己的扩展目录
    /// 才会被加载**，那种就有「装没装」这件事，它有磁盘证据、也必须去查。
    ///
    /// 坏包一律走 <see cref="Loaded"/>=false，不抛——照 <see cref="EditorPluginManifest"/>
    /// 的老规矩：「没声明过」与「声明坏了」是两支，判不了的时候正确答案是「未验」，
    /// 不是替它猜一个。
    /// </summary>
    public sealed class DriverScriptPackage
    {
        /// <summary>
        /// 「安装目录」这一格的字段名。**它是通用的，不是哪个 driver 专属**——
        /// 任何想让脚本包落进宿主的 driver，都在自己的 driver.json「配置schema」里加这一格，
        /// 值只进本机的 local.json（决策 5：仓库里永远看不到某台机器的路径）。
        /// </summary>
        public const string InstallRootFieldName = "安装目录";

        /// <summary>包自述的文件名。</summary>
        public const string ManifestFileName = "plugin.json";

        /// <summary>装完之后写在宿主落点里的「回仓库的路」的文件名。</summary>
        public const string LinkFileName = "link.json";

        /// <summary>
        /// 构造一个包。
        /// </summary>
        /// <param name="name">包名，等于目录名。</param>
        /// <param name="sourceDirectory">源目录绝对路径。</param>
        /// <param name="hostRelativePath">宿主落点，相对「安装目录」，正斜杠。</param>
        /// <param name="markerFileName">标志文件名，相对宿主落点。</param>
        /// <param name="description">这个包是干嘛的。</param>
        /// <param name="activationNote">装完怎么才生效的一句提示。</param>
        /// <param name="loaded">自述解析成功没有。</param>
        /// <param name="loadFailureReason">解析失败的原因；正常时为空串。</param>
        public DriverScriptPackage(
            string name,
            string sourceDirectory,
            string hostRelativePath,
            string markerFileName,
            string description,
            string activationNote,
            bool loaded,
            string loadFailureReason)
        {
            Name = name ?? "";
            SourceDirectory = sourceDirectory ?? "";
            HostRelativePath = hostRelativePath ?? "";
            MarkerFileName = markerFileName ?? "";
            Description = description ?? "";
            ActivationNote = activationNote ?? "";
            Loaded = loaded;
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>包名，等于目录名。</summary>
        public string Name { get; }

        /// <summary>源目录绝对路径。</summary>
        public string SourceDirectory { get; }

        /// <summary>宿主落点，相对「安装目录」，正斜杠。</summary>
        public string HostRelativePath { get; }

        /// <summary>标志文件名，相对宿主落点；装没装全看它在不在。</summary>
        public string MarkerFileName { get; }

        /// <summary>这个包是干嘛的。</summary>
        public string Description { get; }

        /// <summary>装完怎么才生效的一句提示（多半是「重启宿主」）。</summary>
        public string ActivationNote { get; }

        /// <summary>自述解析成功没有；false 表示这是个坏包，判不了装没装。</summary>
        public bool Loaded { get; }

        /// <summary>解析失败的原因，写清坏在哪一条；正常时为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>某个 driver 的脚本目录：Bridges/&lt;driver&gt;/scripts。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string ScriptsDirectory(string repositoryRoot, string driverName)
        {
            return Path.Combine(BridgeDriverDescriptor.DriverDirectory(repositoryRoot, driverName), "scripts");
        }

        /// <summary>
        /// 列一个 driver 底下全部的脚本包（即 <c>scripts/</c> 下的子目录），按包名序数序。
        ///
        /// **子目录里没有 plugin.json 时也产出一条**（Loaded=false），不静默跳过：
        /// 让一件东西从清单上人间蒸发，比多列一行「判不了」糟得多。
        /// scripts/ 目录不存在时给空表，不抛。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static IReadOnlyList<DriverScriptPackage> LoadAll(string repositoryRoot, string driverName)
        {
            var scriptsDirectory = ScriptsDirectory(repositoryRoot, driverName);
            if (!Directory.Exists(scriptsDirectory))
            {
                return Array.Empty<DriverScriptPackage>();
            }

            List<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(scriptsDirectory)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Array.Empty<DriverScriptPackage>();
            }

            return directories.Select(LoadOne).ToList();
        }

        /// <summary>
        /// 按包名找一个包；找不到给 null。名字比对走序数，大小写敏感——
        /// 这个名字要拿去拼宿主目录下的路径，宽松匹配会让两个包互相覆盖。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <param name="packageName">包名。</param>
        public static DriverScriptPackage Find(string repositoryRoot, string driverName, string packageName)
        {
            var wanted = (packageName ?? "").Trim();
            return LoadAll(repositoryRoot, driverName)
                .FirstOrDefault(package => string.Equals(package.Name, wanted, StringComparison.Ordinal));
        }

        /// <summary>读一个包目录，坏了也给一条记录（Loaded=false）。</summary>
        /// <param name="packageDirectory">包目录绝对路径。</param>
        private static DriverScriptPackage LoadOne(string packageDirectory)
        {
            var name = Path.GetFileName(packageDirectory) ?? "";
            var manifestFile = Path.Combine(packageDirectory, ManifestFileName);

            if (!File.Exists(manifestFile))
            {
                return Broken(name, packageDirectory, $"目录里没有 {ManifestFileName}，判不了它要不要装进宿主");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(manifestFile));
            }
            catch (JsonException exception)
            {
                return Broken(name, packageDirectory, $"{ManifestFileName} 不是合法 JSON：{exception.Message}");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Broken(name, packageDirectory, $"{ManifestFileName} 读不出来：{exception.Message}");
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return Broken(name, packageDirectory, $"{ManifestFileName} 的顶层不是一个对象");
                }

                var declaredName = ReadStringOrEmpty(root, "名称");
                if (declaredName.Length == 0)
                {
                    return Broken(name, packageDirectory, $"{ManifestFileName} 里「名称」是空的");
                }

                // 名字与目录名必须一字不差：装完之后对账全靠这个名字，两边对不上就会出现
                // 「命令说装了 A、页面找的是 B」这种谁都查不动的错位。
                if (!string.Equals(declaredName, name, StringComparison.Ordinal))
                {
                    return Broken(
                        name,
                        packageDirectory,
                        $"{ManifestFileName} 里的「名称」是「{declaredName}」，与所在目录名「{name}」对不上");
                }

                var hostRelativePath = ReadStringOrEmpty(root, "宿主落点").Replace('\\', '/').Trim();
                var pathFailure = ValidateHostRelativePath(hostRelativePath);
                if (pathFailure.Length > 0)
                {
                    return Broken(name, packageDirectory, pathFailure);
                }

                var markerFileName = ReadStringOrEmpty(root, "标志文件").Trim();
                if (markerFileName.Length == 0)
                {
                    return Broken(name, packageDirectory, $"{ManifestFileName} 里「标志文件」是空的，装没装就没有判据");
                }

                return new DriverScriptPackage(
                    name,
                    packageDirectory,
                    hostRelativePath.TrimEnd('/'),
                    markerFileName,
                    ReadStringOrEmpty(root, "说明"),
                    ReadStringOrEmpty(root, "生效提示"),
                    true,
                    "");
            }
        }

        /// <summary>
        /// 校验「宿主落点」：必须是**相对**路径、不许带 <c>..</c>。
        /// 这个值会被拼到宿主目录上去写文件、也会被删（覆盖装那一路），
        /// 放任绝对路径或 <c>..</c> 等于允许往宿主目录外面动手。
        /// 这是第一道防穿越，安装器里还有第二道（拼完之后再验一次落在不在安装目录之下）。
        /// </summary>
        /// <param name="hostRelativePath">已经把反斜杠换成正斜杠、去过首尾空白的值。</param>
        /// <returns>不合法时给一句原因；合法给空串。</returns>
        private static string ValidateHostRelativePath(string hostRelativePath)
        {
            if (hostRelativePath.Length == 0)
            {
                return $"{ManifestFileName} 里「宿主落点」是空的";
            }

            if (hostRelativePath.StartsWith("/", StringComparison.Ordinal))
            {
                return $"{ManifestFileName} 里「宿主落点」是绝对路径「{hostRelativePath}」，只许写相对宿主安装目录的相对路径";
            }

            if (hostRelativePath.Length >= 2 && hostRelativePath[1] == ':')
            {
                return $"{ManifestFileName} 里「宿主落点」带盘符「{hostRelativePath}」，只许写相对宿主安装目录的相对路径";
            }

            foreach (var segment in hostRelativePath.Split('/'))
            {
                if (string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    return $"{ManifestFileName} 里「宿主落点」带「..」（{hostRelativePath}），那会把文件写到宿主安装目录外面去";
                }
            }

            return "";
        }

        /// <summary>造一条坏包记录。</summary>
        /// <param name="name">包名。</param>
        /// <param name="packageDirectory">包目录。</param>
        /// <param name="reason">坏在哪。</param>
        private static DriverScriptPackage Broken(string name, string packageDirectory, string reason)
        {
            return new DriverScriptPackage(name, packageDirectory, "", "", "", "", false, reason);
        }

        /// <summary>读一个字符串字段；不是字符串或不存在时给空串。</summary>
        /// <param name="element">JSON 对象。</param>
        /// <param name="propertyName">字段名。</param>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        /// <summary>这个包装到某个安装目录之下时，落在哪个目录。</summary>
        /// <param name="installRoot">宿主安装目录。</param>
        public string TargetDirectoryUnder(string installRoot)
        {
            var combined = Path.Combine(installRoot ?? "");
            foreach (var segment in HostRelativePath.Split('/'))
            {
                if (segment.Length > 0)
                {
                    combined = Path.Combine(combined, segment);
                }
            }

            return combined;
        }

        /// <summary>装没装的判据文件：宿主落点下的标志文件。</summary>
        /// <param name="installRoot">宿主安装目录。</param>
        public string MarkerPathUnder(string installRoot)
        {
            return Path.Combine(TargetDirectoryUnder(installRoot), MarkerFileName);
        }
    }
}
