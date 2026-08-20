using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Template.Toolkit.CreationPipeline.Tests
{
    /// <summary>下游调用器的子进程管道测试：起真子进程验管道、超时必杀、stdout 非 JSON 判响应不合协议。</summary>
    public class BridgeInvokerTests
    {
        private const string DriverJson = """
            {
              "名称": "testdriver",
              "port": ["模型加工"],
              "形态": "本地",
              "契约版本": ">=1.0 <2.0",
              "实现": "bridge-test",
              "字段类型映射": {}
            }
            """;

        /// <summary>起一个真子进程验管道：dotnet --version 退出码 0、stdout 是文本不是 JSON → 响应不合协议。
        /// 这证明进程起得来、退出码拿得到、stdout 真的被读到（读到了非 JSON 内容才会解析失败）。</summary>
        [Fact]
        public void InvokeStartsRealSubprocessAndReadsStdout()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, "dotnet", new[] { "--version" });
            WriteDriver(workspace.Root);

            var result = BridgeInvoker.Invoke(workspace.Root, "testdriver", "caps", EmptyPayload(), timeoutSeconds: 60);

            Assert.False(result.Succeeded);
            Assert.Equal("响应不合协议", result.ErrorCode);
            Assert.False(result.TimedOut);
            Assert.Contains("不是协议 JSON", result.HumanText);
        }

        /// <summary>超时必杀：子进程跑 60 秒、超时给 2 秒，必须被强制终止并报「超时」。</summary>
        [Fact]
        public void InvokeTimesOutAndKillsSubprocess()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, "powershell", new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 60" });
            WriteDriver(workspace.Root);

            var result = BridgeInvoker.Invoke(workspace.Root, "testdriver", "caps", EmptyPayload(), timeoutSeconds: 2);

            Assert.False(result.Succeeded);
            Assert.True(result.TimedOut);
            Assert.Equal("超时", result.ErrorCode);
            Assert.Contains("已强制终止", result.HumanText);
        }

        /// <summary>stdout 不是 JSON 时给「响应不合协议」，人话带上 stderr 末尾（真正的原因通常在那）。</summary>
        [Fact]
        public void InvokeNonJsonStdoutGivesProtocolErrorWithStderrTail()
        {
            using var workspace = new Workspace();
            // cmd 把一行文本打上 stdout 后退出，模拟「子进程活着但 stdout 不是协议」。
            WriteRouteTable(workspace.Root, "cmd", new[] { "/c", "echo 这不是JSON" });
            WriteDriver(workspace.Root);

            var result = BridgeInvoker.Invoke(workspace.Root, "testdriver", "caps", EmptyPayload(), timeoutSeconds: 60);

            Assert.False(result.Succeeded);
            Assert.Equal("响应不合协议", result.ErrorCode);
            Assert.Contains("stderr 末尾", result.HumanText);
        }

        /// <summary>driver 自述缺失时给出明确错误，不起进程。</summary>
        [Fact]
        public void InvokeMissingDriverDescriptorGivesError()
        {
            using var workspace = new Workspace();
            WriteRouteTable(workspace.Root, "dotnet", new[] { "--version" });

            var result = BridgeInvoker.Invoke(workspace.Root, "testdriver", "caps", EmptyPayload(), timeoutSeconds: 60);

            Assert.False(result.Succeeded);
            Assert.Equal("驱动自述缺失", result.ErrorCode);
        }

        private static JsonElement EmptyPayload()
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        private static void WriteRouteTable(string root, string executable, string[] arguments)
        {
            var argumentsJson = string.Join(", ", System.Linq.Enumerable.Select(arguments, argument => "\"" + argument + "\""));
            var json = "{\n"
                + "  \"契约版本\": \"1.0.0\",\n"
                + "  \"域路由\": { \"模型加工\": \"testdriver\" },\n"
                + "  \"实现\": { \"bridge-test\": { \"可执行\": \"" + executable + "\", \"参数\": [" + argumentsJson + "] } }\n"
                + "}";
            var path = Path.Combine(root, "Config", "创作管线", "下游.json");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static void WriteDriver(string root)
        {
            var path = BridgeDriverDescriptor.DriverFile(root, "testdriver");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, DriverJson, new UTF8Encoding(false));
        }

        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Root = Path.Combine(Path.GetTempPath(), "调用器测试-" + Guid.NewGuid().ToString("N"));
            }

            public string Root { get; }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
