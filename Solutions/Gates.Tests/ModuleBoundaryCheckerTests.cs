using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>模块边界检查测试：私有面越界要报、Contracts 与 Events 要放行、模块自引与工具链不管。</summary>
    public class ModuleBoundaryCheckerTests
    {
        /// <summary>模块 Combat 直接引用模块 Level 的 Data，正是规范禁的那种耦合，必须报一条。</summary>
        [Fact]
        public void CrossModulePrivateReferenceIsReported()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Modules/Combat/CombatService.cs",
                    "using Template.Level.Data;",
                    "namespace Template.Combat { public static class CombatService { } }");

                var findings = ModuleBoundaryChecker.Check(scriptsRoot, new GateConfiguration());

                var finding = Assert.Single(findings);
                Assert.Equal(
                    Path.Combine(scriptsRoot, "Modules/Combat/CombatService.cs").Replace('/', Path.DirectorySeparatorChar) + ":1",
                    finding.Location);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        /// <summary>引用别的模块的 Contracts 与 Events 是规范给的正门，一条都不能报。</summary>
        [Fact]
        public void CrossModulePublicReferenceIsAccepted()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Modules/Combat/CombatService.cs",
                    "using Template.Level.Contracts;",
                    "using Template.Level.Events;",
                    "namespace Template.Combat { public static class CombatService { } }");

                var findings = ModuleBoundaryChecker.Check(scriptsRoot, new GateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        /// <summary>模块内部怎么引自己不归这条规矩管：Level 的 View 引 Level 的 Data 是正常的。</summary>
        [Fact]
        public void SameModuleReferenceIsAccepted()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Modules/Level/View/LevelMarker.cs",
                    "using Template.Level.Data;",
                    "namespace Template.Level.View { public sealed class LevelMarker { } }");

                var findings = ModuleBoundaryChecker.Check(scriptsRoot, new GateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        /// <summary>模块之外的业务代码（Shared、View、Boot）越界同样要报，边界不是只管模块之间。</summary>
        [Fact]
        public void ReferenceFromOutsideModulesIsReported()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Boot/GameBootstrap.cs",
                    "using Template.Level.Data;",
                    "namespace Template.Boot { public static class GameBootstrap { } }");

                var findings = ModuleBoundaryChecker.Check(scriptsRoot, new GateConfiguration());

                var finding = Assert.Single(findings);
                Assert.Equal(
                    Path.Combine(scriptsRoot, "Boot/GameBootstrap.cs").Replace('/', Path.DirectorySeparatorChar) + ":1",
                    finding.Location);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        /// <summary>工具链是编辑器侧、天然要深入模块内部，在检查范围之外，一条都不能报。</summary>
        [Fact]
        public void ToolkitTreeIsOutOfScope()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Toolkit/Editor/Level/LevelSceneBuilder.cs",
                    "using Template.Level.Data;",
                    "using Template.Level.View;",
                    "namespace Template.Toolkit.Editor { public static class LevelSceneBuilder { } }");

                var findings = ModuleBoundaryChecker.Check(scriptsRoot, new GateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        /// <summary>注释与字符串字面量里出现的限定名不是引用，不能当越界报出来。</summary>
        [Fact]
        public void ReferenceInCommentOrStringIsNotReported()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Modules/Combat/CombatService.cs",
                    "// 反例：using Template.Level.Data; 就是这条规矩要拦的东西",
                    "namespace Template.Combat {",
                    "    public static class CombatService {",
                    "        private const string Sample = \"Template.Level.Data.LevelChunk\";",
                    "    }",
                    "}");

                var findings = ModuleBoundaryChecker.Check(scriptsRoot, new GateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        /// <summary>挂进豁免清单的路径先放行——欠账要能记账，否则检查器根本上不了线。</summary>
        [Fact]
        public void ExemptPathIsAccepted()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Modules/Combat/CombatService.cs",
                    "using Template.Level.Data;",
                    "namespace Template.Combat { public static class CombatService { } }");

                var configuration = new GateConfiguration
                {
                    ModuleBoundaryExemptPaths = new[] { "Modules/Combat/" },
                };

                var findings = ModuleBoundaryChecker.Check(scriptsRoot, configuration);

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        /// <summary>模块名直接从 Modules/ 的子目录读，不另开清单——目录就是事实源。</summary>
        [Fact]
        public void ModuleNamesComeFromModulesDirectory()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                Assert.Equal(new[] { "Combat", "Level" }, ModuleBoundaryChecker.ReadModuleNames(scriptsRoot).ToArray());
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        private static string CreateScriptsTree()
        {
            var root = Path.Combine(Path.GetTempPath(), "ModuleBoundaryCheckerTests-" + Guid.NewGuid().ToString("N"));
            foreach (var relativePath in new[]
                     {
                         "Modules/Combat", "Modules/Level/Data", "Modules/Level/View",
                         "Shared", "View", "Boot", "Toolkit/Editor/Level",
                     })
            {
                Directory.CreateDirectory(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            return root;
        }

        private static void WriteSource(string root, string relativePath, params string[] lines)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllLines(fullPath, lines);
        }
    }
}
