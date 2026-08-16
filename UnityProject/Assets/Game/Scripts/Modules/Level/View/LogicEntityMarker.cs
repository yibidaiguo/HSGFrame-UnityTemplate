using System.Collections.Generic;
using Template.Level.Contracts;
using Unity.Mathematics;
using UnityEngine;

namespace Template.Level.View
{
    /// <summary>逻辑实体标记：把关卡 JSON 里 Transform 装不下的信息挂在场景物体上。</summary>
    /// <remarks>
    /// 同时是 <see cref="ILevelEntityView"/> 的落地实现——模块外拿到的是接口，
    /// 本类型自身仍在模块私有面里（R2）。位置不另存一份，直接读 Transform，
    /// 免得「组件上记的位置」与「物体真在哪」两份数据各走各的。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LogicEntityMarker : MonoBehaviour, ILevelEntityView
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

        /// <summary>实体在世界里的位置，直接取自 Transform。</summary>
        public float3 Position
        {
            get
            {
                var position = transform.position;
                return new float3(position.x, position.y, position.z);
            }
        }

        /// <summary>实体的自由参数，只读字典。</summary>
        IReadOnlyDictionary<string, string> ILevelEntityView.Parameters => ToParameterDictionary();

        /// <summary>按键取一个自由参数，取不到返回 false。</summary>
        /// <param name="parameterKey">参数键。</param>
        /// <param name="parameterValue">取到的参数值，取不到时为 null。</param>
        public bool TryGetParameter(string parameterKey, out string parameterValue)
        {
            parameterValue = null;
            if (string.IsNullOrEmpty(parameterKey) || _parameters == null)
            {
                return false;
            }

            // 顺着清单扫而不是先建字典：参数条数是个位数，建一次字典比扫一遍还贵，
            // 而这个方法在装配关卡时每个实体都要调好几次。重复键取最后一条，与 ToParameterDictionary 一致。
            var found = false;
            foreach (var entry in _parameters)
            {
                if (string.Equals(entry.Key, parameterKey, System.StringComparison.Ordinal))
                {
                    parameterValue = entry.Value;
                    found = true;
                }
            }

            return found;
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
