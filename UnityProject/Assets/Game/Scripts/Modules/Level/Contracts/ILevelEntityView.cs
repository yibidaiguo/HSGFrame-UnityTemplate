using System.Collections.Generic;
using Unity.Mathematics;

namespace Template.Level.Contracts
{
    /// <summary>
    /// 关卡实体的只读视图：模块外读关卡实体的唯一入口。
    /// </summary>
    /// <remarks>
    /// 存在的理由是 R2：关卡实体的运行时信息本来只挂在 <c>Level/View/</c> 的组件上，
    /// 而 R2 查的是命名空间与目录（跟程序集无关），模块外只要写出 <c>Template.Level.View.*</c> 就红。
    /// 把「编号、类别、位置、参数」这四样收进本接口，模块外按接口读，
    /// 既拿得到数据又不碰模块的私有面。位置用 <c>float3</c> 而不是 <c>Level.Data</c> 里的类型，
    /// 免得调用方为了写出参数类型又撞一次 R2。
    /// </remarks>
    public interface ILevelEntityView
    {
        /// <summary>实体编号，与关卡 JSON 里的编号一致。</summary>
        string EntityId { get; }

        /// <summary>实体类别，例如 NPC / 触发器 / 刷怪点。</summary>
        string EntityKind { get; }

        /// <summary>实体在世界里的位置。</summary>
        float3 Position { get; }

        /// <summary>实体的自由参数，只读字典。</summary>
        IReadOnlyDictionary<string, string> Parameters { get; }

        /// <summary>按键取一个自由参数，取不到返回 false。</summary>
        /// <param name="parameterKey">参数键。</param>
        /// <param name="parameterValue">取到的参数值，取不到时为 null。</param>
        bool TryGetParameter(string parameterKey, out string parameterValue);
    }
}
