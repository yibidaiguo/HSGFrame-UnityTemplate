using System;
using System.Collections.Generic;
using System.IO;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>层边界检查测试：协作目录落资产树要报、_Generated 放行、游戏代码引用协作路径要报、资产目录缺失放行。</summary>
    public class LayerBoundaryCheckerTests
    {
        /// <summary>Unity 资产树下出现 Pools 目录 → 一条发现，位置是仓库相对路径。</summary>
        [Fact]
        public void CollaborationDirectoryUnderAssetsIsReported()
        {
            var root = CreateRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "UnityProject", "Assets", "Pools"));

                var findings = LayerBoundaryChecker.Check(
                    root, Path.Combine(root, "UnityProject", "Assets"), new GateConfiguration());

                var finding = Assert.Single(findings);
                Assert.Contains("Pools", finding.Reason);
                Assert.Contains("UnityProject/Assets/Pools", finding.Location);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>_Generated 不在协作目录名单里——View/_Generated 是 UI 代码生成的合法落点，零发现。</summary>
        [Fact]
        public void GeneratedViewDirectoryIsAccepted()
        {
            var root = CreateRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "UnityProject", "Assets", "Game", "Scripts", "View", "_Generated"));

                var findings = LayerBoundaryChecker.Check(
                    root, Path.Combine(root, "UnityProject", "Assets"), new GateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Game/Scripts 下的 .cs 引用「Pools/x.json」→ 一条发现，位置带行号。</summary>
        [Fact]
        public void GameScriptReferencingPoolsIsReported()
        {
            var root = CreateRoot();
            try
            {
                WriteFile(root, "UnityProject/Assets/Game/Scripts/Modules/Level/LevelData.cs",
                    "namespace Template.Level {",
                    "    public static class LevelData {",
                    "        private const string SamplePath = \"Pools/x.json\";",
                    "    }",
                    "}");

                var findings = LayerBoundaryChecker.Check(
                    root, Path.Combine(root, "UnityProject", "Assets"), new GateConfiguration());

                var finding = Assert.Single(findings);
                Assert.Contains("Pools/", finding.Reason);
                Assert.Contains(":3", finding.Location);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Unity 资产目录不存在 → 零发现且不抛，新生成的项目可能还没有 Unity 工程。</summary>
        [Fact]
        public void MissingAssetsDirectoryReturnsEmpty()
        {
            var root = CreateRoot();
            try
            {
                var findings = LayerBoundaryChecker.Check(
                    root, Path.Combine(root, "UnityProject", "Assets"), new GateConfiguration());

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>建一个空的测试根目录。</summary>
        private static string CreateRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "LayerBoundaryCheckerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>在根下写一个相对路径文件，目录不存在先创建。</summary>
        private static void WriteFile(string root, string relativePath, params string[] lines)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllLines(fullPath, lines);
        }
    }
}
