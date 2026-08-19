using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.Gates
{
    /// <summary>
    /// 层边界检查：协作/过程数据不许落 Unity 资产树，游戏代码不许引用协作层路径。
    /// </summary>
    public static class LayerBoundaryChecker
    {
        /// <summary>协作/过程数据的默认目录名：配置里没写时按这份查。</summary>
        private static readonly string[] DefaultCollaborationDirectoryNames = { "Pools", "_Tasks", "Bridges" };

        /// <summary>游戏代码里禁止出现的协作层路径片段。</summary>
        private static readonly string[] ForbiddenReferenceSegments = { "Pools/", "_Tasks/", "Bridges/" };

        /// <summary>
        /// 检查协作/过程数据落点与游戏代码的协作层引用；Unity 资产目录不存在时返回空列表。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录，用于把路径转成仓库相对路径。</param>
        /// <param name="unityAssetsDirectory">Unity 资产根目录。</param>
        /// <param name="configuration">门禁配置。</param>
        public static IReadOnlyList<GateFinding> Check(
            string repositoryRoot,
            string unityAssetsDirectory,
            GateConfiguration configuration)
        {
            var findings = new List<GateFinding>();
            if (!Directory.Exists(unityAssetsDirectory))
            {
                return findings;
            }

            var collaborationNames = configuration.CollaborationDirectoryNames != null
                ? configuration.CollaborationDirectoryNames
                : DefaultCollaborationDirectoryNames;

            findings.AddRange(FindCollaborationDirectories(repositoryRoot, unityAssetsDirectory, collaborationNames));
            findings.AddRange(FindGameScriptReferences(repositoryRoot, unityAssetsDirectory));
            return findings;
        }

        /// <summary>规则一：协作/过程数据目录出现在 Unity 资产树里即违规，按目录名匹配。</summary>
        private static IEnumerable<GateFinding> FindCollaborationDirectories(
            string repositoryRoot,
            string unityAssetsDirectory,
            IEnumerable<string> collaborationNames)
        {
            var names = new HashSet<string>(collaborationNames, StringComparer.Ordinal);
            foreach (var directoryPath in Directory.EnumerateDirectories(unityAssetsDirectory, "*", SearchOption.AllDirectories))
            {
                var directoryName = Path.GetFileName(directoryPath);
                if (names.Contains(directoryName))
                {
                    yield return new GateFinding(
                        ToRepositoryRelative(repositoryRoot, directoryPath),
                        $"协作/过程数据目录「{directoryName}」出现在 Unity 资产树里",
                        "把它移回仓库根；产品层零协作感知（总纲 §一 三条纪律之一）",
                        "Doc/策划美术工作流接入方案.md");
                }
            }
        }

        /// <summary>规则二：Game/Scripts 下的 *.cs 逐行找协作层路径片段，命中逐行各报一条。</summary>
        private static IEnumerable<GateFinding> FindGameScriptReferences(string repositoryRoot, string unityAssetsDirectory)
        {
            var scriptsDirectory = Path.Combine(unityAssetsDirectory, "Game", "Scripts");
            if (!Directory.Exists(scriptsDirectory))
            {
                yield break;
            }

            foreach (var filePath in Directory.EnumerateFiles(scriptsDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = ToRepositoryRelative(repositoryRoot, filePath);
                if (relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                    || relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lineNumber = 0;
                foreach (var line in File.ReadLines(filePath))
                {
                    lineNumber++;
                    foreach (var segment in ForbiddenReferenceSegments)
                    {
                        if (line.IndexOf(segment, StringComparison.Ordinal) >= 0)
                        {
                            yield return new GateFinding(
                                $"{relativePath}:{lineNumber}",
                                $"游戏代码里引用了协作层路径「{segment}」",
                                "产品层不许感知协作数据，删掉这处引用",
                                "Doc/策划美术工作流接入方案.md");
                        }
                    }
                }
            }
        }

        /// <summary>把绝对路径转成仓库相对路径，正斜杠。</summary>
        private static string ToRepositoryRelative(string repositoryRoot, string filePath)
        {
            return Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(filePath)).Replace('\\', '/');
        }
    }
}
