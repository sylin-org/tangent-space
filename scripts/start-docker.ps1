[CmdletBinding()]
param([switch]$Build)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $fixture = Get-Content -LiteralPath '.local/spaces-network/fixtures.json' -Raw | ConvertFrom-Json
    if ($fixture.status -ne 'ready' -or (Invoke-RestMethod 'http://localhost:2585/health' -TimeoutSec 5).status -ne 'passed') {
        throw 'Start the disposable Spaces network first. Its existing accounts and data must remain available.'
    }
    $networkId = ([DateTimeOffset]$fixture.startedAt).ToUnixTimeMilliseconds()
    $state = Join-Path $repoRoot '.local/docker/site'
    $windowsState = Join-Path $repoRoot ".local/tangent/$networkId/site"
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    $marker = Join-Path $state 'network.json'
    if ((Test-Path -LiteralPath $marker) -and (Get-Content $marker -Raw | ConvertFrom-Json).networkId -ne $networkId) {
        throw 'This Docker database belongs to another fixture network. Preserve it and choose a fresh state directory before creating a new demonstration.'
    }
    $service = & ./probes/spaces-network/register-tangent-service.ps1 -Port 5220
    . ./scripts/local-configuration.ps1
    $settings = New-TangentLocalConfiguration -Fixture $fixture -ManagingApp $service.managingApp -Origin 'http://127.0.0.1:5220' -StateDirectory '/state'
    $settings.Koan.Web.Auth.Atproto.DevelopmentConnectHost = 'host.docker.internal'
    $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $state 'appsettings.json') -Encoding utf8
    @{ networkId = $networkId } | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding utf8
    Copy-Item -LiteralPath src/TangentSpace/koan.lock.json -Destination (Join-Path $state 'koan.lock.json')
    if ($Build) {
        docker compose build tangent
        if ($LASTEXITCODE -ne 0) { throw 'Docker build failed; the existing Windows application was not stopped.' }
    }
    # Stop and snapshot before copying SQLite; never move or decrypt the Windows key ring.
    if (Test-Path -LiteralPath (Join-Path $windowsState 'tangent.sqlite')) {
        & ./scripts/stop-local.ps1 -Instance site
        if (-not (Test-Path -LiteralPath (Join-Path $state 'tangent.sqlite'))) {
            $backup = & ./scripts/backup-local.ps1 -Instance site
            foreach ($name in @('tangent.sqlite', 'tangent.sqlite-wal', 'tangent.sqlite-shm')) {
                $source = Join-Path $backup.backupDirectory $name
                if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $state }
            }
            @{ sourceBackup = $backup.backupDirectory; copiedAt = [DateTimeOffset]::UtcNow.ToString('O');
               sessions = 'New Linux key ring; OAuth sign-in required. Original DPAPI state retained in backup.' } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $state 'migration.json') -Encoding utf8
        }
    }
    docker compose up -d --wait tangent
    if ($LASTEXITCODE -ne 0) { throw 'Container did not become healthy. Inspect docker compose logs tangent.' }
    Write-Output 'Tangent: http://127.0.0.1:5220 | Docker Desktop project: tangent-space'
    Write-Output 'Bootstrap and live logs: docker compose logs -f tangent'
} finally { Pop-Location }
