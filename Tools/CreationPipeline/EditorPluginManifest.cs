using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 插件声明清单里的一条：一个**解包进宿主目录**的编辑器插件。
    /// 这类插件（厂商发的 .unitypackage、装进宿主 addons 目录的 zip）装完之后什么声明都不留下——
    /// 包管理器的 manifest 里没有它，磁盘上只多出一堆文件。所以要人先声明「装完之后哪个路径会出现」，
    /// 我们才判得了它装没装；没有声明就没有判据，绝不拿「目录名看着像」冒充检测。
    /// </summary>
    public sealed class EditorPluginEntry
    {
        /// <summary>
        /// 构造一条插件声明。
        /// </summary>
        /// <param name="name">插件名。</param>
        /// <param name="hostName">装进哪个宿主：unity，或 Bridges/ 下的某个目录名。</param>
        /// <param name="markerPath">装完之后会出现的标志路径（仓库相对，文件或目录都行）；空串表示还没填。</param>
        /// <param name="version">版本；没写就是空串。</param>
        /// <param name="source">下载来源。</param>
        /// <param name="installSteps">手工安装步骤，一句话说清点哪里。</param>
        /// <param name="description">这个插件是干嘛的。</param>
        public EditorPluginEntry(
            string name,
            string hostName,
            string markerPath,
            string version,
            string source,
            string installSteps,
            string description)
        {
            Name = name ?? "";
            HostName = hostName ?? "";
            MarkerPath = markerPath ?? "";
            Version = version ?? "";
            Source = source ?? "";
            InstallSteps = installSteps ?? "";
            Description = description ?? "";
        }

        /// <summary>插件名。</summary>
        public string Name { get; }

        /// <summary>装进哪个宿主：unity，或 Bridges/ 下的某个目录名。</summary>
        public string HostName { get; }

        /// <summary>装完之后会出现的标志路径（仓库相对）；空串表示还没填，判不了装没装。</summary>
        public string MarkerPath { get; }

        /// <summary>版本；没写就是空串。</summary>
        public string Version { get; }

        /// <summary>下载来源。</summary>
        public string Source { get; }

        /// <summary>手工安装步骤。</summary>
        public string InstallSteps { get; }

        /// <summary>这个插件是干嘛的。</summary>
        public string Description { get; }
    }

    /// <summary>
    /// 插件声明清单：<c>Tools/CreationPipeline/Config/editor-plugins.json</c>，进 git。
    ///
    /// 它与包管理器的清单分工分明：包管理器认得的包（Unity 的 UPM 条目）不写进这里——
    /// 那边已经有一份真相，这里再写一份就是两份账迟早各说各话。
    /// 这份清单只收**包管理器看不见的**那类：双击解包进 Assets/ 的 .unitypackage、
    /// 手动丢进宿主 addons 目录的插件、照来源页面手装的东西。
    ///
    /// 文件不存在是正常状态（Loaded=true、条目为空）；文件在但解析失败才是坏（Loaded=false）——
    /// 这两支必须分开，「没声明过插件」与「声明清单坏了」不是一回事（决策 42）。
    /// </summary>
    public sealed class EditorPluginManifest
    {
        /// <summary>
        /// 构造一份插件声明清单。
        /// </summary>
        /// <param name="loaded">文件解析成功或文件不存在；false 表示文件坏掉。</param>
        /// <param name="entries">插件条目，按 (宿主, 名称) 序数序。</param>
        /// <param name="loadFailureReason">文件坏掉时的原因；正常时为空串。</param>
        public EditorPluginManifest(bool loaded, IReadOnlyList<EditorPluginEntry> entries, string loadFailureReason)
        {
            Loaded = loaded;
            Entries = entries ?? Array.Empty<EditorPluginEntry>();
            LoadFailureReason = loadFailureReason ?? "";
        }

        /// <summary>文件解析成功或文件不存在；false 表示文件坏掉。</summary>
        public bool Loaded { get; }

        /// <summary>插件条目，按 (宿主, 名称) 序数序。</summary>
        public IReadOnlyList<EditorPluginEntry> Entries { get; }

        /// <summary>文件坏掉时的原因；正常时为空串。</summary>
        public string LoadFailureReason { get; }

        /// <summary>插件声明清单的路径：Tools/CreationPipeline/Config/editor-plugins.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ManifestFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "editor-plugins.json");
        }

        /// <summary>
        /// 读插件声明清单。文件不存在 → Loaded=true、条目为空（正常状态）；
        /// JSON 坏掉或顶层不是对象 → Loaded=false、原因写清坏在哪。不抛。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static EditorPluginManifest Load(string repositoryRoot)
        {
            var filePath = ManifestFile(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return new EditorPluginManifest(true, Array.Empty<EditorPluginEntry>(), "");
            }

            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(filePath)))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return new EditorPluginManifest(false, Array.Empty<EditorPluginEntry>(), "插件声明清单顶层不是对象");
                    }

                    var entries = new List<EditorPluginEntry>();
                    if (root.TryGetProperty("插件", out var listElement) && listElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in listElement.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }

                            entries.Add(new EditorPluginEntry(
                                ReadStringOrEmpty(item, "名称"),
                                ReadStringOrEmpty(item, "宿主"),
                                ReadStringOrEmpty(item, "标志路径"),
                                ReadStringOrEmpty(item, "版本"),
                                ReadStringOrEmpty(item, "来源"),
                                ReadStringOrEmpty(item, "安装步骤"),
                                ReadStringOrEmpty(item, "说明")));
                        }
                    }

                    entries.Sort((left, right) =>
                    {
                        var byHost = string.CompareOrdinal(left.HostName, right.HostName);
                        return byHost != 0 ? byHost : string.CompareOrdinal(left.Name, right.Name);
                    });
                    return new EditorPluginManifest(true, entries, "");
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                return new EditorPluginManifest(false, Array.Empty<EditorPluginEntry>(), "插件声明清单读不出来：" + exception.Message);
            }
        }

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }
    }
}
