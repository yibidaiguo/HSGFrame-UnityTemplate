using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// `gate.plandoc`：按策划文档规范查 `index.md` 的六条。
    ///
    /// **没有 index.md 不算违规**——需求可以先有骨架后有文档，doc.render 随时补得出来。
    /// 有 index.md 就得合规：门禁查的是「写了的那份对不对」，不是「写没写」。
    /// </summary>
    public static class PlanningDocumentChecker
    {
        /// <summary>正文里的相对引用：![说明](media/x.png) 与 [说明](media/x.mp4) 都算。</summary>
        private static readonly Regex MediaReferencePattern = new Regex(
            @"\]\(\s*(?<path>[^)\s]+)\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// 查池子里全部需求的文档。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="specification">策划文档规范。</param>
        public static IReadOnlyList<PoolFinding> CheckAll(string poolRoot, PlanningDocumentSpec specification)
        {
            var findings = new List<PoolFinding>();
            foreach (var identifier in PoolPaths.EnumerateRequirementIdentifiers(poolRoot))
            {
                findings.AddRange(CheckOne(poolRoot, identifier, specification));
            }

            return findings;
        }

        /// <summary>
        /// 查一条需求的文档；没有 index.md 时返回空列表。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="requirementIdentifier">需求 id，如「REQ-0042」。</param>
        /// <param name="specification">策划文档规范。</param>
        public static IReadOnlyList<PoolFinding> CheckOne(
            string poolRoot,
            string requirementIdentifier,
            PlanningDocumentSpec specification)
        {
            var findings = new List<PoolFinding>();
            var documentPath = PoolPaths.PlanningDocument(poolRoot, requirementIdentifier);
            if (!File.Exists(documentPath))
            {
                return findings;
            }

            if (!PlanningDocument.TryParse(File.ReadAllText(documentPath), specification, out var document, out var reason))
            {
                findings.Add(Finding(documentPath, 0, "策划文档.解析失败", reason));
                return findings;
            }

            CheckFrontMatter(documentPath, requirementIdentifier, document, specification, findings);
            CheckSections(documentPath, requirementIdentifier, poolRoot, document, specification, findings);
            CheckAcceptance(documentPath, document, specification, findings);
            CheckMedia(documentPath, poolRoot, requirementIdentifier, document, findings);
            CheckGeneratedRegion(documentPath, document, findings);

            return findings;
        }

        // 一、frontmatter 必备键齐全；二、需求id 与目录名一致；外加权威侧取值。
        private static void CheckFrontMatter(
            string documentPath,
            string requirementIdentifier,
            PlanningDocument document,
            PlanningDocumentSpec specification,
            List<PoolFinding> findings)
        {
            var frontMatter = document.FrontMatter;
            if (!frontMatter.IsPresent)
            {
                findings.Add(Finding(documentPath, 1, "策划文档.frontmatter缺失"));
                return;
            }

            foreach (var key in specification.FrontMatterRequiredKeys)
            {
                if (!frontMatter.Has(key) || frontMatter.Scalar(key).Trim().Length == 0)
                {
                    findings.Add(Finding(documentPath, 0, "策划文档.必备键缺失", key));
                }
            }

            var documentIdentifier = frontMatter.Scalar("需求id");
            if (documentIdentifier.Length > 0
                && !string.Equals(documentIdentifier, requirementIdentifier, StringComparison.Ordinal))
            {
                findings.Add(Finding(
                    documentPath,
                    frontMatter.LineOf("需求id"),
                    "策划文档.id与目录名",
                    documentIdentifier,
                    requirementIdentifier));
            }

            var authority = frontMatter.Scalar("权威侧");
            if (authority.Length > 0
                && specification.AuthorityValues.Count > 0
                && !Contains(specification.AuthorityValues, authority))
            {
                findings.Add(Finding(
                    documentPath,
                    frontMatter.LineOf("权威侧"),
                    "策划文档.权威侧越界",
                    authority,
                    string.Join("/", specification.AuthorityValues)));
            }
        }

        // 三、必填小节按序在位。类型取自需求骨架，骨架读不到就没有必填小节可查。
        private static void CheckSections(
            string documentPath,
            string requirementIdentifier,
            string poolRoot,
            PlanningDocument document,
            PlanningDocumentSpec specification,
            List<PoolFinding> findings)
        {
            var requirementType = RequirementTypeOf(poolRoot, requirementIdentifier, document);
            var required = specification.RequiredSectionsFor(requirementType);
            if (required.Count == 0)
            {
                return;
            }

            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < document.Sections.Count; index++)
            {
                var section = document.Sections[index];
                if (section.IsInGeneratedRegion || positions.ContainsKey(section.Title))
                {
                    continue;
                }

                positions[section.Title] = index;
            }

            var previousTitle = "";
            var previousPosition = -1;
            foreach (var title in required)
            {
                if (!positions.TryGetValue(title, out var position))
                {
                    findings.Add(Finding(documentPath, 0, "策划文档.小节缺失", title));
                    continue;
                }

                if (previousPosition >= 0 && position < previousPosition)
                {
                    findings.Add(Finding(
                        documentPath,
                        document.Sections[position].LineNumber,
                        "策划文档.小节乱序",
                        title,
                        previousTitle));
                }

                previousTitle = title;
                previousPosition = position;
            }
        }

        // 四、验收标准非空且是有序列表。空行与嵌套的续行不算条目，别的行一律算违规——
        // 散文写的验收标准正是要拦的那种：阶段 4 逐条核，核不动散文。
        private static void CheckAcceptance(
            string documentPath,
            PlanningDocument document,
            PlanningDocumentSpec specification,
            List<PoolFinding> findings)
        {
            if (specification.AcceptanceSection.Length == 0)
            {
                return;
            }

            PlanningDocumentSection acceptance = null;
            foreach (var section in document.Sections)
            {
                if (!section.IsInGeneratedRegion
                    && string.Equals(section.Title, specification.AcceptanceSection, StringComparison.Ordinal))
                {
                    acceptance = section;
                    break;
                }
            }

            if (acceptance == null)
            {
                return;
            }

            var itemCount = 0;
            for (var index = 0; index < acceptance.Lines.Count; index++)
            {
                var line = acceptance.Lines[index];
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                if (line.StartsWith(" ", StringComparison.Ordinal) || line.StartsWith("\t", StringComparison.Ordinal))
                {
                    // 缩进的是上一条的续行，不单算一条。
                    continue;
                }

                if (Regex.IsMatch(line, @"^\d+[.、]\s*\S"))
                {
                    itemCount++;
                    continue;
                }

                findings.Add(Finding(
                    documentPath,
                    acceptance.LineNumber + index + 1,
                    "策划文档.验收标准非有序列表",
                    specification.AcceptanceSection,
                    line.Trim()));
            }

            if (itemCount == 0)
            {
                findings.Add(Finding(
                    documentPath,
                    acceptance.LineNumber,
                    "策划文档.验收标准为空",
                    specification.AcceptanceSection));
            }
        }

        // 五、媒体：frontmatter 登记的要存在、要 ASCII、要带说明；正文里引用的相对路径也要存在。
        private static void CheckMedia(
            string documentPath,
            string poolRoot,
            string requirementIdentifier,
            PlanningDocument document,
            List<PoolFinding> findings)
        {
            var requirementDirectory = PoolPaths.RequirementDirectory(poolRoot, requirementIdentifier);
            var registered = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in document.FrontMatter.List("媒体"))
            {
                entry.TryGetValue("路径", out var relativePath);
                relativePath = (relativePath ?? "").Trim();
                if (relativePath.Length == 0)
                {
                    continue;
                }

                registered.Add(relativePath);
                CheckOneMediaPath(documentPath, requirementDirectory, relativePath, findings);

                entry.TryGetValue("说明", out var description);
                if (string.IsNullOrWhiteSpace(description))
                {
                    findings.Add(Finding(documentPath, 0, "策划文档.媒体缺说明", relativePath));
                }
            }

            for (var index = 0; index < document.BodyLines.Count; index++)
            {
                foreach (Match match in MediaReferencePattern.Matches(document.BodyLines[index]))
                {
                    var referenced = match.Groups["path"].Value.Trim();
                    if (referenced.Length == 0 || registered.Contains(referenced) || IsExternalReference(referenced))
                    {
                        continue;
                    }

                    CheckOneMediaPath(documentPath, requirementDirectory, referenced, findings);
                }
            }
        }

        private static void CheckOneMediaPath(
            string documentPath,
            string requirementDirectory,
            string relativePath,
            List<PoolFinding> findings)
        {
            if (!IsAsciiPath(relativePath))
            {
                findings.Add(Finding(documentPath, 0, "策划文档.媒体名非ASCII", relativePath));
            }

            var absolute = Path.Combine(requirementDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                findings.Add(Finding(documentPath, 0, "策划文档.媒体不存在", relativePath));
            }
        }

        // 六、生成区没被手改：拿正文实际的哈希与 frontmatter 里记的那个比。
        private static void CheckGeneratedRegion(
            string documentPath,
            PlanningDocument document,
            List<PoolFinding> findings)
        {
            if (!document.HasGeneratedRegion)
            {
                return;
            }

            var recorded = document.FrontMatter.Scalar(PlanningDocumentSpec.GeneratedHashKey).Trim();
            if (recorded.Length == 0)
            {
                findings.Add(Finding(documentPath, document.GeneratedRegionLineNumber, "策划文档.生成区hash缺失"));
                return;
            }

            var actual = PlanningDocument.HashGeneratedRegion(document.GeneratedRegionLines);
            if (!string.Equals(recorded, actual, StringComparison.Ordinal))
            {
                findings.Add(Finding(documentPath, document.GeneratedRegionLineNumber, "策划文档.生成区被手改"));
            }
        }

        // 类型优先取需求骨架里的：骨架是权威（所有权=策划端但由 pool.pull 落盘），
        // 文档 frontmatter 里那份是它的镜像。骨架读不到才退回文档自己写的。
        private static string RequirementTypeOf(string poolRoot, string requirementIdentifier, PlanningDocument document)
        {
            var requirementFile = PoolPaths.RequirementFile(poolRoot, requirementIdentifier);
            if (File.Exists(requirementFile))
            {
                try
                {
                    using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(requirementFile)))
                    {
                        if (json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                            && json.RootElement.TryGetProperty("类型", out var value)
                            && value.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            return value.GetString();
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is System.Text.Json.JsonException)
                {
                    // 骨架读不动是 pool.validate 那道门禁的事，这里退回文档自己写的类型继续查。
                }
            }

            return document.FrontMatter.Scalar("类型");
        }

        private static bool IsExternalReference(string reference)
        {
            return reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || reference.StartsWith("#", StringComparison.Ordinal)
                || reference.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAsciiPath(string relativePath)
        {
            foreach (var character in relativePath)
            {
                if (character > 127)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Contains(IReadOnlyList<string> values, string candidate)
        {
            foreach (var value in values)
            {
                if (string.Equals(value, candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static PoolFinding Finding(string documentPath, int lineNumber, string ruleIdentifier, params object[] values)
        {
            var location = lineNumber > 0 ? documentPath + ":" + lineNumber : documentPath;
            return new PoolFinding(
                location,
                ValidationMessageCatalog.Format(ruleIdentifier, values),
                ValidationMessageCatalog.FormatFix(ruleIdentifier),
                PlanningDocumentSpec.ReferencePath);
        }
    }
}
