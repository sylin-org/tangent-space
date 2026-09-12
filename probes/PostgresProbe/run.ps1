param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$pgRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
& (Join-Path $PSScriptRoot '../ProviderLab/check.ps1')
$env:DOTNET_PROCESSOR_COUNT = '2'
if (-not $SkipBuild) {
    dotnet build (Join-Path $PSScriptRoot 'PostgresProbe.csproj') -c Release -m:2 --nologo -v:q -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw 'Postgres probe build failed.' }
}
$pgAssembly = Join-Path $PSScriptRoot 'bin/Release/net10.0/PostgresProbe.dll'
dotnet $pgAssembly --self-test
if ($LASTEXITCODE -ne 0) { throw 'Postgres probe self-checks failed.' }
& (Join-Path $PSScriptRoot '../ProviderLab/check.ps1')
$env:TANGENT_POSTGRES_LAB_CONNECTION = 'Host=127.0.0.1;Port=25432;Database=postgres;Username=epic005;Password=epic005-synthetic-local-only'
$pgExperimentRoot = Join-Path $pgRepoRoot ('.local/experiments/epic005/postgres-health-' + [Guid]::NewGuid().ToString('N'))
dotnet $pgAssembly --repo $pgRepoRoot --root $pgExperimentRoot
if ($LASTEXITCODE -ne 0) { throw "Postgres probe failed; isolated state is retained at $pgExperimentRoot" }
& (Join-Path $PSScriptRoot '../ProviderLab/check.ps1')
