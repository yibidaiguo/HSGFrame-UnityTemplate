using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一份供给产物的状态：相对路径、是否存在、字节数与人工导入提示。</summary>
    public sealed class PackageArtifactStatus
    {
        /// <summary>
        /// 构造一份供给产物的状态。
        /// </summary>
        /// <param name="relativePath">产物相对仓库根的路径，正斜杠。</param>
        /// <param name="exists">产物是否存在。</param>
        /// <param name="byteCount">产物的字节数；不存在时为 0。</param>
        /// <param name="importHint">这份产物的人工导入提示。</param>
        public PackageArtifactStatus(string relativePath, bool exists, long byteCount, string importHint)
        {
            RelativePath = relativePath ?? "";
            Exists = exists;
            ByteCount = byteCount;
            ImportHint = importHint ?? "";
        }

        /// <summary>产物相对仓库根的路径，正斜杠。</summary>
        public string RelativePath { get; }

        /// <summary>产物是否存在。</summary>
        public bool Exists { get; }

        /// <summary>产物的字节数；不存在时为 0。</summary>
        public long ByteCount { get; }

        /// <summary>这份产物的人工导入提示。</summary>
        public string ImportHint { get; }
    }

    /// <summary>一次供给产物检查的结果：driver、逐份产物状态与发现的缺失/空文件。</summary>
    public sealed class PackageInspection
    {
        /// <summary>
        /// 构造一次供给产物检查的结果。
        /// </summary>
        /// <param name="driverName">面向的 driver 名称。</param>
        /// <param name="artifacts">逐份产物的状态，序数序。</param>
        /// <param name="findings">检查发现；未供给时为空列表。</param>
        public PackageInspection(
            string driverName,
            IReadOnlyList<PackageArtifactStatus> artifacts,
            IReadOnlyList<PoolFinding> findings)
        {
            DriverName = driverName ?? "";
            Artifacts = artifacts ?? Array.Empty<PackageArtifactStatus>();
            Findings = findings ?? Array.Empty<PoolFinding>();

            var missingCount = 0;
            var emptyCount = 0;
            foreach (var artifact in Artifacts)
            {
                if (!artifact.Exists)
                {
                    missingCount++;
                }
                else if (artifact.ByteCount == 0)
                {
                    emptyCount++;
                }
            }

            MissingCount = missingCount;
            EmptyCount = emptyCount;
        }

        /// <summary>面向的 driver 名称。</summary>
        public string DriverName { get; }

        /// <summary>逐份产物的状态，序数序。</summary>
        public IReadOnlyList<PackageArtifactStatus> Artifacts { get; }

        /// <summary>缺失的产物份数。</summary>
        public int MissingCount { get; }

        /// <summary>存在但字节数为 0 的产物份数。</summary>
        public int EmptyCount { get; }

        /// <summary>检查发现；未供给时为空列表。</summary>
        public IReadOnlyList<PoolFinding> Findings { get; }
    }

    /// <summary>
    /// 供给产物完整性检查：逐份核对 _Generated/Bridges/&lt;driver&gt; 下的 10 份产物是否齐全非空，
    /// 并给出每份的人工导入提示。产物目录整个不存在视为「未供给」，与指纹对账的既有约定一致。
    /// </summary>
    public static class AssistantPackageInspector
    {
        /// <summary>
        /// 检查某 driver 的供给产物：目录存在时逐份核对，缺失或空文件各出一条发现；
        /// 目录整个不存在时发现为空列表（未供给），产物状态照常列出。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static PackageInspection Inspect(string repositoryRoot, string driverName)
        {
            var artifacts = BuildArtifacts(repositoryRoot, driverName);
            var findings = new List<PoolFinding>();
            if (Directory.Exists(ProvisionPaths.GeneratedBridgeDirectory(repositoryRoot, driverName)))
            {
                foreach (var artifact in artifacts)
                {
                    if (!artifact.Exists)
                    {
                        findings.Add(ArtifactFinding(artifact, "供给产物缺失", driverName));
                    }
                    else if (artifact.ByteCount == 0)
                    {
                        findings.Add(ArtifactFinding(artifact, "供给产物是空文件", driverName));
                    }
                }
            }

            return new PackageInspection(driverName, artifacts, findings);
        }

        /// <summary>按一份产物状态与原因拼一条发现：位置是相对路径，修复是重跑供给。</summary>
        private static PoolFinding ArtifactFinding(PackageArtifactStatus artifact, string reason, string driverName)
        {
            return new PoolFinding(
                artifact.RelativePath,
                reason,
                $"重跑 bridge.provision --driver {driverName}",
                $"Bridges/{driverName}/driver.json");
        }

        /// <summary>列 10 份产物的状态：路径、存在性与字节数按磁盘实况，导入提示来自产物清单。</summary>
        private static IReadOnlyList<PackageArtifactStatus> BuildArtifacts(string repositoryRoot, string driverName)
        {
            var artifacts = new List<PackageArtifactStatus>(ArtifactCount);
            foreach (var spec in ArtifactSpecs(repositoryRoot, driverName))
            {
                var exists = File.Exists(spec.FullPath);
                var byteCount = exists ? new FileInfo(spec.FullPath).Length : 0;
                artifacts.Add(new PackageArtifactStatus(
                    RelativeTo(repositoryRoot, spec.FullPath),
                    exists,
                    byteCount,
                    spec.ImportHint));
            }

            return artifacts;
        }

        /// <summary>产物目录里 10 份产物的绝对路径与导入提示，顺序即产出顺序。</summary>
        private static (string FullPath, string ImportHint)[] ArtifactSpecs(string repositoryRoot, string driverName)
        {
            var packageDirectory = ProvisionPaths.AssistantPackageDirectory(repositoryRoot, driverName);
            var knowledgeDirectory = ProvisionPaths.AssistantKnowledgeDirectory(repositoryRoot, driverName);
            return new[]
            {
                (ProvisionPaths.TableDescriptionFile(repositoryRoot, driverName), "按这份在下游平台建表：字段、类型、单选项、分类型三张表单"),
                (ProvisionPaths.EpicTableFile(repositoryRoot, driverName), "按这份建专项表：每职责一个人员多选的认领列"),
                (ProvisionPaths.ValidationMessageFile(repositoryRoot, driverName), "导入成下游的校验提示文案；拒收回贴与助手用的是同一份"),
                (Path.Combine(packageDirectory, "system-prompt.md"), "全文贴进助手的系统提示框"),
                (Path.Combine(knowledgeDirectory, "design-digest.md"), "上传为助手的知识库文件"),
                (Path.Combine(knowledgeDirectory, "conflicts.md"), "上传为助手的知识库文件"),
                (Path.Combine(knowledgeDirectory, "glossary.md"), "上传为助手的知识库文件"),
                (Path.Combine(knowledgeDirectory, "examples.md"), "上传为助手的知识库文件"),
                (Path.Combine(knowledgeDirectory, "modules.md"), "上传为助手的知识库文件"),
                (Path.Combine(knowledgeDirectory, "module-interfaces.md"), "上传为助手的知识库文件"),
                (Path.Combine(packageDirectory, "import-guide.md"), "给做导入的人看，不用上传"),
                (ProvisionPaths.FingerprintFile(repositoryRoot, driverName), "不上传；它是下次对账的凭据")
            };
        }

        /// <summary>把绝对路径转成仓库相对路径，正斜杠。</summary>
        private static string RelativeTo(string repositoryRoot, string fullPath)
        {
            return Path.GetRelativePath(Path.GetFullPath(repositoryRoot), Path.GetFullPath(fullPath)).Replace('\\', '/');
        }

        /// <summary>一份供给包产出的产物份数。</summary>
        private const int ArtifactCount = 10;
    }
}
