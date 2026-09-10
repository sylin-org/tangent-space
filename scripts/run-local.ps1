[CmdletBinding()]
param([int]$Port = 5220, [switch]$NoBuild, [switch]$Background,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Instance = 'site')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/TangentSpace/TangentSpace.csproj'
$fixturePath = Join-Path $repoRoot '.local/spaces-network/fixtures.json'
if (-not (Test-Path -LiteralPath $fixturePath)) { throw 'Start probes/spaces-network/start.ps1 first. This command uses only its disposable local accounts.' }
$fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
if ($fixture.status -ne 'ready') { throw 'The disposable test network is not ready.' }
$health = Invoke-RestMethod 'http://localhost:2585/health' -TimeoutSec 5
if ($health.status -ne 'passed') { throw 'The protocol test network baseline did not pass.' }
$networkId = ([DateTimeOffset]$fixture.startedAt).ToUnixTimeMilliseconds()
$stateDirectory = Join-Path $repoRoot ".local/tangent/$networkId/$Instance"
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
if (Test-Path -LiteralPath (Join-Path $stateDirectory 'host.pid')) {
    $instanceProcessId = [int](Get-Content -LiteralPath (Join-Path $stateDirectory 'host.pid') -Raw)
    $running = Get-CimInstance Win32_Process -Filter "ProcessId = $instanceProcessId"
    if ($running -and $running.CommandLine.Contains($stateDirectory) -and $running.CommandLine.Contains('TangentSpace.dll')) {
        throw 'This instance is already running. Stop it with scripts/stop-local.ps1 before starting it again.'
    }
}
$origin = "http://127.0.0.1:$Port"
$callback = "$origin/auth/atproto/callback"
$owner = @($fixture.accounts | Where-Object role -eq 'owner')[0]
$authority = @($fixture.accounts | Where-Object role -eq 'authority')[0]
$service = & (Join-Path $repoRoot 'probes/spaces-network/register-tangent-service.ps1') -Port $Port
$participantScope = "space:local.tangent.room?authority=$($authority.did)&collection=local.tangent.message&action=read&action=create"
$authorityScope = "space:local.tangent.room?authority=$($authority.did)&collection=local.tangent.message&action=read_self&action=read&manage=create"
$scopes = @('atproto', $participantScope, $authorityScope)
$clientId = 'http://localhost?redirect_uri=' + [Uri]::EscapeDataString($callback) + '&scope=' + [Uri]::EscapeDataString(($scopes -join ' '))
if (Test-Path -LiteralPath (Join-Path $stateDirectory 'restored-from.json')) {
    $restored = Get-Content -LiteralPath (Join-Path $stateDirectory 'restored-from.json') -Raw | ConvertFrom-Json
    if ($restored.clientId -ne $clientId -or $restored.managingApp -ne $service.managingApp) {
        throw 'Start the restored instance on its original port and managing-app service. Existing protected grants are bound to that client.'
    }
}

. "$PSScriptRoot/local-configuration.ps1"
$settings = New-TangentLocalConfiguration -Fixture $fixture -ManagingApp $service.managingApp -Origin $origin -StateDirectory $stateDirectory
$configuration = Join-Path $stateDirectory 'appsettings.json'
$settings | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $configuration -Encoding utf8

if (-not $NoBuild) {
    & dotnet build $project --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tangent build failed.' }
}
$appDll = Join-Path $repoRoot 'src/TangentSpace/bin/Debug/net10.0/TangentSpace.dll'
if (-not (Test-Path -LiteralPath $appDll)) { throw 'Build Tangent before using -NoBuild.' }
# Koan compares build intent with runtime composition at the configured content root.
$compositionLock = Join-Path $repoRoot 'src/TangentSpace/koan.lock.json'
if (-not (Test-Path -LiteralPath $compositionLock)) { throw 'Build Tangent to generate its Koan composition lock.' }
Copy-Item -LiteralPath $compositionLock -Destination (Join-Path $stateDirectory 'koan.lock.json')
if ($Background) {
    # Run a snapshot so Windows file locks do not block subsequent source builds.
    $runtimeDirectory = Join-Path $stateDirectory ('runtime-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())
    New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath (Split-Path -Parent $appDll) | Copy-Item -Destination $runtimeDirectory -Recurse
    $appDll = Join-Path $runtimeDirectory 'TangentSpace.dll'
}
$arguments = @($appDll, '--urls', $origin, '--environment', 'Development', '--contentRoot', $stateDirectory,
    '--webroot', (Join-Path $repoRoot 'src/TangentSpace/wwwroot'))
if ($Background) {
    # Quote only verified filesystem paths; Start-Process consumes one command line on Windows.
    $quoted = @($arguments | ForEach-Object { if ($_ -match '[\s]') { '"' + $_ + '"' } else { $_ } })
    $process = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList $quoted -WorkingDirectory $stateDirectory `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stateDirectory 'host.stdout.log') `
        -RedirectStandardError (Join-Path $stateDirectory 'host.stderr.log')
    $process.Id | Set-Content -LiteralPath (Join-Path $stateDirectory 'host.pid')
    [pscustomobject]@{ pid = $process.Id; origin = $origin; stateDirectory = $stateDirectory }
} else {
    Write-Output "Disposable Tangent site: $origin"
    Write-Output "State directory: $stateDirectory"
    # Keep console output attached while recording the actual child PID for stop/backup.
    $foreground = [System.Diagnostics.Process]::new()
    $foreground.StartInfo.FileName = (Get-Command dotnet).Source
    $foreground.StartInfo.WorkingDirectory = $stateDirectory
    $foreground.StartInfo.UseShellExecute = $false
    $foreground.StartInfo.CreateNoWindow = $true
    foreach ($argument in $arguments) { $foreground.StartInfo.ArgumentList.Add($argument) }
    $started = $false
    try {
        if (-not $foreground.Start()) { throw 'The foreground Tangent process did not start.' }
        $started = $true
        $foreground.Id | Set-Content -LiteralPath (Join-Path $stateDirectory 'host.pid')
        while (-not $foreground.WaitForExit(250)) { }
        $global:LASTEXITCODE = $foreground.ExitCode
    } finally {
        # Ctrl+C or a PID-write failure must not leave an untracked foreground child.
        if ($started -and -not $foreground.HasExited) {
            $foreground.Kill($true)
            $foreground.WaitForExit()
        }
        $foreground.Dispose()
    }
}

