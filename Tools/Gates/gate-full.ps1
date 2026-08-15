<#
  完整级门禁：把四级门禁按由快到慢串起来跑完，前一条不过就不做后一条。

  用法：
    .\gate-full.ps1 [-TimeoutMinutes 40] [-SkipPlayerBuild]

  四级的分工：
    秒级 + 十秒级 —— gate.ps1（dotnet test / build 与四个 C# 检查器）
    分钟级       —— gate-unity.ps1（Unity 真编译 / EditMode / .meta 完整性）
    完整级       —— 本脚本自己跑的这两道：PlayMode 测试与出包

  退出码：0 全绿，1 有门禁不过，2 环境问题，124 某一步超时（沿用 unity-cmd.ps1 的约定）。

  出包入口方法名写在 gate-config.json 的 playerBuildEntryMethod 里，
  而不是写死在本脚本内：出包入口是每个项目自己的东西，模板不该知道它叫什么。
#>
param(
    [int]$TimeoutMinutes = 40,
    [switch]$SkipPlayerBuild
)

$ErrorActionPreference = 'Stop'

$templateRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$unityCommand = Join-Path $templateRoot 'Tools/Cli/unity-cmd.ps1'
$configurationPath = Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json'

function Invoke-Level {
    param([string]$LevelName, [scriptblock]$Action)

    Write-Host ''
    Write-Host "[gate-full] ======== $LevelName ========"
    & $Action | Out-Host
    return $LASTEXITCODE
}

# 第一级 + 第二级：秒级与十秒级都在 gate.ps1 里，它自己会按顺序跑完。
$exitCode = Invoke-Level '秒级 + 十秒级 · gate.ps1' { & (Join-Path $templateRoot 'Tools/Gates/gate.ps1') }
if ($exitCode -ne 0) {
    Write-Host "[gate-full] FAIL —— 秒级/十秒级未通过，后面几级跳过"
    exit $exitCode
}

# 第三级：分钟级。
$exitCode = Invoke-Level '分钟级 · gate-unity.ps1' { & (Join-Path $templateRoot 'Tools/Gates/gate-unity.ps1') -TimeoutMinutes $TimeoutMinutes }
if ($exitCode -eq 124) {
    Write-Host '[gate-full] 分钟级超时，完整级跳过'
    exit 124
}
if ($exitCode -ne 0) {
    Write-Host '[gate-full] FAIL —— 分钟级未通过，完整级跳过'
    exit 1
}

# 第四级 · 其一：PlayMode 测试。
# 「0 条用例」按不通过处理，理由与 gate-unity.ps1 里那段相同：
# 程序集加载失败时 Unity 照样退出码 0，症状就是用例数悄悄变成 0。
$exitCode = Invoke-Level '完整级 · PlayMode 测试' { & $unityCommand -RunTests PlayMode -TimeoutMinutes $TimeoutMinutes }
if ($exitCode -eq 124) {
    Write-Host '[gate-full] PlayMode 超时'
    exit 124
}
if ($exitCode -ne 0) {
    Write-Host '[gate-full] FAIL —— PlayMode 测试未通过'
    exit 1
}

$latestResult = Get-ChildItem (Join-Path $templateRoot 'Logs') -Filter '测试结果-PlayMode-*.xml' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if ($latestResult) {
    $totalCount = [int]([xml](Get-Content -Raw $latestResult.FullName)).'test-run'.total
    if ($totalCount -eq 0) {
        Write-Host '[gate-full] FAIL —— PlayMode 用例数为 0，测试没被发现'
        exit 1
    }
}
else {
    Write-Host '[gate-full] FAIL —— 没找到 PlayMode 的测试结果 XML'
    exit 1
}

# 第四级 · 其二：出包。这一步最贵，给它单独一个开关，日常迭代时可以跳过。
if ($SkipPlayerBuild) {
    Write-Host ''
    Write-Host '[gate-full] 出包按 -SkipPlayerBuild 跳过'
    Write-Host '[gate-full] PASS —— 除出包外全绿'
    exit 0
}

$playerBuildEntryMethod = $null
if (Test-Path $configurationPath) {
    $playerBuildEntryMethod = (Get-Content -Raw -Path $configurationPath | ConvertFrom-Json).playerBuildEntryMethod
}
if (-not $playerBuildEntryMethod) {
    Write-Host "[gate-full] gate-config.json 里没有 playerBuildEntryMethod，出包这一道无从下手"
    Write-Host "[gate-full] 修复：在 $configurationPath 里加一行，值是出包入口的方法全名"
    exit 2
}

$exitCode = Invoke-Level '完整级 · 出包' { & $unityCommand -ExecuteMethod $playerBuildEntryMethod -TimeoutMinutes $TimeoutMinutes }
if ($exitCode -eq 124) {
    Write-Host '[gate-full] 出包超时'
    exit 124
}
if ($exitCode -ne 0) {
    Write-Host '[gate-full] FAIL —— 出包未通过'
    exit 1
}

Write-Host ''
Write-Host '[gate-full] PASS —— 四级门禁全绿'
exit 0
