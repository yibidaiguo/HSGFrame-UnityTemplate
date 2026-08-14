using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>看板服务与 SSE 日志流测试。</summary>
    public class DashboardServerTests
    {
        /// <summary>根路径返回 200 且正文包含面板标题。</summary>
        [Fact]
        public async Task RootReturnsDashboardPage()
        {
            using var server = StartServer(new LogEventChannel());

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://localhost:{server.Port}/");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("流水线总控面板", body);
        }

        /// <summary>最近日志接口返回 200 且是一个 JSON 数组。</summary>
        [Fact]
        public async Task RecentReturnsJsonArray()
        {
            var channel = new LogEventChannel();
            channel.Publish("{\"级别\":\"信息\",\"内容\":\"第一行\"}");
            using var server = StartServer(channel);

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://localhost:{server.Port}/api/recent");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.StartsWith("[", body);
            Assert.EndsWith("]", body);
        }

        /// <summary>SSE 流里能收到 data 前缀与刚发布的那行日志。</summary>
        [Fact]
        public async Task EventsStreamsPublishedLineAsServerSentEvent()
        {
            var channel = new LogEventChannel();
            using var server = StartServer(channel);

            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{server.Port}/events");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var stream = await response.Content.ReadAsStreamAsync();

            var publishedLine = "{\"级别\":\"信息\",\"内容\":\"看板收到的一行\"}";
            var receiveTask = Task.Run(async () =>
            {
                var buffer = new byte[65536];
                var builder = new StringBuilder();
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    if (builder.ToString().Contains("data: ") && builder.ToString().Contains(publishedLine))
                    {
                        break;
                    }
                }

                return builder.ToString();
            });

            channel.Publish(publishedLine);

            var completed = await Task.WhenAny(receiveTask, Task.Delay(TimeSpan.FromSeconds(5)));
            if (completed != receiveTask)
            {
                Assert.Fail("5 秒内没有从 SSE 流收到事件");
            }

            var received = await receiveTask;
            Assert.Contains("data: ", received);
            Assert.Contains(publishedLine, received);
        }

        /// <summary>未知路径返回 404。</summary>
        [Fact]
        public async Task UnknownPathReturnsNotFound()
        {
            using var server = StartServer(new LogEventChannel());

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://localhost:{server.Port}/no-such-path");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        /// <summary>订阅者 Dispose 之后收不到新行。</summary>
        [Fact]
        public void ChannelSubscriberReceivesNothingAfterDispose()
        {
            var channel = new LogEventChannel();
            var receivedLines = new List<string>();
            var subscription = channel.Subscribe(receivedLines.Add);
            subscription.Dispose();

            channel.Publish("{\"级别\":\"信息\",\"内容\":\"不应收到\"}");

            Assert.Empty(receivedLines);
        }

        private static DashboardServer StartServer(LogEventChannel channel)
        {
            var server = new DashboardServer(channel, 0);
            server.Start();
            return server;
        }
    }
}
