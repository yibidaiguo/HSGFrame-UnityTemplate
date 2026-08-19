using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>ProvisionFingerprint 的哈希、读写与对账测试。</summary>
    public class ProvisionFingerprintTests
    {
        /// <summary>同一份 schema 算两次哈希相同。</summary>
        [Fact]
        public void ComputeSchemaHashIsStableForSameSchema()
        {
            var schema = CreateSchema("需求");

            var first = ProvisionFingerprint.ComputeSchemaHash(schema);
            var second = ProvisionFingerprint.ComputeSchemaHash(schema);

            Assert.Equal(first, second);
        }

        /// <summary>改一个字段名后哈希变化。</summary>
        [Fact]
        public void ComputeSchemaHashChangesWhenFieldRenamed()
        {
            var original = CreateSchema("需求");
            var renamed = CreateSchema("需求");
            var renamedFields = new List<PoolSchemaField>();
            foreach (var field in renamed.Fields)
            {
                if (field.Name == "标题")
                {
                    renamedFields.Add(new PoolSchemaField(
                        "标题2", field.FieldType, field.IsRequired, field.EnumValues,
                        field.ElementType, field.MinimumCount, field.Ownership,
                        field.IsNullable, field.IsEditableAfterLock));
                }
                else
                {
                    renamedFields.Add(field);
                }
            }

            var renamedSchema = new PoolSchema(
                renamed.SchemaVersion, renamed.EntityName, renamed.IdentifierPattern,
                renamedFields, renamed.RequiredByType, renamed.StateMachine);

            Assert.NotEqual(
                ProvisionFingerprint.ComputeSchemaHash(original),
                ProvisionFingerprint.ComputeSchemaHash(renamedSchema));
        }

        /// <summary>设计池汇总目录不存在时哈希非空且稳定。</summary>
        [Fact]
        public void ComputeDesignDigestHashIsStableWhenDirectoryMissing()
        {
            using var workspace = new PoolTestWorkspace();

            var first = ProvisionFingerprint.ComputeDesignDigestHash(workspace.Root);
            var second = ProvisionFingerprint.ComputeDesignDigestHash(workspace.Root);

            Assert.False(string.IsNullOrEmpty(first));
            Assert.Equal(first, second);
        }

        /// <summary>指纹文件不存在时 Reconcile 返回空列表，判定为未供给。</summary>
        [Fact]
        public void ReconcileReturnsEmptyWhenFingerprintFileMissing()
        {
            using var workspace = new PoolTestWorkspace();
            var filePath = Path.Combine(workspace.Root, "_Generated", "指纹.json");

            var findings = ProvisionFingerprint.Reconcile(filePath, "abc", "def");

            Assert.Empty(findings);
        }

        /// <summary>两个哈希都对上时 Reconcile 返回空列表。</summary>
        [Fact]
        public void ReconcileReturnsEmptyWhenHashesMatch()
        {
            using var workspace = new PoolTestWorkspace();
            var filePath = Path.Combine(workspace.Root, "_Generated", "指纹.json");
            var schemaHash = ProvisionFingerprint.ComputeSchemaHash(CreateSchema("需求"));
            var digestHash = ProvisionFingerprint.ComputeDesignDigestHash(workspace.Root);
            ProvisionFingerprint.Create("feishu", ">=1.0 <2.0", schemaHash, digestHash).WriteTo(filePath);

            var findings = ProvisionFingerprint.Reconcile(filePath, schemaHash, digestHash);

            Assert.Empty(findings);
        }

        /// <summary>schema 哈希对不上时 Reconcile 返回 1 条，Reason 含「schema 哈希」。</summary>
        [Fact]
        public void ReconcileReportsSchemaHashMismatch()
        {
            using var workspace = new PoolTestWorkspace();
            var filePath = Path.Combine(workspace.Root, "_Generated", "指纹.json");
            var schemaHash = ProvisionFingerprint.ComputeSchemaHash(CreateSchema("需求"));
            var digestHash = ProvisionFingerprint.ComputeDesignDigestHash(workspace.Root);
            ProvisionFingerprint.Create("feishu", ">=1.0 <2.0", schemaHash, digestHash).WriteTo(filePath);

            var findings = ProvisionFingerprint.Reconcile(filePath, "改了", digestHash);

            var finding = Assert.Single(findings);
            Assert.Contains("schema 哈希", finding.Reason);
            Assert.Equal(filePath, finding.Location);
            Assert.Contains("重跑 bridge.provision", finding.FixAction);
            Assert.Equal("", finding.ReferenceExamplePath);
        }

        /// <summary>WriteTo 后 Read 回来五个字段一致。</summary>
        [Fact]
        public void WriteToThenReadRoundTripsAllFields()
        {
            using var workspace = new PoolTestWorkspace();
            var filePath = Path.Combine(workspace.Root, "_Generated", "指纹.json");
            var schemaHash = ProvisionFingerprint.ComputeSchemaHash(CreateSchema("需求"));
            var digestHash = ProvisionFingerprint.ComputeDesignDigestHash(workspace.Root);
            var written = ProvisionFingerprint.Create("feishu", ">=1.0 <2.0", schemaHash, digestHash);
            written.WriteTo(filePath);

            var readBack = ProvisionFingerprint.Read(filePath);

            Assert.NotNull(readBack);
            Assert.Equal(written.SchemaHash, readBack.SchemaHash);
            Assert.Equal(written.DesignDigestHash, readBack.DesignDigestHash);
            Assert.Equal(written.GeneratedAt, readBack.GeneratedAt);
            Assert.Equal(written.DriverName, readBack.DriverName);
            Assert.Equal(written.ContractRange, readBack.ContractRange);
        }

        /// <summary>文件不存在时 Read 返回 null。</summary>
        [Fact]
        public void ReadReturnsNullWhenFileMissing()
        {
            using var workspace = new PoolTestWorkspace();

            Assert.Null(ProvisionFingerprint.Read(Path.Combine(workspace.Root, "不存在的指纹.json")));
        }

        /// <summary>造一份固定的需求 schema：两字段两分类型必填一个状态机。</summary>
        private static PoolSchema CreateSchema(string entityName)
        {
            var fields = new List<PoolSchemaField>
            {
                new PoolSchemaField("id", "string", true, null, "", 0, "工程", false, false),
                new PoolSchemaField("类型", "enum", true, new[] { "系统", "修改" }, "", 0, "策划端", false, false),
                new PoolSchemaField("标题", "string", true, null, "", 0, "策划端", false, true),
                new PoolSchemaField("验收标准", "array", true, null, "string", 1, "策划端", false, true)
            };

            var requiredByType = new Dictionary<string, IReadOnlyList<string>>
            {
                ["系统"] = new[] { "目标", "玩法" },
                ["修改"] = new[] { "现状", "期望" }
            };

            var transitions = new List<PoolStateTransition>
            {
                new PoolStateTransition("草稿", "已确认", "确认人"),
                new PoolStateTransition("已确认", "进行中", "引擎")
            };

            return new PoolSchema(
                "1.0.0", entityName, "^REQ-\\d{4}$",
                fields, requiredByType, new PoolStateMachine("草稿", transitions));
        }
    }
}
