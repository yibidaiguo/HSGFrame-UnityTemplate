using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>
    /// 下游 driver 的自述：从 &lt;仓库根&gt;/Bridges/&lt;名&gt;/driver.json 读出来的全部字段。
    /// 读不到、JSON 坏掉或必填字段缺失时抛 InvalidOperationException，不做静默降级——
    /// driver 自述是供给链路的配置契约，坏了必须亮出来。
    /// </summary>
    public sealed class BridgeDriverDescriptor
    {
        /// <summary>
        /// 构造一份 driver 自述。
        /// </summary>
        /// <param name="name">driver 名称，与目录名一致。</param>
        /// <param name="ports">对外提供的端口列表，传 null 视为空列表。</param>
        /// <param name="form">形态：线上或本地。</param>
        /// <param name="contractRange">契约版本支持区间，形如「&gt;=1.0 &lt;2.0」。</param>
        /// <param name="secretFieldNames">密钥字段名列表，传 null 视为空列表。</param>
        /// <param name="trialCommand">试跑命令。</param>
        /// <param name="implementationName">实现包名。</param>
        /// <param name="fieldTypeMapping">逻辑类型到下游字段类型的映射，传 null 视为空字典。</param>
        /// <param name="formGroupingField">表单分组依据的字段名。</param>
        /// <param name="configurationFieldNames">配置 schema 的键名，排序后，传 null 视为空列表。</param>
        /// <param name="modelFieldName">模型字段名：配置 schema 里声明了「选项来源: 探测.模型」的那个字段；没有声明时空串。</param>
        public BridgeDriverDescriptor(
            string name,
            IReadOnlyList<string> ports,
            string form,
            string contractRange,
            IReadOnlyList<string> secretFieldNames,
            string trialCommand,
            string implementationName,
            IReadOnlyDictionary<string, string> fieldTypeMapping,
            string formGroupingField,
            IReadOnlyList<string> configurationFieldNames,
            string modelFieldName = "")
        {
            Name = name ?? "";
            Ports = ports ?? Array.Empty<string>();
            Form = form ?? "";
            ContractRange = contractRange ?? "";
            SecretFieldNames = secretFieldNames ?? Array.Empty<string>();
            TrialCommand = trialCommand ?? "";
            ImplementationName = implementationName ?? "";
            FieldTypeMapping = fieldTypeMapping ?? new Dictionary<string, string>();
            FormGroupingField = formGroupingField ?? "";
            ConfigurationFieldNames = configurationFieldNames ?? Array.Empty<string>();
            ModelFieldName = modelFieldName ?? "";
        }

        /// <summary>driver 名称，与目录名一致。</summary>
        public string Name { get; }

        /// <summary>对外提供的端口列表。</summary>
        public IReadOnlyList<string> Ports { get; }

        /// <summary>形态：线上或本地。</summary>
        public string Form { get; }

        /// <summary>契约版本支持区间，形如「&gt;=1.0 &lt;2.0」。</summary>
        public string ContractRange { get; }

        /// <summary>密钥字段名列表，值只进本机配置。</summary>
        public IReadOnlyList<string> SecretFieldNames { get; }

        /// <summary>试跑命令。</summary>
        public string TrialCommand { get; }

        /// <summary>实现包名。</summary>
        public string ImplementationName { get; }

        /// <summary>逻辑类型到下游字段类型的映射。</summary>
        public IReadOnlyDictionary<string, string> FieldTypeMapping { get; }

        /// <summary>表单分组依据的字段名。</summary>
        public string FormGroupingField { get; }

        /// <summary>配置 schema 的键名，按序数序排序。</summary>
        public IReadOnlyList<string> ConfigurationFieldNames { get; }

        /// <summary>
        /// 这个 driver 的**模型字段叫什么**：配置 schema 里声明了「选项来源: 探测.模型」的那个字段名；
        /// 没有哪个字段声明它时是空串。
        ///
        /// 这是唯一的真相来源——调用侧不许按 driver 名去猜字段名叫「模型」还是「模型版本」（决策 17）。
        /// 一个 driver 只能有一个模型字段，声明了两个及以上时 <see cref="Load"/> 当场抛。
        /// </summary>
        public string ModelFieldName { get; }

        /// <summary>
        /// driver 自述所在的目录：&lt;仓库根&gt;/Bridges/&lt;名&gt;。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string DriverDirectory(string repositoryRoot, string driverName)
        {
            return Path.Combine(repositoryRoot, "Bridges", driverName);
        }

        /// <summary>
        /// driver 自述文件的路径：&lt;仓库根&gt;/Bridges/&lt;名&gt;/driver.json。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        public static string DriverFile(string repositoryRoot, string driverName)
        {
            return Path.Combine(DriverDirectory(repositoryRoot, driverName), "driver.json");
        }

        /// <summary>
        /// 读取并校验一份 driver 自述。
        /// 文件缺失、JSON 语法错误、缺必填字段、名称与目录名不符、形态不合法时抛 InvalidOperationException。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名称。</param>
        /// <exception cref="InvalidOperationException">自述文件缺失、非法或字段不满足契约时抛出。</exception>
        public static BridgeDriverDescriptor Load(string repositoryRoot, string driverName)
        {
            var filePath = DriverFile(repositoryRoot, driverName);
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException($"找不到 driver 自述文件：{filePath}");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                throw new InvalidOperationException($"driver 自述文件不是合法 JSON：{filePath}：{exception.Message}", exception);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException($"driver 自述文件不是合法 JSON：{filePath}");
                }

                foreach (var requiredName in RequiredFieldNames)
                {
                    if (!root.TryGetProperty(requiredName, out var value) || value.ValueKind == JsonValueKind.Null)
                    {
                        throw new InvalidOperationException($"driver 自述文件缺少必填字段「{requiredName}」：{filePath}");
                    }
                }

                var name = ReadStringOrEmpty(root, "名称");
                if (!string.Equals(name, driverName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"driver 自述里的名称「{name}」与目录名「{driverName}」不一致");
                }

                var form = ReadStringOrEmpty(root, "形态");
                if (form != "线上" && form != "本地")
                {
                    throw new InvalidOperationException($"driver 自述里的形态「{form}」不合法，形态只能是「线上」或「本地」：{filePath}");
                }

                var configurationFieldNames = new List<string>();
                var modelFieldNames = new List<string>();
                if (root.TryGetProperty("配置schema", out var configurationElement) && configurationElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in configurationElement.EnumerateObject())
                    {
                        configurationFieldNames.Add(property.Name);

                        // 「选项来源: 探测.模型」这一句声明的是「这一格的选项问下游要」，
                        // 顺带也就说明了「这个 driver 的模型字段是哪一个」——两件事同一条声明。
                        if (property.Value.ValueKind == JsonValueKind.Object
                            && property.Value.TryGetProperty("选项来源", out var optionSource)
                            && optionSource.ValueKind == JsonValueKind.String
                            && string.Equals(optionSource.GetString(), ModelOptionSource, StringComparison.Ordinal))
                        {
                            modelFieldNames.Add(property.Name);
                        }
                    }
                }

                configurationFieldNames.Sort(StringComparer.Ordinal);
                modelFieldNames.Sort(StringComparer.Ordinal);

                if (modelFieldNames.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"driver 自述里有 {modelFieldNames.Count} 个字段都声明了「选项来源: {ModelOptionSource}」（{string.Join("、", modelFieldNames)}），一个 driver 只能有一个模型字段：{filePath}");
                }

                return new BridgeDriverDescriptor(
                    name,
                    ReadStringList(root, "port"),
                    form,
                    ReadStringOrEmpty(root, "契约版本"),
                    ReadStringList(root, "密钥字段"),
                    ReadStringOrEmpty(root, "试跑"),
                    ReadStringOrEmpty(root, "实现"),
                    ReadStringDictionary(root, "字段类型映射"),
                    ReadStringOrEmpty(root, "表单分组字段"),
                    configurationFieldNames,
                    modelFieldNames.Count == 1 ? modelFieldNames[0] : "");
            }
        }

        /// <summary>
        /// 把逻辑类型映射成下游字段类型：先查映射表；查不到退回映射表里 string 对应的值；
        /// 连 string 都没有就返回传入值原样。
        /// </summary>
        /// <param name="logicalType">逻辑类型，如 enum / array / string。</param>
        public string MapFieldType(string logicalType)
        {
            if (FieldTypeMapping.TryGetValue(logicalType, out var mapped))
            {
                return mapped;
            }

            if (FieldTypeMapping.TryGetValue("string", out var fallback))
            {
                return fallback;
            }

            return logicalType;
        }

        /// <summary>模型字段的「选项来源」声明值：认这一句的字段就是这个 driver 的模型字段。</summary>
        private const string ModelOptionSource = "探测.模型";

        /// <summary>自述文件里必须存在的字段名。</summary>
        private static readonly string[] RequiredFieldNames =
        {
            "名称", "port", "形态", "契约版本", "实现", "字段类型映射"
        };

        /// <summary>读必须为字符串的属性；缺失或类型不对给空串。</summary>
        private static string ReadStringOrEmpty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        /// <summary>读字符串数组；缺失或类型不对给空列表。</summary>
        private static IReadOnlyList<string> ReadStringList(JsonElement element, string propertyName)
        {
            var values = new List<string>();
            if (!element.TryGetProperty(propertyName, out var listElement) || listElement.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var item in listElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    values.Add(item.GetString() ?? "");
                }
            }

            return values;
        }

        /// <summary>读字符串到字符串的映射对象；缺失或类型不对给空字典。</summary>
        private static IReadOnlyDictionary<string, string> ReadStringDictionary(JsonElement element, string propertyName)
        {
            var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!element.TryGetProperty(propertyName, out var objectElement) || objectElement.ValueKind != JsonValueKind.Object)
            {
                return mapping;
            }

            foreach (var property in objectElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    mapping[property.Name] = property.Value.GetString() ?? "";
                }
            }

            return mapping;
        }
    }
}
