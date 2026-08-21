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

        // 这些目录是编译 / 引擎 / 版本控制的生成物，复制进新项目只会带过去一堆垃圾。
        // HybridCLRData 尤其要跳：它是 800 MB 的本地 il2cpp 数据，且与编辑器版本绑定，
        // 复制过去既让生成变慢几十倍，又会给新项目一份可能过期的环境——环境该由新项目自己装。
        private static readonly string[] SkipSegments =
        {
            "bin", "obj", "Logs", "Build", ".git", "Library", "Temp", "MemoryCaptures", "HybridCLRData", "Bundles"
        };

        // 凡是可能写着模板身份的文本格式都要列进来。
        // 漏掉 .scriban 会让生成出来的代码引用错命名空间（模板里写着 using <模板身份>.Toolkit.*）。
        private static readonly string[] TextExtensions =
        {
            ".json", ".asmdef", ".md", ".csproj", ".cs", ".ps1",
            ".scriban", ".toml", ".sln", ".uss", ".uxml", ".txt",
            ".prefab", ".unity", ".asset", ".xml", ".props", ".targets"
        };

        // 扩展名认不出来的文本文件按文件名前缀认。Jenkinsfile.fast-gate 这类文件的「扩展名」
        // 是中文流水线名，按扩展名匹配永远认不出来，于是里面的解决方案名与入口方法全名
        // 会原样留在新项目里——上一轮就是这么漏掉四条流水线定义的。
        private static readonly string[] TextFileNamePrefixes = { "Jenkinsfile" };

        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        /// <summary>试验区目录名，与 .gitignore 里那条忽略规则、下划线豁免白名单三处一致。</summary>
        public const string ScratchDirectoryName = "_Scratch";

        /// <summary>试验区说明的模板文件名，住在 Tools/Scaffold/Templates/ 下。</summary>
        public const string ScratchNoticeTemplateName = "scratch-readme.md";

        private static readonly Regex MirrorNamesPattern = new Regex(
            @"\$mirrorNames\s*=\s*@\(([^)]*)\)", RegexOptions.Compiled);

        private static readonly Regex MirrorHeaderPattern = new Regex(
            "\\$mirrorHeader\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled);

        private static readonly Regex QuotedNamePattern = new Regex(@"'([^']+)'", RegexOptions.Compiled);

        /// <summary>
        /// 模板自身的根命名空间。生成新项目时按项目名整体替换掉它。
        /// 公开出来是给测试用的：测试要拿它当「被测的模板身份」，写死字面量的话，
        /// 用本模板生成新项目时那些字面量会被生成器一起改掉，新项目里测试就自我矛盾了。
        /// </summary>
        public const string TemplateRootNamespace = "Template";

        /// <summary>
        /// 模板自身的解决方案文件名。它是根命名空间开头但后面接小写，命名空间正则刻意不匹配它，
        /// 单独一条规则改名，免得把 Scriban 的同名 API 之类一起误伤。
        /// 由根命名空间拼出来而不是写死，理由同上。
        /// </summary>
        public const string TemplateSolutionFileName = TemplateRootNamespace + ".sln";

        // 判据：Template 前面不能紧挨标识符字符或点（挡掉 Scriban.Template.Parse），
        // 后面必须是点加一个大写字母（挡掉 Template.sln、Templates 目录）。
        private static readonly Regex TemplateNamespacePattern = new Regex(
            @"(?<![A-Za-z0-9_.])Template\.(?=[A-Z])",
            RegexOptions.Compiled);

        // 模板说明文件缺失时退回这份内置文案，保证 CLAUDE.md 追加永不静默丢功能。
        private const string FallbackTemplateNotice = @"## 本项目由通用 Unity 模板生成

- 项目名：{{项目名}}
- 命名空间：`{{项目名}}.*`（生成时已由 project.create 从模板的 `Template.*` 整体替换）
- 框架包沿用 `com.hsgframe.*` / `HSGFrame.*`：HSGFrame 是框架自己的名字，不跟项目走

跑门禁：`./{{项目名}}/Tools/Gates/gate.ps1`
";

        /// <summary>
        /// 按参数把模板树复制成一个新项目，改写命名空间与项目自述文件。
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
            // 源模板自己的目录名也要换掉：它是「上一个宿主」的名字，
            // 留在配置与文档里就成了新项目身上的一处旧身份。
            var sourceIdentifierName = new DirectoryInfo(Path.GetFullPath(options.TemplateRoot)).Name;
            CopyTree(options.TemplateRoot, targetPath, options.ProjectName, sourceIdentifierName, ref copiedFileCount);

            RewriteGateWhitelist(targetPath, options.ProjectName);
            WriteHostGateConfiguration(targetPath, options.ProjectName);
            AppendTemplateNotice(targetPath, options.TemplateRoot, options.ProjectName);

            // 入口镜像必须在追加模板说明**之后**再同步：那一步刚改过 CLAUDE.md，
            // 而 AGENTS.md 是照着复制过来的旧内容，不重出一次新项目第一次跑门禁就红在第十道。
            SyncAgentEntryMirrors(targetPath);
            WriteScratchArea(targetPath);
            RebuildTestBaseline(targetPath);

            return ProjectCreationResult.Success(targetPath, copiedFileCount, $"已生成新项目到 {targetPath}");
        }

        private static void CopyTree(string sourceRoot, string targetRoot, string projectName, string sourceIdentifierName, ref int copiedFileCount)
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
                    var targetDirectoryName = RenameDirectory(name, projectName);
                    CopyTree(entry, Path.Combine(targetRoot, targetDirectoryName), projectName, sourceIdentifierName, ref copiedFileCount);
                }
                else
                {
                    CopyFile(entry, Path.Combine(targetRoot, RenameDirectory(name, projectName)), projectName, sourceIdentifierName);
                    copiedFileCount++;
                }
            }
        }

        private static void CopyFile(string sourcePath, string targetPath, string projectName, string sourceIdentifierName)
        {
            if (IsTextFile(sourcePath))
            {
                RewriteTextFile(sourcePath, targetPath, projectName, sourceIdentifierName);
            }
            else
            {
                // 二进制文件原样复制，避免按文本读写损坏内容。
                File.Copy(sourcePath, targetPath, overwrite: false);
            }
        }

        // 目录名与文件名里也带模板身份，按与内容同一套判据改。
        // 框架包（com.hsgframe.* / HSGFrame.*）刻意不在替换范围内：HSGFrame 是框架自己的名字，
        // 地位与 Unity.Mathematics 一样是依赖，不跟宿主项目改名。
        private static string RenameDirectory(string directoryName, string projectName)
        {
            // 解决方案文件名单独一条：Template.sln → <项目名>.sln。
            if (string.Equals(directoryName, TemplateSolutionFileName, StringComparison.Ordinal))
            {
                return projectName + ".sln";
            }

            // 文件名里也带根命名空间（Template.Hotfix.Analyzer.dll 与它的 .meta）。
            return TemplateNamespacePattern.Replace(directoryName, projectName + ".");
        }

        private static void RewriteTextFile(string sourcePath, string targetPath, string projectName, string sourceIdentifierName)
        {
            var bytes = File.ReadAllBytes(sourcePath);
            var hasBom = HasUtf8Bom(bytes);
            var text = hasBom
                ? Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length)
                : Encoding.UTF8.GetString(bytes);

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
        // 替新项目先跑一次 Agent 入口镜像同步（R9 那一道查的东西）。
        // 镜像清单与表头都从新项目自己那份 agent-sync.ps1 里读回来——脚本才是单一事实源，
        // 在这里再抄一份清单，将来往脚本加一个模型入口就会两边分叉。
        private static void SyncAgentEntryMirrors(string targetRoot)
        {
            var scriptPath = Path.Combine(targetRoot, "Tools", "AgentSync", "agent-sync.ps1");
            var sourcePath = Path.Combine(targetRoot, "CLAUDE.md");
            if (!File.Exists(scriptPath) || !File.Exists(sourcePath))
            {
                return;
            }

            var script = ReadUtf8Text(scriptPath);
            var namesMatch = MirrorNamesPattern.Match(script);
            var headerMatch = MirrorHeaderPattern.Match(script);
            if (!namesMatch.Success || !headerMatch.Success)
            {
                return;
            }

            var expectedContent = headerMatch.Groups[1].Value + "\n\n" + ReadUtf8Text(sourcePath);
            foreach (Match nameMatch in QuotedNamePattern.Matches(namesMatch.Groups[1].Value))
            {
                WriteUtf8(Path.Combine(targetRoot, nameMatch.Groups[1].Value), expectedContent, hasBom: false);
            }
        }

        // 铺试验区：目录本身与那份说明。忽略规则已经在随树复制过来的 .gitignore 里，
        // 这里只补说明文件——没有说明的空目录 git 根本留不住，规矩也就随着丢了。
        private static void WriteScratchArea(string targetRoot)
        {
            var noticePath = Path.Combine(targetRoot, "Tools", "Scaffold", "Templates", ScratchNoticeTemplateName);
            if (!File.Exists(noticePath))
            {
                return;
            }

            var scratchDirectory = Path.Combine(targetRoot, ScratchDirectoryName);
            Directory.CreateDirectory(scratchDirectory);
            WriteUtf8(Path.Combine(scratchDirectory, "README.md"), ReadUtf8Text(noticePath), hasBom: false);
        }

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

        private static void AppendTemplateNotice(string targetRoot, string templateRoot, string projectName)
        {
            var templatePath = Path.Combine(templateRoot, "Tools", "Scaffold", "Templates", "new-project-readme.md");
            var template = File.Exists(templatePath) ? ReadUtf8Text(templatePath) : FallbackTemplateNotice;

            var notice = template.Replace("{{项目名}}", projectName, StringComparison.Ordinal);

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
