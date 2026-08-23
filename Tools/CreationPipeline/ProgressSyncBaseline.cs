using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 进度同步基线：上一次同步收工时，两侧**商定成同一个值**的那份快照。
    ///
    /// 为什么非要它不可：没有基线，比对只能看出「两侧此刻不一样」，
    /// 看不出**是谁动的**。于是「人在飞书里把进展改成已完成」与
    /// 「引擎刚把阶段推进了一步」这两件完全不同的事，长得一模一样，
    /// 只能靠权威侧硬挑一边——那正是任务书里禁止的「静默挑一边」。
    /// 有了基线，三值比对（工程 / 下游 / 基线）才能分出三种情形：
    /// 只有一侧动过 → 按权威侧复制；两侧都动过 → 冲突，交给人。
    ///
    /// 基线跟着仓库走（进 git，与同步水位同一处、同一条理由）：
    /// 换台机器把基线丢了，第一次同步会把两边都改过的那几格当成单边改动覆盖掉。
    /// </summary>
    public static class ProgressSyncBaseline
    {
        /// <summary>写盘选项：缩进、中文原样，看得懂 git diff。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>基线文件路径：Tools/CreationPipeline/Config/progress-baseline.json。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string BaselineFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot ?? "", "Tools", "CreationPipeline", "Config", "progress-baseline.json");
        }

        /// <summary>
        /// 读基线。文件不存在给一份空快照且 <paramref name="hasBaseline"/> = false——
        /// **「没有基线」与「基线是空的」必须分开**：前者是第一次同步（此时任何差异都只能按权威侧走，
        /// 判成冲突会在首次同步时把整张表变成一堆假冲突），后者才是「上次同步时确实一条需求都没有」。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="hasBaseline">基线文件在不在。</param>
        /// <param name="loadFailureReason">读失败的原因；正常为空串。</param>
        public static ProgressSnapshot Load(string repositoryRoot, out bool hasBaseline, out string loadFailureReason)
        {
            hasBaseline = false;
            loadFailureReason = "";
            var filePath = BaselineFile(repositoryRoot);
            if (!File.Exists(filePath))
            {
                return new ProgressSnapshot(Array.Empty<ProgressEntry>(), null);
            }

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(filePath));
                hasBaseline = true;
                return ProgressSnapshot.FromJson(root);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 坏掉的基线**不许当成没有基线**：那会让这一轮把所有真冲突静默覆盖掉。
                // 带上原因交给调用方，由它决定是停下来还是继续。
                loadFailureReason = $"基线读不了：{exception.Message}";
                hasBaseline = true;
                return new ProgressSnapshot(Array.Empty<ProgressEntry>(), null);
            }
        }

        /// <summary>
        /// 写基线。只在**这一轮同步真做完之后**调用：中途崩了宁可下次多比一点，
        /// 也不许把没落地的值当成商定值写进基线——那会让一次失败的出站
        /// 在下一轮变成「工程侧没动过」，于是永远推不上去。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="snapshot">商定后的快照。</param>
        public static void Save(string repositoryRoot, ProgressSnapshot snapshot)
        {
            var filePath = BaselineFile(repositoryRoot);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = (snapshot ?? new ProgressSnapshot(null, null)).ToJson();
            File.WriteAllText(filePath, payload.ToJsonString(WriteOptions), new UTF8Encoding(false));
        }
    }
}
