<#
  新项目安装向导：clone 完（或 project.create 生成完）跑这一条，把「装到能用」走完。

    1. 编译命令宿主
    2. setup.init —— 生成 local.json 骨架（剥掉密钥键；已存在不动）
    3. setup.check —— 全面体检：密钥文件保护 / 逐下游的密钥键与配置 / 供给状态 / Unity 编辑器
    4. 按体检结果逐条照做，红清完了再跑一遍确认

  密钥永远不经过这个脚本：体检只看「键在不在」，值要你自己填进
  Tools/CreationPipeline/Config/local.json（它在 .gitignore 里，体检会确认这一点）。

  用法：
    pwsh Tools/setup.ps1
    pwsh Tools/setup.ps1 -SkipBuild     # 已编译过时跳过编译
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$commandHostProject = Join-Path $repositoryRoot 'Tools/Cli/CommandHost/CommandHost.csproj'

if (-not $SkipBuild) {
    Write-Host '[1/3] 编译命令宿主...'
    dotnet build $commandHostProject -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error '编译失败。若报 DLL 被占用，先 pwsh Tools/stop.ps1 停掉常驻服务。'
        exit 1
    }
} else {
    Write-Host '[1/3] （-SkipBuild：跳过编译）'
}

function Invoke-SetupCommand {
    param([string]$CommandName)
    $argumentsPath = Join-Path ([System.IO.Path]::GetTempPath()) ("setup-{0}-{1}.json" -f ($CommandName -replace '\.', '-'), $PID)
    @{ RepositoryRoot = $repositoryRoot } | ConvertTo-Json | Set-Content -Path $argumentsPath -Encoding utf8
    & dotnet run --project $commandHostProject --no-build --verbosity quiet -- run $CommandName --arguments-file $argumentsPath | Out-Host
    return $LASTEXITCODE
}

Write-Host '[2/3] 生成本机配置骨架（已存在不动）...'
Invoke-SetupCommand 'setup.init' | Out-Null

Write-Host '[3/3] 安装体检...'
$checkExitCode = Invoke-SetupCommand 'setup.check'

Write-Host ''
if ($checkExitCode -eq 0) {
    Write-Host '装好了。日常起服务：pwsh Tools/start.ps1　跑门禁：pwsh Tools/Gates/gate.ps1'
} else {
    Write-Host '还有红项。按上面每条的「→」照做，做完重跑 pwsh Tools/setup.ps1 -SkipBuild 确认。'
    Write-Host '密钥去哪拿、飞书平台侧要点什么，都在 Doc/creation-pipeline-user-setup.md。'
}
exit $checkExitCode
