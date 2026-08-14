using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.ConfigBridge;

namespace Template.Toolkit.CodeGen
{
    /// <summary>配置表管线的首轮实现：Excel ↔ 镜像走 ConfigBridge，访问代码走 schema + Scriban 直出。</summary>
    /// <remarks>
    /// 这是 <see cref="ITablePipeline"/> 的第一个实现。方案原本的折中是「保留 Luban，藏在接口后」，
    /// 首轮先把接口立起来、用直出实现填上；要接 Luban 就再写一个 LubanTablePipeline，业务不用动。
    /// </remarks>
    public sealed class ScribanTablePipeline : ITablePipeline
    {
        private readonly ConfigSyncService _configSyncService;
        private readonly string _templateRoot;
        private readonly CodeGenerationConfiguration _codeGenerationConfiguration;

        /// <summary>用模板根目录构造，配置根与生成清单都按模板内的约定路径解析。</summary>
        /// <param name="templateRoot">模板根目录。</param>
        public ScribanTablePipeline(string templateRoot)
        {
            _templateRoot = templateRoot;
            _configSyncService = new ConfigSyncService(Path.Combine(templateRoot, "Config"));
            _codeGenerationConfiguration = CodeGenerationConfiguration.LoadFromFile(
                Path.Combine(templateRoot, "Tools", "CodeGen", "Config", "codegen-config.json"));
        }

        /// <summary>管线实现的名字。</summary>
        public string PipelineName => "schema.json + Scriban 直出";

        /// <summary>Excel → 镜像 JSON。</summary>
        /// <param name="tableName">表名。</param>
        public ConfigOperationResult SyncFromWorkbook(string tableName)
        {
            return _configSyncService.Sync(tableName);
        }

        /// <summary>镜像 JSON → Excel。</summary>
        /// <param name="tableName">表名。</param>
        public ConfigOperationResult ApplyToWorkbook(string tableName)
        {
            return _configSyncService.Apply(tableName);
        }

        /// <summary>按 schema 校验镜像内容。</summary>
        /// <param name="tableName">表名。</param>
        public ConfigOperationResult Validate(string tableName)
        {
            return _configSyncService.Validate(tableName);
        }

        /// <summary>按生成清单里属于这张表的目标生成访问代码。</summary>
        /// <param name="tableName">表名。</param>
        public IReadOnlyList<string> GenerateAccessCode(string tableName)
        {
            var writtenPaths = new List<string>();
            foreach (var target in FindTargetsForTable(tableName))
            {
                CodeGenerator.WriteIfChanged(_templateRoot, target);
                writtenPaths.Add(target.OutputPath);
            }

            return writtenPaths;
        }

        /// <summary>导出运行时数据文件。首轮直接用镜像 JSON 当运行时数据，不再多一层格式。</summary>
        /// <param name="tableName">表名。</param>
        public IReadOnlyList<string> ExportRuntimeData(string tableName)
        {
            // 方案里这一步在 Luban 那条链路上会导出 .bytes；首轮镜像 JSON 本身就可读可加载，
            // 先直接把它当运行时数据，等真需要二进制体积优化时再在这里分叉。
            var mirrorPath = Path.Combine(_templateRoot, "Config", "Mirror", tableName + ".json");
            return File.Exists(mirrorPath) ? new[] { mirrorPath } : Array.Empty<string>();
        }

        private IEnumerable<CodeGenerationTarget> FindTargetsForTable(string tableName)
        {
            return _codeGenerationConfiguration.Targets
                .Where(target => target.InputPath.Replace('\\', '/')
                    .EndsWith($"/{tableName}.schema.json", StringComparison.Ordinal));
        }
    }
}
