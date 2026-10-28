[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destination,
    [string]$SourceRepository = 'https://github.com/sylin-org/koan-framework.git',
    [switch]$RunTests
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$patch = Join-Path $PSScriptRoot $manifest.patch
if ((Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.patchSha256) { throw 'Contribution patch checksum mismatch.' }
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) { throw 'Destination must not already exist; this verifier never resets or deletes an existing checkout.' }
git clone --quiet --no-checkout -- $SourceRepository $destinationPath
if ($LASTEXITCODE -ne 0) { throw 'Clone failed.' }
git -C $destinationPath remote set-url origin $manifest.repository
git -C $destinationPath -c advice.detachedHead=false checkout --quiet $manifest.baseRevision
if ($LASTEXITCODE -ne 0) { throw 'Pinned base checkout failed.' }
git -C $destinationPath apply --check $patch
if ($LASTEXITCODE -ne 0) { throw 'Patch apply check failed.' }
git -C $destinationPath apply $patch
if ($LASTEXITCODE -ne 0) { throw 'Patch application failed.' }
git -C $destinationPath diff --check
if ($LASTEXITCODE -ne 0) { throw 'Applied diff whitespace check failed.' }
foreach ($file in $manifest.files) {
    $blob = git -c core.autocrlf=true -C $destinationPath hash-object --path $file.path (Join-Path $destinationPath $file.path)
    if ($LASTEXITCODE -ne 0 -or $blob -ne $file.gitBlob) { throw "Applied content mismatch: $($file.path)" }
}
if ($RunTests) {
    if ($env:KOAN_ATPROTO_LIFECYCLE) { throw 'Clear KOAN_ATPROTO_LIFECYCLE before ordinary verification; live grant revocation is a separate opt-in proof.' }
    Push-Location $destinationPath
    try {
        dotnet build samples/AtprotoIdentity/AtprotoIdentity.csproj --nologo -v:q
        if ($LASTEXITCODE -ne 0) { throw 'Generic sample build failed.' }
        foreach ($project in @(
            'tests/Koan.Web.Auth.Atproto.Tests/Koan.Web.Auth.Atproto.Tests.csproj',
            'tests/Koan.Web.Auth.Tests/Koan.Web.Auth.Tests.csproj',
            'tests/Suites/Auth/Koan.Web.Auth.Integration.Tests/Koan.Web.Auth.Integration.Tests.csproj'
        )) {
            dotnet test $project --nologo -v:q
            if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
        }
    } finally { Pop-Location }
}
[pscustomobject]@{ passed = $true; baseRevision = $manifest.baseRevision; files = $manifest.files.Count; destination = $destinationPath; testsRequested = [bool]$RunTests }
