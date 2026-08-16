using System;

namespace Template.Presentation.Level
{
    /// <summary>实体自由参数的一条键值，做成可序列化的类是为了让参数在 Inspector 里看得见。</summary>
    [Serializable]
    public sealed class EntityParameterEntry
    {
        /// <summary>参数名。</summary>
        public string Key;

        /// <summary>参数值。</summary>
        public string Value;
    }
}
