using System;
using System.Collections.Generic;

namespace HSGhost.Save
{
    /// <summary>按版本号逐级驱动迁移链，把旧存档升到当前支持的版本。</summary>
    public sealed class SaveMigrator
    {
        private readonly int _currentVersion;
        private readonly IReadOnlyList<ISaveMigration> _migrations;

        /// <summary>以当前版本号与迁移列表构造迁移器。</summary>
        public SaveMigrator(int currentVersion, IReadOnlyList<ISaveMigration> migrations)
        {
            _currentVersion = currentVersion;
            _migrations = migrations;
        }

        /// <summary>把文档迁移到当前版本，返回成功与否与已执行的步骤。</summary>
        public SaveMigrationResult Migrate(SaveDocument document)
        {
            // 旧客户端遇到更新存档必须明确拒绝：读出乱数据比直接报错更糟。
            if (document.Version > _currentVersion)
            {
                return SaveMigrationResult.Fail(
                    $"存档版本 {document.Version} 高于本客户端支持的 {_currentVersion}，拒绝读取");
            }

            if (document.Version == _currentVersion)
            {
                return SaveMigrationResult.Success(Array.Empty<string>());
            }

            var appliedSteps = new List<string>();
            var version = document.Version;

            while (version < _currentVersion)
            {
                var migration = FindMigration(version);
                if (migration == null)
                {
                    return SaveMigrationResult.Fail($"缺少从版本 {version} 升到 {version + 1} 的迁移");
                }

                migration.Apply(document);
                appliedSteps.Add($"{version} → {version + 1}");
                version = migration.ToVersion;
            }

            document.Version = _currentVersion;
            return SaveMigrationResult.Success(appliedSteps);
        }

        private ISaveMigration FindMigration(int fromVersion)
        {
            foreach (var migration in _migrations)
            {
                if (migration.FromVersion == fromVersion)
                {
                    return migration;
                }
            }

            return null;
        }
    }

    /// <summary>一次迁移的结果：成功与否、提示消息、已执行的迁移步骤。</summary>
    public sealed class SaveMigrationResult
    {
        /// <summary>迁移是否成功。</summary>
        public bool IsSuccess { get; }

        /// <summary>结果消息，失败时说明原因，成功时为空串。</summary>
        public string Message { get; }

        /// <summary>实际执行的迁移步骤，每步形如「1 → 2」。</summary>
        public IReadOnlyList<string> AppliedSteps { get; }

        private SaveMigrationResult(bool isSuccess, string message, IReadOnlyList<string> appliedSteps)
        {
            IsSuccess = isSuccess;
            Message = message;
            AppliedSteps = appliedSteps;
        }

        /// <summary>构造一个成功结果，附带已执行的步骤。</summary>
        public static SaveMigrationResult Success(IReadOnlyList<string> appliedSteps)
            => new SaveMigrationResult(true, string.Empty, appliedSteps);

        /// <summary>构造一个失败结果，附带失败原因。</summary>
        public static SaveMigrationResult Fail(string message)
            => new SaveMigrationResult(false, message, Array.Empty<string>());
    }
}
