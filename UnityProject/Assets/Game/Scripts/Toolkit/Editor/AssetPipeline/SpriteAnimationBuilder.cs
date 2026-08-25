using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>
    /// 把创作管线出的「横排一行图集 + sheet.json」变成 Unity 真资产：
    /// 切好的一排精灵，和一份 <see cref="AnimationClip"/>。
    ///
    /// **这一步以前是缺的**。管线到 <c>anim.compose</c> 为止产出的是一张 PNG 加一份 sheet.json——
    /// 那两样东西在 Unity 里什么都不是：PNG 是单张精灵，sheet.json 没人读。
    /// 人要拿它做动画，得自己在 Inspector 里改 Sprite Mode、点 Slice、
    /// 再把 N 张精灵拖进 Animation 窗口。**那正是「AI 跑掉大部分内容」要消灭的那种手工活。**
    ///
    /// 切图走 <see cref="ISpriteEditorDataProvider"/>（<c>com.unity.2d.sprite</c> 提供），
    /// 不用 <c>TextureImporter.spritesheet</c>：后者在新版里是 Obsolete 的，
    /// 而且它写进去的东西与精灵编辑器那套数据不是同一份，改完常常在 Inspector 里看不见。
    ///
    /// 建 clip 走 <see cref="AnimationUtility"/>，**不手写 .anim**（铁律 2）。
    /// </summary>
    public static class SpriteAnimationBuilder
    {
        /// <summary>切图与建 clip 的结果，给调用方打日志用。</summary>
        public sealed class Result
        {
            /// <summary>构造一次结果。</summary>
            /// <param name="succeeded">成没成。</param>
            /// <param name="sheetAssetPath">图集在工程里的路径。</param>
            /// <param name="clipAssetPath">生成的 clip 路径；没建时为空串。</param>
            /// <param name="frameCount">切出几帧。</param>
            /// <param name="failureReason">失败原因；成功时为空串。</param>
            /// <param name="notes">给人看的说明。</param>
            public Result(
                bool succeeded,
                string sheetAssetPath,
                string clipAssetPath,
                int frameCount,
                string failureReason,
                IReadOnlyList<string> notes)
            {
                Succeeded = succeeded;
                SheetAssetPath = sheetAssetPath ?? "";
                ClipAssetPath = clipAssetPath ?? "";
                FrameCount = frameCount;
                FailureReason = failureReason ?? "";
                Notes = notes ?? Array.Empty<string>();
            }

            /// <summary>成没成。</summary>
            public bool Succeeded { get; }

            /// <summary>图集在工程里的路径。</summary>
            public string SheetAssetPath { get; }

            /// <summary>生成的 clip 路径；没建时为空串。</summary>
            public string ClipAssetPath { get; }

            /// <summary>切出几帧。</summary>
            public int FrameCount { get; }

            /// <summary>失败原因；成功时为空串。</summary>
            public string FailureReason { get; }

            /// <summary>给人看的说明。</summary>
            public IReadOnlyList<string> Notes { get; }
        }

        /// <summary>sheet.json 里那几个键，逐字对上创作管线侧的 SpriteSheetComposer。</summary>
        private const string SheetMetadataFileName = "sheet.json";

        /// <summary>
        /// 把一张横排图集切成精灵并建出 clip。
        /// </summary>
        /// <param name="sheetSourcePath">图集 PNG 的来源路径（工程外，创作管线的产出）。</param>
        /// <param name="metadataSourcePath">sheet.json 的来源路径；空串时取图集旁边那份。</param>
        /// <param name="sheetAssetPath">图集要落在工程里的哪（Assets/… 开头）。</param>
        /// <param name="clipAssetPath">clip 要落在工程里的哪（Assets/… 开头）；空串表示只切图不建 clip。</param>
        /// <param name="loop">clip 要不要循环。</param>
        /// <param name="targetComponent">clip 驱动谁的 Sprite：SpriteRenderer 或 Image。</param>
        public static Result Build(
            string sheetSourcePath,
            string metadataSourcePath,
            string sheetAssetPath,
            string clipAssetPath,
            bool loop,
            string targetComponent)
        {
            var notes = new List<string>();

            if (string.IsNullOrWhiteSpace(sheetSourcePath) || !File.Exists(sheetSourcePath))
            {
                return Failure($"图集不在：{sheetSourcePath}");
            }

            var metadataPath = string.IsNullOrWhiteSpace(metadataSourcePath)
                ? Path.Combine(Path.GetDirectoryName(sheetSourcePath) ?? ".", SheetMetadataFileName)
                : metadataSourcePath;
            if (!File.Exists(metadataPath))
            {
                return Failure(
                    $"找不到 {SheetMetadataFileName}：{metadataPath}。"
                    + "切图要靠它给的格宽格高与帧数——照图片尺寸猜会在最后一帧不满格时切错。");
            }

            SheetMetadata metadata;
            try
            {
                metadata = SheetMetadata.Parse(File.ReadAllText(metadataPath, Encoding.UTF8));
            }
            catch (Exception exception) when (exception is IOException || exception is FormatException)
            {
                return Failure($"{SheetMetadataFileName} 读不动：{exception.Message}");
            }

            if (metadata.FrameCount <= 0 || metadata.CellWidth <= 0 || metadata.CellHeight <= 0)
            {
                return Failure(
                    $"{SheetMetadataFileName} 里的帧数/格宽/格高不是正数"
                    + $"（帧数 {metadata.FrameCount}、格宽 {metadata.CellWidth}、格高 {metadata.CellHeight}）");
            }

            if (!sheetAssetPath.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
            {
                return Failure($"图集落点必须在 Assets/ 底下，给的是：{sheetAssetPath}");
            }

            // ---- 把图集搬进工程 ----
            try
            {
                var directory = Path.GetDirectoryName(sheetAssetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.Copy(sheetSourcePath, sheetAssetPath, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Failure($"图集拷不进工程：{exception.Message}");
            }

            AssetDatabase.ImportAsset(sheetAssetPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(sheetAssetPath) as TextureImporter;
            if (importer == null)
            {
                return Failure($"{sheetAssetPath} 没有 TextureImporter——它多半没被当成贴图导入");
            }

            // 逐帧动画的图集：**关掉压缩与 mipmap，单位像素数按格高对齐**。
            // 压缩会在边缘吃出半透明杂边，逐帧动画一放就看得见闪；mipmap 对 2D 没用只涨体积。
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = metadata.CellHeight;

            // ---- 切精灵 ----
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
            {
                return Failure("拿不到精灵数据提供者：com.unity.2d.sprite 没装上？");
            }

            provider.InitSpriteEditorDataProvider();

            var baseName = Path.GetFileNameWithoutExtension(sheetAssetPath);
            var rects = new SpriteRect[metadata.FrameCount];
            var pairs = new List<SpriteNameFileIdPair>(metadata.FrameCount);
            for (var index = 0; index < metadata.FrameCount; index++)
            {
                var spriteName = baseName + "_" + index.ToString("00", CultureInfo.InvariantCulture);
                var rect = new SpriteRect
                {
                    name = spriteName,
                    spriteID = GUID.Generate(),
                    // 图集是横排一行，第 N 格就是第 N 帧（sheet.json 的「排布」写死了这一条）。
                    rect = new Rect(index * metadata.CellWidth, 0, metadata.CellWidth, metadata.CellHeight),
                    alignment = AnchorToAlignment(metadata.Anchor),
                    pivot = AnchorToPivot(metadata.Anchor)
                };
                rects[index] = rect;
                pairs.Add(new SpriteNameFileIdPair(spriteName, rect.spriteID));
            }

            provider.SetSpriteRects(rects);

            // 名字↔fileId 那张表也要一起写。**漏了它的症状很隐蔽**：
            // 精灵切出来了，但每次重导入 fileId 会变，于是引用这些精灵的 clip 第二天全指空。
            var nameFileIdProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIdProvider != null)
            {
                nameFileIdProvider.SetNameFileIdPairs(pairs);
            }

            provider.Apply();
            importer.SaveAndReimport();
            notes.Add($"切了 {metadata.FrameCount} 帧，每格 {metadata.CellWidth}×{metadata.CellHeight}，锚点「{metadata.Anchor}」");

            if (string.IsNullOrWhiteSpace(clipAssetPath))
            {
                notes.Add("没给 clip 落点，这一趟只切图。");
                return new Result(true, sheetAssetPath, "", metadata.FrameCount, "", notes);
            }

            // ---- 建 clip ----
            var sprites = LoadSpritesInOrder(sheetAssetPath, metadata.FrameCount);
            if (sprites.Count != metadata.FrameCount)
            {
                return Failure(
                    $"切完之后只读回 {sprites.Count} 张精灵，期望 {metadata.FrameCount} 张——"
                    + "切图那一步没有真的生效，clip 不建（建了也是一半空引用）");
            }

            var frameRate = metadata.FrameRate > 0 ? metadata.FrameRate : 12;
            var clip = new AnimationClip { frameRate = frameRate };

            var componentType = ResolveComponentType(targetComponent);
            if (componentType == null)
            {
                return Failure($"不认识的目标组件「{targetComponent}」：只认 SpriteRenderer 与 Image");
            }

            var binding = EditorCurveBinding.PPtrCurve(string.Empty, componentType, "m_Sprite");
            var keyframes = new ObjectReferenceKeyframe[metadata.FrameCount];
            for (var index = 0; index < metadata.FrameCount; index++)
            {
                keyframes[index] = new ObjectReferenceKeyframe
                {
                    time = index / (float)frameRate,
                    value = sprites[index]
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            try
            {
                var clipDirectory = Path.GetDirectoryName(clipAssetPath);
                if (!string.IsNullOrEmpty(clipDirectory))
                {
                    Directory.CreateDirectory(clipDirectory);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Failure($"clip 落点建不出来：{exception.Message}");
            }

            AssetDatabase.DeleteAsset(clipAssetPath);
            AssetDatabase.CreateAsset(clip, clipAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            notes.Add($"clip 帧率 {frameRate}，{(loop ? "循环" : "不循环")}，驱动 {componentType.Name}.m_Sprite");
            return new Result(true, sheetAssetPath, clipAssetPath, metadata.FrameCount, "", notes);
        }

        /// <summary>按名字后缀序把切出来的精灵读回来。</summary>
        private static List<Sprite> LoadSpritesInOrder(string sheetAssetPath, int expectedCount)
        {
            var loaded = new List<Sprite>(expectedCount);
            var baseName = Path.GetFileNameWithoutExtension(sheetAssetPath);
            var all = AssetDatabase.LoadAllAssetsAtPath(sheetAssetPath);
            var byName = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (var asset in all)
            {
                if (asset is Sprite sprite)
                {
                    byName[sprite.name] = sprite;
                }
            }

            for (var index = 0; index < expectedCount; index++)
            {
                var name = baseName + "_" + index.ToString("00", CultureInfo.InvariantCulture);
                if (byName.TryGetValue(name, out var sprite))
                {
                    loaded.Add(sprite);
                }
            }

            return loaded;
        }

        /// <summary>把目标组件名翻成类型。</summary>
        private static Type ResolveComponentType(string targetComponent)
        {
            var name = (targetComponent ?? "").Trim();
            if (name.Length == 0 || string.Equals(name, "SpriteRenderer", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(SpriteRenderer);
            }

            if (string.Equals(name, "Image", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(UnityEngine.UI.Image);
            }

            return null;
        }

        /// <summary>把 sheet.json 的锚点翻成精灵的对齐方式。</summary>
        private static SpriteAlignment AnchorToAlignment(string anchor)
        {
            switch (anchor)
            {
                case "左上角": return SpriteAlignment.TopLeft;
                case "中心": return SpriteAlignment.Center;
                default: return SpriteAlignment.BottomCenter;
            }
        }

        /// <summary>把 sheet.json 的锚点翻成 pivot（归一化坐标）。</summary>
        private static Vector2 AnchorToPivot(string anchor)
        {
            switch (anchor)
            {
                case "左上角": return new Vector2(0f, 1f);
                case "中心": return new Vector2(0.5f, 0.5f);
                default: return new Vector2(0.5f, 0f);
            }
        }

        /// <summary>失败结果。</summary>
        private static Result Failure(string reason)
        {
            return new Result(false, "", "", 0, reason, Array.Empty<string>());
        }

        /// <summary>
        /// sheet.json 的那几个键。
        /// **自己解而不用 JsonUtility**：那几个键是中文的，JsonUtility 要求字段名与键名一致，
        /// 而 C# 字段不能叫「格宽」。这份结构简单到手解比引一个 JSON 库划算。
        /// </summary>
        private sealed class SheetMetadata
        {
            /// <summary>帧数。</summary>
            public int FrameCount { get; private set; }

            /// <summary>帧率。</summary>
            public int FrameRate { get; private set; }

            /// <summary>格宽，像素。</summary>
            public int CellWidth { get; private set; }

            /// <summary>格高，像素。</summary>
            public int CellHeight { get; private set; }

            /// <summary>锚点。</summary>
            public string Anchor { get; private set; } = "底边中点";

            /// <summary>解一份 sheet.json。</summary>
            /// <param name="text">文件正文。</param>
            public static SheetMetadata Parse(string text)
            {
                return new SheetMetadata
                {
                    FrameCount = ReadInt(text, "帧数"),
                    FrameRate = ReadInt(text, "帧率"),
                    CellWidth = ReadInt(text, "格宽"),
                    CellHeight = ReadInt(text, "格高"),
                    Anchor = ReadString(text, "锚点")
                };
            }

            private static int ReadInt(string text, string key)
            {
                var token = "\"" + key + "\"";
                var at = text.IndexOf(token, StringComparison.Ordinal);
                if (at < 0)
                {
                    return 0;
                }

                var colon = text.IndexOf(':', at + token.Length);
                if (colon < 0)
                {
                    return 0;
                }

                var end = colon + 1;
                while (end < text.Length && (char.IsWhiteSpace(text[end]) || char.IsDigit(text[end]) || text[end] == '-'))
                {
                    end++;
                }

                var slice = text.Substring(colon + 1, end - colon - 1).Trim();
                return int.TryParse(slice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
            }

            private static string ReadString(string text, string key)
            {
                var token = "\"" + key + "\"";
                var at = text.IndexOf(token, StringComparison.Ordinal);
                if (at < 0)
                {
                    return "底边中点";
                }

                var open = text.IndexOf('"', text.IndexOf(':', at + token.Length) + 1);
                if (open < 0)
                {
                    return "底边中点";
                }

                var close = text.IndexOf('"', open + 1);
                return close < 0 ? "底边中点" : text.Substring(open + 1, close - open - 1);
            }
        }
    }
}
