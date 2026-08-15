using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Template.Toolkit.Hotfix
{
    /// <summary>本地热更文件服务器：把一个目录挂在 http 上供读取，供本机跑通更新链路。</summary>
    public sealed class HotfixFileServer : IDisposable
    {
        private readonly string _rootDirectory;
        private readonly int _requestedPort;
        private HttpListener _listener;
        private Thread _workerThread;
        private int _port;

        /// <summary>在指定端口挂一个目录。端口传 0 时由系统挑一个空闲端口。</summary>
        public HotfixFileServer(string rootDirectory, int port)
        {
            _rootDirectory = Path.GetFullPath(rootDirectory);
            _requestedPort = port;
            _port = port;
        }

        /// <summary>服务器的基地址，形如 http://127.0.0.1:8123/。</summary>
        public string BaseUrl => $"http://127.0.0.1:{_port}/";

        /// <summary>开始接收请求，非阻塞。</summary>
        public void Start()
        {
            if (_listener != null)
            {
                return;
            }

            _port = _requestedPort == 0 ? FindFreePort() : _requestedPort;

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();

            _workerThread = new Thread(ProcessRequests)
            {
                IsBackground = true,
                Name = "HotfixFileServer",
            };
            _workerThread.Start();
        }

        /// <summary>停止并释放监听器。</summary>
        public void Dispose()
        {
            var listener = _listener;
            if (listener == null)
            {
                return;
            }

            _listener = null;

            // Stop 之后 GetContext 会抛 HttpListenerException，循环据此安静退出。
            try
            {
                listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException)
            {
            }

            listener.Close();
        }

        private void ProcessRequests()
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                HandleRequest(context);
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    Respond(context, 405);
                    return;
                }

                var rawUrl = context.Request.RawUrl ?? "/";
                var queryIndex = rawUrl.IndexOf('?');
                var rawPath = queryIndex >= 0 ? rawUrl.Substring(0, queryIndex) : rawUrl;
                var decodedPath = Uri.UnescapeDataString(rawPath);

                var filePath = MapToFile(decodedPath);
                if (filePath == null)
                {
                    Respond(context, 403);
                    return;
                }

                if (!File.Exists(filePath))
                {
                    Respond(context, 404);
                    return;
                }

                var content = File.ReadAllBytes(filePath);
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = content.Length;
                context.Response.OutputStream.Write(content, 0, content.Length);
                context.Response.Close();
            }
            catch (IOException)
            {
                Abort(context);
            }
            catch (UnauthorizedAccessException)
            {
                Respond(context, 403);
            }
            catch (UriFormatException)
            {
                Respond(context, 403);
            }
            catch (HttpListenerException)
            {
                Abort(context);
            }
            catch (ObjectDisposedException)
            {
                // 请求处理到一半服务器被关闭，静默退出。
            }
        }

        private string MapToFile(string decodedPath)
        {
            var relativePath = (decodedPath ?? "/").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath));

            var rootPrefix = _rootDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? _rootDirectory
                : _rootDirectory + Path.DirectorySeparatorChar;

            return fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
        }

        private static void Respond(HttpListenerContext context, int statusCode)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentLength64 = 0;
            context.Response.Close();
        }

        private static void Abort(HttpListenerContext context)
        {
            try
            {
                context.Response.Abort();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static int FindFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
    }
}
