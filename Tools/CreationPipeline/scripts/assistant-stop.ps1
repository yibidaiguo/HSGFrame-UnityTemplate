<#
  停飞书助手：先请 assist.serve 自己收尾，再停旁路。

  助手走的是**停止文件**而不是直接杀进程：正在回的那一轮能跑完，
  信号也能按结果归档到 processed/ 或 failed/。直接 kill 会让这一轮的处置悬空。

  用法：
    pwsh Tools/CreationPipeline/scripts/assistant-stop.ps1
#>
[CmdletBinding()]
param(
    # 等 assist.serve 自己退出的秒数；超了就强杀。
    [int]$GracefulSeconds = 20
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$runtimeDirectory = Join-Path $repositoryRoot '_Tasks/sidecar'
$stopFile = Join-Path $runtimeDirectory 'assistant-stop'
$pidFile = Join-Path $runtimeDirectory 'assistant-pids.json'

if (-not (Test-Path $pidFile)) {
    Write-Host "没找到 PID 记录（$pidFile），大概本来就没在跑。"
    exit 0
}

$recordedPids = Get-Content $pidFile -Raw | ConvertFrom-Json

New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
Set-Content -Path $stopFile -Value (Get-Date -Format o) -Encoding UTF8
Write-Host "已放停止文件，等助手收尾（最多 $GracefulSeconds 秒）..."

$deadline = (Get-Date).AddSeconds($GracefulSeconds)
while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $recordedPids.助手 -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Milliseconds 500
}

if (Get-Process -Id $recordedPids.助手 -ErrorAction SilentlyContinue) {
    Write-Host '助手没在期限内退出，强杀。'
    Stop-Process -Id $recordedPids.助手 -Force -ErrorAction SilentlyContinue
} else {
    Write-Host '助手已退出。'
}

# 旁路没有「跑完这一轮」的概念，收在半路的事件本来就还没落盘，直接停即可。
#
# 必须**连子进程一起杀**（taskkill /T），不能只 Stop-Process 记下的那个 PID：
# .venv/Scripts/python.exe 是 uv 的跳板（trampoline），真正跑脚本、真正攥着单实例锁的
# 是它拉起来的那个子解释器。只杀跳板，子进程会活下来继续收事件，
# 而且锁还握在它手里——下次启动会被自己的僵尸挡在门外。
taskkill /T /F /PID $recordedPids.旁路 2>&1 | Out-Null
Write-Host '旁路已停（含子进程）。'

Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
Write-Host '停干净了。'
