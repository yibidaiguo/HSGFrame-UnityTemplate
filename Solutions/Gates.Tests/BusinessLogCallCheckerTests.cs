using System;
using System.IO;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>
    /// 业务层裸日志检查测试：Modules/、Shared/、View/ 里的 UnityEngine.Debug.* 要报，
    /// Boot/ 与 Toolkit/ 放行，注释与字符串字面量里的不算数，豁免清单先放行。
    /// </summary>
    public class BusinessLogCallCheckerTests
    {
        /// <summary>Modules/Combat 是纯逻辑层，写裸 Debug.Log 必须报一条，位置精确到文件与行号。</summary>
        [Fact]
        public void DebugLogInModuleIsReported()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Modules/Combat/CombatService.cs", "Debug.Log(\"hi\");");

                var findings = BusinessLogCallChecker.Check(scriptsRoot, null);

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

        /// <summary>带 UnityEngine. 前缀的限定名调用同样要拦——全限定写不躲检查。</summary>
        [Fact]
        public void QualifiedDebugLogErrorIsReported()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "View/Hud.cs", "UnityEngine.Debug.LogError(\"boom\");");

                var findings = BusinessLogCallChecker.Check(scriptsRoot, null);

                var finding = Assert.Single(findings);
                Assert.Equal(
                    Path.Combine(scriptsRoot, "View/Hud.cs").Replace('/', Path.DirectorySeparatorChar) + ":1",
                    finding.Location);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        /// <summary>Boot/ 是 AOT 启动装配，本来就直接对着引擎说话，不在扫描范围内。</summary>
        [Fact]
        public void DebugLogInBootIsAccepted()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Boot/GameBootstrap.cs", "Debug.Log(\"hi\");");

                var findings = BusinessLogCallChecker.Check(scriptsRoot, null);

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        /// <summary>Toolkit/ 是编辑器工具链，同样对着引擎说话，不在扫描范围内。</summary>
        [Fact]
        public void DebugLogInToolkitIsAccepted()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Toolkit/Editor/BuildEntry.cs", "Debug.Log(\"hi\");");

                var findings = BusinessLogCallChecker.Check(scriptsRoot, null);

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        /// <summary>注释与字符串字面量里的 Debug.Log 不是调用，不能报。</summary>
        [Fact]
        public void DebugLogInCommentOrStringIsAccepted()
        {
            var scriptsRoot = CreateScriptsTree();
            try
            {
                WriteSource(scriptsRoot, "Shared/Helper.cs",
                    "// 反例：Debug.Log(\"x\");",
                    "private const string Sample = \"Debug.Log\";");

                var findings = BusinessLogCallChecker.Check(scriptsRoot, null);

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
                WriteSource(scriptsRoot, "View/Hud.cs", "Debug.Log(\"hi\");");

                var findings = BusinessLogCallChecker.Check(scriptsRoot, new[] { "View/Hud.cs" });

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(scriptsRoot, true);
            }
        }

        private static string CreateScriptsTree()
        {
            var root = Path.Combine(Path.GetTempPath(), "BusinessLogCallCheckerTests-" + Guid.NewGuid().ToString("N"));
            foreach (var relativePath in new[]
                     {
                         "Modules/Combat", "Shared", "View", "Boot", "Toolkit/Editor",
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
