<#
  把飞书助手拉成「自动回复」：两个常驻进程一起起。

    飞书消息 → 长连接旁路(python) → _Tasks/conversations/*.json → assist.serve → 回话

  两个进程缺一不可，**而且以前只有旁路是常驻的**——assist.serve 默认 --max-rounds 1，
  跑一轮就退出，所以机器人表现成「你手动跑一次它回一句」。这里把它拉成 --max-rounds 0。

  为什么先编译再用「跑 dll」而不是 dotnet run：
  桥协议规定子进程 stdout 上只许有那一份 JSON，而 dotnet run 触发编译时会把 MSBuild 警告
  打到 stdout，插在协议 JSON 前面，整次调用被判「响应不合协议」——回话就发不出去。
  编译一次性做完，常驻期间一行 MSBuild 输出都不产生。

  用法：
    pwsh Tools/CreationPipeline/scripts/assistant-start.ps1
    pwsh Tools/CreationPipeline/scripts/assistant-start.ps1 -WriteDownstream:$false   # 只回话不写需求表
    pwsh Tools/CreationPipeline/scripts/assistant-stop.ps1                            # 停
#>
[CmdletBinding()]
param(
    # 校验通过的需求要不要真写进飞书需求表。关掉就只回话，表不动。
    [bool]$WriteDownstream = $true,

    # 旁路用的 python。默认是本机装了 lark_oapi 的那个虚拟环境。
    [string]$PythonExecutable = 'D:/Tools/FeishuWake/.venv/Scripts/python.exe',

    # 两轮之间歇多少毫秒。空闲时就是这个轮询间隔，也就是消息最坏等这么久才被取走。
    [int]$RoundDelayMilliseconds = 2000,

    # 跳过编译，直接用现成产物起。快速重启用；**改过代码就别用**，那样起来的是上一版。
    [switch]$SkipBuild,

    # 出功能图那一步用哪个模型。留空走本机配置那一档。
    # **这一步比聊天挑模型**：它要一口气吐一份几十个字段的 JSON，
    # 轻量档会把预算花在推理里、回一段空 content（报出来是「执行后端回了空文本」）。
    # 为这一步单独换个强档，比把整条会话都抬上去省。
    [string]$InterfaceDraftModel = '',

    # 命令宿主 dll 的路径。默认用仓库 bin 里那份；一键启动脚本会传影子拷贝的路径进来——
    # 常驻进程从 bin 里跑会占住 DLL，门禁与编译全红（老账）。
    [string]$CommandHostDll = ''
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$runtimeDirectory = Join-Path $repositoryRoot '_Tasks/sidecar'
$logDirectory = Join-Path $repositoryRoot 'Logs/assistant'
$stopFile = Join-Path $runtimeDirectory 'assistant-stop'
$commandHostDll = if ($CommandHostDll) { $CommandHostDll } else {
    Join-Path $repositoryRoot 'Tools/Cli/CommandHost/bin/Debug/net8.0/Toolkit.CommandHost.dll'
}
$sidecarScript = Join-Path $repositoryRoot 'Bridges/feishu/scripts/wake_sidecar.py'

New-Item -ItemType Directory -Force -Path $runtimeDirectory, $logDirectory | Out-Null

# 上一次的停止文件必须先清掉，否则新起来的 assist.serve 第一轮就看到它、立刻退出。
if (Test-Path $stopFile) { Remove-Item $stopFile -Force }

Write-Host '[1/4] 编译（桥与命令宿主，常驻期间不再编）...'
$buildTargets = if ($SkipBuild) { @() } else { @(
    'Tools/Cli/CommandHost/CommandHost.csproj',
    'Bridges/feishu/src/BridgeFeishu/BridgeFeishu.csproj',
    'Bridges/oaicompat/src/BridgeOaicompat/BridgeOaicompat.csproj'
) }
if ($SkipBuild) { Write-Host '  （-SkipBuild：跳过，直接用现成产物）' }
foreach ($target in $buildTargets) {
    dotnet build (Join-Path $repositoryRoot $target) -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error @"
编译失败：$target
常见原因是 Toolkit.Dashboard 正开着，占住了自己的 exe 写不进去。先关掉面板再跑这个脚本。
"@
        exit 1
    }
}

if (-not (Test-Path $commandHostDll)) {
    Write-Error "命令宿主没编出来：$commandHostDll"
    exit 1
}

Write-Host '[2/4] 起长连接旁路（收飞书消息 → 写会话目录）...'
if (-not (Test-Path $PythonExecutable)) {
    Write-Error "找不到 python：$PythonExecutable（用 -PythonExecutable 指过去）"
    exit 1
}
# 旁路自己有单实例锁，重复起的那一份会自己退出，这里不必先查。
$sidecarProcess = Start-Process -FilePath $PythonExecutable `
    -ArgumentList $sidecarScript `
    -WorkingDirectory $repositoryRoot `
    -RedirectStandardError (Join-Path $logDirectory 'sidecar.err.log') `
    -RedirectStandardOutput (Join-Path $logDirectory 'sidecar.out.log') `
    -WindowStyle Hidden -PassThru

Write-Host '[3/4] 起助手常驻会话（取消息 → 执行后端 → 回话）...'
$serveArguments = [ordered]@{
    RepositoryRoot         = $repositoryRoot
    PoolRoot               = (Join-Path $repositoryRoot 'Pools')
    MaxRounds              = 0            # 0 = 不自己停，靠停止文件退出
    RoundDelayMilliseconds = $RoundDelayMilliseconds
    StopFilePath           = $stopFile
    DryRun                 = $false       # 真调执行后端、真回话
    WriteDownstream        = $WriteDownstream
    TimeoutSeconds         = 180
    InterfaceDraftModel    = $InterfaceDraftModel
}
$serveArgumentsFile = Join-Path $runtimeDirectory 'assist-serve.json'
$serveArguments | ConvertTo-Json | Set-Content -Path $serveArgumentsFile -Encoding UTF8

$serveProcess = Start-Process -FilePath 'dotnet' `
    -ArgumentList @($commandHostDll, 'run', 'assist.serve', '--arguments-file', $serveArgumentsFile) `
    -WorkingDirectory $repositoryRoot `
    -RedirectStandardError (Join-Path $logDirectory 'serve.err.log') `
    -RedirectStandardOutput (Join-Path $logDirectory 'serve.out.log') `
    -WindowStyle Hidden -PassThru

@{ 旁路 = $sidecarProcess.Id; 助手 = $serveProcess.Id } | ConvertTo-Json |
    Set-Content -Path (Join-Path $runtimeDirectory 'assistant-pids.json') -Encoding UTF8

Write-Host '[4/4] 起来了。'
Write-Host "  旁路 PID = $($sidecarProcess.Id)　助手 PID = $($serveProcess.Id)"
Write-Host "  写需求表 = $WriteDownstream　轮询间隔 = $RoundDelayMilliseconds 毫秒"
Write-Host "  日志：$logDirectory"
Write-Host "  停：pwsh Tools/CreationPipeline/scripts/assistant-stop.ps1"
