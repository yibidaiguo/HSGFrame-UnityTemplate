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

        private readonly string _repositoryRoot;

        private readonly string _poolRoot;

        private readonly PanelCommandRunner _commandRunner;

        private readonly HttpListener _listener;

        private Thread _listenThread;

        private bool _started;

        /// <summary>构造看板服务（不配置面板数据源与命令宿主：面板接口回 503，/cmd 回未配置宿主）。</summary>
        /// <param name="channel">日志事件通道。</param>
        /// <param name="port">监听端口；传 0 表示自动选一个空闲端口。</param>
        public DashboardServer(LogEventChannel channel, int port)
            : this(channel, port, null, null, null)
        {
        }

        /// <summary>构造看板服务。</summary>
        /// <param name="channel">日志事件通道。</param>
        /// <param name="port">监听端口；传 0 表示自动选一个空闲端口。</param>
        /// <param name="repositoryRoot">仓库根目录；为 null 时五个面板接口回 503。</param>
        /// <param name="poolRoot">池子根目录。</param>
        /// <param name="commandHostProjectPath">命令宿主工程的 .csproj 路径；空白时 /cmd 回未配置宿主。</param>
        public DashboardServer(
            LogEventChannel channel,
            int port,
            string repositoryRoot,
            string poolRoot,
            string commandHostProjectPath)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            Port = port == 0 ? FindFreePort() : port;
            _repositoryRoot = repositoryRoot;
            _poolRoot = poolRoot;
            _commandRunner = new PanelCommandRunner(commandHostProjectPath);
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
        }

        /// <summary>实际监听端口。</summary>
        private static readonly JsonSerializerOptions RecentLineOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>面板接口的 JSON 序列化选项：以 Default 为基类，中文原样输出。</summary>
        private static readonly JsonSerializerOptions PanelJsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
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
                    case "/panel":
                        WritePanelHtml(response);
                        break;
                    case "/api/panel/overview":
                        WritePanelPage(response, () => CreationPanelReader.ReadOverview(_repositoryRoot, _poolRoot));
                        break;
                    case "/api/panel/tasks":
                        WritePanelPage(response, () => CreationPanelReader.ReadTasks(_repositoryRoot, _poolRoot));
                        break;
                    case "/api/panel/requirements":
                        WritePanelPage(response, () => CreationPanelReader.ReadRequirements(_repositoryRoot, _poolRoot));
                        break;
                    case "/api/panel/gates":
                        WritePanelPage(response, () => CreationPanelReader.ReadGateReport(_repositoryRoot));
                        break;
                    case "/api/panel/engine":
                        WritePanelPage(response, () => CreationPanelReader.ReadEngine(_repositoryRoot, _poolRoot));
                        break;
                    case "/api/panel/task":
                        WriteTaskDetail(request, response);
                        break;
                    case "/cmd":
                        HandleCommand(request, response);
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

        /// <summary>写创作管线面板页面：五页装在一份自包含 HTML 里。</summary>
        private void WritePanelHtml(HttpListenerResponse response)
        {
            var bytes = Encoding.UTF8.GetBytes(CreationPanelPage.Html);
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
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

        /// <summary>
        /// 写一个面板页面：仓库根没配置时一律回 503 + 错误 JSON，配置了才读数据写 200。
        /// 数据读取器自身保证不抛，这里不需要额外包异常。
        /// </summary>
        private void WritePanelPage(HttpListenerResponse response, Func<object> readPage)
        {
            if (_repositoryRoot == null)
            {
                WritePanelJson(response, new Dictionary<string, string> { ["错误"] = "面板未配置仓库根" }, HttpStatusCode.ServiceUnavailable);
                return;
            }

            WritePanelJson(response, readPage(), HttpStatusCode.OK);
        }

        /// <summary>
        /// 写单条任务的详情文本：/api/panel/task?id=REQ-0001。
        /// 与 CLI 的 task.status 同源，面板与命令行看到的是同一份渲染。
        /// </summary>
        private void WriteTaskDetail(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (_repositoryRoot == null)
            {
                WritePanelJson(response, new Dictionary<string, string> { ["错误"] = "面板未配置仓库根" }, HttpStatusCode.ServiceUnavailable);
                return;
            }

            var identifier = request.QueryString["id"] ?? "";
            var text = string.IsNullOrWhiteSpace(identifier)
                ? "请带上 ?id=REQ-xxxx"
                : CreationPanelReader.ReadTaskDetail(_repositoryRoot, _poolRoot, identifier);

            var bytes = Encoding.UTF8.GetBytes(text);
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        /// <summary>
        /// 处理 /cmd：只接受 POST，其它方法回 405；请求体是 JSON，取顶层「命令行」交给命令执行器，
        /// 白名单拒绝回 403，通过回 200。
        /// </summary>
        private void HandleCommand(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                var methodMessage = Encoding.UTF8.GetBytes("只接受 POST");
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                response.ContentType = "text/plain; charset=utf-8";
                response.ContentLength64 = methodMessage.Length;
                response.OutputStream.Write(methodMessage, 0, methodMessage.Length);
                response.Close();
                return;
            }

            var commandLine = ReadCommandLine(request);
            var outcome = _commandRunner.Run(commandLine);
            WritePanelJson(response, outcome, outcome.IsAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        }

        /// <summary>从请求体里读顶层「命令行」字符串；请求体不是合法 JSON 时按空命令行处理。</summary>
        private static string ReadCommandLine(HttpListenerRequest request)
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    var body = reader.ReadToEnd();
                    using (var document = JsonDocument.Parse(body))
                    {
                        var root = document.RootElement;
                        if (root.ValueKind == JsonValueKind.Object
                            && root.TryGetProperty("命令行", out var commandElement)
                            && commandElement.ValueKind == JsonValueKind.String)
                        {
                            return commandElement.GetString() ?? "";
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException)
            {
                // 请求体读不了或不是合法 JSON 时按空命令行处理，白名单会以「命令行为空」拒绝。
            }

            return "";
        }

        /// <summary>把对象序列化成中文原样输出的 JSON 写回响应。</summary>
        private static void WritePanelJson(HttpListenerResponse response, object payload, HttpStatusCode statusCode)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, PanelJsonOptions));
            response.StatusCode = (int)statusCode;
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
