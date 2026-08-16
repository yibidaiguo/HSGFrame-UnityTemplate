using System;
using System.Collections.Generic;
using Template.Level.Contracts;
using Template.Level.Data;
using UnityEngine;

namespace Template.Level.View
{
    /// <summary>「实体类别 → 资源地址」映射里的一条。</summary>
    [Serializable]
    public sealed class LevelEntityResourceEntry
    {
        [SerializeField] [Tooltip("实体类别，与关卡 JSON 里的「类别」一字不差")]
        private string _entityKind;

        [SerializeField] [Tooltip("YooAsset 寻址名，等于 ResourceArt/Level 下预制体的文件名（不含扩展名）")]
        private string _resourceAddress;

        /// <summary>实体类别。</summary>
        public string EntityKind
        {
            get => _entityKind;
            set => _entityKind = value;
        }

        /// <summary>资源地址。</summary>
        public string ResourceAddress
        {
            get => _resourceAddress;
            set => _resourceAddress = value;
        }
    }

    /// <summary>
    /// 实体类别到资源地址映射的资产件：这份 <c>.asset</c> 就是映射的事实源。
    /// </summary>
    /// <remarks>
    /// 查表逻辑不写在这里，而是交给 <see cref="LevelEntityResourceMap"/>——
    /// 那半边零 UnityEngine，能在纯 dotnet 下跑测试；本类型只管把编辑器里配的东西序列化下来。
    /// </remarks>
    [CreateAssetMenu(fileName = "实体资源映射", menuName = "Template/关卡/实体资源映射", order = 0)]
    public sealed class LevelEntityResourceMapAsset : ScriptableObject
    {
        [SerializeField] [Tooltip("每一条把一个实体类别接到一个资源地址上")]
        private List<LevelEntityResourceEntry> _entries = new List<LevelEntityResourceEntry>();

        /// <summary>资产里配着的全部映射条目。</summary>
        public IReadOnlyList<LevelEntityResourceEntry> Entries => _entries;

        /// <summary>把资产里的条目读成一份只读映射。</summary>
        /// <exception cref="ArgumentException">条目有空白字段或类别重复，异常信息里带修复指引。</exception>
        public ILevelEntityResourceMap ToResourceMap()
        {
            var pairs = new List<KeyValuePair<string, string>>();
            if (_entries != null)
            {
                foreach (var entry in _entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    pairs.Add(new KeyValuePair<string, string>(entry.EntityKind, entry.ResourceAddress));
                }
            }

            return new LevelEntityResourceMap(pairs);
        }
    }
}
