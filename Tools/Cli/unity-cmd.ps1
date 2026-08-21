<#
  Unity batchmode 的唯一入口：固定编辑器版本、超时必杀、日志落盘并在失败时吐尾部。

  用法：
    .\unity-cmd.ps1 -ExecuteMethod <方法全名> [-ArgumentsFile args.json] [-TimeoutMinutes 40]

  退出码：0 成功，124 超时（沿用 GNU timeout 约定，用来和真失败区分），其余为 Unity 自己的失败码。
  纯 dotnet 侧的命令走同目录的 toolkit-cmd.ps1，那条路径不启动编辑器。
#>
param(
    [string]$ExecuteMethod,
    [string]$ArgumentsFile,
    [int]$TimeoutMinutes = 40,
    [string]$ProjectPath,
    # 缺省从 ProjectVersion.txt 的版本推常见装机路径；只有装在别处才需要显式指。
    [string]$UnityExecutable = '',
    [ValidateSet('EditMode', 'PlayMode')][string]$RunTests,
    # 透传给被调方法自己解析的参数，例如 -PassThroughArguments '-packageVersion','1.0.1'。
    # Unity 认不认无所谓，它会原样留在 Environment.GetCommandLineArgs() 里。
    [string[]]$PassThroughArguments = @()
)

$ErrorActionPreference = 'Stop'

function Show-LogTail {
    param([string]$Path, [int]$LineCount = 60)

    if (Test-Path $Path) {
        Write-Host "[unity-cmd] ---- 日志尾部 $LineCount 行 ----"
        Get-Content -Path $Path -Tail $LineCount | ForEach-Object { Write-Host $_ }
    }
}

# 版本的单一事实源是 ProjectVersion.txt；编辑器路径按版本在常见装机位置探测。
# 换 Unity 版本只改 ProjectVersion.txt 一处，这里跟着走——别再把 exe 全路径抄进脚本。
function Resolve-UnityExecutable {
    param([string]$UnityProjectPath)

    $versionFile = Join-Path $UnityProjectPath 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path $versionFile)) {
        return $null
    }

    $versionLine = Get-Content $versionFile | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    if (-not $versionLine) {
        return $null
    }

    $version = ($versionLine -split ':', 2)[1].Trim()
    $candidates = @(
        "D:/Unity/Editor/$version/Unity.exe",
        "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe",
        "D:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    Write-Host "[unity-cmd] 工程要求 Unity $version，但常见装机路径都没找到：`n  $($candidates -join "`n  ")"
    return $null
}

if (-not $ProjectPath) {
    $ProjectPath = Join-Path $PSScriptRoot '../../UnityProject'
}
$ProjectPath = (Resolve-Path $ProjectPath).Path

if (-not $UnityExecutable) {
    $UnityExecutable = Resolve-UnityExecutable -UnityProjectPath $ProjectPath
    if (-not $UnityExecutable) {
        Write-Host '[unity-cmd] 用 Unity Hub 装上工程要求的版本，或用 -UnityExecutable 指到编辑器 exe'
        exit 2
    }
}

if (-not (Test-Path $UnityExecutable)) {
    Write-Host "[unity-cmd] 找不到编辑器：$UnityExecutable"
    exit 2
}

$logDirectory = Join-Path $PSScriptRoot '../../Logs'
if (-not (Test-Path $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
}
$taskName = if ($RunTests) { "tests-$RunTests" } else { $ExecuteMethod }
if (-not $taskName) {
    Write-Host '[unity-cmd] 需要 -ExecuteMethod 或 -RunTests 其中之一'
    exit 1
}

$logFileName = 'unity-{0}-{1}.log' -f ($taskName -replace '[^A-Za-z0-9\.]', '_'), (Get-Date -Format 'yyyyMMdd-HHmmss')
$logPath = Join-Path $logDirectory $logFileName

# 跑测试时用 -runTests 而不是 -quit：测试运行器要自己决定退出时机，
# 加 -quit 会在测试跑完前把编辑器关掉。
if ($RunTests) {
    $testResultPath = Join-Path $logDirectory ("测试结果-{0}-{1}.xml" -f $RunTests, (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $unityArguments = @(
        '-batchmode', '-nographics',
        '-projectPath', $ProjectPath,
        '-runTests', '-testPlatform', $RunTests,
        '-testResults', $testResultPath,
        '-logFile', $logPath
    )
}
else {
    $unityArguments = @(
        '-batchmode', '-quit', '-nographics',
        '-projectPath', $ProjectPath,
        '-executeMethod', $ExecuteMethod,
        '-logFile', $logPath
    )
}

if ($ArgumentsFile) {
    $unityArguments += @('-argumentsFile', (Resolve-Path $ArgumentsFile).Path)
}

if ($PassThroughArguments.Count -gt 0) {
    $unityArguments += $PassThroughArguments
}

Write-Host "[unity-cmd] 任务=$taskName 超时=${TimeoutMinutes}分钟 日志=$logPath"
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
if ($RunTests -and (Test-Path $testResultPath)) {
    Write-Host "[unity-cmd] 测试结果：$testResultPath"
    $summary = ([xml](Get-Content -Raw $testResultPath)).'test-run'
    if ($summary) {
        Write-Host "[unity-cmd] 用例 $($summary.total) 条：通过 $($summary.passed)，失败 $($summary.failed)，跳过 $($summary.skipped)"
    }
}
if ($exitCode -ne 0) {
    Write-Host "[unity-cmd] 失败，退出码 $exitCode。日志：$logPath"
    Show-LogTail -Path $logPath
}
else {
    Write-Host "[unity-cmd] 成功。日志：$logPath"
}

exit $exitCode
