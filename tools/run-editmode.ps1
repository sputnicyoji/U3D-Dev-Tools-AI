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

.PARAMETER EditorVersion
  Run with this Editor version (e.g. 6000.3.12f1) instead of the one in ProjectVersion.txt.
  ProjectVersion.txt is set to it for the run. Every ProjectSettings file is restored afterwards
  (an Editor upgrade rewrites ProjectSettings.asset), so the worktree stays clean.

.PARAMETER Project
  test-runner | editor-debug | lua-device-debug  (folder under TestProjects/), or an
  absolute project path.

.EXAMPLE
  pwsh -File tools/run-editmode.ps1 -Project test-runner
#>
param(
    [Parameter(Mandatory = $true)] [string] $Project,
    [string] $Unity = '',
    [string] $EditorVersion = '',
    [int]    $TimeoutSec = 600
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-UnityExecutable {
    param(
        [Parameter(Mandatory = $true)] [string] $ProjectPath,
        [string] $RequestedUnity,
        [string] $RequestedVersion
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedUnity)) {
        if (Test-Path $RequestedUnity) {
            return (Resolve-Path $RequestedUnity).Path
        }
        Write-Error "Unity not found: $RequestedUnity"
        exit 3
    }

    foreach ($envName in @('UNITY_EXE', 'UNITY_EDITOR_PATH')) {
        $value = [Environment]::GetEnvironmentVariable($envName)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            if (Test-Path $value) {
                return (Resolve-Path $value).Path
            }
            Write-Error "$envName points to a missing Unity executable: $value"
            exit 3
        }
    }

    $versionFile = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
    $projectVersion = $null
    if (Test-Path $versionFile) {
        $match = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(.+)$' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($match) {
            $projectVersion = $match.Matches[0].Groups[1].Value.Trim()
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        $projectVersion = $RequestedVersion
    }

    $editorRoots = @(
        'C:\Program Files\Unity\Hub\Editor',
        'C:\Program Files\Unity Hub\Editor'
    )
    # Test seam: tools/test-run-editmode-script.py points this at a fake Editor tree.
    if (-not [string]::IsNullOrWhiteSpace($env:U3D_EDITOR_ROOTS)) {
        $editorRoots = $env:U3D_EDITOR_ROOTS -split ';'
    }

    if ($projectVersion) {
        foreach ($root in $editorRoots) {
            foreach ($exe in @('Unity.exe', 'Unity.cmd')) {
                $candidate = Join-Path $root "$projectVersion\Editor\$exe"
                if (Test-Path $candidate) {
                    return (Resolve-Path $candidate).Path
                }
            }
        }

        Write-Error "Unity $projectVersion not found. Pass -Unity or set UNITY_EXE / UNITY_EDITOR_PATH to use a different editor explicitly."
        exit 3
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    foreach ($root in $editorRoots) {
        if (Test-Path $root) {
            Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending |
                ForEach-Object {
                    $candidates.Add((Join-Path $_.FullName 'Editor\Unity.exe'))
                }
        }
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    Write-Error "Unity not found. Pass -Unity or set UNITY_EXE / UNITY_EDITOR_PATH."
    exit 3
}

if (Test-Path $Project) {
    $projectPath = (Resolve-Path $Project).Path
} else {
    $projectPath = Join-Path $repoRoot "TestProjects\$Project"
}
if (-not (Test-Path (Join-Path $projectPath 'ProjectSettings\ProjectVersion.txt'))) {
    Write-Error "Not a Unity project: $projectPath"
    exit 3
}
if (-not (Test-Path (Join-Path $projectPath 'Assets'))) {
    New-Item -ItemType Directory -Force -Path (Join-Path $projectPath 'Assets') | Out-Null
}
$Unity = Resolve-UnityExecutable -ProjectPath $projectPath -RequestedUnity $Unity -RequestedVersion $EditorVersion

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

# Snapshot ProjectSettings: opening the project in another Editor version rewrites
# ProjectVersion.txt and ProjectSettings.asset. Restored in finally, whatever the outcome.
$settingsDir = Join-Path $projectPath 'ProjectSettings'
$settingsSnapshot = @{}
Get-ChildItem -Path $settingsDir -File | ForEach-Object {
    $settingsSnapshot[$_.FullName] = [IO.File]::ReadAllBytes($_.FullName)
}
if (-not [string]::IsNullOrWhiteSpace($EditorVersion)) {
    [IO.File]::WriteAllText((Join-Path $settingsDir 'ProjectVersion.txt'),
        "m_EditorVersion: $EditorVersion`n", (New-Object Text.UTF8Encoding $false))
}

function Restore-ProjectSettings {
    $restored = 0
    foreach ($path in $settingsSnapshot.Keys) {
        $original = $settingsSnapshot[$path]
        $current = if (Test-Path $path) { [IO.File]::ReadAllBytes($path) } else { $null }
        if ($null -eq $current -or -not [Linq.Enumerable]::SequenceEqual([byte[]]$current, [byte[]]$original)) {
            [IO.File]::WriteAllBytes($path, $original)
            $restored++
        }
    }
    if ($restored -gt 0) { Write-Host "[run-editmode] restored $restored ProjectSettings file(s)" }
}

Write-Host "[run-editmode] project : $projectPath"
Write-Host "[run-editmode] unity   : $Unity"
if ($EditorVersion) { Write-Host "[run-editmode] version : $EditorVersion (ProjectSettings restored after the run)" }
Write-Host "[run-editmode] results : $resultsXml"
Write-Host "[run-editmode] log     : $logFile"
Write-Host "[run-editmode] launching Unity batchmode (timeout ${TimeoutSec}s)..."

$code = 0
try {
    $code = & {
    $unityArgs = @(
        '-batchmode', '-nographics', '-runTests',
        '-projectPath', $projectPath,
        '-testPlatform', 'EditMode',
        '-testResults', $resultsXml,
        '-logFile', $logFile
    )
    $proc = Start-Process -FilePath $Unity -ArgumentList $unityArgs -PassThru
    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        Write-Host "[run-editmode] TIMEOUT after ${TimeoutSec}s; killing pid $($proc.Id)"
        try { $proc.Kill() } catch {}
        return 4
    }
    [void]$proc.WaitForExit()
    $exit = $proc.ExitCode
    Write-Host "[run-editmode] Unity exited with code $exit"

    if (-not (Test-Path $resultsXml)) {
        $license = $null
        if (Test-Path $logFile) {
            $license = Select-String -Path $logFile -Pattern 'LicensingClient|Licensing::.*(Timed-out|aborting)' -ErrorAction SilentlyContinue |
                Select-Object -First 1
        }
        if ($exit -eq 199 -or $license) {
            Write-Host "[run-editmode] LICENSE FAILURE: the Unity licensing client was not available (exit $exit)."
            if ($license) { Write-Host "  $($license.Line)" }
            Write-Host "[run-editmode] Not a compile failure. Wait for the Unity Hub / licensing client, then rerun."
            return 5
        }
        Write-Host "[run-editmode] NO result XML -> likely compile failure. Compile errors in log:"
        if (Test-Path $logFile) {
            $upmPathBug = Select-String -Path $logFile -Pattern 'The "path" argument must be of type string\. Received undefined' -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($upmPathBug) {
                Write-Host "[run-editmode] Package Manager failed before script compilation:"
                Write-Host "  $($upmPathBug.Line)"
                Write-Host "[run-editmode] This is a Unity/UPM startup failure, not a C# compile failure."
            }
            Select-String -Path $logFile -Pattern 'error CS|Compilation failed|: error|is not a valid Unity version' |
                Select-Object -First 40 | ForEach-Object { Write-Host "  $($_.Line)" }
        }
        return 3
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
        return 2
    }
    Write-Host "[run-editmode] all green"
    return 0
    }
}
finally {
    Restore-ProjectSettings
}
exit $code