<#
  取 Luban 命令行工具。它约 6 MB / 197 个文件，按 .gitignore 的约定不进仓库，用这个脚本现取。

  用法：
    pwsh -NoProfile -File ./fetch-tool.ps1 [-Version v4.11.0]

  退出码：0 已就位，1 取失败。
#>
param(
    [string]$Version = 'v4.11.0'
)

$ErrorActionPreference = 'Stop'
$toolRoot = $PSScriptRoot

if (Test-Path (Join-Path $toolRoot 'Luban.dll')) {
    Write-Host "[取工具] Luban 已就位：$toolRoot"
    exit 0
}

$archivePath = Join-Path $toolRoot 'Luban.7z'
$downloadUrl = "https://github.com/focus-creative-games/luban/releases/download/$Version/Luban.7z"

Write-Host "[取工具] 下载 $downloadUrl"
Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

# 官方只发 .7z，而 Windows 自带的 tar 解不了 7z。这里用 python 的 py7zr，
# 它是本仓库里唯一已经在用的解压途径；没有 py7zr 时先装它。
Write-Host '[取工具] 解压'
python -c "import py7zr" 2>$null
if ($LASTEXITCODE -ne 0) {
    python -m pip install --quiet py7zr
}

python -c @"
import py7zr, shutil, os
root = r'$toolRoot'
with py7zr.SevenZipFile(os.path.join(root, 'Luban.7z'), 'r') as archive:
    archive.extractall(root)
nested = os.path.join(root, 'Luban')
if os.path.isdir(nested):
    for name in os.listdir(nested):
        shutil.move(os.path.join(nested, name), os.path.join(root, name))
    os.rmdir(nested)
"@

Remove-Item $archivePath -Force -ErrorAction SilentlyContinue

if (-not (Test-Path (Join-Path $toolRoot 'Luban.dll'))) {
    Write-Host '[取工具] 解压后没看到 Luban.dll，取失败'
    exit 1
}

Write-Host "[取工具] 完成，版本 $Version"
exit 0
