[CmdletBinding()]
param([Parameter(Mandatory)][string]$BackupDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Instance)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$backup = (Resolve-Path -LiteralPath $BackupDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $backup 'backup.json') -Raw | ConvertFrom-Json
$fixture = Get-Content -LiteralPath (Join-Path $repoRoot '.local/spaces-network/fixtures.json') -Raw | ConvertFrom-Json
$networkId = ([DateTimeOffset]$fixture.startedAt).ToUnixTimeMilliseconds()
if ($manifest.networkId -ne $networkId) { throw 'The original disposable PDS network is no longer present. Application state alone does not restore its accounts or Spaces.' }
$destination = [IO.Path]::GetFullPath((Join-Path $repoRoot ".local/tangent/$networkId/$Instance"))
if (Test-Path -LiteralPath $destination) { throw 'Restore requires a new instance directory; existing state is never replaced.' }
New-Item -ItemType Directory -Path $destination | Out-Null
foreach ($name in @('tangent.sqlite', 'tangent.sqlite-wal', 'tangent.sqlite-shm', 'data', 'oauth')) {
    $source = Join-Path $backup $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $destination -Recurse }
}
Copy-Item -LiteralPath (Join-Path $backup 'backup.json') -Destination (Join-Path $destination 'restored-from.json')
[pscustomobject]@{ instance = $Instance; stateDirectory = $destination; next = 'Start on the same origin/port with scripts/run-local.ps1. Windows DPAPI requires the same Windows user.' }
