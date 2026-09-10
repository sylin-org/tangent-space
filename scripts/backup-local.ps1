[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Instance = 'site')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixture = Get-Content -LiteralPath (Join-Path $repoRoot '.local/spaces-network/fixtures.json') -Raw | ConvertFrom-Json
$networkId = ([DateTimeOffset]$fixture.startedAt).ToUnixTimeMilliseconds()
$state = [IO.Path]::GetFullPath((Join-Path $repoRoot ".local/tangent/$networkId/$Instance"))
if (-not (Test-Path -LiteralPath (Join-Path $state 'tangent.sqlite'))) { throw 'This instance has no persisted application database.' }
if (Test-Path -LiteralPath (Join-Path $state 'host.pid')) {
    $instanceProcessId = [int](Get-Content -LiteralPath (Join-Path $state 'host.pid') -Raw)
    $running = Get-CimInstance Win32_Process -Filter "ProcessId = $instanceProcessId"
    if ($running -and $running.CommandLine.Contains($state)) { throw 'Stop this instance with scripts/stop-local.ps1 before taking a consistent backup.' }
}
$destination = Join-Path $repoRoot ('.local/backups/' + $networkId + '-' + $Instance + '-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())
New-Item -ItemType Directory -Path $destination | Out-Null
foreach ($name in @('tangent.sqlite', 'tangent.sqlite-wal', 'tangent.sqlite-shm', 'data', 'oauth')) {
    $source = Join-Path $state $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $destination -Recurse }
}
$settings = Get-Content -LiteralPath (Join-Path $state 'appsettings.json') -Raw | ConvertFrom-Json
@{ networkId = $networkId; instance = $Instance; takenAt = [DateTimeOffset]::UtcNow.ToString('O');
   clientId = $settings.Koan.Web.Auth.Providers.atproto.ClientId; ownerDid = $settings.Tangent.Site.OwnerDid;
   authorityDid = $settings.Tangent.Spaces.AuthorityDid; managingApp = $settings.Tangent.Spaces.ManagingApp } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'backup.json') -Encoding utf8
[pscustomobject]@{ backupDirectory = $destination; containsProtectedSessionsAndKeys = $true }
