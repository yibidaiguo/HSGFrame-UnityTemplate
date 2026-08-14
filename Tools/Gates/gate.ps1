<#
  门禁总编排：按由快到慢的顺序跑完四道检查，任何一道红就地停下。

  用法：
    .\gate.ps1 [-RepositoryRoot D:\Projects\Unity\RPG]

  检查逻辑全部在 C# 侧（Toolkit.Gates + 命令宿主），这个脚本只负责调用与汇总，
  这样执行后端也能用 dotnet test 自己验证门禁本身。

  退出码：0 全绿，1 有门禁不过，2 环境问题（工程或命令宿主找不到）。
#>
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $RepositoryRoot = Join-Path $PSScriptRoot '../../..'
}
$RepositoryRoot = (Resolve-Path $RepositoryRoot).Path

$solutionPath = Join-Path $RepositoryRoot 'Template/Solutions/Template.sln'
$commandHostProject = Join-Path $RepositoryRoot 'Template/Tools/Cli/CommandHost/CommandHost.csproj'

foreach ($requiredPath in @($solutionPath, $commandHostProject)) {
    if (-not (Test-Path $requiredPath)) {
        Write-Host "[gate] 找不到 $requiredPath"
        exit 2
    }
}

$failedGateNames = @()

function Write-GateHeader {
    param([string]$GateName)
    Write-Host ''
    Write-Host "[gate] ==== $GateName ===="
}

function Invoke-GateCommand {
    param([string]$CommandName, [hashtable]$CommandArguments)

    $argumentsPath = Join-Path ([System.IO.Path]::GetTempPath()) ("gate-{0}.json" -f ($CommandName -replace '\.', '-'))
    $CommandArguments | ConvertTo-Json -Depth 5 | Set-Content -Path $argumentsPath -Encoding utf8

    # Out-Host 是必需的：不消费掉子进程的输出，它会连同退出码一起成为函数的返回值，
    # 调用方拿到的就是数组而不是整数，判定永远为「失败」。
    & dotnet run --project $commandHostProject --verbosity quiet -- run $CommandName --arguments-file $argumentsPath | Out-Host
    return $LASTEXITCODE
}

# 秒级：纯 C# 测试。改一处就该跑的那一道。
Write-GateHeader '秒级门禁 · dotnet test'
& dotnet test $solutionPath --nologo
if ($LASTEXITCODE -ne 0) { $failedGateNames += '秒级门禁（dotnet test）' }

# 十秒级：全解决方案编译，.editorconfig 的命名规则也在这一步报出来。
Write-GateHeader '十秒级门禁 · dotnet build'
& dotnet build $solutionPath --nologo
if ($LASTEXITCODE -ne 0) { $failedGateNames += '十秒级门禁（dotnet build）' }

Write-GateHeader '命名与注释规范'
if ((Invoke-GateCommand -CommandName 'gate.naming' -CommandArguments @{ RootDirectory = 'Template' }) -ne 0) {
    $failedGateNames += '命名检查器'
}

Write-GateHeader '测试基线锁'
if ((Invoke-GateCommand -CommandName 'gate.baseline' -CommandArguments @{ RepositoryRoot = $RepositoryRoot }) -ne 0) {
    $failedGateNames += '测试基线锁'
}

Write-GateHeader '改动文件白名单'
$changedPaths = @()
$editorOwnedPaths = @()
foreach ($statusLine in (& git -C $RepositoryRoot status --porcelain)) {
    $path = $statusLine.Substring(3).Trim('"')
    # 重命名条目形如 "旧路径 -> 新路径"，白名单只关心落点。
    if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1] }

    # RPG_Unity/ 下的变化由一个常驻的 Unity 编辑器进程自己写（Addressables 组、场景、Library/），
    # 工具链两侧对它都是 deny，所以在白名单判定前摘出去，只报一行知会。
    if ($path.StartsWith('RPG_Unity/')) { $editorOwnedPaths += $path } else { $changedPaths += $path }
}
if ($editorOwnedPaths.Count -gt 0) {
    Write-Host "[gate] 知会：RPG_Unity/ 下有 $($editorOwnedPaths.Count) 处变化，来自常驻编辑器进程，未计入白名单判定"
}
if ((Invoke-GateCommand -CommandName 'gate.whitelist' -CommandArguments @{ ChangedPathsText = ($changedPaths -join "`n") }) -ne 0) {
    $failedGateNames += '改动文件白名单'
}

Write-GateHeader '文档长度'
if ((Invoke-GateCommand -CommandName 'gate.doc' -CommandArguments @{ RepositoryRoot = $RepositoryRoot }) -ne 0) {
    $failedGateNames += '文档长度'
}

Write-Host ''
if ($failedGateNames.Count -gt 0) {
    Write-Host "[gate] FAIL —— 未通过：$($failedGateNames -join '、')"
    exit 1
}

Write-Host '[gate] PASS —— 六道门禁全绿'
exit 0
