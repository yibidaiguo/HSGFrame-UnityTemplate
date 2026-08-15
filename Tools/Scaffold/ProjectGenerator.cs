using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Template.Toolkit.Scaffold
{
    /// <summary>把模板树复制成新项目并改写项目标识的生成器。</summary>
    public static class ProjectGenerator
    {
        private static readonly Regex ProjectNamePattern = new Regex("^[A-Za-z][A-Za-z0-9_.]*$", RegexOptions.Compiled);

        private static readonly Regex PackagePrefixPattern = new Regex("^[a-z0-9]+(\\.[a-z0-9]+)*\\.$", RegexOptions.Compiled);

        // 这些目录是编译 / 引擎 / 版本控制的生成物，复制进新项目只会带过去一堆垃圾。
        // HybridCLRData 尤其要跳：它是 800 MB 的本地 il2cpp 数据，且与编辑器版本绑定，
        // 复制过去既让生成变慢几十倍，又会给新项目一份可能过期的环境——环境该由新项目自己装。
        private static readonly string[] SkipSegments =
        {
            "bin", "obj", "Logs", "Build", ".git", "Library", "Temp", "MemoryCaptures", "HybridCLRData", "Bundles"
        };

        // 凡是可能写着包名或模板标识名的文本格式都要列进来。
        // 漏掉 .scriban 会让生成出来的代码引用错命名空间（模板里写着 using <模板名>.UiFramework）。
        private static readonly string[] TextExtensions =
        {
            ".json", ".asmdef", ".md", ".csproj", ".cs", ".ps1",
            ".scriban", ".toml", ".sln", ".uss", ".uxml", ".txt",
            ".prefab", ".unity", ".asset", ".xml", ".props", ".targets"
        };

        // 扩展名认不出来的文本文件按文件名前缀认。Jenkinsfile.秒级门禁 这类文件的「扩展名」
        // 是中文流水线名，按扩展名匹配永远认不出来，于是里面的解决方案名与入口方法全名
        // 会原样留在新项目里——上一轮就是这么漏掉四条流水线定义的。
        private static readonly string[] TextFileNamePrefixes = { "Jenkinsfile" };

        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        private const string TemplateDirectoryPrefix = "com.gametemplateforagent.";

        // 模板自身的标识名。生成新项目时连同它一起换掉，否则新项目的程序集、命名空间、
        // 文档标题会一直顶着模板的名字——菜单叫「RPG模板工具」那类问题就是这么来的。
        private const string TemplateIdentifierName = "GameTemplateForAgent";

        // 模板解决方案文件名。它是 Template. 开头但后面接小写，命名空间正则刻意不匹配它，
        // 单独一条规则改名，免得把 Scriban 的 Template.Parse 之类一起误伤。
        private const string TemplateSolutionFileName = "Template.sln";

        // 判据：Template 前面不能紧挨标识符字符或点（挡掉 Scriban.Template.Parse），
        // 后面必须是点加一个大写字母（挡掉 Template.sln、Templates 目录）。
        private static readonly Regex TemplateNamespacePattern = new Regex(
            @"(?<![A-Za-z0-9_.])Template\.(?=[A-Z])",
            RegexOptions.Compiled);

        // 模板说明文件缺失时退回这份内置文案，保证 CLAUDE.md 追加永不静默丢功能。
        private const string FallbackTemplateNotice = @"## 本项目由通用 Unity 模板生成

- 项目名：{{项目名}}
- UPM 包前缀：{{包前缀}}
- 命名空间：`{{项目名}}.*`（生成时已由 project.create 从模板的 `Template.*` 整体替换）

跑门禁：`./{{项目名}}/Tools/Gates/gate.ps1`
";

        /// <summary>
        /// 按参数把模板树复制成一个新项目，改写 UPM 包前缀与项目自述文件。
        /// </summary>
        /// <param name="options">生成参数。</param>
        public static ProjectCreationResult Create(ProjectCreationOptions options)
        {
            if (options == null)
            {
                return ProjectCreationResult.Failure("参数不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.TemplateRoot) || !Directory.Exists(options.TemplateRoot))
            {
                return ProjectCreationResult.Failure("TemplateRoot 不存在");
            }

            if (string.IsNullOrWhiteSpace(options.ProjectName) || !ProjectNamePattern.IsMatch(options.ProjectName))
            {
                return ProjectCreationResult.Failure("ProjectName 需匹配 ^[A-Za-z][A-Za-z0-9_.]*$");
            }

            if (string.IsNullOrWhiteSpace(options.PackagePrefix) || !PackagePrefixPattern.IsMatch(options.PackagePrefix))
            {
                return ProjectCreationResult.Failure("PackagePrefix 需匹配 ^[a-z0-9]+(\\.[a-z0-9]+)*\\.$");
            }

            if (string.IsNullOrWhiteSpace(options.TargetDirectory))
            {
                return ProjectCreationResult.Failure("TargetDirectory 不能为空");
            }

            var targetPath = Path.Combine(options.TargetDirectory, options.ProjectName);

            // 目标目录已有内容就拒绝，避免把别人正在写的工程整个覆盖掉。
            if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
            {
                return ProjectCreationResult.Failure("目标目录已有内容");
            }

            var copiedFileCount = 0;
            // 源模板自己的目录名（本仓库里是 RebuiltRPG）也要换掉：它是「上一个宿主」的名字，
            // 留在配置与文档里就成了新项目身上的一处旧身份。
            var sourceIdentifierName = new DirectoryInfo(Path.GetFullPath(options.TemplateRoot)).Name;
            CopyTree(options.TemplateRoot, targetPath, options.PackagePrefix, options.ProjectName, sourceIdentifierName, ref copiedFileCount);

            RewriteGateWhitelist(targetPath, options.ProjectName);
            WriteHostGateConfiguration(targetPath, options.ProjectName);
            AppendTemplateNotice(targetPath, options.TemplateRoot, options.ProjectName, options.PackagePrefix);
            RebuildTestBaseline(targetPath);

            return ProjectCreationResult.Success(targetPath, copiedFileCount, $"已生成新项目到 {targetPath}");
        }

        private static void CopyTree(string sourceRoot, string targetRoot, string packagePrefix, string projectName, string sourceIdentifierName, ref int copiedFileCount)
        {
            Directory.CreateDirectory(targetRoot);

            foreach (var entry in Directory.EnumerateFileSystemEntries(sourceRoot))
            {
                var name = Path.GetFileName(entry);
                if (ShouldSkipSegment(name))
                {
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    var targetDirectoryName = RenameDirectory(name, packagePrefix, projectName);
                    CopyTree(entry, Path.Combine(targetRoot, targetDirectoryName), packagePrefix, projectName, sourceIdentifierName, ref copiedFileCount);
                }
                else
                {
                    CopyFile(entry, Path.Combine(targetRoot, RenameDirectory(name, packagePrefix, projectName)), packagePrefix, projectName, sourceIdentifierName);
                    copiedFileCount++;
                }
            }
        }

        private static void CopyFile(string sourcePath, string targetPath, string packagePrefix, string projectName, string sourceIdentifierName)
        {
            if (IsTextFile(sourcePath))
            {
                RewriteTextFile(sourcePath, targetPath, packagePrefix, projectName, sourceIdentifierName);
            }
            else
            {
                // 二进制文件原样复制，避免按文本读写损坏内容。
                File.Copy(sourcePath, targetPath, overwrite: false);
            }
        }

        // 只改 com.gametemplateforagent. 开头的目录：com.gametemplateforagent.save + com.example. → com.example.save。
        private static string RenameDirectory(string directoryName, string packagePrefix, string projectName)
        {
            if (directoryName.StartsWith(TemplateDirectoryPrefix, StringComparison.Ordinal))
            {
                return packagePrefix + directoryName.Substring(TemplateDirectoryPrefix.Length);
            }

            // 解决方案文件名单独一条：Template.sln → <项目名>.sln。
            if (string.Equals(directoryName, TemplateSolutionFileName, StringComparison.Ordinal))
            {
                return projectName + ".sln";
            }

            // 文件名里也带根命名空间（Template.Hotfix.Analyzer.dll 与它的 .meta）。
            var renamed = TemplateNamespacePattern.Replace(directoryName, projectName + ".");
            return renamed.Replace(TemplateIdentifierName, projectName, StringComparison.Ordinal);
        }

        private static void RewriteTextFile(string sourcePath, string targetPath, string packagePrefix, string projectName, string sourceIdentifierName)
        {
            var bytes = File.ReadAllBytes(sourcePath);
            var hasBom = HasUtf8Bom(bytes);
            var text = hasBom
                ? Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length)
                : Encoding.UTF8.GetString(bytes);

            text = text
                .Replace(TemplateDirectoryPrefix, packagePrefix, StringComparison.Ordinal)
                .Replace(TemplateIdentifierName, projectName, StringComparison.Ordinal);

            // 解决方案文件名要先换：它是 Template. 开头但后面小写，命名空间正则不管它。
            text = text.Replace(TemplateSolutionFileName, projectName + ".sln", StringComparison.Ordinal);
            text = TemplateNamespacePattern.Replace(text, projectName + ".");

            // 源模板目录名与项目名相同时跳过，免得把刚换好的名字又替一遍。
            if (!string.IsNullOrEmpty(sourceIdentifierName)
                && !string.Equals(sourceIdentifierName, projectName, StringComparison.Ordinal))
            {
                text = text.Replace(sourceIdentifierName, projectName, StringComparison.Ordinal);
            }

            WriteUtf8(targetPath, text, hasBom);
        }

        // 生成时改写了测试文件的内容（命名空间跟着项目名换），模板那份基线的哈希就对不上了。
        // 不在这里重建，新项目第一次跑门禁必然红——而阶段 14 的验收正是「新项目里门禁全绿」。
        private static void RebuildTestBaseline(string targetRoot)
        {
            var configurationPath = Path.Combine(targetRoot, "Tools", "Gates", "Config", "gate-config.json");
            if (!File.Exists(configurationPath))
            {
                return;
            }

            var configuration = Template.Toolkit.Gates.GateConfiguration.LoadFromFile(configurationPath);
            var baselinePath = Path.Combine(targetRoot, "Tools", "Gates", "Config", "test-baseline.json");
            Template.Toolkit.Gates.TestBaselineLock.WriteBaseline(targetRoot, configuration, baselinePath);
        }

        // 把 changedPathWhitelist 第一项从 Template/ 换成 <ProjectName>/，让门禁认新项目根。
        // 白名单是宿主专属配置，正常住在 gate-config.host.json 里；老布局把它写在 gate-config.json 里，
        // 所以两个文件都试一遍，哪个有这一项就改哪个。
        private static void RewriteGateWhitelist(string targetRoot, string projectName)
        {
            var configDirectory = Path.Combine(targetRoot, "Tools", "Gates", "Config");
            foreach (var fileName in new[] { "gate-config.host.json", "gate-config.json" })
            {
                RewriteGateWhitelistInFile(Path.Combine(configDirectory, fileName), projectName);
            }
        }

        private static void RewriteGateWhitelistInFile(string configPath, string projectName)
        {
            if (!File.Exists(configPath))
            {
                return;
            }

            var bytes = File.ReadAllBytes(configPath);
            var hasBom = HasUtf8Bom(bytes);
            var text = hasBom
                ? Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length)
                : Encoding.UTF8.GetString(bytes);

            var marker = "\"changedPathWhitelist\": [";
            var markerIndex = text.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var firstItemIndex = text.IndexOf("\"Template/\"", markerIndex, StringComparison.Ordinal);
                if (firstItemIndex >= 0)
                {
                    text = text.Remove(firstItemIndex + 1, "Template/".Length).Insert(firstItemIndex + 1, projectName + "/");
                }
            }

            WriteUtf8(configPath, text, hasBom);
        }

        // 宿主专属门禁配置整份重写，而不是在复制过来的那份上打补丁。
        // 三项都必须归零或改写：changedPathWhitelist 是来源仓库的目录前缀；
        // editorOwnedPathPrefixes 装着来源仓库那个常驻编辑器工程的名字；
        // genericNameBlacklist 装着来源仓库自己的名字——它会被上面的标识名替换改写成
        // 新项目名，于是新项目的每一个标识符都会被通用性门禁判成违规，整道门禁必红。
        private static void WriteHostGateConfiguration(string targetRoot, string projectName)
        {
            var configDirectory = Path.Combine(targetRoot, "Tools", "Gates", "Config");
            if (!Directory.Exists(configDirectory))
            {
                return;
            }

            var content = "{\n"
                + "  \"_说明\": \"宿主专属配置，只在这一个仓库成立。template.sync 跳过这个文件，同名项覆盖 gate-config.json 里的值。\",\n"
                + "  \"changedPathWhitelist\": [\n"
                + "    \"" + projectName + "/\",\n"
                + "    \"Doc/\"\n"
                + "  ],\n"
                + "  \"editorOwnedPathPrefixes\": [],\n"
                + "  \"genericNameBlacklist\": []\n"
                + "}\n";

            WriteUtf8(Path.Combine(configDirectory, "gate-config.host.json"), content, hasBom: false);
        }

        private static void AppendTemplateNotice(string targetRoot, string templateRoot, string projectName, string packagePrefix)
        {
            var templatePath = Path.Combine(templateRoot, "Tools", "Scaffold", "Templates", "新项目说明.md");
            var template = File.Exists(templatePath) ? ReadUtf8Text(templatePath) : FallbackTemplateNotice;

            var notice = template
                .Replace("{{项目名}}", projectName, StringComparison.Ordinal)
                .Replace("{{包前缀}}", packagePrefix, StringComparison.Ordinal);

            AppendUtf8Text(Path.Combine(targetRoot, "CLAUDE.md"), notice);
        }

        private static void AppendUtf8Text(string path, string content)
        {
            if (!File.Exists(path))
            {
                WriteUtf8(path, content, hasBom: false);
                return;
            }

            var bytes = File.ReadAllBytes(path);
            var hasBom = HasUtf8Bom(bytes);
            var existing = hasBom
                ? Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length)
                : Encoding.UTF8.GetString(bytes);

            var combined = existing.TrimEnd() + "\n\n" + content;
            WriteUtf8(path, combined, hasBom);
        }

        private static string ReadUtf8Text(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (HasUtf8Bom(bytes))
            {
                return Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length);
            }

            return Encoding.UTF8.GetString(bytes);
        }

        // 写回时保留原文件是否带 BOM：.ps1 掉了 BOM 会让 Windows PowerShell 5.1 读错中文。
        private static void WriteUtf8(string path, string text, bool hasBom)
        {
            var content = Encoding.UTF8.GetBytes(text);
            File.WriteAllBytes(path, hasBom ? PrependBom(content) : content);
        }

        private static byte[] PrependBom(byte[] content)
        {
            var result = new byte[Utf8Bom.Length + content.Length];
            Array.Copy(Utf8Bom, result, Utf8Bom.Length);
            Array.Copy(content, 0, result, Utf8Bom.Length, content.Length);
            return result;
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= Utf8Bom.Length
                && bytes[0] == Utf8Bom[0]
                && bytes[1] == Utf8Bom[1]
                && bytes[2] == Utf8Bom[2];
        }

        private static bool IsTextFile(string path)
        {
            var extension = Path.GetExtension(path);
            if (TextExtensions.Any(entry => string.Equals(entry, extension, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var fileName = Path.GetFileName(path);
            return TextFileNamePrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ShouldSkipSegment(string name)
        {
            return SkipSegments.Any(segment => string.Equals(segment, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
