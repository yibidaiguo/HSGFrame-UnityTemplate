using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Template.Toolkit.Editor
{
    /// <summary>
    /// 模块包清点：扫 <c>Packages/</c> 下的包目录，对上 manifest.json 的 dependencies，
    /// 得出每个包「在不在盘上、装没装」的现状。
    /// 认包不靠名字前缀而靠位置——<c>Packages/</c> 下有 package.json 的目录就是一个模块包，
    /// 清单里值以 <c>file:../../Packages/</c> 开头的条目就是指向它们的那一条。
    /// 前缀会随框架改名而变，位置不会。
    /// </summary>
    public static class ModulePackageCatalog
    {
        /// <summary>模块包目录相对模板根的位置。</summary>
        public const string PackagesDirectoryName = "Packages";

        /// <summary>清单相对模板根的位置。</summary>
        public const string ManifestRelativePath = "UnityProject/Packages/manifest.json";

        /// <summary>清单里指向本地模块包的值前缀，相对 UnityProject/Packages/ 而言。</summary>
        private const string LocalPackagePrefix = "file:../../Packages/";

        /// <summary>
        /// 清点一遍，返回未安装的排在前、其余按包名排的列表。
        /// </summary>
        /// <param name="templateRoot">模板根目录，即 Tools/Gates/Config/gate-config.json 所在的那一级。</param>
        public static IReadOnlyList<ModulePackageInfo> Scan(string templateRoot)
        {
            var packages = new List<ModulePackageInfo>();
            if (string.IsNullOrWhiteSpace(templateRoot))
            {
                return packages;
            }

            var manifestPath = Path.Combine(templateRoot, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var dependencies = ModulePackageManifest.ReadDependencies(manifestPath);
            var packagesDirectory = Path.Combine(templateRoot, PackagesDirectoryName);

            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            if (Directory.Exists(packagesDirectory))
            {
                foreach (var directory in Directory.EnumerateDirectories(packagesDirectory))
                {
                    var descriptorPath = Path.Combine(directory, "package.json");
                    if (!File.Exists(descriptorPath))
                    {
                        continue;
                    }

                    var directoryName = Path.GetFileName(directory);
                    var package = ReadDescriptor(descriptorPath, directoryName);
                    package.IsOnDisk = true;
                    package.DirectoryRelativePath = PackagesDirectoryName + "/" + directoryName;
                    package.InstallExpression = LocalPackagePrefix + directoryName;
                    package.IsInstalled = dependencies.ContainsKey(package.PackageName);
                    packages.Add(package);
                    seenNames.Add(package.PackageName);
                }
            }

            // 清单里写着、盘上却没有的本地包：feature.remove 摘掉包目录之后留下的悬空条目，
            // 或者别人的分支删了包而清单没跟上。它不该被静默忽略，面板要能看见并摘掉。
            foreach (var entry in dependencies)
            {
                if (seenNames.Contains(entry.Key) || !entry.Value.StartsWith(LocalPackagePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                packages.Add(new ModulePackageInfo
                {
                    PackageName = entry.Key,
                    DisplayName = entry.Key,
                    Description = string.Empty,
                    Version = string.Empty,
                    IsOnDisk = false,
                    IsInstalled = true,
                    InstallExpression = entry.Value,
                    Dependencies = Array.Empty<ModulePackageDependency>(),
                });
            }

            packages.Sort(CompareForDisplay);
            return packages;
        }

        /// <summary>
        /// 读一份 package.json，读不动时退回一个只有包名的条目——一个包的 json 坏了不该让整个面板空掉。
        /// </summary>
        /// <param name="descriptorPath">package.json 的完整路径。</param>
        /// <param name="fallbackName">读不出 name 时用的名字，一般是包目录名。</param>
        public static ModulePackageInfo ReadDescriptor(string descriptorPath, string fallbackName)
        {
            var package = new ModulePackageInfo
            {
                PackageName = fallbackName,
                DisplayName = fallbackName,
                Description = string.Empty,
                Version = string.Empty,
                Dependencies = Array.Empty<ModulePackageDependency>(),
            };

            string text;
            try
            {
                text = File.ReadAllText(descriptorPath);
            }
            catch (IOException)
            {
                return package;
            }

            var dependencies = new List<ModulePackageDependency>();
            ParseDescriptor(text, package, dependencies);

            if (string.IsNullOrWhiteSpace(package.PackageName))
            {
                package.PackageName = fallbackName;
            }

            if (string.IsNullOrWhiteSpace(package.DisplayName))
            {
                package.DisplayName = package.PackageName;
            }

            package.Dependencies = dependencies;
            return package;
        }

        private static int CompareForDisplay(ModulePackageInfo left, ModulePackageInfo right)
        {
            // 没装的排最前：面板存在的理由就是「装回来」，要装的东西不该让人往下滚才看得见。
            if (left.IsInstalled != right.IsInstalled)
            {
                return left.IsInstalled ? 1 : -1;
            }

            return string.Compare(left.PackageName, right.PackageName, StringComparison.Ordinal);
        }

        // package.json 里 author 也有一个 name 键，按行 grep 会把它当成包名，
        // 所以这里老老实实按层级扫：只认最外层对象的键，dependencies 只认它下面那一层。
        private static void ParseDescriptor(
            string text, ModulePackageInfo package, List<ModulePackageDependency> dependencies)
        {
            var index = 0;
            SkipWhitespace(text, ref index);
            if (index >= text.Length || text[index] != '{')
            {
                return;
            }

            index++;
            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] == '}')
                {
                    return;
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] != '"')
                {
                    return;
                }

                var key = ReadString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                {
                    return;
                }

                index++;
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    return;
                }

                if (string.Equals(key, "dependencies", StringComparison.Ordinal) && text[index] == '{')
                {
                    ReadDependencyPairs(text, ref index, dependencies);
                    continue;
                }

                if (text[index] == '"')
                {
                    AssignField(package, key, ReadString(text, ref index));
                    continue;
                }

                SkipValue(text, ref index);
            }
        }

        private static void AssignField(ModulePackageInfo package, string key, string value)
        {
            switch (key)
            {
                case "name":
                    package.PackageName = value;
                    break;
                case "displayName":
                    package.DisplayName = value;
                    break;
                case "description":
                    package.Description = value;
                    break;
                case "version":
                    package.Version = value;
                    break;
            }
        }

        private static void ReadDependencyPairs(string text, ref int index, List<ModulePackageDependency> dependencies)
        {
            index++;
            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    return;
                }

                if (text[index] == '}')
                {
                    index++;
                    return;
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] != '"')
                {
                    return;
                }

                var key = ReadString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                {
                    return;
                }

                index++;
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    return;
                }

                if (text[index] == '"')
                {
                    dependencies.Add(new ModulePackageDependency
                    {
                        PackageName = key,
                        VersionExpression = ReadString(text, ref index),
                    });
                    continue;
                }

                SkipValue(text, ref index);
            }
        }

        private static void SkipValue(string text, ref int index)
        {
            if (index >= text.Length)
            {
                return;
            }

            var character = text[index];
            if (character == '"')
            {
                ReadString(text, ref index);
                return;
            }

            if (character == '{' || character == '[')
            {
                var closing = character == '{' ? '}' : ']';
                var depth = 0;
                while (index < text.Length)
                {
                    var current = text[index];
                    if (current == '"')
                    {
                        ReadString(text, ref index);
                        continue;
                    }

                    if (current == character)
                    {
                        depth++;
                    }
                    else if (current == closing)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            index++;
                            return;
                        }
                    }

                    index++;
                }

                return;
            }

            while (index < text.Length && text[index] != ',' && text[index] != '}' && text[index] != ']')
            {
                index++;
            }
        }

        private static string ReadString(string text, ref int index)
        {
            var builder = new StringBuilder();
            index++;
            while (index < text.Length)
            {
                var character = text[index];
                if (character == '\\' && index + 1 < text.Length)
                {
                    index++;
                    builder.Append(ReadEscape(text[index]));
                    index++;
                    continue;
                }

                if (character == '"')
                {
                    index++;
                    return builder.ToString();
                }

                builder.Append(character);
                index++;
            }

            return builder.ToString();
        }

        private static char ReadEscape(char escaped)
        {
            switch (escaped)
            {
                case 'n':
                    return '\n';
                case 't':
                    return '\t';
                case 'r':
                    return '\r';
                default:
                    return escaped;
            }
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }
    }
}
