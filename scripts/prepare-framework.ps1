# Clones the pinned Koan revision into .local/upstream/koan-framework, or verifies that the checkout
# already there is clean and at the pin. A directory junction to a checkout counts as present.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $repoRoot '.local/upstream/koan-framework'
$repository = 'https://github.com/sylin-org/koan-framework.git'
$revision = 'e1a1531825c23e43fd8dbef0e621463c656e54d4'
if (-not (Test-Path -LiteralPath $destination)) {
    git clone $repository $destination
    if ($LASTEXITCODE -ne 0) { throw 'The pinned Koan checkout could not be cloned.' }
    git -C $destination checkout --detach $revision
    if ($LASTEXITCODE -ne 0) { throw 'The pinned Koan revision could not be checked out.' }
} else {
    $actualRevision = git -C $destination rev-parse HEAD
    $changes = git -C $destination status --porcelain
    if ($LASTEXITCODE -ne 0 -or $actualRevision -ne $revision -or $changes) {
        throw 'The Koan checkout differs from the pinned clean revision. It was left as it is.'
    }
}
Write-Output "Pinned Koan revision $revision is ready."
