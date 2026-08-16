using System.Collections.Generic;

namespace Template.Level.Contracts
{
    /// <summary>
    /// 实体类别到资源地址的只读映射：把关卡 JSON 里的「类别」接到 <c>ResourceArt/</c> 里的预制体上。
    /// </summary>
    /// <remarks>
    /// 地址是 YooAsset 的寻址名，收集器用的是 AddressByFileName，
    /// 所以地址就是预制体的文件名去掉扩展名（例：类别「NPC」对上地址 <c>P_Npc</c>）。
    /// </remarks>
    public interface ILevelEntityResourceMap
    {
        /// <summary>映射里登记的全部类别，按序数序排列。</summary>
        IReadOnlyList<string> EntityKinds { get; }

        /// <summary>按类别取资源地址，类别没登记时返回 false。</summary>
        /// <param name="entityKind">实体类别。</param>
        /// <param name="resourceAddress">取到的资源地址，取不到时为 null。</param>
        bool TryGetResourceAddress(string entityKind, out string resourceAddress);
    }
}
