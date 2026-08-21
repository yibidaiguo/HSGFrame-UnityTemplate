using Template.Toolkit.CommandFramework;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>命令参数校验器的边界测试。</summary>
    public class CommandArgumentValidatorTests
    {
        [Fact]
        public void EmptyJsonProducesSingleDiagnosticWithFixAction()
        {
            var diagnostics = CommandArgumentValidator.Validate(BuildDescriptor(), string.Empty);

            var diagnostic = Assert.Single(diagnostics);
            Assert.False(string.IsNullOrEmpty(diagnostic.FixAction));
        }

        [Fact]
        public void WhitespaceJsonProducesSingleDiagnostic()
        {
            var diagnostics = CommandArgumentValidator.Validate(BuildDescriptor(), "   \n\t ");

            Assert.Single(diagnostics);
        }

        [Fact]
        public void MalformedJsonReportsLineOrColumn()
        {
            var diagnostics = CommandArgumentValidator.Validate(BuildDescriptor(), "{\"A\":}");

            var diagnostic = Assert.Single(diagnostics);
            Assert.Matches("[0-9]", diagnostic.Reason);
        }

        [Fact]
        public void TopLevelArrayProducesSingleDiagnostic()
        {
            var diagnostics = CommandArgumentValidator.Validate(BuildDescriptor(), "[]");

            Assert.Single(diagnostics);
        }

        [Fact]
        public void MissingRequiredParameterReportsItsNameAsLocation()
        {
            var descriptor = BuildDescriptor(Required("RepositoryRoot", "String"));
            var diagnostics = CommandArgumentValidator.Validate(descriptor, "{}");

            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal("RepositoryRoot", diagnostic.Location);
        }

        [Fact]
        public void NullRequiredParameterIsReportedMissing()
        {
            var descriptor = BuildDescriptor(Required("RepositoryRoot", "String"));
            var diagnostics = CommandArgumentValidator.Validate(descriptor, "{\"RepositoryRoot\":null}");

            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal("RepositoryRoot", diagnostic.Location);
            Assert.Contains("缺失", diagnostic.Reason);
        }

        [Fact]
        public void WhitespaceStringRequiredParameterIsReportedMissing()
        {
            var descriptor = BuildDescriptor(Required("RepositoryRoot", "String"));
            var diagnostics = CommandArgumentValidator.Validate(descriptor, "{\"RepositoryRoot\":\"   \"}");

            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal("RepositoryRoot", diagnostic.Location);
            Assert.Contains("缺失", diagnostic.Reason);
        }

        [Fact]
        public void WrongTypeReportsExpectedAndActualType()
        {
            var descriptor = BuildDescriptor(Required("RepositoryRoot", "String"));
            var diagnostics = CommandArgumentValidator.Validate(descriptor, "{\"RepositoryRoot\":123}");

            var diagnostic = Assert.Single(diagnostics);
            Assert.Contains("String", diagnostic.Reason);
            Assert.Contains("数字", diagnostic.Reason);
        }

        [Fact]
        public void AllRequiredParametersPresentProduceNoDiagnostics()
        {
            var descriptor = BuildDescriptor(
                Required("RepositoryRoot", "String"),
                Required("IncludeWarnings", "Boolean"));
            var diagnostics = CommandArgumentValidator.Validate(
                descriptor,
                "{\"RepositoryRoot\":\"D:/示例/仓库根\",\"IncludeWarnings\":true}");

            Assert.Empty(diagnostics);
        }

        [Fact]
        public void MultipleMissingRequiredParametersAreAllReported()
        {
            var descriptor = BuildDescriptor(
                Required("RepositoryRoot", "String"),
                Required("SolutionPath", "String"),
                Required("TableName", "String"));
            var diagnostics = CommandArgumentValidator.Validate(descriptor, "{}");

            Assert.Equal(3, diagnostics.Count);
        }

        /// <summary>
        /// 认不出来的参数键要报错，不许静默忽略。
        /// 这一条是真事故换来的：driver 自述里的试跑写成 `--dry-run`，而属性名是 `DryRun`，
        /// 键对不上被吞掉、命令按默认值跑完、退出码 0，于是一次「干跑」按钮执行了一次真跑。
        /// </summary>
        [Fact]
        public void UnknownParameterNameIsRejected()
        {
            var descriptor = BuildDescriptor(Required("Driver", "String"));

            var diagnostics = CommandArgumentValidator.Validate(descriptor, "{\"Driver\":\"x\",\"dry-run\":true}");

            Assert.Single(diagnostics);
            Assert.Equal("dry-run", diagnostics[0].Location);
            Assert.Contains("不认识这个参数名", diagnostics[0].Reason);
            Assert.Contains("Driver", diagnostics[0].FixAction);
        }

        /// <summary>参数名大小写不敏感：写成 driver 照样认，不该被当成未知参数。</summary>
        [Fact]
        public void ParameterNameMatchIsCaseInsensitive()
        {
            var descriptor = BuildDescriptor(Required("Driver", "String"));

            var diagnostics = CommandArgumentValidator.Validate(descriptor, "{\"driver\":\"x\"}");

            Assert.Empty(diagnostics);
        }

        /// <summary>多个未知参数一次全报出来，不是报一个就停。</summary>
        [Fact]
        public void AllUnknownParameterNamesAreReportedAtOnce()
        {
            var descriptor = BuildDescriptor(Required("Driver", "String"));

            var diagnostics = CommandArgumentValidator.Validate(descriptor, "{\"Driver\":\"x\",\"a\":1,\"b\":2}");

            Assert.Equal(2, diagnostics.Count);
        }

        private static CommandDescriptor BuildDescriptor(params CommandParameterSchema[] schemas)
        {
            var method = typeof(CommandArgumentValidatorTests).GetMethod(nameof(NoOpCommand));
            return new CommandDescriptor("test.command", "测试命令", typeof(object), method, schemas);
        }

        private static CommandParameterSchema Required(string name, string typeName)
        {
            return new CommandParameterSchema(name, typeName, isRequired: true, description: "参数说明");
        }

        private static void NoOpCommand()
        {
        }
    }
}
