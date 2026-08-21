<#
  一键启动：编译一次 → 影子拷贝 → 起面板 + 飞书助手。

  为什么要影子拷贝：常驻进程直接从 bin 里跑会占住自己的 DLL，
  之后 dotnet build / 门禁全编不动（老账：「常驻进程开着门禁全红」）。
  把编译产物拷到仓库外的运行目录再起，bin 从头到尾没人占。

  用法：
    pwsh Tools/start.ps1                       # 面板 + 助手都起
    pwsh Tools/start.ps1 -NoAssistant          # 只起面板
    pwsh Tools/start.ps1 -NoDashboard          # 只起助手
    pwsh Tools/start.ps1 -Port 8790            # 指定面板端口
    pwsh Tools/stop.ps1                        # 全停
#>
[CmdletBinding()]
param(
    # 面板端口。多项目并行时各仓库传不同端口，别都用默认值。
    [int]$Port = 8766,

    # 不起飞书助手（没配密钥的机器用这个，面板照常能看）。
    [switch]$NoAssistant,

    # 不起面板。
    [switch]$NoDashboard,

    # 校验通过的需求要不要真写进飞书需求表（透传给 assistant-start.ps1）。
    [bool]$WriteDownstream = $true,

    # 跳过编译，直接用现成产物。快速重启用；改过代码就别用。
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryName = Split-Path -Leaf $repositoryRoot
$runRoot = Join-Path $env:LOCALAPPDATA (Join-Path 'HSGFrameRun' $repositoryName)
$runtimeDirectory = Join-Path $repositoryRoot '_Tasks/sidecar'
$logDirectory = Join-Path $repositoryRoot 'Logs/services'
$dashboardStopFile = Join-Path $runtimeDirectory 'dashboard-stop'

New-Item -ItemType Directory -Force -Path $runRoot, $runtimeDirectory, $logDirectory | Out-Null

Write-Host '[1/4] 编译（之后常驻期间不再碰 bin）...'
if ($SkipBuild) {
    Write-Host '  （-SkipBuild：跳过，直接用现成产物）'
} else {
    dotnet build (Join-Path $repositoryRoot 'Solutions/Template.sln') -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error '编译失败。若报 DLL 被占用，先跑 pwsh Tools/stop.ps1 停掉上一轮服务再来。'
        exit 1
    }
}

Write-Host "[2/4] 影子拷贝编译产物到运行目录（$runRoot）..."
$shadowPairs = @(
    @{ Source = 'Tools/Dashboard/bin/Debug/net8.0';       Target = 'dashboard' },
    @{ Source = 'Tools/Cli/CommandHost/bin/Debug/net8.0'; Target = 'commandhost' }
)
foreach ($pair in $shadowPairs) {
    $sourcePath = Join-Path $repositoryRoot $pair.Source
    if (-not (Test-Path $sourcePath)) {
        Write-Error "编译产物不存在：$sourcePath（先不带 -SkipBuild 跑一次）"
        exit 1
    }
    $targetPath = Join-Path $runRoot $pair.Target
    robocopy $sourcePath $targetPath /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        Write-Error "影子拷贝失败：$sourcePath → $targetPath（robocopy 退出码 $LASTEXITCODE）"
        exit 1
    }
}

$dashboardProcessId = $null
if (-not $NoDashboard) {
    Write-Host '[3/4] 起面板（常驻模式，停止文件退出）...'
    if (Test-Path $dashboardStopFile) { Remove-Item $dashboardStopFile -Force }
    $dashboardDll = Join-Path $runRoot 'dashboard/Toolkit.Dashboard.dll'
    $dashboardProcess = Start-Process -FilePath 'dotnet' `
        -ArgumentList @($dashboardDll, '--port', $Port, '--repository-root', $repositoryRoot, '--stop-file', $dashboardStopFile) `
        -WorkingDirectory $repositoryRoot `
        -RedirectStandardError (Join-Path $logDirectory 'dashboard.err.log') `
        -RedirectStandardOutput (Join-Path $logDirectory 'dashboard.out.log') `
        -WindowStyle Hidden -PassThru
    $dashboardProcessId = $dashboardProcess.Id
    @{ 面板 = $dashboardProcessId; 端口 = $Port } | ConvertTo-Json |
        Set-Content -Path (Join-Path $runtimeDirectory 'dashboard-pid.json') -Encoding UTF8
} else {
    Write-Host '[3/4] （-NoDashboard：面板不起）'
}

if (-not $NoAssistant) {
    Write-Host '[4/4] 起飞书助手（旁路 + 常驻会话，走影子拷贝的命令宿主）...'
    & (Join-Path $PSScriptRoot 'CreationPipeline/scripts/assistant-start.ps1') `
        -SkipBuild `
        -WriteDownstream $WriteDownstream `
        -CommandHostDll (Join-Path $runRoot 'commandhost/Toolkit.CommandHost.dll')
} else {
    Write-Host '[4/4] （-NoAssistant：助手不起）'
}

Write-Host ''
Write-Host '都起来了：'
if ($dashboardProcessId) {
    Write-Host "  面板　http://localhost:$Port/panel　（日志页 http://localhost:$Port/）"
}
Write-Host "  日志　$logDirectory"
Write-Host '  停　　pwsh Tools/stop.ps1'
