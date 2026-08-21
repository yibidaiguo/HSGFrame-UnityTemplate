using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.CommandFramework;
using Xunit;

namespace Template.Toolkit.IndexingTests
{
    /// <summary>
    /// 门禁接线对账测试：检查器命令必须真被某个 gate 脚本调用。
    /// 「加了检查器忘接线」此前是静默的——asset 族十道有实现有单测，却整整八期不在任何脚本里。
    /// 这里把接线钉成测试：注册了 gate.* 或列入必接清单的命令，不在脚本文本里出现就红。
    /// </summary>
    public class GateWiringCoverageTests
    {
        // 检查器族之外的必接清单：不是 gate.* 但同样是「跑一遍给判定」的命令。
        // 新写的检查器命令加进这里，忘了接线这条测试会替你记得。
        private static readonly string[] RequiredCheckerCommands =
        {
            "asset.validate", "asset.references", "asset.dependencies", "asset.bundlegroups",
            "asset.loadgroups", "asset.rulecoverage", "asset.duplicates", "asset.atlas",
            "asset.residentbudget", "index.check", "config.validate",
            "pool.validate", "schema.check", "codegen.run", "ui.scaffold"
        };

        // 注册为 gate.* 但刻意不由脚本调用的例外，每条都要写清理由。
        private static readonly Dictionary<string, string> ExemptGateCommands = new()
        {
        };

        /// <summary>每个注册的 gate.* 命令都被某个 gate 脚本调用（例外要列进豁免表并写理由）。</summary>
        [Fact]
        public void EveryGateCommandIsWiredIntoAScript()
        {
            var scriptText = ReadAllGateScripts();
            var gateCommands = ScanCommands().Where(name => name.StartsWith("gate.", StringComparison.Ordinal));

            var missing = gateCommands
                .Where(name => !ExemptGateCommands.ContainsKey(name))
                .Where(name => !scriptText.Contains("'" + name + "'", StringComparison.Ordinal))
                .ToList();

            Assert.True(missing.Count == 0,
                "这些 gate.* 命令没有被任何 gate 脚本调用（接进脚本，或列进豁免表并写理由）：" + string.Join("、", missing));
        }

        /// <summary>必接清单里的检查器命令都被某个 gate 脚本调用。</summary>
        [Fact]
        public void EveryRequiredCheckerIsWiredIntoAScript()
        {
            var scriptText = ReadAllGateScripts();
            var registered = ScanCommands().ToHashSet(StringComparer.Ordinal);

            var unknown = RequiredCheckerCommands.Where(name => !registered.Contains(name)).ToList();
            Assert.True(unknown.Count == 0, "必接清单里有不存在的命令（清单该更新了）：" + string.Join("、", unknown));

            var missing = RequiredCheckerCommands
                .Where(name => !scriptText.Contains("'" + name + "'", StringComparison.Ordinal))
                .ToList();
            Assert.True(missing.Count == 0,
                "这些检查器命令没有被任何 gate 脚本调用：" + string.Join("、", missing));
        }

        /// <summary>豁免表里不许出现其实已接线的命令——豁免要随接线一起清掉，不许烂在表里。</summary>
        [Fact]
        public void ExemptListContainsOnlyUnwiredCommands()
        {
            var scriptText = ReadAllGateScripts();
            var stale = ExemptGateCommands.Keys
                .Where(name => scriptText.Contains("'" + name + "'", StringComparison.Ordinal))
                .ToList();

            Assert.True(stale.Count == 0, "这些命令已接线，豁免条目该删了：" + string.Join("、", stale));
        }

        private static IEnumerable<string> ScanCommands()
        {
            return CommandRegistry.ScanAssemblies(typeof(Template.Toolkit.CommandHost.Program).Assembly)
                .Select(command => command.CommandName);
        }

        /// <summary>三个 gate 脚本的全文拼一起；按仓库根定位，测试跑在 bin 下要一路向上找。</summary>
        private static string ReadAllGateScripts()
        {
            var root = FindRepositoryRoot();
            var scripts = new[] { "gate.ps1", "gate-unity.ps1", "gate-full.ps1" };
            return string.Join("\n", scripts.Select(name =>
                File.ReadAllText(Path.Combine(root, "Tools", "Gates", name))));
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Tools", "Gates", "gate.ps1")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("从测试输出目录向上找不到仓库根（Tools/Gates/gate.ps1 不在）");
        }
    }
}
