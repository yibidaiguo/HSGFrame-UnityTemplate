using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 把职责清单合成「专项」表：固定 id / 名称 / 说明 三列，
    /// 每个职责加一个「认领.&lt;职责&gt;」的人员多选列，供下游成员认领专项。
    /// </summary>
    public static class EpicTableBuilder
    {
        /// <summary>
        /// 建「专项」表的建表描述：表名固定为 专项，字段为固定三列加每职责一列认领列，表单为空。
        /// </summary>
        /// <param name="poolRoot">池子根目录，用于读卡片路由表取职责清单。</param>
        /// <param name="driver">下游 driver 的自述，固定三列的下游类型走它的 string 映射。</param>
        public static TableDescription Build(string poolRoot, BridgeDriverDescriptor driver)
        {
            var fields = new List<TableFieldDescription>
            {
                new("id", driver.MapFieldType("string"), true, Array.Empty<string>(), "策划端", true),
                new("名称", driver.MapFieldType("string"), true, Array.Empty<string>(), "策划端", true),
                new("说明", driver.MapFieldType("string"), false, Array.Empty<string>(), "策划端", true)
            };

            foreach (var duty in CollectDuties(poolRoot))
            {
                fields.Add(new TableFieldDescription(
                    $"认领.{duty}",
                    "人员多选",
                    false,
                    Array.Empty<string>(),
                    "下游成员",
                    true));
            }

            return new TableDescription("专项", fields, Array.Empty<TableFormDescription>());
        }

        /// <summary>
        /// 取职责清单：卡片路由表的全部职责值去重后按序数序排序；
        /// 取出来为空则退回内建默认路由表的职责值。
        /// </summary>
        /// <param name="poolRoot">池子根目录。</param>
        private static IReadOnlyList<string> CollectDuties(string poolRoot)
        {
            var duties = new SortedSet<string>(StringComparer.Ordinal);
            AddDuties(duties, CardRouteTable.Load(poolRoot));
            if (duties.Count == 0)
            {
                AddDuties(duties, CardRouteTable.Default());
            }

            return new List<string>(duties);
        }

        /// <summary>
        /// 伪职责：这些名字在路由表里是「运行时才解析成具体人」的占位，
        /// 不是可以被谁认领的工种，所以不给它们开认领列。见子文档 01 与锁定决策 9。
        /// </summary>
        private static readonly string[] PseudoDuties = { "提出人", "管理员" };

        /// <summary>把一张路由表的全部职责值去重后并入集合，伪职责跳过。</summary>
        private static void AddDuties(SortedSet<string> duties, CardRouteTable table)
        {
            foreach (var cardType in table.CardTypes)
            {
                var duty = table.DutyOf(cardType);
                if (string.IsNullOrEmpty(duty))
                {
                    continue;
                }

                if (Array.IndexOf(PseudoDuties, duty) >= 0)
                {
                    continue;
                }

                duties.Add(duty);
            }
        }
    }
}
