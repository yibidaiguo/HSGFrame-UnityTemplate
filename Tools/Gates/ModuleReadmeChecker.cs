using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Template.Toolkit.Gates
{
    /// <summary>
    /// 模块自述门禁（《结构规范-代码》第六节）：每个模块根必须放一份 ≤maxLines 行的 README.md——
    /// 一句话职责、公开面清单、依赖了谁的事件。规矩一直写死在规范里却从未被机器检查，
    /// 全靠自觉，这里把它变成门禁：README 缺了或超行数都会红。
    /// </summary>
    public static class ModuleReadmeChecker
    {
        /// <summary>模块自述文件名，固定 README.md。</summary>
        private const string ReadmeFileName = "README.md";

        /// <summary>参考示例：仓库里那份合格的模块 README。</summary>
        private const string ReferenceExample = "UnityProject/Assets/Game/Scripts/Modules/Combat/README.md";

        /// <summary>
        /// 检查每个模块根的 README.md 是否存在且不超过行数上限。
        /// </summary>
        /// <param name="modulesRootDirectory">模块根目录，即 <c>UnityProject/Assets/Game/Scripts/Modules</c>。</param>
        /// <param name="maxLines">README.md 允许的最大物理行数。</param>
        public static IReadOnlyList<GateFinding> Check(string modulesRootDirectory, int maxLines)
        {
            // 生成出来的新项目可能还没有模块树，这时候没有可检查的东西，
            // 返回空清单让门禁跳过而不是当红。
            if (string.IsNullOrWhiteSpace(modulesRootDirectory) || !Directory.Exists(modulesRootDirectory))
            {
                return Array.Empty<GateFinding>();
            }

            var findings = new List<GateFinding>();
            foreach (var moduleDirectory in Directory.EnumerateDirectories(modulesRootDirectory))
            {
                var readmePath = Path.Combine(moduleDirectory, ReadmeFileName);
                if (!File.Exists(readmePath))
                {
                    findings.Add(new GateFinding(
                        moduleDirectory,
                        "模块根缺 README.md",
                        "补一份 ≤" + maxLines + " 行的 README：职责 / 公开面 / 依赖",
                        ReferenceExample));
                    continue;
                }

                // 行数按物理行算（ReadAllLines 的长度），空行也计入，不掐。
                var lineCount = File.ReadAllLines(readmePath).Length;
                if (lineCount > maxLines)
                {
                    findings.Add(new GateFinding(
                        readmePath,
                        $"README.md 共 {lineCount} 行，超过上限 {maxLines} 行",
                        $"精简到 ≤{maxLines} 行：一句话职责 / 公开面清单 / 依赖了谁",
                        ReferenceExample));
                }
            }

            findings.Sort((left, right) => string.CompareOrdinal(left.Location, right.Location));
            return findings;
        }
    }
}
