# 把本 Skill 同步到 ~/.claude/skills/test-runner-mcp/，让 Claude Code 加载。
# 每次改完 SKILL.md / client.py / references 后跑一次。用法：PowerShell 跑 .\install.ps1
$ErrorActionPreference = "Stop"
$srcDir = $PSScriptRoot
$dstDir = Join-Path $env:USERPROFILE ".claude\skills\test-runner-mcp"
Write-Host "src: $srcDir"
Write-Host "dst: $dstDir"
if (Test-Path $dstDir) { Remove-Item -Recurse -Force $dstDir }
New-Item -ItemType Directory -Path $dstDir | Out-Null
$refDst = Join-Path $dstDir "references"
New-Item -ItemType Directory -Path $refDst | Out-Null
Copy-Item -Path (Join-Path $srcDir "SKILL.md") -Destination $dstDir
Copy-Item -Path (Join-Path $srcDir "client.py") -Destination $dstDir
$refSrc = Join-Path $srcDir "references"
if (Test-Path $refSrc) {
    Get-ChildItem -Path $refSrc -File | ForEach-Object { Copy-Item $_.FullName -Destination $refDst }
}
Write-Host "done."
Get-ChildItem -Path $dstDir -Recurse -File | ForEach-Object { Write-Host ("  " + $_.FullName) }
