using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSGFrame.Hotfix
{
    /// <summary>热更清单的 JSON 编解码。键名全英文，这份清单是给程序与 CDN 解析的。</summary>
    public static class HotfixManifestCodec
    {
        private static readonly JsonSerializerOptions SerializeOptions = new JsonSerializerOptions
        {
            // 缩进便于 git diff 逐行比对清单变化。
            WriteIndented = true,
        };

        /// <summary>把清单序列化成 JSON 文本。</summary>
        public static string ToJson(HotfixManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return JsonSerializer.Serialize(ToDto(manifest), SerializeOptions);
        }

        /// <summary>从 JSON 文本反序列化清单，格式不对时抛 HotfixManifestException。</summary>
        public static HotfixManifest FromJson(string json)
        {
            ManifestDto dto;
            try
            {
                dto = JsonSerializer.Deserialize<ManifestDto>(json);
            }
            catch (JsonException exception)
            {
                throw new HotfixManifestException(
                    $"位置：清单 JSON；原因：JSON 无法解析；修复：核对 JSON 语法与字段类型；参考：{exception.Message}",
                    exception);
            }

            if (dto == null || string.IsNullOrEmpty(dto.VersionText))
            {
                throw new HotfixManifestException(
                    "位置：清单 JSON；原因：缺少 versionText 字段；修复：在清单里补上 versionText；参考：1.2.3");
            }

            var packages = dto.Packages ?? new List<PackageEntryDto>();
            var entries = new List<HotfixPackageEntry>(packages.Count);
            for (var index = 0; index < packages.Count; index++)
            {
                var package = packages[index];
                if (package == null || string.IsNullOrEmpty(package.FileName))
                {
                    throw new HotfixManifestException(
                        $"位置：清单 packages 第 {index} 条；原因：缺少 fileName 字段；修复：给每个包补上 fileName；参考：Hotfix.Logic.dll");
                }

                entries.Add(new HotfixPackageEntry(package.PackageName, package.FileName, package.ContentHash, package.ByteSize));
            }

            return new HotfixManifest(dto.VersionText, entries);
        }

        private static ManifestDto ToDto(HotfixManifest manifest)
        {
            var packages = new List<PackageEntryDto>(manifest.Packages.Count);
            foreach (var package in manifest.Packages)
            {
                packages.Add(new PackageEntryDto
                {
                    PackageName = package.PackageName,
                    FileName = package.FileName,
                    ContentHash = package.ContentHash,
                    ByteSize = package.ByteSize,
                });
            }

            return new ManifestDto
            {
                VersionText = manifest.VersionText,
                Packages = packages,
            };
        }

        private sealed class ManifestDto
        {
            [JsonPropertyName("versionText")]
            public string VersionText { get; set; }

            [JsonPropertyName("packages")]
            public List<PackageEntryDto> Packages { get; set; }
        }

        private sealed class PackageEntryDto
        {
            [JsonPropertyName("packageName")]
            public string PackageName { get; set; }

            [JsonPropertyName("fileName")]
            public string FileName { get; set; }

            [JsonPropertyName("contentHash")]
            public string ContentHash { get; set; }

            [JsonPropertyName("byteSize")]
            public long ByteSize { get; set; }
        }
    }

    /// <summary>热更清单解析失败时抛出，消息按四要素书写。</summary>
    public sealed class HotfixManifestException : Exception
    {
        /// <summary>以失败消息构造。</summary>
        public HotfixManifestException(string message)
            : base(message)
        {
        }

        /// <summary>以失败消息与内部异常构造。</summary>
        public HotfixManifestException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
