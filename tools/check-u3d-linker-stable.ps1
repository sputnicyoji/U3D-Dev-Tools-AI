param(
  [string]$RepoRoot = (Resolve-Path ".").Path,
  [switch]$RequireRemoteTags
)

$ErrorActionPreference = "Stop"

function Read-Json($path) {
  Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

$packageName = "com.sputnicyoji.u3d-dev-tools-ai"
$packagePath = "Packages/com.sputnicyoji.u3d-dev-tools-ai"
$entryId = "u3d-dev-tools-ai"

$stablePath = Join-Path $RepoRoot "Registry/stable.json"
$snapshotPath = Join-Path $RepoRoot "$packagePath/Registry/stable.json"
$stable = Read-Json $stablePath
$snapshot = Read-Json $snapshotPath

$stableText = (Get-Content -LiteralPath $stablePath -Raw).Replace("`r`n", "`n").TrimEnd("`n")
$snapshotText = (Get-Content -LiteralPath $snapshotPath -Raw).Replace("`r`n", "`n").TrimEnd("`n")
if ($stableText -ne $snapshotText) {
  throw "stable registry snapshot differs from root Registry/stable.json"
}

$entries = @($stable.entries)
if ($entries.Count -ne 1) {
  throw "stable registry must contain exactly one package entry"
}

$entry = $entries[0]
if ($entry.id -ne $entryId) { throw "unexpected stable entry id: $($entry.id)" }
if ($entry.status -ne "ready") { throw "stable registry entry must be ready" }
if ($entry.packageName -ne $packageName) { throw "unexpected packageName: $($entry.packageName)" }
if ($entry.packagePath -ne $packagePath) { throw "unexpected packagePath: $($entry.packagePath)" }

$pkgPath = Join-Path $RepoRoot "$packagePath/package.json"
if (!(Test-Path -LiteralPath $pkgPath)) {
  throw "missing package.json: $pkgPath"
}

$pkg = Read-Json $pkgPath
$expectedRevision = "$entryId-v$($pkg.version)"
if ($entry.revision -ne $expectedRevision) {
  throw "revision mismatch: registry=$($entry.revision) expected=$expectedRevision"
}

function Assert-LocalTag($tag) {
  $tagExists = git -C $RepoRoot tag --list $tag
  if ([string]::IsNullOrWhiteSpace($tagExists)) {
    throw "missing local tag $tag"
  }
}

function Assert-RemoteTag($tag) {
  if (!$RequireRemoteTags) { return }
  $remoteTag = git -C $RepoRoot ls-remote --tags origin $tag
  if ([string]::IsNullOrWhiteSpace($remoteTag)) {
    throw "missing remote origin tag $tag"
  }
}

Assert-LocalTag $entry.revision
Assert-RemoteTag $entry.revision

Write-Host "stable release checks passed"
