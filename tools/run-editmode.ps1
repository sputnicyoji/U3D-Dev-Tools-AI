<#
.SYNOPSIS
  Headless Unity batchmode EditMode test runner for the U3D-Dev-Tools-AI TestProjects.

.DESCRIPTION
  Launches Unity in batchmode (-runTests -testPlatform EditMode), waits for it to exit,
  then parses the NUnit3 result XML and reports pass/fail counts. If the result XML is
  missing it scans the log for compile errors. This is the Rule-0 compile+test gate used
  by the dev-tools enhancement mission; no such headless script existed before.

  Notes:
  - -runTests auto-quits the Editor; do NOT pass -quit alongside it.
  - A stale Temp/UnityLockfile (no Unity process holding the project) is removed first.
    Only the lockfile is removed, never the Library cache.
  - Do not run this against a project that another Editor currently has open.

.PARAMETER Project
  test-runner | editor-debug | lua-device-debug  (folder under TestProjects/), or an
  absolute project path.

.EXAMPLE
  pwsh -File tools/run-editmode.ps1 -Project test-runner
#>
param(
    [Parameter(Mandatory = $true)] [string] $Project,
    [string] $Unity = 'E:\Unity\Unity Editor\6000.3.16f1\Editor\Unity.exe',
    [int]    $TimeoutSec = 600
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (Test-Path $Project) {
    $projectPath = (Resolve-Path $Project).Path
} else {
    $projectPath = Join-Path $repoRoot "TestProjects\$Project"
}
if (-not (Test-Path (Join-Path $projectPath 'ProjectSettings\ProjectVersion.txt'))) {
    Write-Error "Not a Unity project: $projectPath"
    exit 3
}
if (-not (Test-Path $Unity)) {
    Write-Error "Unity not found: $Unity"
    exit 3
}

$resultsDir = Join-Path $projectPath 'TestResults'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$stamp      = Get-Date -Format 'yyyyMMdd-HHmmss'
$resultsXml = Join-Path $resultsDir "editmode-$stamp.xml"
$logFile    = Join-Path $resultsDir "editmode-$stamp.log"

# Clear a stale lockfile (only the lockfile, never Library).
$lock = Join-Path $projectPath 'Temp\UnityLockfile'
if (Test-Path $lock) {
    Write-Host "[run-editmode] removing stale lockfile: $lock"
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
}

Write-Host "[run-editmode] project : $projectPath"
Write-Host "[run-editmode] results : $resultsXml"
Write-Host "[run-editmode] log     : $logFile"
Write-Host "[run-editmode] launching Unity batchmode (timeout ${TimeoutSec}s)..."

$args = @(
    '-batchmode', '-nographics', '-runTests',
    '-projectPath', $projectPath,
    '-testPlatform', 'EditMode',
    '-testResults', $resultsXml,
    '-logFile', $logFile
)
$proc = Start-Process -FilePath $Unity -ArgumentList $args -PassThru
if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
    Write-Host "[run-editmode] TIMEOUT after ${TimeoutSec}s; killing pid $($proc.Id)"
    try { $proc.Kill() } catch {}
    exit 4
}
$exit = $proc.ExitCode
Write-Host "[run-editmode] Unity exited with code $exit"

if (-not (Test-Path $resultsXml)) {
    Write-Host "[run-editmode] NO result XML -> likely compile failure. Compile errors in log:"
    if (Test-Path $logFile) {
        Select-String -Path $logFile -Pattern 'error CS|Compilation failed|: error' |
            Select-Object -First 40 | ForEach-Object { Write-Host "  $($_.Line)" }
    }
    exit 3
}

[xml]$doc = Get-Content $resultsXml
$run = $doc.'test-run'
$total  = [int]$run.total
$passed = [int]$run.passed
$failed = [int]$run.failed
$skipped = [int]$run.skipped
$result = $run.result
Write-Host ""
Write-Host "[run-editmode] ===== RESULT: $result ====="
Write-Host "[run-editmode] total=$total passed=$passed failed=$failed skipped=$skipped"

if ($failed -gt 0 -or $result -ne 'Passed') {
    Write-Host "[run-editmode] failing tests:"
    $doc.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
        Write-Host "  FAIL: $($_.fullname)"
        $msg = $_.SelectSingleNode('failure/message')
        if ($msg) { Write-Host "        $($msg.InnerText.Trim())" }
    }
    exit 2
}
Write-Host "[run-editmode] all green"
exit 0
