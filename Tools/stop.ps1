<#
  一键停止：面板走停止文件优雅退出，助手走 assistant-stop.ps1。
  用法：pwsh Tools/stop.ps1
#>
[CmdletBinding()]
param(
    # 面板优雅退出最多等几秒，超时就强杀。
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runtimeDirectory = Join-Path $repositoryRoot '_Tasks/sidecar'
$dashboardStopFile = Join-Path $runtimeDirectory 'dashboard-stop'
$dashboardPidFile = Join-Path $runtimeDirectory 'dashboard-pid.json'

if (Test-Path $dashboardPidFile) {
    Write-Host '[1/2] 停面板（写停止文件，等它自己退）...'
    $dashboardInfo = Get-Content $dashboardPidFile -Raw | ConvertFrom-Json
    New-Item -ItemType File -Force -Path $dashboardStopFile | Out-Null

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline -and (Get-Process -Id $dashboardInfo.面板 -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 500
    }
    $lingering = Get-Process -Id $dashboardInfo.面板 -ErrorAction SilentlyContinue
    if ($lingering) {
        Write-Host "  超时没退，强杀 PID $($dashboardInfo.面板)"
        Stop-Process -Id $dashboardInfo.面板 -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $dashboardPidFile -Force -ErrorAction SilentlyContinue
    Remove-Item $dashboardStopFile -Force -ErrorAction SilentlyContinue
} else {
    Write-Host '[1/2] 面板没有在跑（没有 PID 文件）。'
}

$assistantStop = Join-Path $PSScriptRoot 'CreationPipeline/scripts/assistant-stop.ps1'
if (Test-Path (Join-Path $runtimeDirectory 'assistant-pids.json')) {
    Write-Host '[2/2] 停飞书助手...'
    & $assistantStop
} else {
    Write-Host '[2/2] 助手没有在跑（没有 PID 文件）。'
}

Write-Host '停完了。'
