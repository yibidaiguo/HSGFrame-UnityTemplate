<#
  纯 dotnet 侧命令的入口（十秒级快路径，不启动 Unity）：把参数透传给命令宿主控制台。

  用法：
    .\toolkit-cmd.ps1 list
    .\toolkit-cmd.ps1 describe <命令名>
    .\toolkit-cmd.ps1 run <命令名> --arguments-file <json路径>

  需要启动编辑器的命令走同目录的 unity-cmd.ps1。
#>
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CommandArguments)

$hostProjectPath = Join-Path $PSScriptRoot 'CommandHost/CommandHost.csproj'
dotnet run --project $hostProjectPath --verbosity quiet -- @CommandArguments
exit $LASTEXITCODE
