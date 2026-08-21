using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>建表描述里的一列：字段名、下游类型与锁定后的可改性等。</summary>
    public sealed class TableFieldDescription
    {
        /// <summary>
        /// 构造一列字段描述。
        /// </summary>
        /// <param name="name">字段名。</param>
        /// <param name="downstreamType">下游字段类型，如 文本 / 单选 / 多行文本。</param>
        /// <param name="isRequired">是否必填。</param>
        /// <param name="enumValues">单选项取值列表，传 null 视为空列表。</param>
        /// <param name="ownership">字段归属方，如 策划端 / 工程。</param>
        /// <param name="isEditableAfterLock">锁定后是否可改。</param>
        /// <param name="logicalType">schema 里的逻辑类型（string / 数组 / 对象 / bool / enum…）。
        /// 下游类型只说「存成什么列」，逻辑类型说「这一列里那串文字原本是什么」——
        /// 数组存进多行文本之后，只有它能告诉读回来的人该不该切回数组。缺省空串表示不声明。</param>
        public TableFieldDescription(
            string name,
            string downstreamType,
            bool isRequired,
            IReadOnlyList<string> enumValues,
            string ownership,
            bool isEditableAfterLock,
            string logicalType = "")
        {
            Name = name ?? "";
            DownstreamType = downstreamType ?? "";
            IsRequired = isRequired;
            EnumValues = enumValues ?? Array.Empty<string>();
            Ownership = ownership ?? "";
            IsEditableAfterLock = isEditableAfterLock;
            LogicalType = logicalType ?? "";
        }

        /// <summary>字段名。</summary>
        public string Name { get; }

        /// <summary>下游字段类型，如 文本 / 单选 / 多行文本。</summary>
        public string DownstreamType { get; }

        /// <summary>是否必填。</summary>
        public bool IsRequired { get; }

        /// <summary>单选项取值列表。</summary>
        public IReadOnlyList<string> EnumValues { get; }

        /// <summary>字段归属方，如 策划端 / 工程。</summary>
        public string Ownership { get; }

        /// <summary>锁定后是否可改。</summary>
        public bool IsEditableAfterLock { get; }

        /// <summary>schema 里的逻辑类型（string / 数组 / 对象 / bool / enum…）；空串表示不声明。</summary>
        public string LogicalType { get; }
    }

    /// <summary>建表描述里的一张表单：按某个类型值分组后的可见字段。</summary>
    public sealed class TableFormDescription
    {
        /// <summary>
        /// 构造一张表单描述。
        /// </summary>
        /// <param name="typeName">分组值，即这个表单对应的类型名。</param>
        /// <param name="fieldNames">表单可见的字段名列表，传 null 视为空列表。</param>
        public TableFormDescription(string typeName, IReadOnlyList<string> fieldNames)
        {
            TypeName = typeName ?? "";
            FieldNames = fieldNames ?? Array.Empty<string>();
        }

        /// <summary>分组值，即这个表单对应的类型名。</summary>
        public string TypeName { get; }

        /// <summary>表单可见的字段名列表。</summary>
        public IReadOnlyList<string> FieldNames { get; }
    }

    /// <summary>一份建表描述：表名、字段清单与按类型分组的多张表单，可整体落成 JSON。</summary>
    public sealed class TableDescription
    {
        /// <summary>
        /// 构造一份建表描述。
        /// </summary>
        /// <param name="tableName">表名。</param>
        /// <param name="fields">字段清单，传 null 视为空列表。</param>
        /// <param name="forms">表单清单，传 null 视为空列表。</param>
        public TableDescription(string tableName, IReadOnlyList<TableFieldDescription> fields, IReadOnlyList<TableFormDescription> forms)
        {
            TableName = tableName ?? "";
            Fields = fields ?? Array.Empty<TableFieldDescription>();
            Forms = forms ?? Array.Empty<TableFormDescription>();
        }

        /// <summary>表名。</summary>
        public string TableName { get; }

        /// <summary>字段清单。</summary>
        public IReadOnlyList<TableFieldDescription> Fields { get; }

        /// <summary>表单清单。</summary>
        public IReadOnlyList<TableFormDescription> Forms { get; }

        /// <summary>
        /// 把建表描述写成 JSON 文件：顶层键 表名 / 字段 / 表单；
        /// 字段项键 名称 / 下游类型 / 必填 / 单选项 / 所有权 / 锁定后可改；
        /// 表单项键 类型 / 字段。
        /// </summary>
        /// <param name="filePath">输出路径。</param>
        public void WriteTo(string filePath)
        {
            var fieldPayload = new List<object>();
            foreach (var field in Fields)
            {
                fieldPayload.Add(new Dictionary<string, object>
                {
                    ["名称"] = field.Name,
                    ["下游类型"] = field.DownstreamType,
                    ["必填"] = field.IsRequired,
                    ["单选项"] = field.EnumValues,
                    ["所有权"] = field.Ownership,
                    ["锁定后可改"] = field.IsEditableAfterLock,
                    ["逻辑类型"] = field.LogicalType
                });
            }

            var formPayload = new List<object>();
            foreach (var form in Forms)
            {
                formPayload.Add(new Dictionary<string, object>
                {
                    ["类型"] = form.TypeName,
                    ["字段"] = form.FieldNames
                });
            }

            var payload = new Dictionary<string, object>
            {
                ["表名"] = TableName,
                ["字段"] = fieldPayload,
                ["表单"] = formPayload
            };

            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(payload, JsonOptions),
                new UTF8Encoding(false));
        }

        /// <summary>写建表描述用的序列化选项：缩进 + 中文不转义。</summary>
        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
    }

    /// <summary>把合并 schema 与 driver 自述合成一份下游建表描述。</summary>
    public static class TableDescriptionBuilder
    {
        /// <summary>
        /// 合并 schema → 建表描述：表名取实体名，字段按原序映射下游类型，
        /// 表单按 driver 的分组字段枚举值各出一张。
        /// </summary>
        /// <param name="schema">合并后的池子 schema。</param>
        /// <param name="driver">下游 driver 的自述。</param>
        public static TableDescription Build(PoolSchema schema, BridgeDriverDescriptor driver)
        {
            var fields = new List<TableFieldDescription>();
            foreach (var field in schema.Fields)
            {
                var downstreamType = field.EnumValues.Count > 0
                    ? driver.MapFieldType("enum")
                    : driver.MapFieldType(field.FieldType);
                fields.Add(new TableFieldDescription(
                    field.Name,
                    downstreamType,
                    field.IsRequired,
                    field.EnumValues,
                    field.Ownership,
                    field.IsEditableAfterLock,
                    field.FieldType));
            }

            // 分类型必填的那几个字段（目标 / 玩法 / 现状 / 期望 / 复现步骤 / 实际）**不在 schema.Fields 里**，
            // 它们只出现在「分类型必填」表里。原来只把它们摆进表单、没给列——
            // 结果是一条合法的「系统」需求根本写不进下游表（真跑撞出来的：
            // 写记录时报「字段『目标』不在建表描述里」）。表单引用一个不存在的列，本身就是坏的。
            // 所以这里给它们补上列：可选、归策划端、锁定后可改（它们是内容不是状态）。
            foreach (var pair in schema.RequiredByType)
            {
                foreach (var name in pair.Value)
                {
                    if (fields.Exists(field => string.Equals(field.Name, name, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    fields.Add(new TableFieldDescription(
                        name,
                        driver.MapFieldType("string"),
                        isRequired: false,
                        enumValues: Array.Empty<string>(),
                        ownership: "策划端",
                        isEditableAfterLock: false,
                        logicalType: "string"));
                }
            }

            var forms = new List<TableFormDescription>();
            var groupingField = schema.FindField(driver.FormGroupingField);
            if (groupingField != null && groupingField.EnumValues.Count > 0)
            {
                var requiredFieldNames = new List<string>();
                foreach (var field in schema.Fields)
                {
                    if (field.IsRequired)
                    {
                        requiredFieldNames.Add(field.Name);
                    }
                }

                foreach (var groupingValue in groupingField.EnumValues)
                {
                    var fieldNames = new List<string>(requiredFieldNames);
                    if (schema.RequiredByType.TryGetValue(groupingValue, out var typeRequired))
                    {
                        foreach (var name in typeRequired)
                        {
                            if (!fieldNames.Contains(name, StringComparer.Ordinal))
                            {
                                fieldNames.Add(name);
                            }
                        }
                    }

                    forms.Add(new TableFormDescription(groupingValue, fieldNames));
                }
            }

            return new TableDescription(schema.EntityName, fields, forms);
        }
    }
}
