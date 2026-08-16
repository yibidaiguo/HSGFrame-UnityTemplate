using System.Collections.Generic;

namespace Template.Level.Contracts
{
    /// <summary>当前关卡里全部实体的只读名录，模块外按编号或类别检索。</summary>
    public interface ILevelEntityCatalog
    {
        /// <summary>名录里的全部实体，按关卡构建顺序排列。</summary>
        IReadOnlyList<ILevelEntityView> Entities { get; }

        /// <summary>按编号找一个实体，找不到返回 false。</summary>
        /// <param name="entityId">实体编号。</param>
        /// <param name="entity">找到的实体，找不到时为 null。</param>
        bool TryFind(string entityId, out ILevelEntityView entity);

        /// <summary>按类别取出全部实体，没有匹配时返回空清单。</summary>
        /// <param name="entityKind">实体类别。</param>
        IReadOnlyList<ILevelEntityView> FindByKind(string entityKind);
    }
}
