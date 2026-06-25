# 把本 Skill 同步到 ~/.claude/skills/unity-editor-debug-mcp/，让 Claude Code 加载。
# 注意：UPM 包名是 com.sputnicyoji.u3d-dev-tools-ai（C# namespace 不变），仅 Skill 标识符冠以 unity- 前缀避免歧义。
#
# 为什么需要：Claude Code 只从 ~/.claude/skills/ 读取 Skill；UPM 包里的 Skill/ 目录
# 是源文件，不会被自动加载。每次改完 SKILL.md / client.py / references 后跑一次本脚本。
#
# 用法：在本目录下双击或 PowerShell 跑 `.\install.ps1`
$ErrorActionPreference = "Stop"

$srcDir = $PSScriptRoot
$dstDir = Join-Path $env:USERPROFILE ".claude\skills\unity-editor-debug-mcp"

Write-Host "src: $srcDir"
Write-Host "dst: $dstDir"

if (Test-Path $dstDir) {
    Write-Host "  removing existing destination..."
    Remove-Item -Recurse -Force $dstDir
}

New-Item -ItemType Directory -Path $dstDir | Out-Null
$refDst = Join-Path $dstDir "references"
New-Item -ItemType Directory -Path $refDst | Out-Null

# 复制顶层文件（排除 .meta 与本脚本自身）
Copy-Item -Path (Join-Path $srcDir "SKILL.md") -Destination $dstDir
Copy-Item -Path (Join-Path $srcDir "client.py") -Destination $dstDir

# 复制 references 子目录所有 .md（排除 .meta）
$refSrc = Join-Path $srcDir "references"
if (Test-Path $refSrc) {
    Get-ChildItem -Path $refSrc -File -Filter *.md | ForEach-Object { Copy-Item $_.FullName -Destination $refDst }
}

Write-Host "done. installed files:"
Get-ChildItem -Path $dstDir -Recurse -File | ForEach-Object { Write-Host ("  " + $_.FullName) }
