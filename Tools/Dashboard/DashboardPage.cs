namespace Template.Toolkit.Dashboard
{
    /// <summary>总控面板页面：一页自包含的 HTML，零外部依赖、零 CDN，靠 SSE 实时追加日志。</summary>
    public static class DashboardPage
    {
        /// <summary>面板的完整 HTML 文档。</summary>
        public static string Html { get; } = @"<!DOCTYPE html>
<html lang='zh-CN'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>流水线总控面板</title>
<style>
html, body { height: 100%; margin: 0; }
body {
    font-family: 'Microsoft YaHei', 'PingFang SC', sans-serif;
    background: #1e1e2e;
    color: #cdd6f4;
    display: flex;
    flex-direction: column;
}
header {
    padding: 12px 16px;
    background: #181825;
    border-bottom: 1px solid #313244;
    font-size: 18px;
    font-weight: bold;
}
#日志区 {
    flex: 1;
    margin: 0;
    padding: 12px 16px;
    overflow: auto;
    font-family: 'Consolas', 'Courier New', monospace;
    font-size: 13px;
    line-height: 1.5;
    white-space: pre-wrap;
    word-break: break-all;
}
</style>
</head>
<body>
<header>流水线总控面板</header>
<pre id='日志区'></pre>
<script>
var logArea = document.getElementById('日志区');
var source = new EventSource('/events');
source.onmessage = function (event) {
    logArea.textContent += event.data + '\n';
    logArea.scrollTop = logArea.scrollHeight;
};
source.onerror = function () {
    logArea.textContent += '[连接中断，正在自动重连…]\n';
};
</script>
</body>
</html>";
    }
}
