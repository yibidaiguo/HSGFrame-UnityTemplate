using System;
using System.IO;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>导入期的规则执行器：资产一进工程就按所在目录的「import-rules.json」把导入设置调好，并对停在收件箱里的资产提个醒。</summary>
    public sealed class AssetImportRuleEnforcer : AssetPostprocessor
    {
        private const string ImportRuleFileName = "import-rules.json";
        private const string InboxDirectoryName = "_Inbox";
        private const string LogPrefix = "[资产管线] ";

        /// <summary>贴图导入前按目录规则设置导入项。写在 Preprocess 而不是 Postprocess，才不会多触发一次重导入。</summary>
        private void OnPreprocessTexture()
        {
            var rule = LoadRuleForAsset(assetPath);
            if (rule == null)
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;

            // 收件箱是中转站，进来的东西还没定用途，导入设置留到归档之后按正式目录的规则来。
            if (IsInsideInbox(assetPath))
            {
                return;
            }

            // 只在首次导入时定这几项：之后人在 Inspector 里的调整应当留得住，
            // 每次导入都强推会让手工调过的参数悄悄被冲掉。
            if (!string.IsNullOrEmpty(importer.userData))
            {
                return;
            }

            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.userData = "按导入规则初始化";
        }

        /// <summary>音频导入前按目录规则设置导入项。</summary>
        private void OnPreprocessAudio()
        {
            if (LoadRuleForAsset(assetPath) == null || IsInsideInbox(assetPath))
            {
                return;
            }

            var importer = (AudioImporter)assetImporter;
            if (!string.IsNullOrEmpty(importer.userData))
            {
                return;
            }

            importer.forceToMono = true;
            importer.userData = "按导入规则初始化";
        }

        /// <summary>一批资产导入完成后，对停在收件箱里的那些打一行提示，指明用哪条命令归档。</summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var pendingCount = 0;
            foreach (var assetPath in importedAssets)
            {
                if (IsInsideInbox(assetPath) && !IsRuleFile(assetPath) && !AssetDatabase.IsValidFolder(assetPath))
                {
                    pendingCount++;
                }
            }

            if (pendingCount > 0)
            {
                // 只提醒不自动搬：归档会改文件名与路径，是不可逆动作，交给人显式跑一条命令。
                Debug.Log($"{LogPrefix}收件箱里有 {pendingCount} 个资产待归档，跑 asset.import 把它们分派到正式目录。");
            }
        }

        // 从资产所在目录逐级向上找 import-rules.json，找不到返回 null。
        // 这与命令层 AssetImportRuleSet.LoadForDirectory 是同一套查找语义，两侧对同一个资产得出同一条规则。
        private static AssetImportRuleView LoadRuleForAsset(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath);
            while (!string.IsNullOrEmpty(directory))
            {
                var rulePath = Path.Combine(directory, ImportRuleFileName);
                if (File.Exists(rulePath))
                {
                    try
                    {
                        return JsonSerializer.Deserialize<AssetImportRuleView>(
                            File.ReadAllText(rulePath),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (JsonException exception)
                    {
                        Debug.LogWarning($"{LogPrefix}导入规则解析失败：{rulePath}，{exception.Message}");
                        return null;
                    }
                }

                var parent = Path.GetDirectoryName(directory);
                if (string.Equals(parent, directory, StringComparison.Ordinal))
                {
                    break;
                }

                directory = parent;
            }

            return null;
        }

        private static bool IsInsideInbox(string assetPath)
        {
            return assetPath.Replace('\\', '/').Contains("/" + InboxDirectoryName + "/");
        }

        private static bool IsRuleFile(string assetPath)
        {
            var fileName = Path.GetFileName(assetPath);
            return string.Equals(fileName, ImportRuleFileName, StringComparison.Ordinal)
                || string.Equals(fileName, "archive-routes.json", StringComparison.Ordinal);
        }
    }

    /// <summary>导入规则里导入期用得到的那几项。命令层有一份同形状的模型，这里只取本侧要用的字段。</summary>
    public sealed class AssetImportRuleView
    {
        /// <summary>目录用途，例如「贴图」。</summary>
        [System.Text.Json.Serialization.JsonPropertyName("目录用途")]
        public string DirectoryPurpose { get; set; }

        /// <summary>文件名前缀，例如「T_」。</summary>
        [System.Text.Json.Serialization.JsonPropertyName("文件名前缀")]
        public string FileNamePrefix { get; set; }

        /// <summary>单个文件的最大字节数。</summary>
        [System.Text.Json.Serialization.JsonPropertyName("最大文件字节")]
        public long MaximumFileBytes { get; set; }
    }
}
