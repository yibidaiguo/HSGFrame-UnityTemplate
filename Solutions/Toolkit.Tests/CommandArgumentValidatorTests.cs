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
