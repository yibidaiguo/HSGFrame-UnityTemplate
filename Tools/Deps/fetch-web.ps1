<#
  取网页资源那一档的内置依赖：按 packages-web.txt 把 js/css 下到 web/。

  与 fetch.ps1 分开是因为这一档不走 NuGet——没有闭包可解，就是照地址下文件。
  硬塞进那份清单会让「每一行都能交给 NuGet」这条不再成立。

  用法：
    pwsh -NoProfile -File ./fetch-web.ps1

  要联网。退出码：0 成功，1 失败。
#>
$ErrorActionPreference = 'Stop'

$depsRoot = $PSScriptRoot
$webDir = Join-Path $depsRoot 'web'
$manifestPath = Join-Path $depsRoot 'packages-web.txt'

if (-not (Test-Path $manifestPath)) {
    Write-Host "[网页依赖] 找不到清单：$manifestPath"
    exit 1
}

New-Item -ItemType Directory -Force -Path $webDir | Out-Null

$count = 0
foreach ($line in Get-Content $manifestPath -Encoding UTF8) {
    $trimmed = $line.Trim()
    if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
    $parts = $line -split "`t" | Where-Object { $_.Trim() -ne '' }
    if ($parts.Count -ne 3) {
        Write-Host "[网页依赖] 清单这行不是三列（文件名<TAB>版本<TAB>地址）：$line"
        exit 1
    }

    $fileName = $parts[0].Trim()
    $version = $parts[1].Trim()
    $url = $parts[2].Trim()
    $target = Join-Path $webDir $fileName

    Write-Host "[网页依赖] 下 $fileName@$version"
    try {
        Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing
    }
    catch {
        Write-Host "[网页依赖] 下不来：$url（$($_.Exception.Message)）"
        exit 1
    }

    $size = (Get-Item $target).Length
    if ($size -lt 1024) {
        # 小于 1 KB 的多半是一张错误页而不是库本体。**当场判失败**——
        # 让它留在 web/ 下的后果是页面空白，而那时没人会想到是这一步下错了东西。
        Write-Host "[网页依赖] $fileName 只有 $size 字节，八成下到的是错误页而不是库；删掉并判失败"
        Remove-Item $target -Force -ErrorAction SilentlyContinue
        exit 1
    }

    # 版本号落一份在旁边：文件名里不带版本（页面按固定名字引它），
    # 不记的话「现在装的是哪一版」只能靠翻这份脚本的 git 历史。
    Set-Content -Path "$target.version" -Value "$version`n$url" -Encoding UTF8 -NoNewline
    Write-Host "[网页依赖]   $([Math]::Round($size / 1KB)) KB"
    $count++
}

Write-Host "[网页依赖] 完成，共 $count 个"
exit 0
