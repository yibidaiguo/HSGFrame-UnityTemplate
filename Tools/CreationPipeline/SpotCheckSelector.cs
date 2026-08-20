using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 放行流水抽查选取器：按比例从「未抽查」条目里确定性选出该抽查的几条。
    /// 全程零随机数——随机抽查在门禁里没法验（同一份流水两次跑抽出不同结果），
    /// 而且真发现问题时没法复现「当初抽的是哪几条」。均匀跨步同样能铺开采样面，
    /// 还能被测试逐条断言。
    /// </summary>
    public static class SpotCheckSelector
    {
        /// <summary>
        /// 按比例选出该抽查的流水条目。只在未抽查的条目里挑；候选数为 0 或比例小于等于 0
        /// 返回空列表；比例大于等于 1 返回全部候选；否则 想抽几条 = 候选数 × 比例 向上取整
        /// 且至少 1 条。选取确定性：候选按 id 序数序排好，步长 = 候选数 / 想抽几条（浮点），
        /// 第 k 条取下标 (int)(k × 步长)，下标越界夹到最后一个；同一个下标被取到两次时
        /// 跳到下一个没被取过的下标。结果按 id 序数序返回。
        /// </summary>
        /// <param name="ledger">放行流水。</param>
        /// <param name="ratio">抽查比例，0 到 1 之间。</param>
        public static IReadOnlyList<ReleaseLedgerEntry> Select(ReleaseLedger ledger, double ratio)
        {
            if (ledger == null)
            {
                return Array.Empty<ReleaseLedgerEntry>();
            }

            var candidates = ledger.Entries
                .Where(entry => !entry.IsSpotChecked)
                .OrderBy(entry => entry.Identifier, StringComparer.Ordinal)
                .ToList();

            if (candidates.Count == 0 || ratio <= 0.0)
            {
                return Array.Empty<ReleaseLedgerEntry>();
            }

            if (ratio >= 1.0)
            {
                return candidates;
            }

            var desiredCount = (int)Math.Ceiling(candidates.Count * ratio);
            if (desiredCount < 1)
            {
                desiredCount = 1;
            }

            if (desiredCount >= candidates.Count)
            {
                return candidates;
            }

            var step = candidates.Count / (double)desiredCount;
            var pickedIndexes = new HashSet<int>();
            var picked = new List<ReleaseLedgerEntry>(desiredCount);

            for (var k = 0; k < desiredCount; k++)
            {
                var index = (int)(k * step);
                if (index >= candidates.Count)
                {
                    index = candidates.Count - 1;
                }

                while (pickedIndexes.Contains(index))
                {
                    index++;
                    if (index >= candidates.Count)
                    {
                        index = candidates.Count - 1;
                    }
                }

                pickedIndexes.Add(index);
                picked.Add(candidates[index]);
            }

            picked.Sort((left, right) => string.CompareOrdinal(left.Identifier, right.Identifier));
            return picked;
        }
    }
}
