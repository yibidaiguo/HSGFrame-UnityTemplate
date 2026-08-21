<#
  热更包打包脚本：编译热更程序集 → 归集 dll → 生成清单。

  用法：
    .\pack-hotfix.ps1 -Version 1.2.3 [-PackageDirectory <目录>] [-OutputManifest <路径>] [-SkipUnity]

  退出码：0 全部完成，1 参数或环境有问题，3 需要 Unity 那一步没做（本机没有编辑器，或加了 -SkipUnity）。
  这样分码是为了让流水线区分「本机能做的部分做完了」和「真失败了」。

  编辑器占着工程时 batchmode 打不开同一个工程（Unity 锁目录）。
  那种情况下用 -SkipUnity 跳过编译，在编辑器里走菜单「工具链/热更/编译热更程序集」，再回来跑这个脚本。
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$PackageDirectory,
    [string]$OutputManifest,
    [switch]$SkipUnity,
    # 缺省交给 unity-cmd.ps1 按 ProjectVersion.txt 推；只有装在别处才需要显式指。
    [string]$UnityExecutable = ''
)

$ErrorActionPreference = 'Stop'

# 脚本在 <模板根>/Tools/Hotfix/ 下，模板根往上两级。
# 记的是相对模板根的位置，而不是相对仓库根——模板被复制成别的项目名之后这条仍然成立。
$templateRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

if (-not ($Version -match '^\d+\.\d+\.\d+$')) {
    Write-Host "[pack-hotfix] 版本号形状应为 1.2.3，收到：$Version"
    exit 1
}

if (-not $PackageDirectory) {
    $PackageDirectory = Join-Path $templateRoot 'Build/HotfixPackages'
}
if (-not $OutputManifest) {
    $OutputManifest = Join-Path $templateRoot 'Build/热更清单.json'
}

Write-Host "[pack-hotfix] 步骤 1/3 · 纯 C# 侧编译"
& dotnet build (Join-Path $templateRoot 'Solutions/Template.sln') --nologo | Out-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host '[pack-hotfix] 纯 C# 侧编译未通过，先修编译再打包'
    exit 1
}

$unitySkipped = $false
Write-Host "[pack-hotfix] 步骤 2/3 · HybridCLR 编译热更程序集并归集到打包目录"
if ($SkipUnity) {
    Write-Host '[pack-hotfix] 收到 -SkipUnity，跳过编译，直接用打包目录里现有的 dll'
    $unitySkipped = $true
}
elseif ($UnityExecutable -and -not (Test-Path $UnityExecutable)) {
    Write-Host "[pack-hotfix] 本机没有编辑器（$UnityExecutable），跳过编译，直接用打包目录里现有的 dll"
    $unitySkipped = $true
}
else {
    # 编辑器路径交给 unity-cmd.ps1 统一解析（它按 ProjectVersion.txt 推）；显式给了才透传。
    $unityCommandArguments = @{
        ExecuteMethod  = 'HSGFrame.Hotfix.Editor.HotfixBuildEntry.CompileFromCommandLine'
        TimeoutMinutes = 20
    }
    if ($UnityExecutable) { $unityCommandArguments.UnityExecutable = $UnityExecutable }
    & (Join-Path $PSScriptRoot '../Cli/unity-cmd.ps1') @unityCommandArguments | Out-Host
    if ($LASTEXITCODE -eq 2) {
        Write-Host '[pack-hotfix] 本机找不到工程要求的编辑器，跳过编译，直接用打包目录里现有的 dll'
        $unitySkipped = $true
    }
    elseif ($LASTEXITCODE -ne 0) {
        Write-Host "[pack-hotfix] 热更程序集编译失败（退出码 $LASTEXITCODE）"
        exit 1
    }
}

Write-Host "[pack-hotfix] 步骤 3/3 · 生成热更清单"
if (-not (Test-Path $PackageDirectory)) {
    Write-Host "[pack-hotfix] 打包目录不存在：$PackageDirectory"
    exit 1
}

# 清单交给命令层生成，算哈希与写 JSON 的逻辑只有 C# 侧一份，脚本这边不再抄一遍。
$argumentsFile = Join-Path ([System.IO.Path]::GetTempPath()) ("pack-hotfix-{0}.json" -f [System.Guid]::NewGuid().ToString('N'))
[ordered]@{
    PackageDirectory = $PackageDirectory
    VersionText      = $Version
    OutputPath       = $OutputManifest
} | ConvertTo-Json | Set-Content -Path $argumentsFile -Encoding utf8

try {
    & (Join-Path $PSScriptRoot '../Cli/toolkit-cmd.ps1') run hotfix.manifest --arguments-file $argumentsFile | Out-Host
    $manifestExitCode = $LASTEXITCODE
}
finally {
    Remove-Item -Path $argumentsFile -Force -ErrorAction SilentlyContinue
}

if ($manifestExitCode -ne 0) {
    Write-Host "[pack-hotfix] 清单生成失败（退出码 $manifestExitCode）"
    exit 1
}

Write-Host "[pack-hotfix] 清单已生成：$OutputManifest"

if ($unitySkipped) {
    Write-Host '[pack-hotfix] 完成，但热更程序集这一步是跳过的，清单描述的是打包目录里已有的 dll'
    exit 3
}

exit 0
