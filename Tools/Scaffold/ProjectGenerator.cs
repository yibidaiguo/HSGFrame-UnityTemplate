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
        private static readonly string[] SkipSegments =
        {
            "bin", "obj", "Logs", "Build", ".git", "Library", "Temp", "MemoryCaptures"
        };

        private static readonly string[] TextExtensions =
        {
            ".json", ".asmdef", ".md", ".csproj", ".cs", ".ps1"
        };

        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        private const string TemplateDirectoryPrefix = "com.gametemplateforagent.";

        // 模板说明文件缺失时退回这份内置文案，保证 CLAUDE.md 追加永不静默丢功能。
        private const string FallbackTemplateNotice = @"## 本项目由通用 Unity 模板生成

- 项目名：{{项目名}}
- UPM 包前缀：{{包前缀}}
- 命名空间沿用模板的 `Template.*`，需要改名时做一次全局替换（模板首轮刻意没做这一步）

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
            CopyTree(options.TemplateRoot, targetPath, options.PackagePrefix, ref copiedFileCount);

            RewriteGateWhitelist(targetPath, options.ProjectName);
            AppendTemplateNotice(targetPath, options.TemplateRoot, options.ProjectName, options.PackagePrefix);

            return ProjectCreationResult.Success(targetPath, copiedFileCount, $"已生成新项目到 {targetPath}");
        }

        private static void CopyTree(string sourceRoot, string targetRoot, string packagePrefix, ref int copiedFileCount)
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
                    var targetDirectoryName = RenameDirectory(name, packagePrefix);
                    CopyTree(entry, Path.Combine(targetRoot, targetDirectoryName), packagePrefix, ref copiedFileCount);
                }
                else
                {
                    CopyFile(entry, Path.Combine(targetRoot, name), packagePrefix);
                    copiedFileCount++;
                }
            }
        }

        private static void CopyFile(string sourcePath, string targetPath, string packagePrefix)
        {
            if (IsTextFile(sourcePath))
            {
                RewriteTextFile(sourcePath, targetPath, packagePrefix);
            }
            else
            {
                // 二进制文件原样复制，避免按文本读写损坏内容。
                File.Copy(sourcePath, targetPath, overwrite: false);
            }
        }

        // 只改 com.gametemplateforagent. 开头的目录：com.gametemplateforagent.save + com.example. → com.example.save。
        private static string RenameDirectory(string directoryName, string packagePrefix)
        {
            if (directoryName.StartsWith(TemplateDirectoryPrefix, StringComparison.Ordinal))
            {
                return packagePrefix + directoryName.Substring(TemplateDirectoryPrefix.Length);
            }

            return directoryName;
        }

        private static void RewriteTextFile(string sourcePath, string targetPath, string packagePrefix)
        {
            var bytes = File.ReadAllBytes(sourcePath);
            var hasBom = HasUtf8Bom(bytes);
            var text = hasBom
                ? Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length)
                : Encoding.UTF8.GetString(bytes);

            text = text.Replace(TemplateDirectoryPrefix, packagePrefix, StringComparison.Ordinal);

            WriteUtf8(targetPath, text, hasBom);
        }

        // 把 changedPathWhitelist 第一项从 Template/ 换成 <ProjectName>/，让门禁认新项目根。
        private static void RewriteGateWhitelist(string targetRoot, string projectName)
        {
            var configPath = Path.Combine(targetRoot, "Tools", "Gates", "Config", "gate-config.json");
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
            return TextExtensions.Any(entry => string.Equals(entry, extension, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ShouldSkipSegment(string name)
        {
            return SkipSegments.Any(segment => string.Equals(segment, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
