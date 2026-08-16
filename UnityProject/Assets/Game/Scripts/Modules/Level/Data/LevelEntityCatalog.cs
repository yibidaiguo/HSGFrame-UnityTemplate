using System;
using System.Collections.Generic;
using Template.Level.Contracts;

namespace Template.Level.Data
{
    /// <summary>关卡实体名录的实现：收下一批标记，按编号与类别建索引。</summary>
    /// <remarks>
    /// 建一次索引就不再变：关卡装配完之后实体集合是稳定的，
    /// 每次检索都去扫场景既慢又会把「装配中」的半成品状态漏出去。
    /// </remarks>
    public sealed class LevelEntityCatalog : ILevelEntityCatalog
    {
        private static readonly ILevelEntityView[] EmptyEntities = new ILevelEntityView[0];

        private readonly List<ILevelEntityView> _entities = new List<ILevelEntityView>();
        private readonly Dictionary<string, ILevelEntityView> _byEntityId = new Dictionary<string, ILevelEntityView>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ILevelEntityView>> _byEntityKind = new Dictionary<string, List<ILevelEntityView>>(StringComparer.Ordinal);

        /// <summary>用一批实体视图构造名录。</summary>
        /// <param name="entities">要收进名录的实体，null 元素会被跳过。</param>
        public LevelEntityCatalog(IEnumerable<ILevelEntityView> entities)
        {
            if (entities == null)
            {
                return;
            }

            foreach (var entity in entities)
            {
                if (entity == null)
                {
                    continue;
                }

                _entities.Add(entity);

                // 编号重复时留第一条：关卡校验器本来就把编号唯一性当硬规矩，
                // 走到这里说明校验被绕过了，这时「按编号查到谁」保持确定比抛异常更有用。
                if (!string.IsNullOrEmpty(entity.EntityId) && !_byEntityId.ContainsKey(entity.EntityId))
                {
                    _byEntityId[entity.EntityId] = entity;
                }

                var kind = entity.EntityKind ?? string.Empty;
                if (!_byEntityKind.TryGetValue(kind, out var bucket))
                {
                    bucket = new List<ILevelEntityView>();
                    _byEntityKind[kind] = bucket;
                }

                bucket.Add(entity);
            }
        }

        /// <summary>名录里的全部实体，按关卡构建顺序排列。</summary>
        public IReadOnlyList<ILevelEntityView> Entities => _entities;

        /// <summary>按编号找一个实体，找不到返回 false。</summary>
        /// <param name="entityId">实体编号。</param>
        /// <param name="entity">找到的实体，找不到时为 null。</param>
        public bool TryFind(string entityId, out ILevelEntityView entity)
        {
            if (string.IsNullOrEmpty(entityId))
            {
                entity = null;
                return false;
            }

            return _byEntityId.TryGetValue(entityId, out entity);
        }

        /// <summary>按类别取出全部实体，没有匹配时返回空清单。</summary>
        /// <param name="entityKind">实体类别。</param>
        public IReadOnlyList<ILevelEntityView> FindByKind(string entityKind)
        {
            if (entityKind != null && _byEntityKind.TryGetValue(entityKind, out var bucket))
            {
                return bucket;
            }

            return EmptyEntities;
        }
    }
}
