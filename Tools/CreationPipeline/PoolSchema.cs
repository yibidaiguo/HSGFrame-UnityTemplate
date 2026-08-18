using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>状态机的一条转换：从某个状态到另一个状态，由谁驱动。</summary>
    public sealed class PoolStateTransition
    {
        /// <summary>
        /// 构造一条状态转换。
        /// </summary>
        /// <param name="from">起始状态，* 表示任意状态。</param>
        /// <param name="to">目标状态。</param>
        /// <param name="actor">驱动该转换的角色。</param>
        public PoolStateTransition(string from, string to, string actor)
        {
            From = from;
            To = to;
            Actor = actor;
        }

        /// <summary>起始状态，* 表示任意状态。</summary>
        public string From { get; }

        /// <summary>目标状态。</summary>
        public string To { get; }

        /// <summary>驱动该转换的角色。</summary>
        public string Actor { get; }
    }

    /// <summary>实体的状态机定义：初始状态与全部转换。</summary>
    public sealed class PoolStateMachine
    {
        /// <summary>
        /// 构造一个状态机定义。
        /// </summary>
        /// <param name="initialState">初始状态。</param>
        /// <param name="transitions">转换列表，传 null 视为空列表。</param>
        public PoolStateMachine(string initialState, IReadOnlyList<PoolStateTransition> transitions)
        {
            InitialState = initialState;
            Transitions = transitions ?? Array.Empty<PoolStateTransition>();
        }

        /// <summary>初始状态。</summary>
        public string InitialState { get; }

        /// <summary>转换列表。</summary>
        public IReadOnlyList<PoolStateTransition> Transitions { get; }
    }

    /// <summary>schema 中的一个字段定义：名称、类型、必填与所有权等全部约束。</summary>
    public sealed class PoolSchemaField
    {
        /// <summary>
        /// 构造一个字段定义。
        /// </summary>
        /// <param name="name">字段名。</param>
        /// <param name="fieldType">字段类型：string / enum / 数组 / 对象 / bool 等。</param>
        /// <param name="isRequired">是否必填。</param>
        /// <param name="enumValues">枚举取值列表，传 null 视为空列表。</param>
        /// <param name="elementType">数组元素的类型，非数组字段为空串。</param>
        /// <param name="minimumCount">数组最少条数，非数组字段为 0。</param>
        /// <param name="ownership">字段归属方：工程 / 策划端 等。</param>
        /// <param name="isNullable">是否可空。</param>
        /// <param name="isEditableAfterLock">锁定后是否可改。</param>
        public PoolSchemaField(
            string name,
            string fieldType,
            bool isRequired,
            IReadOnlyList<string> enumValues,
            string elementType,
            int minimumCount,
            string ownership,
            bool isNullable,
            bool isEditableAfterLock)
        {
            Name = name;
            FieldType = fieldType;
            IsRequired = isRequired;
            EnumValues = enumValues ?? Array.Empty<string>();
            ElementType = elementType;
            MinimumCount = minimumCount;
            Ownership = ownership;
            IsNullable = isNullable;
            IsEditableAfterLock = isEditableAfterLock;
        }

        /// <summary>字段名。</summary>
        public string Name { get; }

        /// <summary>字段类型：string / enum / 数组 / 对象 / bool 等。</summary>
        public string FieldType { get; }

        /// <summary>是否必填。</summary>
        public bool IsRequired { get; }

        /// <summary>枚举取值列表。</summary>
        public IReadOnlyList<string> EnumValues { get; }

        /// <summary>数组元素的类型，非数组字段为空串。</summary>
        public string ElementType { get; }

        /// <summary>数组最少条数，非数组字段为 0。</summary>
        public int MinimumCount { get; }

        /// <summary>字段归属方：工程 / 策划端 等。</summary>
        public string Ownership { get; }

        /// <summary>是否可空。</summary>
        public bool IsNullable { get; }

        /// <summary>锁定后是否可改。</summary>
        public bool IsEditableAfterLock { get; }
    }

    /// <summary>一份池子实体的完整 schema：版本、实体名、id 模式、字段、分类型必填与状态机。</summary>
    public sealed class PoolSchema
    {
        /// <summary>
        /// 构造一份 schema。
        /// </summary>
        /// <param name="schemaVersion">schema 版本号。</param>
        /// <param name="entityName">实体名，如「需求」「工作项」。</param>
        /// <param name="identifierPattern">id 的正则模式。</param>
        /// <param name="fields">字段列表，传 null 视为空列表。</param>
        /// <param name="requiredByType">分类型必填：类型名到字段名列表的映射，传 null 视为空字典。</param>
        /// <param name="stateMachine">状态机定义，可为 null（表示该实体没有状态机）。</param>
        public PoolSchema(
            string schemaVersion,
            string entityName,
            string identifierPattern,
            IReadOnlyList<PoolSchemaField> fields,
            IReadOnlyDictionary<string, IReadOnlyList<string>> requiredByType,
            PoolStateMachine stateMachine)
        {
            SchemaVersion = schemaVersion;
            EntityName = entityName;
            IdentifierPattern = identifierPattern;
            Fields = fields ?? Array.Empty<PoolSchemaField>();
            RequiredByType = requiredByType ?? new Dictionary<string, IReadOnlyList<string>>();
            StateMachine = stateMachine;
        }

        /// <summary>schema 版本号。</summary>
        public string SchemaVersion { get; }

        /// <summary>实体名，如「需求」「工作项」。</summary>
        public string EntityName { get; }

        /// <summary>id 的正则模式。</summary>
        public string IdentifierPattern { get; }

        /// <summary>字段列表。</summary>
        public IReadOnlyList<PoolSchemaField> Fields { get; }

        /// <summary>分类型必填：类型名到字段名列表的映射。</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredByType { get; }

        /// <summary>状态机定义，可为 null（表示该实体没有状态机）。</summary>
        public PoolStateMachine StateMachine { get; }

        /// <summary>
        /// 按名称精确匹配查找字段，找不到返回 null。
        /// </summary>
        /// <param name="name">字段名。</param>
        public PoolSchemaField FindField(string name)
        {
            foreach (var field in Fields)
            {
                if (string.Equals(field.Name, name, StringComparison.Ordinal))
                {
                    return field;
                }
            }

            return null;
        }
    }
}
