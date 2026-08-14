<#
  热更包打包脚本：能在无 Unity 的机器上跑到「需要 Unity」那一步为止。

  用法：
    .\pack-hotfix.ps1 -Version 1.2.3 [-PackageDirectory <目录>] [-OutputManifest <路径>]

  退出码：0 清单已生成（后面那步需要 Unity），1 参数或环境有问题，3 到达需要 Unity 的步骤且未提供编辑器。
  这样分码是为了让流水线区分「本机能做的部分做完了」和「真失败了」。
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$PackageDirectory,
    [string]$OutputManifest,
    [string]$UnityExecutable = 'D:/Unity/Editor/6000.3.11f1/Unity.exe'
)

$ErrorActionPreference = 'Stop'

# 脚本在 Template/Tools/Hotfix/ 下，仓库根要往上三级。
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path

if (-not ($Version -match '^\d+\.\d+\.\d+$')) {
    Write-Host "[pack-hotfix] 版本号形状应为 1.2.3，收到：$Version"
    exit 1
}

if (-not $PackageDirectory) {
    $PackageDirectory = Join-Path $repositoryRoot 'Template/Build/HotfixPackages'
}
if (-not $OutputManifest) {
    $OutputManifest = Join-Path $repositoryRoot 'Template/Build/热更清单.json'
}

Write-Host "[pack-hotfix] 步骤 1/3 · 纯 C# 侧编译"
& dotnet build (Join-Path $repositoryRoot 'Template/Solutions/Template.sln') --nologo | Out-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host '[pack-hotfix] 纯 C# 侧编译未通过，先修编译再打包'
    exit 1
}

Write-Host "[pack-hotfix] 步骤 2/3 · 生成热更清单"
if (-not (Test-Path $PackageDirectory)) {
    New-Item -ItemType Directory -Path $PackageDirectory -Force | Out-Null
}

$packageEntries = @()
foreach ($file in (Get-ChildItem -Path $PackageDirectory -File -Recurse)) {
    $packageEntries += [ordered]@{
        packageName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        fileName    = $file.Name
        contentHash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        byteSize    = $file.Length
    }
}

$manifestDirectory = Split-Path -Parent $OutputManifest
if (-not (Test-Path $manifestDirectory)) {
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
}

# 清单键用英文：它是给启动器与 CDN 用的接口，不是给策划读的数据。
[ordered]@{ versionText = $Version; packages = $packageEntries } |
    ConvertTo-Json -Depth 5 |
    Set-Content -Path $OutputManifest -Encoding utf8
Write-Host "[pack-hotfix] 清单已生成：$OutputManifest（包条目 $($packageEntries.Count) 个）"

Write-Host "[pack-hotfix] 步骤 3/3 · HybridCLR 补充元数据 + YooAsset 构建（需要 Unity）"
if (-not (Test-Path $UnityExecutable)) {
    Write-Host "[pack-hotfix] 本机没有编辑器（$UnityExecutable），到此为止。"
    exit 3
}

Write-Host "[pack-hotfix] 这一步走 unity-cmd.ps1，人回来后补验："
Write-Host "  ./Template/Tools/Cli/unity-cmd.ps1 -ExecuteMethod Template.Toolkit.Editor.HotfixBuild.BuildAll -TimeoutMinutes 40"
exit 3
