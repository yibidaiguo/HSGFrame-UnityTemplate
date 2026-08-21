<#
  派活入口：把任务书分派给执行后端（角色档案 + OpenAI 兼容 API 工具循环）。

  用法：
    pwsh Tools/dispatch.ps1 -Role implementer -TaskFile _Scratch/tasks/task-x.md
    pwsh Tools/dispatch.ps1 -Role verifier    -TaskFile _Scratch/tasks/verify-x.md
    pwsh Tools/dispatch.ps1 -Role explore     -TaskFile _Scratch/tasks/locate-x.md -MaxRounds 12
    pwsh Tools/dispatch.ps1 -Role implementer -TaskFile ... -DryRun     # 只组装不发，不花钱

  执行后端配置在 Tools/CreationPipeline/Config/local.json：
  「下游配置.oaicompat.地址/模型」+「执行后端密钥」。任何 OpenAI 兼容服务都行。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('implementer', 'verifier', 'operator', 'explore')]
    [string]$Role,

    [Parameter(Mandatory = $true)]
    [string]$TaskFile,

    # 轮数上限（一轮 = 一次 chat/completions 调用）。
    [int]$MaxRounds = 40,

    # 回报正文的字符上限；全文总在 Logs/agent/ 的报告文件里。
    [int]$MaxReportChars = 6000,

    # 模型名覆盖；默认用 local.json 里配的。
    [string]$Model = '',

    # 只组装不发。
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not (Test-Path $TaskFile)) {
    Write-Error "任务书不存在：$TaskFile"
    exit 2
}

$argumentsPath = Join-Path ([System.IO.Path]::GetTempPath()) ("agent-dispatch-{0}.json" -f $PID)
[ordered]@{
    Role           = $Role
    TaskFile       = (Resolve-Path $TaskFile).Path
    RepositoryRoot = $repositoryRoot
    MaxRounds      = $MaxRounds
    MaxReportChars = $MaxReportChars
    Model          = $Model
    DryRun         = [bool]$DryRun
} | ConvertTo-Json | Set-Content -Path $argumentsPath -Encoding utf8

& dotnet run --project (Join-Path $repositoryRoot 'Tools/Cli/CommandHost/CommandHost.csproj') --verbosity quiet -- `
    run agent.dispatch --arguments-file $argumentsPath
exit $LASTEXITCODE
