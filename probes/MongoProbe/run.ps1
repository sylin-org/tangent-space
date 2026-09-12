param([ValidateSet(10000, 100000)][int]$Posts = 10000, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$experimentRoot = Join-Path $repoRoot ('.local/experiments/epic005/mongo-baseline-' + [Guid]::NewGuid().ToString('N'))
if ((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024 -lt 8192) { throw 'Need at least 8 GiB host headroom.' }
$env:DOTNET_PROCESSOR_COUNT = '2'
& (Join-Path $PSScriptRoot '../ProviderLab/check.ps1')
if (-not $SkipBuild) {
    dotnet build (Join-Path $PSScriptRoot 'MongoProbe.csproj') -c Release --nologo -m:2 -v:q -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw 'MongoProbe build failed.' }
}
dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0/MongoProbe.dll') --self-test
if ($LASTEXITCODE -ne 0) { throw 'MongoProbe self-test failed.' }
try {
    dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0/MongoProbe.dll') --repo $repoRoot --root $experimentRoot --posts $Posts
    if ($LASTEXITCODE -ne 0) { throw "MongoProbe failed; isolated evidence retained at $experimentRoot" }
} finally { & (Join-Path $PSScriptRoot '../ProviderLab/check.ps1') }
