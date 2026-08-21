using System;
using System.Collections.Generic;
using Template.Level.Contracts;

namespace Template.Level.Data
{
    /// <summary>
    /// 实体类别到资源地址映射的纯 C# 实现：构造时收下一批条目，之后只读。
    /// </summary>
    /// <remarks>
    /// 刻意不在这里读文件：映射的事实源是 <c>Settings/Level/</c> 下的那份 <c>.asset</c>，
    /// 由 View 侧的包装件序列化。本类只负责「查得对不对」这一半，
    /// 于是这一半能在纯 dotnet 下跑测试，不必开 Unity。
    /// </remarks>
    public sealed class LevelEntityResourceMap : ILevelEntityResourceMap
    {
        private readonly Dictionary<string, string> _addressByKind;
        private readonly List<string> _entityKinds;

        /// <summary>用一批「类别→地址」条目构造。</summary>
        /// <param name="entries">映射条目，键是类别、值是资源地址。</param>
        /// <exception cref="ArgumentException">类别或地址为空白，或同一个类别登记了两次。</exception>
        public LevelEntityResourceMap(IEnumerable<KeyValuePair<string, string>> entries)
        {
            _addressByKind = new Dictionary<string, string>(StringComparer.Ordinal);

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key))
                    {
                        throw new ArgumentException(
                            "位置：LevelEntityResourceMap 构造函数；原因：实体类别为空白；修复：给每一条映射写上类别；参考：Levels/<关卡>/block-*.json 里的「类别」字段",
                            nameof(entries));
                    }

                    if (string.IsNullOrWhiteSpace(entry.Value))
                    {
                        throw new ArgumentException(
                            $"位置：LevelEntityResourceMap 构造函数；原因：类别「{entry.Key}」的资源地址为空白；修复：填上 ResourceArt/Level 下预制体的文件名（不含扩展名）；参考：Assets/Game/ResourceArt/Level/",
                            nameof(entries));
                    }

                    // 重复键不做「后一条覆盖」：映射是配置，一个类别配两个地址属于配错了，
                    // 悄悄取其中一条会让「装出来的东西不对」变成一桩查不到源头的悬案。
                    if (_addressByKind.ContainsKey(entry.Key))
                    {
                        throw new ArgumentException(
                            $"位置：LevelEntityResourceMap 构造函数；原因：实体类别「{entry.Key}」登记了两次；修复：一个类别只留一条映射；参考：Assets/Game/Settings/Level/实体资源映射.asset",
                            nameof(entries));
                    }

                    _addressByKind[entry.Key] = entry.Value;
                }
            }

            _entityKinds = new List<string>(_addressByKind.Keys);
            _entityKinds.Sort(StringComparer.Ordinal);
        }

        /// <summary>映射里登记的全部类别，按序数序排列。</summary>
        public IReadOnlyList<string> EntityKinds => _entityKinds;

        /// <summary>按类别取资源地址，类别没登记时返回 false。</summary>
        /// <param name="entityKind">实体类别。</param>
        /// <param name="resourceAddress">取到的资源地址，取不到时为 null。</param>
        public bool TryGetResourceAddress(string entityKind, out string resourceAddress)
        {
            if (string.IsNullOrEmpty(entityKind))
            {
                resourceAddress = null;
                return false;
            }

            return _addressByKind.TryGetValue(entityKind, out resourceAddress);
        }
    }
}
