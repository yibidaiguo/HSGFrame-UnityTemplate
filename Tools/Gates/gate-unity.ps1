<#
  分钟级门禁：资产十道（不启 Unity）→ Unity 真编译 → EditMode 测试 → .meta 完整性。
  资产门禁排在最前：不用冷启编辑器，便宜的先跑，红了也把贵的跑完给全量结论。

  用法：
    .\gate-unity.ps1 [-TimeoutMinutes 15]

  退出码：0 全绿，1 有门禁不过，124 某一步超时（沿用 unity-cmd.ps1 的约定）。
  秒级与十秒级那几道在 gate.ps1 里，这两个脚本刻意分开：
  分钟级要冷启动编辑器，跟改一行就跑的那几道不是同一个节奏。
#>
param(
    [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'

$templateRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$unityCommand = Join-Path $templateRoot 'Tools/Cli/unity-cmd.ps1'
$assetsRoot = Join-Path $templateRoot 'UnityProject/Assets'
$commandHostProject = Join-Path $templateRoot 'Tools/Cli/CommandHost/CommandHost.csproj'

$failedGateNames = @()

function Invoke-GateCommand {
    param([string]$CommandName, [hashtable]$CommandArguments)

    # 参数文件名带进程号：两个仓库并行跑门禁时，固定文件名会互相覆盖、判定串台。
    $argumentsPath = Join-Path ([System.IO.Path]::GetTempPath()) ("gate-{0}-{1}.json" -f ($CommandName -replace '\.', '-'), $PID)
    $CommandArguments | ConvertTo-Json -Depth 5 | Set-Content -Path $argumentsPath -Encoding utf8

    # Out-Host 是必需的：不消费掉子进程的输出，它会连同退出码一起成为函数的返回值，
    # 调用方拿到的就是数组而不是整数，判定永远为「失败」。
    & dotnet run --project $commandHostProject --verbosity quiet -- run $CommandName --arguments-file $argumentsPath | Out-Host
    return $LASTEXITCODE
}

# 资产门禁：全部是纯 C# 检查器，不启 Unity。此前这十道有实现、有单测，却不在任何 gate 脚本里——
# 加了检查器忘接线是静默的，这里接上之后由 Gates.Tests 的接线对账测试盯死。
Write-Host ''
Write-Host '[gate-unity] ==== 资产门禁（不启 Unity 的十道）===='

# asset.validate 按「带 import-rules.json 的目录」逐个跑：清单动态发现，加一个资产根不用改脚本。
$importRuleDirectories = @(Get-ChildItem $assetsRoot -Recurse -Filter 'import-rules.json' -ErrorAction SilentlyContinue |
    ForEach-Object { $_.DirectoryName })
$assetValidateFailed = $false
foreach ($ruleDirectory in $importRuleDirectories) {
    if ((Invoke-GateCommand -CommandName 'asset.validate' -CommandArguments @{ AssetDirectory = $ruleDirectory }) -ne 0) {
        $assetValidateFailed = $true
    }
}
if ($assetValidateFailed) { $failedGateNames += '资产校验（asset.validate）' }
if ($importRuleDirectories.Count -eq 0) {
    Write-Host '[gate-unity] 知会：Assets 下没有 import-rules.json，asset.validate 无处可跑'
}

$assetRootGates = @(
    @{ Name = '资产引用（asset.references）';     Command = 'asset.references' },
    @{ Name = '依赖方向（asset.dependencies）';   Command = 'asset.dependencies' },
    @{ Name = '打包分组（asset.bundlegroups）';   Command = 'asset.bundlegroups' },
    @{ Name = '加载分组（asset.loadgroups）';     Command = 'asset.loadgroups' },
    @{ Name = '规则覆盖（asset.rulecoverage）';   Command = 'asset.rulecoverage' },
    @{ Name = '重复资产（asset.duplicates）';     Command = 'asset.duplicates' },
    @{ Name = '图集对齐（asset.atlas）';          Command = 'asset.atlas' },
    @{ Name = '常驻预算（asset.residentbudget）'; Command = 'asset.residentbudget' }
)
foreach ($assetGate in $assetRootGates) {
    if ((Invoke-GateCommand -CommandName $assetGate.Command -CommandArguments @{ AssetsRootDirectory = $assetsRoot }) -ne 0) {
        $failedGateNames += $assetGate.Name
    }
}

# 索引新鲜度：资产变了索引就旧，红了的修法是跑一次 index.rebuild 再提交索引文件。
if ((Invoke-GateCommand -CommandName 'index.check' -CommandArguments @{ TemplateRoot = $templateRoot }) -ne 0) {
    $failedGateNames += '索引新鲜度（index.check，修法：跑 index.rebuild）'
}

Write-Host ''
Write-Host '[gate-unity] ==== Unity 真编译 ===='
& $unityCommand -ExecuteMethod Template.Toolkit.Editor.CompileCheckEntry.Run -TimeoutMinutes $TimeoutMinutes | Out-Host
$compileExitCode = $LASTEXITCODE
if ($compileExitCode -eq 124) {
    Write-Host '[gate-unity] Unity 真编译超时，后面两道跳过'
    exit 124
}
if ($compileExitCode -ne 0) {
    Write-Host '[gate-unity] Unity 真编译未通过，后面两道跳过（前一条不过就别做后一条）'
    exit 1
}

Write-Host ''
Write-Host '[gate-unity] ==== EditMode 测试 ===='
& $unityCommand -RunTests EditMode -TimeoutMinutes $TimeoutMinutes | Out-Host
$testExitCode = $LASTEXITCODE
if ($testExitCode -eq 124) { exit 124 }
if ($testExitCode -ne 0) { $failedGateNames += 'EditMode 测试' }

# 「0 条测试」也算不过：程序集加载失败时 Unity 照样退出码 0，
# 症状就是用例数悄悄变成 0——真踩过（补 System.Text.Json dll 那次漏了 Unsafe）。
$latestResult = Get-ChildItem (Join-Path $templateRoot 'Logs') -Filter '测试结果-EditMode-*.xml' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if ($latestResult) {
    $totalCount = [int]([xml](Get-Content -Raw $latestResult.FullName)).'test-run'.total
    if ($totalCount -eq 0) {
        Write-Host '[gate-unity] EditMode 用例数为 0——测试没被发现，按不通过处理'
        $failedGateNames += 'EditMode 测试（用例数为 0）'
    }
}

Write-Host ''
Write-Host '[gate-unity] ==== .meta 完整性 ===='
if ((Invoke-GateCommand -CommandName 'gate.meta' -CommandArguments @{ AssetsRootDirectory = $assetsRoot; ConfigurationPath = (Join-Path $templateRoot 'Tools/Gates/Config/gate-config.json') }) -ne 0) {
    $failedGateNames += '.meta 完整性'
}

Write-Host ''
if ($failedGateNames.Count -gt 0) {
    Write-Host "[gate-unity] FAIL —— 未通过：$($failedGateNames -join '、')"
    exit 1
}

Write-Host '[gate-unity] PASS —— 分钟级门禁全绿'
exit 0
