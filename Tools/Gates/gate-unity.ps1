<#
  分钟级门禁：要启动 Unity 的那一层，按 Unity 真编译 → EditMode 测试 → .meta 完整性的顺序跑。

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

$failedGateNames = @()

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
$missingMetaPaths = @()
$orphanMetaPaths = @()

foreach ($item in (Get-ChildItem -Path $assetsRoot -Recurse -Force)) {
    if ($item.Name -eq '.DS_Store') { continue }

    if ($item.Extension -eq '.meta') {
        # 孤儿 meta：对应的资产已经不在了，Unity 下次打开会把它当垃圾留在库里。
        $ownerPath = $item.FullName.Substring(0, $item.FullName.Length - 5)
        if (-not (Test-Path -LiteralPath $ownerPath)) { $orphanMetaPaths += $item.FullName }
        continue
    }

    if (-not (Test-Path -LiteralPath ($item.FullName + '.meta'))) { $missingMetaPaths += $item.FullName }
}

if ($missingMetaPaths.Count -gt 0 -or $orphanMetaPaths.Count -gt 0) {
    foreach ($path in $missingMetaPaths) { Write-Host "[gate-unity] 缺少 .meta：$path" }
    foreach ($path in $orphanMetaPaths) { Write-Host "[gate-unity] 孤儿 .meta：$path" }
    $failedGateNames += '.meta 完整性'
}
else {
    Write-Host '[gate-unity] .meta 完整性通过，问题 0 条'
}

Write-Host ''
if ($failedGateNames.Count -gt 0) {
    Write-Host "[gate-unity] FAIL —— 未通过：$($failedGateNames -join '、')"
    exit 1
}

Write-Host '[gate-unity] PASS —— 分钟级门禁三道全绿'
exit 0
