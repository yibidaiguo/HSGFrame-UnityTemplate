using System;
using System.ComponentModel;
using System.Reflection;
using Template.Toolkit.CommandFramework;
using Template.Toolkit.CommandHost;
using Xunit;

namespace Template.Toolkit.Tests
{
    /// <summary>参数绑定器的默认值填充测试：框架要在绑定阶段把 [DefaultValue] 填进参数对象。</summary>
    public class CommandArgumentBinderTests
    {
        // 样例命令方法：只服务于前四条测试的绑定行为，不参与注册表扫描结果。
        [EditorCommand("test.sample")]
        private static CommandResult SampleCommand(SampleArguments arguments)
        {
            return CommandResult.Success("ok");
        }

        /// <summary>样例参数类：三类带默认值的属性加一个必填属性。</summary>
        private sealed class SampleArguments
        {
            /// <summary>带默认值的字符串属性。</summary>
            [DefaultValue("默认路径")]
            public string ConfigurationPath { get; set; }

            /// <summary>带默认值的布尔属性。</summary>
            [DefaultValue(true)]
            public bool PlanOnly { get; set; }

            /// <summary>带默认值的整数属性。</summary>
            [DefaultValue(15)]
            public int TimeoutMinutes { get; set; }

            /// <summary>没有默认值，必须由调用方提供。</summary>
            public string RequiredName { get; set; }
        }

        [Fact]
        public void BindFillsDefaultValueWhenPropertyIsAbsent()
        {
            var arguments = (SampleArguments)CommandArgumentBinder.Bind(BuildSampleDescriptor(), "{}");

            Assert.Equal("默认路径", arguments.ConfigurationPath);
            Assert.True(arguments.PlanOnly);
            Assert.Equal(15, arguments.TimeoutMinutes);
        }

        // 这条是本次最要紧的断言：显式写的 false 不能被默认值 true 盖掉。
        // 「值等于类型默认值就覆盖」那种判法会把调用方写的 false 悄悄改成 true。
        [Fact]
        public void BindKeepsExplicitValueEvenWhenItEqualsTypeDefault()
        {
            var arguments = (SampleArguments)CommandArgumentBinder.Bind(
                BuildSampleDescriptor(),
                "{\"PlanOnly\":false}");

            Assert.False(arguments.PlanOnly);
        }

        [Fact]
        public void BindTreatsEmptyTextAsEmptyObject()
        {
            var fromEmptyText = (SampleArguments)CommandArgumentBinder.Bind(BuildSampleDescriptor(), "");
            var fromNull = (SampleArguments)CommandArgumentBinder.Bind(BuildSampleDescriptor(), null);

            Assert.Equal("默认路径", fromEmptyText.ConfigurationPath);
            Assert.Equal("默认路径", fromNull.ConfigurationPath);
        }

        [Fact]
        public void BindMatchesPropertyNameCaseInsensitively()
        {
            var arguments = (SampleArguments)CommandArgumentBinder.Bind(
                BuildSampleDescriptor(),
                "{\"configurationPath\":\"来自调用方\"}");

            // 小写开头也算「JSON 里写了」，不能被默认值盖掉。
            Assert.Equal("来自调用方", arguments.ConfigurationPath);
        }

        // 把「标了默认值却不兜底」这类缺陷钉死在框架层：全部命令参数类里，
        // 凡是带 [DefaultValue] 的属性，绑定器在空参数下都必须填上特性声明的值。
        [Fact]
        public void EveryCommandWithDefaultValueGetsItMaterialized()
        {
            var descriptors = CommandRegistry.ScanAssemblies(typeof(Program).Assembly);

            foreach (var descriptor in descriptors)
            {
                var arguments = CommandArgumentBinder.Bind(descriptor, "{}");

                foreach (var property in descriptor.ArgumentType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanWrite)
                    {
                        continue;
                    }

                    var attribute = property.GetCustomAttribute<DefaultValueAttribute>();
                    if (attribute == null)
                    {
                        continue;
                    }

                    var actual = property.GetValue(arguments);
                    Assert.True(
                        Equals(attribute.Value, actual),
                        $"命令 {descriptor.CommandName} 的参数 {property.Name} 标了 [DefaultValue] 却没被框架填值");
                }
            }
        }

        private static CommandDescriptor BuildSampleDescriptor()
        {
            var method = typeof(CommandArgumentBinderTests).GetMethod(
                nameof(SampleCommand),
                BindingFlags.NonPublic | BindingFlags.Static);

            return new CommandDescriptor(
                "test.sample",
                "样例命令",
                typeof(SampleArguments),
                method,
                Array.Empty<CommandParameterSchema>());
        }
    }
}
