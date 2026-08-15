using System;
using System.IO;
using ClosedXML.Excel;
using Template.Toolkit.ConfigBridge;

namespace Template.Toolkit.ConfigBridge.Tests
{
    /// <summary>五个回归测试文件共用的临时目录夹具：摆出 Tables / Schema / Mirror 三个子目录并暴露服务。</summary>
    /// <remarks>
    /// 每个实例在系统临时目录下开一个独一无二的配置根，测试结束时整棵删掉，
    /// 不碰仓库里的真实 Config 目录，也不把测试绑死在任何固定布局上。
    /// </remarks>
    internal sealed class ConfigTestWorkbookBuilder : IDisposable
    {
        private readonly string _configRoot;
        private readonly string _tableName;
        private readonly string _sheetName;

        /// <summary>按表名与 Sheet 名建一个临时配置根目录，并实例化桥接服务。</summary>
        /// <param name="tableName">表名，同时是 schema 与 xlsx 文件名的主干。</param>
        /// <param name="sheetName">xlsx 里承载这张表的 Sheet 名。</param>
        public ConfigTestWorkbookBuilder(string tableName, string sheetName)
        {
            _tableName = tableName;
            _sheetName = sheetName;
            _configRoot = Path.Combine(Path.GetTempPath(), "config-bridge-tests", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(_configRoot, "Tables"));
            Directory.CreateDirectory(Path.Combine(_configRoot, "Schema"));
            Directory.CreateDirectory(Path.Combine(_configRoot, "Mirror"));

            Service = new ConfigSyncService(_configRoot);
        }

        /// <summary>临时配置根目录的绝对路径。</summary>
        public string ConfigRoot => _configRoot;

        /// <summary>指向这个配置根的桥接服务。</summary>
        public ConfigSyncService Service { get; }

        /// <summary>xlsx 表文件的绝对路径。</summary>
        public string TablePath => Path.Combine(_configRoot, "Tables", _tableName + ".xlsx");

        /// <summary>schema 文件的绝对路径。</summary>
        public string SchemaPath => Path.Combine(_configRoot, "Schema", _tableName + ".schema.json");

        /// <summary>镜像 JSON 文件的绝对路径。</summary>
        public string MirrorPath => Path.Combine(_configRoot, "Mirror", _tableName + ".json");

        /// <summary>把 schema JSON 原文写进 Schema 子目录。</summary>
        /// <param name="schemaJson">完整的 schema JSON 文本。</param>
        public void WriteSchema(string schemaJson)
        {
            File.WriteAllText(SchemaPath, schemaJson);
        }

        /// <summary>建 workbook、按 Sheet 名建主 Sheet、依次回调、保存到 xlsx 路径。</summary>
        /// <param name="writeSheet">填主 Sheet 的表头与数据行。</param>
        /// <param name="writeWorkbook">可选，在 workbook 上追加附加 Sheet（如统计公式 Sheet）。</param>
        public void WriteWorkbook(Action<IXLWorksheet> writeSheet, Action<XLWorkbook> writeWorkbook = null)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.AddWorksheet(_sheetName);
            writeSheet?.Invoke(worksheet);
            writeWorkbook?.Invoke(workbook);
            workbook.SaveAs(TablePath);
        }

        /// <summary>读回镜像并把值按 schema 归一化成 CLR 原生值。</summary>
        public MirrorDocument LoadMirror()
        {
            var mirror = MirrorDocument.LoadFromFile(MirrorPath);
            mirror.NormalizeValues(SchemaLoader.LoadFromFile(SchemaPath));
            return mirror;
        }

        /// <summary>删除临时配置根目录。</summary>
        public void Dispose()
        {
            if (Directory.Exists(_configRoot))
            {
                Directory.Delete(_configRoot, recursive: true);
            }
        }
    }
}
