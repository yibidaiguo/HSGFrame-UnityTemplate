using System.Collections.Generic;
using UnityEngine;

namespace Template.Presentation.Level
{
    /// <summary>逻辑实体标记：把关卡 JSON 里 Transform 装不下的信息挂在场景物体上。</summary>
    [DisallowMultipleComponent]
    public sealed class LogicEntityMarker : MonoBehaviour
    {
        [SerializeField] [Tooltip("实体编号，与关卡 JSON 里的编号一致")]
        private string _entityId;

        [SerializeField] [Tooltip("实体类别，例如 NPC / 触发器 / 刷怪点")]
        private string _entityKind;

        [SerializeField] [Tooltip("实体的自由参数，键值对")]
        private List<EntityParameterEntry> _parameters = new List<EntityParameterEntry>();

        /// <summary>实体编号。</summary>
        public string EntityId
        {
            get => _entityId;
            set => _entityId = value;
        }

        /// <summary>实体类别。</summary>
        public string EntityKind
        {
            get => _entityKind;
            set => _entityKind = value;
        }

        /// <summary>实体的自由参数，键值对清单。</summary>
        public List<EntityParameterEntry> Parameters
        {
            get => _parameters;
            set => _parameters = value;
        }

        /// <summary>把参数清单读成字典，重复键以最后一条为准。</summary>
        public Dictionary<string, string> ToParameterDictionary()
        {
            var dictionary = new Dictionary<string, string>();
            if (_parameters == null)
            {
                return dictionary;
            }

            foreach (var entry in _parameters)
            {
                dictionary[entry.Key] = entry.Value;
            }

            return dictionary;
        }

        /// <summary>用一份字典覆盖参数清单，保持字典的键顺序。</summary>
        public void SetParameters(IReadOnlyDictionary<string, string> parameters)
        {
            // 这里刻意不按序数序重排：System.Text.Json 序列化字典保持插入顺序，重排会让导出 json 的
            // 参数键序偏离源 json，破坏往返后的逐字符比对。源字典的键序来自源 json 的键出现顺序，
            // 本身确定，照抄即稳定。
            var entries = new List<EntityParameterEntry>();
            if (parameters != null)
            {
                foreach (var pair in parameters)
                {
                    entries.Add(new EntityParameterEntry { Key = pair.Key, Value = pair.Value });
                }
            }

            _parameters = entries;
        }
    }
}
