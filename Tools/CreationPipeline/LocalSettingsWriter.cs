using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 改本机配置里**非密钥**的那些字段：<c>下游配置.&lt;driver&gt;.&lt;字段&gt;</c>。
    /// 地址、可执行文件、超时秒这类东西每换一台机器就要重填一次，让人手开 local.json
    /// 是在拿一个 JSON 语法错误换一次「面板整页红」。
    ///
    /// 三条红线，全部在这里挡住，不指望调用方自觉：
    /// 1. **密钥不走这条路**。密钥住在文件顶层，不在「下游配置」节里；要写密钥用
    ///    <see cref="SetSecret"/>，那条路单独守它自己的规矩（写得进、永不读回）。
    /// 2. **只认 driver.json「配置schema」里声明过的字段名**。凭空造一个字段写进去没人读，
    ///    页面还会把它显示成「已配」——那是一条自造的假绿。
    /// 3. **读改写，不重建**。文件里其余内容（尤其是密钥）原样保留；JSON 坏掉时拒绝写，
    ///    绝不用一份干净骨架把人填了一半的文件盖掉。
    /// </summary>
    public static class LocalSettingsWriter
    {
        /// <summary>本机配置里放 driver 配置的那一节。</summary>
        private const string DriverSectionName = "下游配置";

        /// <summary>
        /// 写一个 driver 的一个非密钥配置字段。值传空串表示**删掉这个键**——
        /// 「键在=已配」是全局判据（决策 78），留一个空串会被判成已配，那是假绿。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名，取 Bridges/ 下的目录名。</param>
        /// <param name="fieldName">字段名，必须在那份 driver.json 的「配置schema」里。</param>
        /// <param name="value">要写的值；空串表示删掉这个键。</param>
        public static ConfigWriteOutcome SetDriverField(
            string repositoryRoot,
            string driverName,
            string fieldName,
            string value)
        {
            var filePath = LocalSettingsFile(repositoryRoot);
            if (string.IsNullOrWhiteSpace(driverName))
            {
                return ConfigWriteOutcome.Failure("必须指定 driver 名", filePath);
            }

            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return ConfigWriteOutcome.Failure("必须指定字段名", filePath);
            }

            BridgeDriverDescriptor descriptor;
            try
            {
                descriptor = BridgeDriverDescriptor.Load(repositoryRoot, driverName);
            }
            catch (InvalidOperationException exception)
            {
                return ConfigWriteOutcome.Failure($"读不出 driver「{driverName}」的自述：{exception.Message}", filePath);
            }

            if (descriptor.SecretFieldNames.Contains(fieldName, StringComparer.Ordinal))
            {
                return ConfigWriteOutcome.Failure(
                    $"「{fieldName}」是密钥字段，它住在本机配置的**顶层**，不在「{DriverSectionName}」节里；写它走 bridge.secret.set",
                    filePath);
            }

            if (!descriptor.ConfigurationFieldNames.Contains(fieldName, StringComparer.Ordinal))
            {
                return ConfigWriteOutcome.Failure(
                    $"driver「{driverName}」的自述里没有「{fieldName}」这个配置字段；能填的是：{string.Join("、", descriptor.ConfigurationFieldNames)}",
                    filePath);
            }

            var fieldType = ReadFieldType(repositoryRoot, driverName, fieldName);
            if (string.Equals(fieldType, "secret", StringComparison.Ordinal))
            {
                return ConfigWriteOutcome.Failure(
                    $"「{fieldName}」在自述里的类型是 secret，写它走 bridge.secret.set（密钥在顶层，不在「{DriverSectionName}」节里）",
                    filePath);
            }

            JsonObject root;
            try
            {
                root = ReadRoot(filePath);
            }
            catch (InvalidOperationException exception)
            {
                return ConfigWriteOutcome.Failure(exception.Message, filePath);
            }

            if (root[DriverSectionName] is not JsonObject section)
            {
                if (root.ContainsKey(DriverSectionName))
                {
                    return ConfigWriteOutcome.Failure($"本机配置里的「{DriverSectionName}」不是对象，不敢动它", filePath);
                }

                section = new JsonObject();
                root[DriverSectionName] = section;
            }

            if (section[driverName] is not JsonObject driverSection)
            {
                if (section.ContainsKey(driverName))
                {
                    return ConfigWriteOutcome.Failure($"本机配置里的「{DriverSectionName}.{driverName}」不是对象，不敢动它", filePath);
                }

                driverSection = new JsonObject();
                section[driverName] = driverSection;
            }

            if (value.Length == 0)
            {
                var removed = driverSection.Remove(fieldName);
                Write(filePath, root);
                return ConfigWriteOutcome.Success(
                    removed
                        ? $"已删掉 {DriverSectionName}.{driverName}.{fieldName}（空值不留空串：留了会被判成「已配」）"
                        : $"{DriverSectionName}.{driverName}.{fieldName} 本来就没有，没动文件里的别的东西",
                    filePath);
            }

            JsonNode typedValue;
            if (string.Equals(fieldType, "number", StringComparison.Ordinal))
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return ConfigWriteOutcome.Failure($"「{fieldName}」在自述里是 number，而「{value}」不是数字", filePath);
                }

                typedValue = JsonValue.Create(number);
            }
            else if (string.Equals(fieldType, "boolean", StringComparison.Ordinal))
            {
                if (!bool.TryParse(value, out var flag))
                {
                    return ConfigWriteOutcome.Failure($"「{fieldName}」在自述里是 boolean，而「{value}」不是 true / false", filePath);
                }

                typedValue = JsonValue.Create(flag);
            }
            else
            {
                typedValue = JsonValue.Create(value);
            }

            driverSection[fieldName] = typedValue;
            Write(filePath, root);
            return ConfigWriteOutcome.Success($"已写 {DriverSectionName}.{driverName}.{fieldName}", filePath);
        }

        /// <summary>
        /// 写一个密钥键（本机配置的**顶层**，决策 5 定的位置）。值传空串表示删掉这个键。
        ///
        /// 决策 78 原本写着「密钥不给输入框、不给保存按钮」，2026-08-22 由项目主人当面改掉：
        /// **写这一侧放开，读这一侧一寸不让**。所以这个方法：
        /// 只写不读——不返回值、不报长度、不报前缀、不写日志，成功文案里只有**键名**；
        /// 参数里的密钥值除了落进 local.json 那一处，不许流到任何别的地方。
        ///
        /// 键名不许凭空造：必须是某个 driver.json 的「密钥字段」里声明过的名字。
        /// 写错一个字母，密钥会安安静静地躺在文件里永远没人读，而面板显示「已配」——那是最贵的一种假绿。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="secretFieldName">密钥键名，如「模型生成密钥」。</param>
        /// <param name="value">密钥值；空串表示删掉这个键。这个值只会落进 local.json，不进任何返回文案。</param>
        public static ConfigWriteOutcome SetSecret(string repositoryRoot, string secretFieldName, string value)
        {
            var filePath = LocalSettingsFile(repositoryRoot);
            if (string.IsNullOrWhiteSpace(secretFieldName))
            {
                return ConfigWriteOutcome.Failure("必须指定密钥键名", filePath);
            }

            var declaredNames = DeclaredSecretFieldNames(repositoryRoot);
            if (!declaredNames.Contains(secretFieldName, StringComparer.Ordinal))
            {
                return ConfigWriteOutcome.Failure(
                    declaredNames.Count == 0
                        ? "没有任何 driver 声明过密钥字段，写不了密钥"
                        : $"没有 driver 声明过密钥键「{secretFieldName}」；声明过的是：{string.Join("、", declaredNames)}",
                    filePath);
            }

            JsonObject root;
            try
            {
                root = ReadRoot(filePath);
            }
            catch (InvalidOperationException exception)
            {
                return ConfigWriteOutcome.Failure(exception.Message, filePath);
            }

            if (value.Length == 0)
            {
                var removed = root.Remove(secretFieldName);
                Write(filePath, root);
                return ConfigWriteOutcome.Success(
                    removed
                        ? $"已删掉密钥键「{secretFieldName}」"
                        : $"密钥键「{secretFieldName}」本来就没有，没动文件里的别的东西",
                    filePath);
            }

            root[secretFieldName] = JsonValue.Create(value);
            Write(filePath, root);

            // 成功文案里只有键名。这一行是密钥红线的最后一道：文案会被面板显示、会进命令输出区，
            // 一旦把值拼进来，它就跟着截图、日志、聊天记录跑得到处都是。
            return ConfigWriteOutcome.Success($"已写密钥键「{secretFieldName}」（值不回显，页面只报「已配」）", filePath);
        }

        /// <summary>枚举 Bridges 下所有 driver 声明过的密钥键名，序数序去重；读不动的 driver 跳过。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static IReadOnlyList<string> DeclaredSecretFieldNames(string repositoryRoot)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            var bridgesDirectory = Path.Combine(repositoryRoot, "Bridges");
            if (!Directory.Exists(bridgesDirectory))
            {
                return Array.Empty<string>();
            }

            foreach (var driverDirectory in Directory.EnumerateDirectories(bridgesDirectory))
            {
                if (!File.Exists(Path.Combine(driverDirectory, "driver.json")))
                {
                    continue;
                }

                try
                {
                    var descriptor = BridgeDriverDescriptor.Load(repositoryRoot, Path.GetFileName(driverDirectory));
                    foreach (var name in descriptor.SecretFieldNames)
                    {
                        names.Add(name);
                    }
                }
                catch (InvalidOperationException)
                {
                    // 这个 driver 的自述坏了：桥接包页会把它单独报出来，这里跳过它就是了。
                }
            }

            return names.ToList();
        }

        /// <summary>本机配置文件路径：Tools/CreationPipeline/Config/local.json（在 .gitignore 里）。</summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        public static string LocalSettingsFile(string repositoryRoot)
        {
            return Path.Combine(repositoryRoot, "Tools", "CreationPipeline", "Config", "local.json");
        }

        /// <summary>给人看的相对路径，用在提示文案里。</summary>
        private static string RelativeSettingsPath()
        {
            return "Tools/CreationPipeline/Config/local.json";
        }

        /// <summary>
        /// 读顶层对象：文件不存在给一个空对象（新机器上第一次填就是这种情况）；
        /// JSON 坏掉抛 InvalidOperationException——**绝不**当成空对象接着写，
        /// 那等于拿一份干净骨架把人填了一半的文件（连同密钥）盖掉。
        /// </summary>
        private static JsonObject ReadRoot(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new JsonObject();
            }

            JsonNode node;
            try
            {
                node = JsonNode.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new InvalidOperationException(
                    $"本机配置不是合法 JSON，没敢写：{exception.Message}（先把 {RelativeSettingsPath()} 修成合法 JSON）");
            }

            if (node is not JsonObject root)
            {
                throw new InvalidOperationException($"本机配置顶层不是对象，没敢写（{RelativeSettingsPath()}）");
            }

            return root;
        }

        /// <summary>读 driver.json 里某个配置字段声明的类型；读不到给空串（当字符串处理）。</summary>
        private static string ReadFieldType(string repositoryRoot, string driverName, string fieldName)
        {
            try
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(BridgeDriverDescriptor.DriverFile(repositoryRoot, driverName))))
                {
                    if (document.RootElement.TryGetProperty("配置schema", out var schema)
                        && schema.ValueKind == JsonValueKind.Object
                        && schema.TryGetProperty(fieldName, out var field)
                        && field.ValueKind == JsonValueKind.Object
                        && field.TryGetProperty("类型", out var type)
                        && type.ValueKind == JsonValueKind.String)
                    {
                        return type.GetString() ?? "";
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // 自述已经 Load 过一次了，这里再失败几乎不可能；当字符串处理，别为读类型这件事炸掉一次保存。
            }

            return "";
        }

        /// <summary>写回文件：缩进、中文原样、UTF-8 无 BOM，并保证目录在。</summary>
        private static void Write(string filePath, JsonObject root)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions(JsonSerializerOptions.Default)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            // 末尾补一个换行：JsonNode 不写它，而仓库里的 JSON 都是有的——
            // 少这一个字符，每次保存都会在 git diff 里留一条「\ No newline at end of file」的噪音。
            File.WriteAllText(filePath, root.ToJsonString(options) + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
