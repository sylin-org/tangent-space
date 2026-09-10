[CmdletBinding()]
param(
    [switch]$Build,
    # Explicit opt-in for the one-time legacy Windows state migration. Never automatic.
    [switch]$MigrateWindowsState,
    # Internal seams used only by scripts/test-docker-lifecycle.ps1. Defaults exercise the real system.
    [string]$StateRoot,
    [string]$FixtureFile,
    [int]$Port = 5220,
    [scriptblock]$HealthProbe,
    [scriptblock]$Registrar,
    [scriptblock]$CommandRunner,
    [scriptblock]$WindowsStateMigrator
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. "$PSScriptRoot/local-configuration.ps1"
$commandRunner = if ($CommandRunner) { $CommandRunner } else {
    {
        param([Parameter(Mandatory)][string[]]$CommandArguments)
        $executable, $rest = $CommandArguments
        & $executable @rest
        if ($LASTEXITCODE -ne 0) { throw "Command failed (exit $LASTEXITCODE): $($CommandArguments -join ' ')" }
    }
}
$healthProbe = if ($HealthProbe) { $HealthProbe } else { { param([int]$ProbePort) Invoke-RestMethod 'http://localhost:2585/health' -TimeoutSec 5 } }
$registrar = if ($Registrar) { $Registrar } else {
    { param([string]$Root, [int]$ServicePort) & (Join-Path $Root 'probes/spaces-network/register-tangent-service.ps1') -Port $ServicePort }
}
# The default migrator performs the one-time legacy Windows copy described in docs/DOCKER.md.
$windowsStateMigrator = if ($WindowsStateMigrator) { $WindowsStateMigrator } else {
    {
        param([string]$Root, [long]$NetworkId, [string]$StateDirectory)
        $windowsState = Join-Path $Root ".local/tangent/$NetworkId/site"
        if (-not (Test-Path -LiteralPath (Join-Path $windowsState 'tangent.sqlite'))) { return 'no-windows-state' }
        # Stop and snapshot before copying SQLite; never move or decrypt the Windows key ring.
        & (Join-Path $Root 'scripts/stop-local.ps1') -Instance site
        if (-not (Test-Path -LiteralPath (Join-Path $StateDirectory 'tangent.sqlite'))) {
            $backup = & (Join-Path $Root 'scripts/backup-local.ps1') -Instance site
            foreach ($name in @('tangent.sqlite', 'tangent.sqlite-wal', 'tangent.sqlite-shm')) {
                $source = Join-Path $backup.backupDirectory $name
                if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $StateDirectory }
            }
            @{ sourceBackup = $backup.backupDirectory; copiedAt = [DateTimeOffset]::UtcNow.ToString('O');
                sessions = 'New Linux key ring; OAuth sign-in required. Original DPAPI state retained in backup.' } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $StateDirectory 'migration.json') -Encoding utf8
        }
        return 'migrated'
    }
}
Push-Location $repoRoot
try {
    # Read-only fixture validation. This script never restarts, recreates or stops the
    # disposable Spaces network container; it only reads fixtures.json and the health URL.
    $fixturePath = if ($FixtureFile) { [IO.Path]::GetFullPath($FixtureFile, $repoRoot) } else { Join-Path $repoRoot '.local/spaces-network/fixtures.json' }
    if (-not (Test-Path -LiteralPath $fixturePath)) {
        throw "The disposable Spaces network is not initialized. Start it once with: ./probes/spaces-network/start.ps1 -Build (keep an existing network running; recreating it creates new DIDs). Then launch Tangent again."
    }
    $fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    if ($fixture.status -ne 'ready') {
        throw "The disposable Spaces network is not ready (status: $($fixture.status)). Start it once with: ./probes/spaces-network/start.ps1 -Build and wait for http://localhost:2585/health to report status 'passed'. Do not restart or recreate an existing network."
    }
    $health = & $healthProbe $Port
    if ($health.status -ne 'passed') {
        throw "The protocol test network baseline did not pass (health: $($health.status)). Start it once with: ./probes/spaces-network/start.ps1 -Build and wait for http://localhost:2585/health to report status 'passed'. Do not restart or recreate an existing network."
    }
    $networkId = ([DateTimeOffset]$fixture.startedAt).ToUnixTimeMilliseconds()

    # State directory: default <repo>/.local/docker/site, always strictly inside <repo>/.local/docker.
    $allowedRoot = Join-Path $repoRoot '.local/docker'
    $stateRootPath = if ($StateRoot) {
        $combined = if ([IO.Path]::IsPathRooted($StateRoot)) { $StateRoot } else { Join-Path $repoRoot $StateRoot }
        [IO.Path]::GetFullPath($combined)
    } else { $allowedRoot }
    $comparison = [StringComparison]::OrdinalIgnoreCase
    if ($stateRootPath -ne $allowedRoot -and -not $stateRootPath.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw "The Docker state root must stay inside $allowedRoot"
    }
    if (($StateRoot -or $Port -ne 5220) -and -not $CommandRunner) { throw 'Custom state roots and ports are test seams; Compose uses .local/docker/site on port 5220.' }
    $state = Join-Path $stateRootPath 'site'
    New-Item -ItemType Directory -Path $state -Force | Out-Null

    $marker = Set-TangentNetworkMarker -HostStateDirectory $state -NetworkId $networkId
    Write-Output "Fixture network marker $($marker.status) (networkId $($marker.networkId))."

    if (Test-Path -LiteralPath (Join-Path $state 'appsettings.json')) {
        # Repeat launch: the existing configuration is retained byte-for-byte and the
        # fixture container is not contacted for registration.
        $service = $null
    } else {
        # Fresh site: register this Tangent service with the existing fixture network
        # (a PLC service record only; it reuses the persisted registration for this port)
        # and leave ownership open for the first verified sign-in.
        $service = & $registrar $repoRoot $Port
        if (-not $service -or -not $service.managingApp) { throw 'Tangent managing-app fixture registration failed.' }
    }
    $owner = @($fixture.accounts | Where-Object role -eq 'owner')[0]
    $managingApp = if ($service) { $service.managingApp } else { '' }
    $configuration = Save-TangentDockerConfiguration -Fixture $fixture -ManagingApp $managingApp -Origin "http://127.0.0.1:$Port" -HostStateDirectory $state -OwnerDid ''
    Write-Output "Configuration $($configuration.status): $($configuration.path)"

    # The composition lock is refreshed from the current source tree on every launch.
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src/TangentSpace/koan.lock.json') -Destination (Join-Path $state 'koan.lock.json') -Force

    if ($MigrateWindowsState) {
        $migration = & $windowsStateMigrator $repoRoot $networkId $state
        Write-Output "Legacy Windows state migration: $migration"
    } elseif (Test-Path -LiteralPath (Join-Path (Join-Path $repoRoot ".local/tangent/$networkId/site") 'tangent.sqlite')) {
        Write-Output 'Legacy Windows state is present but was not migrated. Use -MigrateWindowsState to migrate it explicitly.'
    }

    if ($Build) {
        & $commandRunner @('docker', 'compose', 'build', 'tangent')
    }
    & $commandRunner @('docker', 'compose', 'up', '-d', '--no-deps', '--wait', 'tangent')
    Write-Output "Tangent: http://127.0.0.1:$Port | Docker Desktop project: tangent-space"
    Write-Output 'Bootstrap and live logs: docker compose logs -f tangent'
} finally { Pop-Location }
