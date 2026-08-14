using System.Collections.Generic;

namespace Template.Toolkit.ConfigBridge
{
    /// <summary>配置表管线的可替换面：同步、回写、校验、生成访问代码、导出运行时数据。</summary>
    /// <remarks>
    /// 业务代码只依赖生成出来的访问 API 与运行时加载层，不直接依赖这个接口的任何实现。
    /// 首轮的实现是 schema.json + Scriban 直出；将来要换 Luban，就再加一个实现，业务零改动。
    /// </remarks>
    public interface ITablePipeline
    {
        /// <summary>管线实现的名字，出现在日志里用来分辨当前走的是哪条链路。</summary>
        string PipelineName { get; }

        /// <summary>Excel → 镜像 JSON，以 Excel 为准，成功后更新基线。</summary>
        /// <param name="tableName">表名。</param>
        ConfigOperationResult SyncFromWorkbook(string tableName);

        /// <summary>镜像 JSON → Excel，回写前校验基线哈希。</summary>
        /// <param name="tableName">表名。</param>
        ConfigOperationResult ApplyToWorkbook(string tableName);

        /// <summary>按 schema 校验镜像内容。</summary>
        /// <param name="tableName">表名。</param>
        ConfigOperationResult Validate(string tableName);

        /// <summary>生成强类型访问代码，返回写出的文件路径。</summary>
        /// <param name="tableName">表名。</param>
        IReadOnlyList<string> GenerateAccessCode(string tableName);

        /// <summary>导出运行时数据文件，返回写出的文件路径。</summary>
        /// <param name="tableName">表名。</param>
        IReadOnlyList<string> ExportRuntimeData(string tableName);
    }
}
