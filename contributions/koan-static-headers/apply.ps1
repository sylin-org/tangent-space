[CmdletBinding()]
param([Parameter(Mandatory)][string]$Checkout, [switch]$RunTests)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$patch = Join-Path $PSScriptRoot $manifest.patch
$checkoutPath = (Resolve-Path -LiteralPath $Checkout).Path
$authPackage = Join-Path (Split-Path -Parent $PSScriptRoot) 'koan-atproto-auth'
$auth = Get-Content -LiteralPath (Join-Path $authPackage 'manifest.json') -Raw | ConvertFrom-Json
if ($auth.patchSha256 -ne $manifest.requiredAuthPatchSha256) { throw 'The required auth contribution has changed; review the layered dependency before applying.' }
if ((Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.patchSha256) { throw 'Static-header patch checksum mismatch.' }
$revision = git -C $checkoutPath rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $revision -ne $manifest.baseRevision) { throw 'Static-header correction requires its exact pinned Koan base.' }
foreach ($file in $auth.files) {
    $path = Join-Path $checkoutPath $file.path
    if (-not (Test-Path -LiteralPath $path)) { throw "Apply the required auth contribution first: missing $($file.path)." }
    $blob = git -c core.autocrlf=true -C $checkoutPath hash-object --path $file.path $path
    if ($LASTEXITCODE -ne 0 -or $blob -ne $file.gitBlob) { throw "Auth contribution differs at $($file.path); existing edits were preserved." }
}
function Test-HeaderPostimages {
    foreach ($file in $manifest.files) {
        $path = Join-Path $checkoutPath $file.path
        if (-not (Test-Path -LiteralPath $path)) { return $false }
        $blob = git -c core.autocrlf=true -C $checkoutPath hash-object --path $file.path $path
        if ($LASTEXITCODE -ne 0 -or $blob -ne $file.gitBlob) { return $false }
    }
    return $true
}
if (-not (Test-HeaderPostimages)) {
    git -C $checkoutPath apply --check $patch
    if ($LASTEXITCODE -ne 0) { throw 'Static-header patch does not apply cleanly; existing edits were preserved.' }
    git -C $checkoutPath apply $patch
    if ($LASTEXITCODE -ne 0) { throw 'Static-header patch application failed.' }
    if (-not (Test-HeaderPostimages)) { throw 'Static-header postimage verification failed.' }
}
git -C $checkoutPath diff --check -- $manifest.files.path
if ($LASTEXITCODE -ne 0) { throw 'Static-header diff whitespace check failed.' }
if ($RunTests) {
    Push-Location $checkoutPath
    try {
        dotnet test tests/Suites/Web/Koan.Web.WellKnown.Tests/Koan.Web.WellKnown.Tests.csproj --filter FullyQualifiedName~StaticSecurityHeadersSpec --nologo -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'Static-header HTTP regression failed.' }
    } finally { Pop-Location }
}
[pscustomobject]@{ passed = $true; checkout = $checkoutPath; files = $manifest.files.Count; authPostimagesPreserved = $true; testsRequested = [bool]$RunTests }
