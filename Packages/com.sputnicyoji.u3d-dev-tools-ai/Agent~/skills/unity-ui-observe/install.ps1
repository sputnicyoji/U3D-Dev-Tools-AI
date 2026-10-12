# 把本 Skill 同步到 ~/.claude/skills/unity-ui-observe/，让 Claude Code 加载。
# client.py 依赖同目录的 port_resolver.py，三个顶层文件都要拷。
$ErrorActionPreference = "Stop"

$srcDir = $PSScriptRoot
$dstDir = Join-Path $env:USERPROFILE ".claude\skills\unity-ui-observe"

if (Test-Path $dstDir) { Remove-Item -Recurse -Force $dstDir }
New-Item -ItemType Directory -Path (Join-Path $dstDir "references") | Out-Null

foreach ($name in @("SKILL.md", "client.py", "port_resolver.py")) {
    Copy-Item -Path (Join-Path $srcDir $name) -Destination $dstDir
}
Get-ChildItem -Path (Join-Path $srcDir "references") -File -Filter *.md | ForEach-Object { Copy-Item $_.FullName -Destination (Join-Path $dstDir "references") }

Write-Host "done. installed files:"
Get-ChildItem -Path $dstDir -Recurse -File | ForEach-Object { Write-Host ("  " + $_.FullName) }
