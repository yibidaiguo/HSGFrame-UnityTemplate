using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace Template.Toolkit.Dashboard
{
    /// <summary>看板 HTTP 服务：用 BCL 的 HttpListener 提供总控面板、SSE 日志流与最近日志三个路由。</summary>
    public sealed class DashboardServer : IDisposable
    {
        private readonly LogEventChannel _channel;

        private readonly HttpListener _listener;

        private Thread _listenThread;

        private bool _started;

        /// <summary>构造看板服务。</summary>
        /// <param name="channel">日志事件通道。</param>
        /// <param name="port">监听端口；传 0 表示自动选一个空闲端口。</param>
        public DashboardServer(LogEventChannel channel, int port)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            Port = port == 0 ? FindFreePort() : port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
        }

        /// <summary>实际监听端口。</summary>
        private static readonly JsonSerializerOptions RecentLineOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public int Port { get; }

        /// <summary>开始监听：在后台线程上跑接受循环。</summary>
        public void Start()
        {
            if (_started)
            {
                return;
            }

            _listener.Start();
            _started = true;
            _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "DashboardServer" };
            _listenThread.Start();
        }

        /// <summary>停止监听并释放端口。</summary>
        public void Stop()
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[看板] 停止监听时异常：{exception.Message}");
            }
        }

        /// <summary>释放资源，等价于 Stop。</summary>
        public void Dispose()
        {
            Stop();
        }

        // 先借一个空闲端口再关掉它，再让 HttpListener 用这个端口：
        // HttpListener 没有「自动选端口」的 API，前缀里写 0 会解析成非法端口；
        // 而 TcpListener(IPAddress.Loopback, 0) 会让内核挑一个空闲端口，测试靠它避免端口冲突。
        private static int FindFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private void ListenLoop()
        {
            while (_started)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (Exception exception)
                {
                    if (!_started)
                    {
                        return;
                    }

                    Console.Error.WriteLine($"[看板] 接受连接异常：{exception.Message}");
                    continue;
                }

                ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                switch (request.Url.AbsolutePath)
                {
                    case "/":
                        WriteHtml(response);
                        break;
                    case "/events":
                        WriteEvents(request, response);
                        break;
                    case "/api/recent":
                        WriteRecent(response);
                        break;
                    default:
                        WriteNotFound(response);
                        break;
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[看板] 处理请求异常：{exception.Message}");
                TryClose(context.Response);
            }
        }

        private void WriteHtml(HttpListenerResponse response)
        {
            var bytes = Encoding.UTF8.GetBytes(DashboardPage.Html);
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        private void WriteRecent(HttpListenerResponse response)
        {
            // 中文原样输出：日志是给人读的，转义成 \uXXXX 就失去了双读性。
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_channel.RecentLines(50), RecentLineOptions));
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        private void WriteEvents(HttpListenerRequest request, HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/event-stream; charset=utf-8";
            response.Headers.Add("Cache-Control", "no-cache");
            response.SendChunked = true;

            var stream = response.OutputStream;

            // 先写一个 SSE 注释帧把响应头推出去：客户端用 ResponseHeadersRead 打开连接时等的就是头，
            // 不先 flush 一次，头会一直憋到第一条真实事件才发出，连接建立这一步就死等。
            WriteComment(stream);

            // 告诉浏览器断线后隔多久重连，并顺带把重连间隔也冲出去。
            WriteRetry(stream);

            // 断点重连：浏览器原生会在重连请求里带上 Last-Event-ID，这里只补发编号比它大的行。
            var afterEventId = ParseLastEventId(request.Headers["Last-Event-ID"]);

            var subscription = _channel.Subscribe(
                (eventId, line) => WriteEvent(stream, eventId, line),
                afterEventId);
            try
            {
                // 保持连接直到客户端断开：断开后下一次写出抛异常，这里退出循环并清理订阅。
                while (true)
                {
                    Thread.Sleep(1000);
                }
            }
            catch (Exception)
            {
                // 连接断开是正常路径，不往上抛。
            }
            finally
            {
                subscription.Dispose();
                TryClose(response);
            }
        }

        // Last-Event-ID 头是字符串，非数字时按「不带断点」处理，补发全部历史而不是抛异常。
        private static long? ParseLastEventId(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                return null;
            }

            return long.TryParse(header, out var eventId) ? eventId : (long?)null;
        }

        private static void WriteComment(Stream stream)
        {
            var payload = Encoding.UTF8.GetBytes(": connected\n\n");
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private static void WriteRetry(Stream stream)
        {
            var payload = Encoding.UTF8.GetBytes("retry: 3000\n\n");
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private static void WriteEvent(Stream stream, long eventId, string line)
        {
            var payload = Encoding.UTF8.GetBytes($"id: {eventId}\ndata: {line}\n\n");
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private static void WriteNotFound(HttpListenerResponse response)
        {
            var bytes = Encoding.UTF8.GetBytes("404 Not Found");
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        private static void TryClose(HttpListenerResponse response)
        {
            try
            {
                response.Close();
            }
            catch (Exception)
            {
                // 客户端已断开时再次 Close 会抛异常，忽略即可。
            }
        }
    }
}
