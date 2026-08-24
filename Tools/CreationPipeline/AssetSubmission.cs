using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一次资产提交的推断结果：落到哪、叫什么、还差哪两条要问人。</summary>
    public sealed class AssetSubmissionPlan
    {
        /// <summary>
        /// 构造一份提交计划。
        /// </summary>
        /// <param name="assetType">资产类型（照资产规格里的名字）。</param>
        /// <param name="destinationDirectory">落点目录，仓库相对。</param>
        /// <param name="fileName">落地文件名（含扩展名）。</param>
        /// <param name="namingPattern">这一类的命名模式。</param>
        /// <param name="questions">推不出来、要回来问人的那几条（一轮最多两条）。</param>
        /// <param name="blockers">拦住这次提交的硬问题（类型不认识、扩展名不对……）。</param>
        public AssetSubmissionPlan(
            string assetType,
            string destinationDirectory,
            string fileName,
            string namingPattern,
            IReadOnlyList<string> questions,
            IReadOnlyList<string> blockers)
        {
            AssetType = assetType ?? "";
            DestinationDirectory = destinationDirectory ?? "";
            FileName = fileName ?? "";
            NamingPattern = namingPattern ?? "";
            Questions = questions ?? Array.Empty<string>();
            Blockers = blockers ?? Array.Empty<string>();
        }

        /// <summary>资产类型。</summary>
        public string AssetType { get; }

        /// <summary>落点目录，仓库相对。</summary>
        public string DestinationDirectory { get; }

        /// <summary>落地文件名（含扩展名）。</summary>
        public string FileName { get; }

        /// <summary>这一类的命名模式。</summary>
        public string NamingPattern { get; }

        /// <summary>推不出来、要回来问人的那几条。</summary>
        public IReadOnlyList<string> Questions { get; }

        /// <summary>拦住这次提交的硬问题。</summary>
        public IReadOnlyList<string> Blockers { get; }

        /// <summary>落地的完整仓库相对路径。</summary>
        public string DestinationPath
        {
            get
            {
                return DestinationDirectory.Length == 0 || FileName.Length == 0
                    ? ""
                    : DestinationDirectory.TrimEnd('/') + "/" + FileName;
            }
        }

        /// <summary>这次提交能不能往下走：没有硬问题、也没有要问的。</summary>
        public bool CanProceed => Blockers.Count == 0 && Questions.Count == 0;
    }

    /// <summary>
    /// 人在飞书里丢一个资产过来之后，把「这东西是什么、该叫什么、该落哪」推出来。
    ///
    /// **能推的都推出来，推不出来的才问**（任务书 §4.2）：类型与模块推不出来是真要问的，
    /// 而落点与命名从来不该问人——那两样是资产规格里写死的，问人等于把规范背给他听。
    ///
    /// **一轮最多问两条**：这是助手那三条形状里的第一条（子文档 02 §五）。
    /// 攒一屏问题回过去，人只会挑一条回，剩下的下一轮还得再问一遍。
    /// </summary>
    public static class AssetSubmission
    {
        /// <summary>一轮最多问几条。</summary>
        public const int MaximumQuestionCount = 2;

        /// <summary>图片类扩展名。</summary>
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".tga", ".psd" };

        /// <summary>模型类扩展名。</summary>
        private static readonly string[] ModelExtensions = { ".fbx", ".glb", ".gltf", ".obj" };

        /// <summary>音频类扩展名。</summary>
        private static readonly string[] AudioExtensions = { ".wav", ".ogg", ".mp3" };

        /// <summary>
        /// 推一份提交计划。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="sourcePath">本机源文件（附件取回来落在 _Tasks/conversations/attachments/ 下的那个）。</param>
        /// <param name="assetType">资产类型；空串表示模型没推出来。</param>
        /// <param name="moduleName">模块名；空串表示没推出来。</param>
        /// <param name="naming">落地叫什么（不带扩展名）；空串表示没推出来。</param>
        public static AssetSubmissionPlan Plan(
            string repositoryRoot,
            string sourcePath,
            string assetType,
            string moduleName,
            string naming)
        {
            var questions = new List<string>();
            var blockers = new List<string>();

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                blockers.Add($"源文件不在：{sourcePath}");
                return new AssetSubmissionPlan("", "", "", "", questions, blockers);
            }

            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            var family = FamilyOf(extension);
            if (family.Length == 0)
            {
                blockers.Add($"不认识的文件类型「{extension}」：图（{string.Join("/", ImageExtensions)}）、"
                    + $"模型（{string.Join("/", ModelExtensions)}）、音频（{string.Join("/", AudioExtensions)}）之外的都收不了");
                return new AssetSubmissionPlan("", "", "", "", questions, blockers);
            }

            var catalog = AssetSpecCatalog.Load(repositoryRoot, moduleName ?? "");
            var type = string.IsNullOrWhiteSpace(assetType) ? null : catalog.Find(assetType.Trim());

            if (type == null)
            {
                // 类型是真要问的：同样一张 PNG 可以是图标、界面底图、立绘，
                // 落点与命名前缀各不相同，猜错一次就落错目录。
                var candidates = catalog.Types.Values
                    .Where(item => FamilyMatches(family, item))
                    .Select(item => item.TypeName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
                questions.Add(candidates.Count > 0
                    ? $"这是哪一类资产？（{string.Join(" / ", candidates)}）"
                    : "这是哪一类资产？资产规格里没有能收这种文件的类型");
            }

            if (string.IsNullOrWhiteSpace(moduleName))
            {
                questions.Add("它属于哪个模块？");
            }

            if (questions.Count > MaximumQuestionCount)
            {
                questions = questions.Take(MaximumQuestionCount).ToList();
            }

            if (type == null)
            {
                return new AssetSubmissionPlan("", "", "", "", questions, blockers);
            }

            var pattern = type.NamingPattern;
            var stem = (naming ?? "").Trim();
            if (stem.Length == 0)
            {
                // 命名推不出来时**不问人**：问的是「你想叫什么」，而人并不知道这一类要 T_ 开头。
                // 拦下来让上游按模式现拟一个，比把规范背给人听强。
                blockers.Add($"还没有命名。这一类要匹配 {pattern}，按它拟一个再提交");
            }
            else if (pattern.Length > 0 && !Regex.IsMatch(stem, pattern))
            {
                blockers.Add($"命名「{stem}」不匹配这一类的模式 {pattern}");
            }

            return new AssetSubmissionPlan(
                type.TypeName,
                type.Destination,
                stem.Length == 0 ? "" : stem + extension,
                pattern,
                questions,
                blockers);
        }

        /// <summary>扩展名属于哪一族：图 / 模型 / 音频；都不是给空串。</summary>
        public static string FamilyOf(string extension)
        {
            var lowered = (extension ?? "").ToLowerInvariant();
            if (ImageExtensions.Contains(lowered))
            {
                return "图";
            }

            if (ModelExtensions.Contains(lowered))
            {
                return "模型";
            }

            return AudioExtensions.Contains(lowered) ? "音频" : "";
        }

        /// <summary>
        /// 这一族的文件配不配得上这个资产类型：按落点粗判
        /// （Texture 收图、Model 收模型、Audio 收音频）。
        /// 粗判是有意的——资产规格里没有「收哪种扩展名」这一栏，
        /// 而**按落点判至少不会把一张 PNG 提议成角色模型**。
        /// </summary>
        private static bool FamilyMatches(string family, AssetTypeSpecification specification)
        {
            var destination = specification?.Destination ?? "";
            return family switch
            {
                "图" => destination.Contains("/Texture/", StringComparison.OrdinalIgnoreCase),
                "模型" => destination.Contains("/Model/", StringComparison.OrdinalIgnoreCase),
                "音频" => destination.Contains("/Audio/", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
    }
}
