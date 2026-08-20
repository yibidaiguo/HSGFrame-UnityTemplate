using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 模型度量：由加工站产出、本任务只负责读。
    /// 文件缺失或 JSON 坏掉抛 InvalidOperationException；缺哪个键就那一项取 0，不抛——
    /// 加工站可能只报它算得出的那几项，缺项由机检去判，不该在加载阶段炸掉。
    /// </summary>
    public sealed class ModelMetrics
    {
        /// <summary>
        /// 构造一份模型度量。
        /// </summary>
        /// <param name="triangleCount">面数。</param>
        /// <param name="materialCount">材质数。</param>
        /// <param name="textureSize">贴图尺寸。</param>
        /// <param name="boundingBoxX">包围盒 x 轴长度（米）。</param>
        /// <param name="boundingBoxY">包围盒 y 轴长度（米）。</param>
        /// <param name="boundingBoxZ">包围盒 z 轴长度（米）。</param>
        /// <param name="boneCount">骨骼数。</param>
        /// <param name="missingFieldNames">缺了的键名，序数序；传 null 视为空列表。</param>
        public ModelMetrics(
            int triangleCount,
            int materialCount,
            int textureSize,
            decimal boundingBoxX,
            decimal boundingBoxY,
            decimal boundingBoxZ,
            int boneCount,
            IReadOnlyList<string> missingFieldNames)
        {
            TriangleCount = triangleCount;
            MaterialCount = materialCount;
            TextureSize = textureSize;
            BoundingBoxX = boundingBoxX;
            BoundingBoxY = boundingBoxY;
            BoundingBoxZ = boundingBoxZ;
            BoneCount = boneCount;
            MissingFieldNames = missingFieldNames ?? Array.Empty<string>();
        }

        /// <summary>面数。</summary>
        public int TriangleCount { get; }

        /// <summary>材质数。</summary>
        public int MaterialCount { get; }

        /// <summary>贴图尺寸。</summary>
        public int TextureSize { get; }

        /// <summary>包围盒 x 轴长度（米）。</summary>
        public decimal BoundingBoxX { get; }

        /// <summary>包围盒 y 轴长度（米）。</summary>
        public decimal BoundingBoxY { get; }

        /// <summary>包围盒 z 轴长度（米）。</summary>
        public decimal BoundingBoxZ { get; }

        /// <summary>骨骼数。</summary>
        public int BoneCount { get; }

        /// <summary>缺了的键名，序数序。</summary>
        public IReadOnlyList<string> MissingFieldNames { get; }

        /// <summary>
        /// 从文件读一份模型度量；文件不存在或 JSON 坏掉抛 InvalidOperationException，文案带绝对路径。
        /// 缺哪个键就那一项取 0，并把键名带进 MissingFieldNames。
        /// </summary>
        /// <param name="filePath">度量文件的绝对或相对路径。</param>
        /// <exception cref="InvalidOperationException">文件缺失或 JSON 非法时抛出。</exception>
        public static ModelMetrics LoadFromFile(string filePath)
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"找不到模型度量文件：{fullPath}");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(fullPath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new InvalidOperationException($"模型度量文件不是合法 JSON：{fullPath}：{exception.Message}", exception);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException($"模型度量文件不是合法 JSON：{fullPath}");
                }

                var missing = new SortedSet<string>(StringComparer.Ordinal);
                var triangleCount = ReadInt32(root, "面数", missing);
                var materialCount = ReadInt32(root, "材质数", missing);
                var textureSize = ReadInt32(root, "贴图尺寸", missing);
                var boneCount = ReadInt32(root, "骨骼数", missing);

                decimal boundingBoxX = 0m;
                decimal boundingBoxY = 0m;
                decimal boundingBoxZ = 0m;
                if (!root.TryGetProperty("包围盒米", out var boundingBoxElement) || boundingBoxElement.ValueKind != JsonValueKind.Object)
                {
                    missing.Add("包围盒米");
                }
                else
                {
                    boundingBoxX = ReadDecimal(boundingBoxElement, "x", "包围盒米.x", missing);
                    boundingBoxY = ReadDecimal(boundingBoxElement, "y", "包围盒米.y", missing);
                    boundingBoxZ = ReadDecimal(boundingBoxElement, "z", "包围盒米.z", missing);
                }

                return new ModelMetrics(
                    triangleCount,
                    materialCount,
                    textureSize,
                    boundingBoxX,
                    boundingBoxY,
                    boundingBoxZ,
                    boneCount,
                    new List<string>(missing));
            }
        }

        /// <summary>读整数度量；键缺失、null 或类型不对给 0 并把键名记进缺项。</summary>
        private static int ReadInt32(JsonElement element, string propertyName, SortedSet<string> missing)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return value.GetInt32();
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                    // 数字越界或形状不合法：按缺项处理，不让加载阶段炸掉。
                }
            }

            missing.Add(propertyName);
            return 0;
        }

        /// <summary>读包围盒单轴度量；键缺失、null 或类型不对给 0 并把「包围盒米.轴」记进缺项。</summary>
        private static decimal ReadDecimal(JsonElement element, string propertyName, string missingName, SortedSet<string> missing)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return value.GetDecimal();
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidOperationException || exception is OverflowException)
                {
                    // 同上：按缺项处理。
                }
            }

            missing.Add(missingName);
            return 0m;
        }
    }
}
