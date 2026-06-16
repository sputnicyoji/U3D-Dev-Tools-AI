param(
  [string]$RepoRoot = (Resolve-Path ".").Path,
  [switch]$RequireRemoteTags
)

$ErrorActionPreference = "Stop"

function Read-Json($path) {
  Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

$stablePath = Join-Path $RepoRoot "Registry/stable.json"
$snapshotPath = Join-Path $RepoRoot "Packages/com.yoji.u3d-ai-linker/Registry/stable.json"
$stable = Read-Json $stablePath
$snapshot = Read-Json $snapshotPath

$stableText = (Get-Content -LiteralPath $stablePath -Raw).Replace("`r`n", "`n").TrimEnd("`n")
$snapshotText = (Get-Content -LiteralPath $snapshotPath -Raw).Replace("`r`n", "`n").TrimEnd("`n")
if ($stableText -ne $snapshotText) {
  throw "stable registry snapshot differs from root Registry/stable.json"
}

$ready = @($stable.entries | Where-Object { $_.status -eq "ready" })
$requiredReadyIds = @("editor-core", "editor-debug", "test-runner")
$requiredNotReadyIds = @("lua-device-debug")
$readyIds = @($ready | ForEach-Object { $_.id })

foreach ($id in $requiredReadyIds) {
  if ($readyIds -notcontains $id) {
    throw "stable registry missing required ready entry: $id"
  }
}

foreach ($id in $readyIds) {
  if ($requiredReadyIds -notcontains $id) {
    throw "stable registry has unexpected ready entry: $id"
  }
}

foreach ($id in $requiredNotReadyIds) {
  $entry = $stable.entries | Where-Object { $_.id -eq $id } | Select-Object -First 1
  if ($null -eq $entry) {
    throw "stable registry missing expected non-ready entry: $id"
  }
  if ($entry.status -eq "ready") {
    throw "stable registry entry must remain non-ready until generic host proof exists: $id"
  }
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

foreach ($entry in $ready) {
  $pkgPath = Join-Path $RepoRoot ($entry.packagePath + "/package.json")
  if (!(Test-Path -LiteralPath $pkgPath)) {
    throw "missing package.json for $($entry.id): $pkgPath"
  }

  $pkg = Read-Json $pkgPath
  $expectedRevision = "$($entry.id)-v$($pkg.version)"
  if ($entry.revision -ne $expectedRevision) {
    throw "revision mismatch for $($entry.id): registry=$($entry.revision) expected=$expectedRevision"
  }

  Assert-LocalTag $entry.revision
  Assert-RemoteTag $entry.revision
}

$linkerPkg = Read-Json (Join-Path $RepoRoot "Packages/com.yoji.u3d-ai-linker/package.json")
$linkerTag = "u3d-ai-linker-v$($linkerPkg.version)"
Assert-LocalTag $linkerTag
Assert-RemoteTag $linkerTag

Write-Host "stable release checks passed"
