<#
  Agent 入口同步：以 CLAUDE.md 为源，镜像出 AGENTS.md 等其他模型（Codex / Kimi /
  DeepSeek 系工具 / Gemini …）的入口文件。

  为什么：规范正文只有一份（模板内 规范/），各家模型的入口文件只是指路的薄层。
  薄层也坚持单一事实源：改 CLAUDE.md → 跑本脚本 → 所有镜像跟着变。

  用法：
    .\agent-sync.ps1            同步（镜像缺失或不一致就重写）
    .\agent-sync.ps1 -Verify    只校验不写入，有漂移退出码 1（给门禁用）

  要支持新模型入口：往 $mirrorNames 加文件名即可（如 'GEMINI.md'、'QWEN.md'、'.clinerules'）。
  作用域自动探测：模板根一份 CLAUDE.md，仓库根若另有一份也各自成对同步，脚本里不写死任何仓库名。
#>
param([switch]$Verify)

$ErrorActionPreference = 'Stop'

$mirrorNames = @('AGENTS.md')

$mirrorHeader = "<!-- 镜像文件：由 agent-sync.ps1 从同目录 CLAUDE.md 生成。改内容请改 CLAUDE.md，再重跑模板目录下的 Tools/AgentSync/agent-sync.ps1 -->"

# 模板根从脚本位置推；仓库根靠 .git 探测。模板自己就是仓库根（独立模板仓库）时只处理一个作用域。
$templateRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$repoRoot = if (Test-Path (Join-Path $templateRoot '.git')) { $templateRoot } else { (Resolve-Path (Join-Path $templateRoot '..')).Path }

$scopeDirectories = @($templateRoot)
if ($repoRoot -ne $templateRoot) { $scopeDirectories += $repoRoot }

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$driftCount = 0

foreach ($scopeDirectory in $scopeDirectories) {
    $sourcePath = Join-Path $scopeDirectory 'CLAUDE.md'
    if (-not (Test-Path $sourcePath)) {
        Write-Host "[agent-sync] 跳过：$scopeDirectory 下没有 CLAUDE.md"
        continue
    }

    $sourceContent = [System.IO.File]::ReadAllText($sourcePath)
    $expectedContent = $mirrorHeader + "`n`n" + $sourceContent

    foreach ($mirrorName in $mirrorNames) {
        $mirrorPath = Join-Path $scopeDirectory $mirrorName
        $actualContent = if (Test-Path $mirrorPath) { [System.IO.File]::ReadAllText($mirrorPath) } else { $null }

        if ($actualContent -eq $expectedContent) {
            Write-Host "[agent-sync] 一致：$mirrorPath"
            continue
        }

        $driftCount++
        if ($Verify) {
            Write-Host "[agent-sync] 漂移：$mirrorPath 与同目录 CLAUDE.md 不一致（改源文件后跑一次同步）"
        }
        else {
            [System.IO.File]::WriteAllText($mirrorPath, $expectedContent, $utf8NoBom)
            Write-Host "[agent-sync] 已更新：$mirrorPath"
        }
    }
}

if ($Verify -and $driftCount -gt 0) {
    Write-Host "[agent-sync] FAIL —— $driftCount 处镜像漂移"
    exit 1
}

Write-Host '[agent-sync] PASS'
exit 0
