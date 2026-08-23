using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 模块策划案门禁：这份正本还立不立得住。
    ///
    /// 查四件事，每一件都对应一种「文档还在但已经开始骗人」的死法：
    /// ① 必备键与必填小节缺了 —— 骨架塌了；
    /// ② 生成区被手改 —— 人改的下次重渲染会被吃掉，而他以为自己改生效了；
    /// ③ 配置表声明指不到 schema —— 那张参数表要么渲不出来，要么渲的是别的表；
    /// ④「往后要做成什么样」还是占位符 —— 读的人无法判断某个缺口是没做还是不做。
    /// </summary>
    public static class PlanningDocumentChecker
    {
        /// <summary>占位符：新建骨架时摆的那一行，留着不改就等于这一节没写。</summary>
        private const string PlaceholderLine = "（待补）";

        /// <summary>「往后要做成什么样」那一节的标题；它是唯一一个不许空转的人写小节。</summary>
        private const string FutureSection = "往后要做成什么样";

        /// <summary>
        /// 查池子里全部模块策划案。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="specification">模块策划案规范。</param>
        public static IReadOnlyList<PoolFinding> Check(
            string repositoryRoot, string poolRoot, PlanningDocumentSpec specification)
        {
            var findings = new List<PoolFinding>();
            var root = PoolPaths.ModulePlanRoot(poolRoot);
            if (!Directory.Exists(root))
            {
                // 一份都还没建**不是违规**：项目刚起步时本来就没有。
                return findings;
            }

            var directories = new List<string>(Directory.GetDirectories(root));
            directories.Sort(StringComparer.Ordinal);

            foreach (var directory in directories)
            {
                var moduleName = Path.GetFileName(directory);
                CheckOne(repositoryRoot, poolRoot, moduleName, specification, findings);
            }

            return findings;
        }

        /// <summary>
        /// 查一份模块策划案。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="moduleName">模块名。</param>
        /// <param name="specification">模块策划案规范。</param>
        /// <param name="findings">发现累加到这里。</param>
        public static void CheckOne(
            string repositoryRoot,
            string poolRoot,
            string moduleName,
            PlanningDocumentSpec specification,
            List<PoolFinding> findings)
        {
            var path = PoolPaths.ModulePlanDocument(poolRoot, moduleName);
            var location = Relative(repositoryRoot, path);

            if (!File.Exists(path))
            {
                findings.Add(new PoolFinding(
                    location,
                    $"模块 {moduleName} 有目录却没有 index.md",
                    "跑 plan.render --module " + moduleName + " 建一份，或者把这个空目录删掉",
                    PlanningDocumentSpec.ReferencePath));
                return;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                findings.Add(new PoolFinding(
                    location, "读不动：" + exception.Message, "看一眼这个文件的权限与占用", PlanningDocumentSpec.ReferencePath));
                return;
            }

            var lines = new List<string>(text.Replace("\r\n", "\n").TrimStart('﻿').Split('\n'));

            CheckFrontMatter(lines, location, specification, findings);
            CheckSections(lines, location, specification, findings);
            CheckGeneratedRegion(lines, location, specification, findings);
            CheckConfigTables(repositoryRoot, lines, location, findings);
        }

        private static void CheckFrontMatter(
            IReadOnlyList<string> lines, string location, PlanningDocumentSpec specification, List<PoolFinding> findings)
        {
            if (lines.Count == 0 || lines[0].Trim() != "---")
            {
                findings.Add(new PoolFinding(
                    location, "开头没有 frontmatter", "照参考示例在开头补一段 --- 包起来的 frontmatter",
                    PlanningDocumentSpec.ReferencePath));
                return;
            }

            var present = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 1; index < lines.Count; index++)
            {
                var line = lines[index];
                if (line.Trim() == "---")
                {
                    break;
                }

                var colon = line.IndexOf(':');
                if (colon > 0 && !line.StartsWith(" ", StringComparison.Ordinal))
                {
                    present.Add(line.Substring(0, colon).Trim());
                }
            }

            foreach (var key in specification.FrontMatterRequiredKeys)
            {
                if (!present.Contains(key))
                {
                    findings.Add(new PoolFinding(
                        location, $"frontmatter 缺必备键「{key}」", $"在 frontmatter 里补上「{key}」",
                        PlanningDocumentSpec.ReferencePath));
                }
            }
        }

        private static void CheckSections(
            IReadOnlyList<string> lines, string location, PlanningDocumentSpec specification, List<PoolFinding> findings)
        {
            var titles = new HashSet<string>(StringComparer.Ordinal);
            string currentSection = null;
            var bodyByTitle = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var line in lines)
            {
                // 生成区一开始，人写区就结束了。不在这儿断的话，
                // 最后一个人写小节会把开始标记那一行算进自己的正文，
                // 于是「这一节只有一个占位符」这条判据永远为假——**门禁静默失效**。
                if (line.Trim() == specification.GeneratedRegionBegin)
                {
                    break;
                }

                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    currentSection = line.Substring(3).Trim();
                    titles.Add(currentSection);
                    bodyByTitle[currentSection] = new List<string>();
                    continue;
                }

                if (currentSection != null && line.Trim().Length > 0)
                {
                    bodyByTitle[currentSection].Add(line.Trim());
                }
            }

            // 生成区那一节的标题在断点之后，单独补进来，免得被判成「缺小节」。
            titles.Add(specification.GeneratedSection);

            foreach (var section in specification.RequiredSections)
            {
                if (!titles.Contains(section))
                {
                    findings.Add(new PoolFinding(
                        location, $"缺必填小节「## {section}」", $"照参考示例补上「## {section}」这一节",
                        PlanningDocumentSpec.ReferencePath));
                }
            }

            // 不空转：这一节留着占位符等于没写（§六 最后一条）。
            if (bodyByTitle.TryGetValue(FutureSection, out var future)
                && (future.Count == 0 || (future.Count == 1 && future[0] == PlaceholderLine)))
            {
                findings.Add(new PoolFinding(
                    location,
                    $"「{FutureSection}」还是占位符",
                    "写清这个模块往后要做成什么样。读的人靠这一节区分「还没做」与「故意不做」，"
                        + "空着的话那两件事看起来一样，而它们对要不要提需求的影响完全相反",
                    PlanningDocumentSpec.ReferencePath));
            }
        }

        private static void CheckGeneratedRegion(
            IReadOnlyList<string> lines, string location, PlanningDocumentSpec specification, List<PoolFinding> findings)
        {
            var body = GeneratedRegion.Read(
                lines, specification.GeneratedRegionBegin, specification.GeneratedRegionEnd, out var present);

            if (!present)
            {
                findings.Add(new PoolFinding(
                    location, "没有生成区", "跑 plan.render 生成一次", PlanningDocumentSpec.ReferencePath));
                return;
            }

            var declared = "";
            for (var index = 1; index < lines.Count; index++)
            {
                if (lines[index].Trim() == "---")
                {
                    break;
                }

                if (lines[index].StartsWith(PlanningDocumentSpec.GeneratedHashKey + ":", StringComparison.Ordinal))
                {
                    declared = lines[index].Substring(PlanningDocumentSpec.GeneratedHashKey.Length + 1).Trim();
                    break;
                }
            }

            if (declared.Length == 0)
            {
                findings.Add(new PoolFinding(
                    location, $"frontmatter 里没有「{PlanningDocumentSpec.GeneratedHashKey}」",
                    "跑 plan.render 重生成一次，它会写上", PlanningDocumentSpec.ReferencePath));
                return;
            }

            var actual = GeneratedRegion.Hash(body);
            if (!string.Equals(actual, declared, StringComparison.Ordinal))
            {
                findings.Add(new PoolFinding(
                    location,
                    "生成区被手改过（算出来的哈希与 frontmatter 里那个对不上）",
                    "生成区是投影不是正本——要改内容去改正本（需求池 / 界面规格 / Config/Schema / 代码），"
                        + "然后跑 plan.render 重生成。手改的这一版下次重渲染会被吃掉",
                    PlanningDocumentSpec.ReferencePath));
            }
        }

        private static void CheckConfigTables(
            string repositoryRoot, IReadOnlyList<string> lines, string location, List<PoolFinding> findings)
        {
            foreach (var table in ReadInlineList(lines, "配置表"))
            {
                if (ConfigTableSchemaReader.Read(repositoryRoot, table, out var reason) == null)
                {
                    findings.Add(new PoolFinding(
                        location,
                        $"配置表声明「{table}」解析不了：{reason}",
                        "改成 Config/Schema 下真有的表名，或者把这一项从 frontmatter 的「配置表」里去掉",
                        PlanningDocumentSpec.ReferencePath));
                }
            }
        }

        /// <summary>读 frontmatter 里 `键: [a, b]` 这种行内列表；只认这一种写法，认不出给空表。</summary>
        private static IReadOnlyList<string> ReadInlineList(IReadOnlyList<string> lines, string key)
        {
            var values = new List<string>();
            if (lines.Count == 0 || lines[0].Trim() != "---")
            {
                return values;
            }

            for (var index = 1; index < lines.Count; index++)
            {
                if (lines[index].Trim() == "---")
                {
                    break;
                }

                if (!lines[index].StartsWith(key + ":", StringComparison.Ordinal))
                {
                    continue;
                }

                var inline = lines[index].Substring(key.Length + 1).Trim();
                if (!inline.StartsWith("[", StringComparison.Ordinal) || !inline.EndsWith("]", StringComparison.Ordinal))
                {
                    break;
                }

                foreach (var item in inline.Substring(1, inline.Length - 2).Split(','))
                {
                    var value = item.Trim().Trim('"', '\'');
                    if (value.Length > 0)
                    {
                        values.Add(value);
                    }
                }

                break;
            }

            return values;
        }

        private static string Relative(string repositoryRoot, string path)
        {
            return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
        }
    }
}
