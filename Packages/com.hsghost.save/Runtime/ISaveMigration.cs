namespace HSGhost.Save
{
    /// <summary>单步存档迁移：把文档从 FromVersion 升到相邻的 ToVersion。</summary>
    public interface ISaveMigration
    {
        /// <summary>迁移起始版本号。</summary>
        int FromVersion { get; }

        /// <summary>迁移目标版本号，约定等于 FromVersion + 1，迁移链一步一升。</summary>
        int ToVersion { get; }

        /// <summary>对文档施加本步迁移的改写。</summary>
        void Apply(SaveDocument document);
    }
}
