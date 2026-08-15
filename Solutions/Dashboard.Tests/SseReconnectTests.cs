using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Template.Toolkit.Dashboard;
using Xunit;

namespace Template.Toolkit.DashboardTests
{
    /// <summary>SSE 断点重连与事件编号测试。</summary>
    public class SseReconnectTests
    {
        /// <summary>SSE 响应里出现 retry: 行，值是正整数毫秒。</summary>
        [Fact]
        public async Task SseResponseContainsRetryLine()
        {
            using var server = StartServer(new LogEventChannel());
            using var client = new HttpClient();

            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{server.Port}/events");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var stream = await response.Content.ReadAsStreamAsync();

            var text = await ReadUntil(stream, "retry:");

            Assert.Contains("retry: 3000", text);
        }

        /// <summary>每个数据帧带 id: 行，编号严格递增。</summary>
        [Fact]
        public async Task SseFramesCarryIncreasingIds()
        {
            var channel = new LogEventChannel();
            using var server = StartServer(channel);
            using var client = new HttpClient();

            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{server.Port}/events");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var stream = await response.Content.ReadAsStreamAsync();

            channel.Publish("第一行");
            channel.Publish("第二行");
            channel.Publish("第三行");

            var text = await ReadUntilCount(stream, "data: ", 3);
            var ids = ExtractIds(text);

            Assert.Equal(new long[] { 1, 2, 3 }, ids);
        }

        /// <summary>带 Last-Event-ID 头重连时，只补发编号大于它的行。</summary>
        [Fact]
        public void SubscribeWithAfterEventIdReplaysOnlyNewerLines()
        {
            var channel = new LogEventChannel();
            channel.Publish("第一行");
            channel.Publish("第二行");
            channel.Publish("第三行");
            var receivedIds = new List<long>();

            var subscription = channel.Subscribe((id, line) => receivedIds.Add(id), afterEventId: 1);

            Assert.Equal(new long[] { 2, 3 }, receivedIds);
            subscription.Dispose();
        }

        /// <summary>带 Last-Event-ID 头且编号已经是最新时，不补发任何历史行。</summary>
        [Fact]
        public void SubscribeWithLatestEventIdReplaysNothing()
        {
            var channel = new LogEventChannel();
            channel.Publish("第一行");
            channel.Publish("第二行");
            channel.Publish("第三行");
            var receivedIds = new List<long>();

            var subscription = channel.Subscribe((id, line) => receivedIds.Add(id), afterEventId: 3);

            Assert.Empty(receivedIds);
            subscription.Dispose();
        }

        /// <summary>不带 Last-Event-ID 时，照旧补发缓冲里的全部历史行。</summary>
        [Fact]
        public void SubscribeWithoutAfterEventIdReplaysAll()
        {
            var channel = new LogEventChannel();
            channel.Publish("第一行");
            channel.Publish("第二行");
            channel.Publish("第三行");
            var receivedIds = new List<long>();

            var subscription = channel.Subscribe((id, line) => receivedIds.Add(id), afterEventId: null);

            Assert.Equal(new long[] { 1, 2, 3 }, receivedIds);
            subscription.Dispose();
        }

        /// <summary>Last-Event-ID 是非数字时按「不带」处理，不抛异常。</summary>
        [Fact]
        public async Task NonNumericLastEventIdTreatedAsAbsent()
        {
            var channel = new LogEventChannel();
            channel.Publish("第一行");
            channel.Publish("第二行");
            using var server = StartServer(channel);
            using var client = new HttpClient();

            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{server.Port}/events");
            // 头值必须是 ASCII，用 abc 这类非数字字符串模拟「非数字的 Last-Event-ID」。
            request.Headers.TryAddWithoutValidation("Last-Event-ID", "abc");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var stream = await response.Content.ReadAsStreamAsync();

            var text = await ReadUntilCount(stream, "data: ", 2);

            Assert.Contains("第一行", text);
            Assert.Contains("第二行", text);
        }

        /// <summary>Last-Event-ID 比缓冲里最旧的还小时，把缓冲里有的都补上。</summary>
        [Fact]
        public void AfterEventIdOlderThanOldestReplaysAllAvailable()
        {
            var channel = new LogEventChannel();
            for (var index = 0; index < 210; index++)
            {
                channel.Publish($"行{index}");
            }

            // 缓冲容量 200，最旧的 10 行已被挤掉，现存编号是 11..210。
            var receivedIds = new List<long>();
            var subscription = channel.Subscribe((id, line) => receivedIds.Add(id), afterEventId: 0);

            Assert.Equal(200, receivedIds.Count);
            Assert.Equal(11L, receivedIds[0]);
            subscription.Dispose();
        }

        /// <summary>两个订阅者拿到的编号序列一致。</summary>
        [Fact]
        public void TwoSubscribersSeeSameIdSequence()
        {
            var channel = new LogEventChannel();
            var firstIds = new List<long>();
            var secondIds = new List<long>();

            var firstSubscription = channel.Subscribe((id, line) => firstIds.Add(id), afterEventId: null);
            channel.Publish("行1");
            channel.Publish("行2");
            channel.Publish("行3");
            var secondSubscription = channel.Subscribe((id, line) => secondIds.Add(id), afterEventId: null);

            firstSubscription.Dispose();
            secondSubscription.Dispose();

            Assert.Equal(new long[] { 1, 2, 3 }, firstIds);
            Assert.Equal(new long[] { 1, 2, 3 }, secondIds);
        }

        private static DashboardServer StartServer(LogEventChannel channel)
        {
            var server = new DashboardServer(channel, 0);
            server.Start();
            return server;
        }

        private static async Task<string> ReadUntil(Stream stream, string marker)
        {
            var buffer = new byte[65536];
            var builder = new StringBuilder();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!builder.ToString().Contains(marker))
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token);
                if (read == 0)
                {
                    break;
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }

            return builder.ToString();
        }

        private static async Task<string> ReadUntilCount(Stream stream, string marker, int count)
        {
            var buffer = new byte[65536];
            var builder = new StringBuilder();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (CountOccurrences(builder.ToString(), marker) < count)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token);
                if (read == 0)
                {
                    break;
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }

            return builder.ToString();
        }

        private static int CountOccurrences(string text, string marker)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += marker.Length;
            }

            return count;
        }

        private static List<long> ExtractIds(string text)
        {
            var ids = new List<long>();
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith("id: ", StringComparison.Ordinal))
                {
                    ids.Add(long.Parse(line.Substring("id: ".Length)));
                }
            }

            return ids;
        }
    }
}
