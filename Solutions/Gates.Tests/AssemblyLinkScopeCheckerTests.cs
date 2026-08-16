using System;
using System.IO;
using Template.Toolkit.Gates;
using Xunit;

namespace Template.Toolkit.Gates.Tests
{
    /// <summary>
    /// 装配对账检查测试（《结构规范-代码》第三节 R3）：csproj 链接范围与 Game.Logic 装配覆盖一致时不报，
    /// 两边各偏一度就各报一条。最小树里 Modules 归 Game.Logic、Shared 经 asmref 并入 Game.Logic、
    /// 模块内 View 夹经 asmref 归 Game.View、顶层 View 归 Game.View。
    /// </summary>
    public class AssemblyLinkScopeCheckerTests
    {
        private const string ProjectFileName = "Logic.Core.csproj";

        /// <summary>最小树 + 与真实 csproj 同形的链接，csproj 侧与 Game.Logic 侧完全对齐，一条都不能报。</summary>
        [Fact]
        public void AlignedScopeReportsNothing()
        {
            var root = CreateScriptsTree();
            try
            {
                WriteProjectFile(root, BuildProjectFile(includeSharedCompile: true, includeModulesExclude: true));

                var findings = AssemblyLinkScopeChecker.Check(
                    Path.Combine(root, ProjectFileName), root);

                Assert.Empty(findings);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Modules 那条 Compile 的 Exclude 整个去掉，View 夹里的文件被链接进来却归 Game.View，要报一条。</summary>
        [Fact]
        public void FileLinkedButNotInGameLogicIsReported()
        {
            var root = CreateScriptsTree();
            try
            {
                WriteProjectFile(root, BuildProjectFile(includeSharedCompile: true, includeModulesExclude: false));

                var findings = AssemblyLinkScopeChecker.Check(
                    Path.Combine(root, ProjectFileName), root);

                var finding = Assert.Single(findings);
                Assert.Equal("Modules/Level/View/LevelMarker.cs", finding.Location);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>csproj 只留 Modules 那条、去掉 Shared 那条，Shared 里的文件归 Game.Logic 却没被链接，要报一条。</summary>
        [Fact]
        public void FileInGameLogicButNotLinkedIsReported()
        {
            var root = CreateScriptsTree();
            try
            {
                WriteProjectFile(root, BuildProjectFile(includeSharedCompile: false, includeModulesExclude: true));

                var findings = AssemblyLinkScopeChecker.Check(
                    Path.Combine(root, ProjectFileName), root);

                var finding = Assert.Single(findings);
                Assert.Equal("Shared/Contracts/ILogSink.cs", finding.Location);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>顶层 View 归 Game.View，csproj 与 Game.Logic 两侧都不该有它，检查结果里不能出现它。</summary>
        [Fact]
        public void FilesOutsideGameLogicAreIgnored()
        {
            var root = CreateScriptsTree();
            try
            {
                WriteProjectFile(root, BuildProjectFile(includeSharedCompile: true, includeModulesExclude: true));

                var findings = AssemblyLinkScopeChecker.Check(
                    Path.Combine(root, ProjectFileName), root);

                Assert.DoesNotContain(findings, finding => finding.Location == "View/Hud.cs");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// 在临时目录造一棵固定最小树：Modules 落 Game.Logic.asmdef，模块内 View 夹放指向 Game.View 的 asmref，
        /// Shared 放指向 Game.Logic 的 asmref，顶层 View 落 Game.View.asmdef。
        /// </summary>
        private static string CreateScriptsTree()
        {
            var root = Path.Combine(Path.GetTempPath(), "AssemblyLinkScopeCheckerTests-" + Guid.NewGuid().ToString("N"));
            WriteText(Path.Combine(root, "Modules/Game.Logic.asmdef"), "{\"name\":\"Game.Logic\"}");
            WriteText(Path.Combine(root, "Modules/Combat/CombatService.cs"),
                "namespace Template.Combat { public static class CombatService { } }");
            WriteText(Path.Combine(root, "Modules/Level/View/Game.View.asmref"), "{\"reference\":\"Game.View\"}");
            WriteText(Path.Combine(root, "Modules/Level/View/LevelMarker.cs"),
                "namespace Template.Level.View { public sealed class LevelMarker { } }");
            WriteText(Path.Combine(root, "Shared/Game.Logic.asmref"), "{\"reference\":\"Game.Logic\"}");
            WriteText(Path.Combine(root, "Shared/Contracts/ILogSink.cs"),
                "namespace Template.Shared.Contracts { public interface ILogSink { } }");
            WriteText(Path.Combine(root, "View/Game.View.asmdef"), "{\"name\":\"Game.View\"}");
            WriteText(Path.Combine(root, "View/Hud.cs"),
                "namespace Template.View { public sealed class Hud { } }");
            return root;
        }

        /// <summary>
        /// 造一份与真实 Logic.Core.csproj 同形的项目文件：Include 相对 scripts 根写、Exclude 排除 View/Editor 夹。
        /// </summary>
        /// <param name="includeSharedCompile">是否保留 Shared 那条 Compile。</param>
        /// <param name="includeModulesExclude">Modules 那条 Compile 是否带 Exclude（去掉它 View 夹就被链接进来）。</param>
        private static string BuildProjectFile(bool includeSharedCompile, bool includeModulesExclude)
        {
            var modulesExclude = includeModulesExclude
                ? "             Exclude=\"../../UnityProject/Assets/Game/Scripts/Modules/**/View/**/*.cs;" +
                  "../../UnityProject/Assets/Game/Scripts/Modules/**/Editor/**/*.cs\"\n"
                : string.Empty;
            var sharedCompile = includeSharedCompile
                ? "    <Compile Include=\"../../UnityProject/Assets/Game/Scripts/Shared/**/*.cs\"\n" +
                  "             Exclude=\"../../UnityProject/Assets/Game/Scripts/Shared/**/View/**/*.cs;" +
                  "../../UnityProject/Assets/Game/Scripts/Shared/**/Editor/**/*.cs\" />\n"
                : string.Empty;
            return "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n"
                + "    <Compile Include=\"../../UnityProject/Assets/Game/Scripts/Modules/**/*.cs\"\n"
                + modulesExclude + " />\n"
                + sharedCompile
                + "  </ItemGroup>\n</Project>\n";
        }

        private static void WriteProjectFile(string root, string content)
        {
            File.WriteAllText(Path.Combine(root, ProjectFileName), content);
        }

        private static void WriteText(string path, string content)
        {
            var fullPath = path.Replace('/', Path.DirectorySeparatorChar);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
        }
    }
}
