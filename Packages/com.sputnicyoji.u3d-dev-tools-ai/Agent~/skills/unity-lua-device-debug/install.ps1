# 把本 Skill 同步到 ~/.claude/skills/unity-lua-device-debug/，让 Claude Code 加载。
# client.py 依赖同目录的 port_resolver.py，三个顶层文件都要拷。
# Android 诊断需要 PATH 上有 adb（client.py adb-forward / adb-remove），见 references/android-adb-forwarding.md；
# 本脚本只拷文件，不调用 adb。
#
# 每次改完 SKILL.md / client.py / references 后跑一次。用法：PowerShell 跑 .\install.ps1
$ErrorActionPreference = "Stop"

$srcDir = $PSScriptRoot
$dstDir = Join-Path $env:USERPROFILE ".claude\skills\unity-lua-device-debug"

Write-Host "src: $srcDir"
Write-Host "dst: $dstDir"

if (Test-Path $dstDir) {
    Write-Host "  removing existing destination..."
    Remove-Item -Recurse -Force $dstDir
}

New-Item -ItemType Directory -Path $dstDir | Out-Null
$refDst = Join-Path $dstDir "references"
New-Item -ItemType Directory -Path $refDst | Out-Null

# 复制顶层文件（排除本脚本自身）
foreach ($name in @("SKILL.md", "client.py", "port_resolver.py")) {
    Copy-Item -Path (Join-Path $srcDir $name) -Destination $dstDir
}

# 复制 references 子目录所有 .md
$refSrc = Join-Path $srcDir "references"
if (Test-Path $refSrc) {
    Get-ChildItem -Path $refSrc -File -Filter *.md | ForEach-Object { Copy-Item $_.FullName -Destination $refDst }
}

Write-Host "done. installed files:"
Get-ChildItem -Path $dstDir -Recurse -File | ForEach-Object { Write-Host ("  " + $_.FullName) }
