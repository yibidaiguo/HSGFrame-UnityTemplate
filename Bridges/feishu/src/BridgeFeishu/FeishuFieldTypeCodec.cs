using System;
using System.Collections.Generic;

namespace Template.Bridges.Feishu
{
    /// <summary>
    /// 下游字段类型名到飞书多维表格建表接口数字类型码的映射（字段类型映射的第二段）。
    /// 第一段（逻辑类型 → 下游类型名）在 driver.json 的「字段类型映射」里，由供给引擎泛化完成。
    /// 第二段是飞书私有的线上协议——1/2/3/4/7 这几个数出了飞书毫无意义，
    /// 所以它**只能住在这个桥里**，既不进 driver.json，也绝不能放进 Tools/CreationPipeline。
    /// 决策 11 那句「供给引擎完全泛化」就是拦这件事的：引擎里一旦躺着某个下游的线上协议，
    /// 换一个需求编辑端就要改引擎。
    /// **下游边界门禁拦不住这种越界**——它只逐行 grep driver 名，
    /// 把类名写成「目标平台」就能绕过去，而绕过去的那一刻边界已经破了（决策 93）。
    /// 未知类型必须显式报错，不许默默回落——默默给一个默认码会把一列单选变成自由文本，
    /// 而表建出来就撤不掉了（写进别人工作区的动作只做一次，建错没有自动撤的路）。
    /// </summary>
    public static class FeishuFieldTypeCodec
    {
        /// <summary>多行文本/文本类型的飞书数字码。</summary>
        public const int TextCode = 1;

        /// <summary>数字类型的飞书数字码。</summary>
        public const int NumberCode = 2;

        /// <summary>单选类型的飞书数字码。</summary>
        public const int SingleSelectCode = 3;

        /// <summary>多选类型的飞书数字码。</summary>
        public const int MultiSelectCode = 4;

        /// <summary>复选框类型的飞书数字码。</summary>
        public const int CheckboxCode = 7;

        /// <summary>
        /// 下游类型名 → 飞书数字码；未知类型返回 false。
        /// </summary>
        /// <param name="downstreamType">下游字段类型名，如 文本 / 单选 / 多行文本。</param>
        /// <param name="typeCode">映射到的飞书数字码；失败时为 0。</param>
        /// <param name="failureReason">未知类型时的报错原因；成功时为 ""。</param>
        public static bool TryMap(string downstreamType, out int typeCode, out string failureReason)
        {
            typeCode = 0;
            failureReason = "";

            if (string.IsNullOrWhiteSpace(downstreamType))
            {
                failureReason = "字段类型名为空";
                return false;
            }

            if (KnownTypeCodes.TryGetValue(downstreamType, out var knownCode))
            {
                typeCode = knownCode;
                return true;
            }

            failureReason = $"不认识的字段类型「{downstreamType}」，已知类型："
                + string.Join("、", KnownTypeCodes.Keys);
            return false;
        }

        /// <summary>已知类型名 → 数字码。键是下游类型名，值是飞书建表接口的数字码。</summary>
        private static readonly IReadOnlyDictionary<string, int> KnownTypeCodes =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["文本"] = TextCode,
                ["多行文本"] = TextCode,
                ["数字"] = NumberCode,
                ["单选"] = SingleSelectCode,
                ["多选"] = MultiSelectCode,
                ["复选框"] = CheckboxCode
            };

        /// <summary>该下游类型建表时是否需要带选项列表（单选 / 多选需要，其余不需要）。</summary>
        /// <param name="downstreamType">下游字段类型名。</param>
        public static bool RequiresOptions(string downstreamType)
        {
            if (!TryMap(downstreamType, out var typeCode, out _))
            {
                return false;
            }

            return typeCode == SingleSelectCode || typeCode == MultiSelectCode;
        }
    }
}
