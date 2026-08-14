<#
  Unity batchmode 的唯一入口：固定编辑器版本、超时必杀、日志落盘并在失败时吐尾部。

  用法：
    .\unity-cmd.ps1 -ExecuteMethod <方法全名> [-ArgumentsFile args.json] [-TimeoutMinutes 40]

  退出码：0 成功，124 超时（沿用 GNU timeout 约定，用来和真失败区分），其余为 Unity 自己的失败码。
  纯 dotnet 侧的命令走同目录的 toolkit-cmd.ps1，那条路径不启动编辑器。
#>
param(
    [Parameter(Mandatory = $true)][string]$ExecuteMethod,
    [string]$ArgumentsFile,
    [int]$TimeoutMinutes = 40,
    [string]$ProjectPath,
    [string]$UnityExecutable = 'D:/Unity/Editor/6000.3.11f1/Unity.exe'
)

$ErrorActionPreference = 'Stop'

function Show-LogTail {
    param([string]$Path, [int]$LineCount = 60)

    if (Test-Path $Path) {
        Write-Host "[unity-cmd] ---- 日志尾部 $LineCount 行 ----"
        Get-Content -Path $Path -Tail $LineCount | ForEach-Object { Write-Host $_ }
    }
}

if (-not $ProjectPath) {
    $ProjectPath = Join-Path $PSScriptRoot '../../UnityProject'
}
$ProjectPath = (Resolve-Path $ProjectPath).Path

if (-not (Test-Path $UnityExecutable)) {
    Write-Host "[unity-cmd] 找不到编辑器：$UnityExecutable"
    exit 2
}

$logDirectory = Join-Path $PSScriptRoot '../../Logs'
if (-not (Test-Path $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
}
$logFileName = 'unity-{0}-{1}.log' -f ($ExecuteMethod -replace '[^A-Za-z0-9\.]', '_'), (Get-Date -Format 'yyyyMMdd-HHmmss')
$logPath = Join-Path $logDirectory $logFileName

$unityArguments = @(
    '-batchmode', '-quit', '-nographics',
    '-projectPath', $ProjectPath,
    '-executeMethod', $ExecuteMethod,
    '-logFile', $logPath
)
if ($ArgumentsFile) {
    $unityArguments += @('-argumentsFile', (Resolve-Path $ArgumentsFile).Path)
}

Write-Host "[unity-cmd] 方法=$ExecuteMethod 超时=${TimeoutMinutes}分钟 日志=$logPath"
$process = Start-Process -FilePath $UnityExecutable -ArgumentList $unityArguments -PassThru

# Wait-Process 超时抛异常但进程仍活着，必须显式强杀：
# Unity 卡死是挂机跑最容易堵死整夜的地方，宁可丢一条命令也别丢一整夜。
$timedOut = $false
try {
    Wait-Process -Id $process.Id -Timeout ($TimeoutMinutes * 60)
}
catch [System.TimeoutException] {
    $timedOut = $true
}

if ($timedOut) {
    try { Stop-Process -Id $process.Id -Force -ErrorAction Stop } catch {}
    Write-Host "[unity-cmd] 超时 $TimeoutMinutes 分钟，已强杀。日志：$logPath"
    Show-LogTail -Path $logPath
    exit 124
}

$exitCode = $process.ExitCode
if ($exitCode -ne 0) {
    Write-Host "[unity-cmd] 失败，退出码 $exitCode。日志：$logPath"
    Show-LogTail -Path $logPath
}
else {
    Write-Host "[unity-cmd] 成功。日志：$logPath"
}

exit $exitCode
