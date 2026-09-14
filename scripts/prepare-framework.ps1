[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $repoRoot '.local/upstream/koan-framework'
$repository = 'https://github.com/sylin-org/koan-framework.git'
$revision = '21b18c69a4d2305ef94ba0eeef1aaf3084388152'
if (-not (Test-Path -LiteralPath $destination)) {
    git clone $repository $destination
    if ($LASTEXITCODE -ne 0) { throw 'The pinned Koan checkout could not be cloned.' }
    git -C $destination checkout --detach $revision
    if ($LASTEXITCODE -ne 0) { throw 'The pinned Koan revision could not be checked out.' }
} else {
    $actualRevision = git -C $destination rev-parse HEAD
    $changes = git -C $destination status --porcelain
    if ($LASTEXITCODE -ne 0 -or $actualRevision -ne $revision -or $changes) {
        throw 'The local Koan checkout differs from the pinned clean revision. Existing work was preserved.'
    }
}
Write-Output "Pinned Koan revision $revision is ready."
