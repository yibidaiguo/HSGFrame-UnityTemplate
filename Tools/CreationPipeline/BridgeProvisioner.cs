using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次供给的结果：面向的 driver、是否干跑、写出（或将写出）的全部产物路径与两个哈希。</summary>
    public sealed class ProvisionOutcome
    {
        /// <summary>
        /// 构造一次供给结果。
        /// </summary>
        /// <param name="driverName">面向的 driver 名称。</param>
        /// <param name="isDryRun">是否干跑。</param>
        /// <param name="producedFiles">写出（或将写出）的产物绝对路径列表。</param>
        /// <param name="schemaHash">合并 schema 的规范化哈希。</param>
        /// <param name="designDigestHash">设计池汇总的汇总哈希。</param>
        public ProvisionOutcome(
            string driverName,
            bool isDryRun,
            IReadOnlyList<string> producedFiles,
            string schemaHash,
            string designDigestHash)
        {
            DriverName = driverName ?? "";
            IsDryRun = isDryRun;
            ProducedFiles = producedFiles ?? Array.Empty<string>();
            SchemaHash = schemaHash ?? "";
            DesignDigestHash = designDigestHash ?? "";
        }

        /// <summary>面向的 driver 名称。</summary>
        public string DriverName { get; }

        /// <summary>是否干跑：为 true 时一个文件都没写。</summary>
        public bool IsDryRun { get; }

        /// <summary>写出（或将写出）的全部产物绝对路径列表，顺序即写盘顺序。</summary>
        public IReadOnlyList<string> ProducedFiles { get; }

        /// <summary>合并 schema 的规范化哈希。</summary>
        public string SchemaHash { get; }

        /// <summary>设计池汇总的汇总哈希。</summary>
        public string DesignDigestHash { get; }
    }

    /// <summary>
    /// 供给编排：一次跑出建表描述、专项表、校验错误文案、assistant-package与指纹全部产物。
    /// 指纹必须最后写，中途失败不会留下一份自称新鲜的指纹。
    /// </summary>
    public static class BridgeProvisioner
    {
        /// <summary>
        /// 跑一次供给：读 driver 自述与合并 schema，算两个哈希，按顺序产出全部文件。
        /// 干跑时不写任何文件，产物列表与真跑完全一致。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录，产物落在 _Generated/Bridges/&lt;driver&gt; 下。</param>
        /// <param name="poolRoot">池子根目录，schema 与设计池汇总从这里读。</param>
        /// <param name="driverName">面向的下游 driver 名称。</param>
        /// <param name="isDryRun">为 true 时只算路径与哈希，不落任何文件。</param>
        public static ProvisionOutcome Run(
            string repositoryRoot,
            string poolRoot,
            string driverName,
            bool isDryRun)
        {
            var driver = BridgeDriverDescriptor.Load(repositoryRoot, driverName);
            var schema = PoolSchemaLoader.Load(poolRoot, "需求");
            var schemaHash = ProvisionFingerprint.ComputeSchemaHash(schema);
            var designDigestHash = ProvisionFingerprint.ComputeDesignDigestHash(poolRoot);

            var producedFiles = new List<string>();
            if (!isDryRun)
            {
                // TableDescription.WriteTo 不建目录，编排层负责把产物目录先建出来。
                Directory.CreateDirectory(ProvisionPaths.GeneratedBridgeDirectory(repositoryRoot, driverName));

                TableDescriptionBuilder.Build(schema, driver)
                    .WriteTo(ProvisionPaths.TableDescriptionFile(repositoryRoot, driverName));
                producedFiles.Add(ProvisionPaths.TableDescriptionFile(repositoryRoot, driverName));

                EpicTableBuilder.Build(poolRoot, driver)
                    .WriteTo(ProvisionPaths.EpicTableFile(repositoryRoot, driverName));
                producedFiles.Add(ProvisionPaths.EpicTableFile(repositoryRoot, driverName));

                ValidationMessageExporter.WriteTo(ProvisionPaths.ValidationMessageFile(repositoryRoot, driverName));
                producedFiles.Add(ProvisionPaths.ValidationMessageFile(repositoryRoot, driverName));

                var packageFiles = AssistantPackageBuilder.Build(repositoryRoot, poolRoot, schema, driverName, ConflictList.Load(poolRoot));
                producedFiles.AddRange(packageFiles);

                ProvisionFingerprint.Create(driver.Name, driver.ContractRange, schemaHash, designDigestHash)
                    .WriteTo(ProvisionPaths.FingerprintFile(repositoryRoot, driverName));
                producedFiles.Add(ProvisionPaths.FingerprintFile(repositoryRoot, driverName));
            }
            else
            {
                producedFiles.Add(ProvisionPaths.TableDescriptionFile(repositoryRoot, driverName));
                producedFiles.Add(ProvisionPaths.EpicTableFile(repositoryRoot, driverName));
                producedFiles.Add(ProvisionPaths.ValidationMessageFile(repositoryRoot, driverName));
                producedFiles.AddRange(AssistantPackageBuilder.ProspectiveFiles(repositoryRoot, driverName));
                producedFiles.Add(ProvisionPaths.FingerprintFile(repositoryRoot, driverName));
            }

            return new ProvisionOutcome(driver.Name, isDryRun, producedFiles, schemaHash, designDigestHash);
        }
    }
}
