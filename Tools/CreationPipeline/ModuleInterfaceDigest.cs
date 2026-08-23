using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一个模块抽出来的公开面。</summary>
    /// <param name="ModuleName">模块名。</param>
    /// <param name="Types">公开类型（接口 / 类 / 记录 / 枚举）及其一句话摘要。</param>
    /// <param name="Members">公开成员（方法 / 事件 / 属性）的签名。</param>
    public sealed record ModuleInterface(
        string ModuleName, IReadOnlyList<string> Types, IReadOnlyList<string> Members);

    /// <summary>
    /// 从模块代码里抽出**公开面**，给助手当「这个项目已经做了什么」的依据。
    ///
    /// 为什么必须有：助手今天只拿到一份模块名清单（八行字），
    /// 于是人问「背包系统写了没」，它只能回「有个 Inventory 模块，但我看不到代码」——
    /// **有清单没内容，等于知道有这个抽屉却不知道里面装了什么**。
    /// 而它的活正是「顺着既有实现聊需求，别重复建已经有的东西」。
    ///
    /// 抽的是公开面不是整份代码，两个理由：
    /// ① 整份代码进知识包，token 成本按项目大小线性涨，而助手九成时候只需要知道
    ///    「有哪些能力、叫什么名字」；② 私有实现改来改去，公开面才是契约——
    ///    拿实现细节喂它，反而会让它把临时写法当成设计。
    ///
    /// **靠正则抽，不做语法分析**：这里要的是一份给人和模型看的摘要，
    /// 不是编译器。抽漏一两个签名只是摘要少一行；上一套语法分析器却要跟着 C# 版本跑。
    /// 抽不出来时如实留空，不编。
    /// </summary>
    public static class ModuleInterfaceDigest
    {
        /// <summary>公开类型：`public interface IFoo` / `public sealed class Bar` / `public enum Baz`。</summary>
        private static readonly Regex TypePattern = new Regex(
            @"^\s*public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*(interface|class|record|struct|enum)\s+(\w+)",
            RegexOptions.Compiled);

        /// <summary>公开成员：方法、事件、属性。故意不认字段——字段不该是公开契约。</summary>
        private static readonly Regex MemberPattern = new Regex(
            @"^\s*public\s+(?:static\s+|virtual\s+|override\s+|async\s+|event\s+|readonly\s+)*"
            + @"([\w<>\[\],\.\?]+)\s+(\w+)\s*(\(|\{|=>)",
            RegexOptions.Compiled);

        /// <summary>一个模块最多摆几条成员。再多就不是摘要了，而助手真要细节时该去查代码。</summary>
        private const int MemberLimit = 24;

        /// <summary>模块根目录：UnityProject/Assets/Game/Scripts/Modules。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string ModulesRoot(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "UnityProject", "Assets", "Game", "Scripts", "Modules");
        }

        /// <summary>
        /// 抽全部模块的公开面。模块目录不存在时给空表——那只是这个项目还没有模块。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static IReadOnlyList<ModuleInterface> Collect(string repositoryRoot)
        {
            var modules = new List<ModuleInterface>();
            var root = ModulesRoot(repositoryRoot);
            if (!Directory.Exists(root))
            {
                return modules;
            }

            var directories = new List<string>(Directory.GetDirectories(root));
            directories.Sort(StringComparer.Ordinal);

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                modules.Add(CollectOne(name, directory));
            }

            return modules;
        }

        /// <summary>抽一个模块。</summary>
        /// <param name="moduleName">模块名。</param>
        /// <param name="directory">模块目录。</param>
        private static ModuleInterface CollectOne(string moduleName, string directory)
        {
            var types = new List<string>();
            var members = new List<string>();
            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            var seenMembers = new HashSet<string>(StringComparer.Ordinal);

            var files = new List<string>(Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories));
            files.Sort(StringComparer.Ordinal);

            foreach (var filePath in files)
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(filePath);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    // 读不动一个文件只是这份摘要少几行，不该让整次供给失败。
                    continue;
                }

                for (var index = 0; index < lines.Length; index++)
                {
                    var line = lines[index];

                    var typeMatch = TypePattern.Match(line);
                    if (typeMatch.Success)
                    {
                        var entry = typeMatch.Groups[1].Value + " " + typeMatch.Groups[2].Value;
                        var summary = ReadSummaryAbove(lines, index);
                        if (summary.Length > 0)
                        {
                            entry += " —— " + summary;
                        }

                        if (seenTypes.Add(entry))
                        {
                            types.Add(entry);
                        }

                        continue;
                    }

                    if (members.Count >= MemberLimit)
                    {
                        continue;
                    }

                    var memberMatch = MemberPattern.Match(line);
                    if (!memberMatch.Success)
                    {
                        continue;
                    }

                    // 构造函数与属性的 get/set 会误命中，靠「返回类型不等于类型名」滤掉一部分；
                    // 滤不干净也无妨——摘要多一行比少一行强。
                    var signature = line.Trim().TrimEnd('{', '=', '>').Trim();
                    if (seenMembers.Add(signature))
                    {
                        members.Add(signature);
                    }
                }
            }

            return new ModuleInterface(moduleName, types, members);
        }

        /// <summary>
        /// 读一个声明上方的 XML 摘要。仓库的规矩是公开类型必须带中文摘要，
        /// 所以这一句往往正是「这东西是干什么的」——比任何自动生成的描述都准。
        /// </summary>
        /// <param name="lines">整份文件。</param>
        /// <param name="declarationIndex">声明所在行。</param>
        private static string ReadSummaryAbove(string[] lines, int declarationIndex)
        {
            for (var index = declarationIndex - 1; index >= 0 && index >= declarationIndex - 6; index--)
            {
                var line = lines[index].Trim();
                if (line.StartsWith("/// <summary>", StringComparison.Ordinal))
                {
                    var text = line.Substring("/// <summary>".Length).Replace("</summary>", "").Trim();
                    if (text.Length > 0)
                    {
                        return text;
                    }

                    // 摘要写成多行时取下一行。
                    if (index + 1 < lines.Length)
                    {
                        return lines[index + 1].Trim().TrimStart('/').Trim().Replace("</summary>", "").Trim();
                    }
                }

                if (line.Length > 0 && !line.StartsWith("///", StringComparison.Ordinal)
                    && !line.StartsWith("[", StringComparison.Ordinal))
                {
                    break;
                }
            }

            return "";
        }

        /// <summary>
        /// 渲成给助手看的 markdown。
        ///
        /// 顶上写清**这是摘要不是全文**，并告诉它拿不准时该说「我看的是摘要」——
        /// 不写这句的话，它会拿一份不全的清单当成全部事实，
        /// 回一句「项目里没有这个功能」，而其实只是摘要没抽到。
        /// </summary>
        /// <param name="modules">各模块的公开面。</param>
        public static string Render(IReadOnlyList<ModuleInterface> modules)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# 各模块已经实现了什么");
            builder.AppendLine();
            builder.AppendLine("下面是从代码里抽出来的**公开面摘要**（接口、事件、公开方法），不是全部代码。");
            builder.AppendLine("聊需求时先看这里：**已经有的别当成新需求**，顺着既有实现往下谈。");
            builder.AppendLine("拿不准某个细节时，如实说「我看的是接口摘要，具体实现要看代码」——");
            builder.AppendLine("**不许因为摘要里没写就断言项目里没有**。");
            builder.AppendLine();

            if (modules == null || modules.Count == 0)
            {
                builder.AppendLine("这个项目还没有任何模块。");
                return builder.ToString();
            }

            foreach (var module in modules)
            {
                builder.Append("## ").AppendLine(module.ModuleName);
                builder.AppendLine();

                if (module.Types.Count == 0 && module.Members.Count == 0)
                {
                    builder.AppendLine("目录在，但没抽到公开类型——多半还是个空壳。");
                    builder.AppendLine();
                    continue;
                }

                if (module.Types.Count > 0)
                {
                    builder.AppendLine("**公开类型**");
                    foreach (var type in module.Types)
                    {
                        builder.Append("- ").AppendLine(type);
                    }

                    builder.AppendLine();
                }

                if (module.Members.Count > 0)
                {
                    builder.AppendLine("**公开成员**");
                    foreach (var member in module.Members)
                    {
                        builder.Append("- `").Append(member).AppendLine("`");
                    }

                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }
    }
}
