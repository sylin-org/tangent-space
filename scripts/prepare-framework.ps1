[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $repoRoot '.local/upstream/koan-framework'
$package = Join-Path $repoRoot 'contributions/koan-atproto-auth'
$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
if (-not (Test-Path -LiteralPath $destination)) {
    & (Join-Path $package 'verify.ps1') -Destination $destination
} else {
    $revision = git -C $destination rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $revision -ne $manifest.baseRevision) { throw 'The local Koan checkout has a different base. Keep it intact and choose a separate checkout through KoanSourceRoot.' }
    foreach ($file in $manifest.files) {
        $path = Join-Path $destination $file.path
        if (-not (Test-Path -LiteralPath $path)) { throw "The local contribution is incomplete: $($file.path)" }
        $blob = git -c core.autocrlf=true -C $destination hash-object --path $file.path $path
        if ($LASTEXITCODE -ne 0 -or $blob -ne $file.gitBlob) { throw "The local Koan contribution differs at $($file.path). Existing edits were preserved." }
    }
    Write-Output 'Pinned Koan contribution is ready.'
}
& (Join-Path $repoRoot 'contributions/koan-static-headers/apply.ps1') -Checkout $destination
