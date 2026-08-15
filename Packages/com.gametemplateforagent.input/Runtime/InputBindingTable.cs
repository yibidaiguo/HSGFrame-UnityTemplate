using System;
using System.Collections.Generic;
using System.Linq;

namespace GameTemplateForAgent.Input
{
    /// <summary>改键冲突：想绑的键已经被别的动作占着。</summary>
    public sealed class InputBindingConflict
    {
        /// <summary>占着这个键的动作名。</summary>
        public string OccupyingActionName { get; }

        /// <summary>冲突的按键。</summary>
        public string ConflictingKey { get; }

        /// <summary>以占用者动作名与冲突按键构造。</summary>
        /// <param name="occupyingActionName">占着这个键的动作名。</param>
        /// <param name="conflictingKey">冲突的按键。</param>
        public InputBindingConflict(string occupyingActionName, string conflictingKey)
        {
            OccupyingActionName = occupyingActionName;
            ConflictingKey = conflictingKey;
        }
    }

    /// <summary>输入绑定表：一个动作对应主键与副键，支持按键反查动作与带冲突检测的改键。</summary>
    public sealed class InputBindingTable
    {
        // 动作名与按键都区分大小写，用序数比较器保证同一性。
        private readonly Dictionary<string, InputBindingEntry> _entriesByAction =
            new Dictionary<string, InputBindingEntry>(StringComparer.Ordinal);

        // 默认绑定是构造时那一组的拷贝，供 ResetToDefaults 恢复；改键改的是 _entriesByAction 里的条目，不碰这里。
        private readonly List<InputBindingEntry> _defaults = new List<InputBindingEntry>();

        /// <summary>用一组绑定构造，同时把这一组记成「默认绑定」供 ResetToDefaults 用。</summary>
        /// <param name="entries">初始绑定。</param>
        public InputBindingTable(IEnumerable<InputBindingEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            // 记录每个按键的占用者，用于构造期的重复占键检测。
            var keyOwner = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var clone = Clone(entry);
                AssertKeyNotOccupiedByAnotherAction(keyOwner, clone.ActionName, clone.PrimaryKey);
                AssertKeyNotOccupiedByAnotherAction(keyOwner, clone.ActionName, clone.SecondaryKey);

                _entriesByAction[clone.ActionName] = clone;
                _defaults.Add(Clone(clone));
            }
        }

        /// <summary>全部动作名，按序数序排列。</summary>
        public IReadOnlyList<string> ActionNames =>
            _entriesByAction.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();

        /// <summary>按动作名取绑定，取不到返回 null。</summary>
        public InputBindingEntry Find(string actionName)
        {
            return _entriesByAction.TryGetValue(actionName, out var entry) ? entry : null;
        }

        /// <summary>按按键反查占用它的动作名，没人占返回 null。主键与副键都算占用。</summary>
        public string FindActionByKey(string key)
        {
            // 空键表示没绑，不存在占用者。
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            foreach (var entry in _entriesByAction.Values)
            {
                if (string.Equals(entry.PrimaryKey, key, StringComparison.Ordinal)
                    || string.Equals(entry.SecondaryKey, key, StringComparison.Ordinal))
                {
                    return entry.ActionName;
                }
            }

            return null;
        }

        /// <summary>检测把某个动作的主键或副键改成某个键会不会撞车，不撞返回 null。</summary>
        /// <param name="actionName">要改键的动作。</param>
        /// <param name="key">想绑的键。</param>
        public InputBindingConflict DetectConflict(string actionName, string key)
        {
            // 空键表示没绑，不参与冲突检测。
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            var occupant = FindActionByKey(key);
            if (occupant == null || string.Equals(occupant, actionName, StringComparison.Ordinal))
            {
                // 没人占，或占用者就是自己（自己跟自己换手），都不算冲突。
                return null;
            }

            return new InputBindingConflict(occupant, key);
        }

        /// <summary>改主键。撞车时不改动任何东西，返回冲突；成功返回 null。</summary>
        public InputBindingConflict RebindPrimary(string actionName, string key)
        {
            return Rebind(actionName, key, isPrimary: true);
        }

        /// <summary>改副键，语义同 RebindPrimary。</summary>
        public InputBindingConflict RebindSecondary(string actionName, string key)
        {
            return Rebind(actionName, key, isPrimary: false);
        }

        /// <summary>把全部绑定恢复成构造时那一组。</summary>
        public void ResetToDefaults()
        {
            foreach (var defaultEntry in _defaults)
            {
                if (_entriesByAction.TryGetValue(defaultEntry.ActionName, out var current))
                {
                    current.PrimaryKey = defaultEntry.PrimaryKey;
                    current.SecondaryKey = defaultEntry.SecondaryKey;
                }
            }
        }

        /// <summary>导出当前全部绑定的快照，按动作名序数序排列。</summary>
        public IReadOnlyList<InputBindingEntry> Snapshot()
        {
            return _entriesByAction.Values
                .OrderBy(entry => entry.ActionName, StringComparer.Ordinal)
                .Select(Clone)
                .ToList();
        }

        private InputBindingConflict Rebind(string actionName, string key, bool isPrimary)
        {
            var entry = RequireAction(actionName);

            var conflict = DetectConflict(actionName, key);
            if (conflict != null)
            {
                return conflict;
            }

            if (isPrimary)
            {
                entry.PrimaryKey = key;
            }
            else
            {
                entry.SecondaryKey = key;
            }

            return null;
        }

        private InputBindingEntry RequireAction(string actionName)
        {
            if (!_entriesByAction.TryGetValue(actionName, out var entry))
            {
                throw new ArgumentException(
                    $"位置：输入绑定表改键；原因：动作不存在；修复：改用已登记的动作名；参考：动作名「{actionName}」",
                    nameof(actionName));
            }

            return entry;
        }

        private static void AssertKeyNotOccupiedByAnotherAction(
            Dictionary<string, string> keyOwner, string actionName, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (keyOwner.TryGetValue(key, out var existingOwner)
                && !string.Equals(existingOwner, actionName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"位置：输入绑定表构造；原因：按键「{key}」被多个动作占用；" +
                    $"修复：给「{existingOwner}」与「{actionName}」分配不同按键；参考：每个键至多绑定一个动作");
            }

            keyOwner[key] = actionName;
        }

        private static InputBindingEntry Clone(InputBindingEntry entry)
        {
            return new InputBindingEntry
            {
                ActionName = entry.ActionName,
                PrimaryKey = entry.PrimaryKey,
                SecondaryKey = entry.SecondaryKey,
            };
        }
    }
}
