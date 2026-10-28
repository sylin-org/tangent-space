[CmdletBinding()]
param(
    [switch]$Build,
    # Internal seams used only by scripts/test-server-lifecycle.ps1. Defaults exercise the real system.
    [string]$StateRoot,
    [int]$Port = 5220,
    [scriptblock]$CommandRunner
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
Push-Location $repoRoot
try {
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

    # An existing configuration is retained byte-for-byte; a fresh site leaves ownership open
    # for the first verified sign-in.
    $configuration = Save-TangentDockerConfiguration -Origin "http://127.0.0.1:$Port" -HostStateDirectory $state
    Write-Output "Configuration $($configuration.status): $($configuration.path)"

    # The composition lock is refreshed from the current source tree on every launch.
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src/server/web/koan.lock.json') -Destination (Join-Path $state 'koan.lock.json') -Force

    if ($Build) {
        & $commandRunner @('docker', 'compose', 'build', 'tangent')
    }
    & $commandRunner @('docker', 'compose', 'up', '-d', '--no-deps', '--wait', 'tangent')
    Write-Output "Tangent: http://127.0.0.1:$Port | Docker Desktop project: tangent-space"
    Write-Output 'Bootstrap and live logs: docker compose logs -f tangent'
} finally { Pop-Location }
