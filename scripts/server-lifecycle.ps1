# Tangent Space server lifecycle engine. Two servers live under src/server:
#   web       — the .NET/Koan Tangent web+experience server; runs as the Docker container.
#   connector — the Rust local connector (src/connector); a host-run binary, no container.
# Every action applies to all servers where it is meaningful:
# Wipe: stop/remove only the Compose tangent service, then clear one validated state
#   directory under <repo>/.local/docker. Interactive use requires typing WIPE; -Force
#   is the internal/script path and -WhatIf performs a dry run that touches nothing.
#   The connector keeps no server-side state: its local state belongs to the operator
#   (TANGENT_CONNECTOR_HOME) and is never touched here.
# Build: verify the pinned framework contribution, then docker compose build tangent
#   (web) and cargo build --release (connector). No stop and no wipe.
# Launch: delegate to scripts/start-docker.ps1 (retain or create the configuration, then
#   start only Tangent). The connector is launched by its hosts (agent applications run
#   `tangent-connector serve`; operators use the CLI), so launch reports its binary path
#   instead of starting a process.
[CmdletBinding()]
param(
    [ValidateSet('Wipe', 'Build', 'Launch')][string]$Action,
    [switch]$Force,
    [switch]$WhatIf,
    [switch]$Build,
    [scriptblock]$CommandRunner,
    [scriptblock]$FrameworkPreparer,
    [scriptblock]$Prompt
)
$ErrorActionPreference = 'Stop'

$script:DefaultTangentCommandRunner = {
    param([Parameter(Mandatory)][string[]]$CommandArguments)
    $executable, $rest = $CommandArguments
    & $executable @rest
    if ($LASTEXITCODE -ne 0) { throw "Command failed (exit $LASTEXITCODE): $($CommandArguments -join ' ')" }
}

function Get-TangentDockerRunner {
    param([scriptblock]$Runner)
    if ($Runner) { return $Runner }
    return $script:DefaultTangentCommandRunner
}

# Resolves and validates a wipe target. The absolute path must sit strictly inside
# $AllowedRoot; the allowed root itself, repository/drive roots, files and junction or
# other reparse points (on the target or any existing component below the allowed root)
# are rejected before any recursive delete can happen.
# The only directory a wipe ever deletes, under the allowed root.
$script:StateDirectoryName = 'site'

function Resolve-TangentWipeTarget {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$AllowedRoot)
    $allowed = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\')
    $allowedRootParent = Split-Path -Parent $allowed
    if (-not $allowedRootParent -or $allowed -eq [IO.Path]::GetPathRoot($allowed)) {
        throw "Refusing to use a filesystem root as the allowed wipe root: $allowed"
    }
    $ancestor = $allowed
    while ($ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Wipe root crosses a reparse point: $ancestor" }
        $ancestor = Split-Path -Parent $ancestor
    }
    $allowedAttributes = Get-Item -LiteralPath $allowed -Force -ErrorAction SilentlyContinue
    if ($allowedAttributes.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "The allowed wipe root is a reparse point: $allowed" }
    # The target is not chosen: it is always the one state directory under the allowed root.
    # Nothing can name another path, so traversal, an outside-the-root target and the root
    # itself cannot arise, and none of them is checked for.
    $absolute = [IO.Path]::GetFullPath((Join-Path $allowed $script:StateDirectoryName))
    # A reparse point on the target is still reachable — something could plant a junction at
    # the state directory — and a recursive delete would follow it out of the tree.
    if ((Test-Path -LiteralPath $absolute) -and ((Get-Item -LiteralPath $absolute -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Wipe path crosses a junction or reparse point: $absolute"
    }
    $existed = Test-Path -LiteralPath $absolute
    if ($existed -and -not (Test-Path -LiteralPath $absolute -PathType Container)) {
        throw "Wipe target is not a directory: $absolute"
    }
    if ($existed -and (Get-ChildItem -LiteralPath $absolute -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1)) {
        throw "Wipe target contains a junction or symbolic link: $absolute"
    }
    return [pscustomobject]@{ absolutePath = $absolute; existed = $existed; allowedRoot = $allowed }
}

# Interactive confirmation: the exact absolute target is shown and the word WIPE must be
# typed. -Force skips the prompt for scripts and tests. A declined prompt cancels safely.
function Confirm-TangentWipe {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$TargetPath, [switch]$Force, [scriptblock]$Prompt)
    if ($Force) { return $true }
    $read = if ($Prompt) { $Prompt } else { { param([string]$Message) Read-Host -Prompt $Message } }
    $answer = & $read ("Type WIPE to permanently erase this Tangent state directory: $TargetPath")
    return ($answer -is [string] -and $answer.Trim() -ceq 'WIPE')
}

# Stops and removes only the Compose tangent service of this repository's project.
function Stop-TangentComposeService {
    [CmdletBinding()]
    param([Parameter(Mandatory)][scriptblock]$Runner, [Parameter(Mandatory)][string]$RepoRoot)
    $previous = Get-Location
    Set-Location -LiteralPath $RepoRoot
    try {
        & $Runner @('docker', 'compose', 'stop', 'tangent')
        & $Runner @('docker', 'compose', 'rm', '--force', 'tangent')
    } finally { Set-Location -LiteralPath $previous.Path }
}

# The wipe itself: validate, confirm, stop/remove only the tangent service, re-validate,
# then recursively delete the state directory with native PowerShell file operations.
function Invoke-TangentWipe {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][string]$AllowedRoot,
        [Parameter(Mandatory)][string]$RepoRoot,
        [switch]$Force,
        [scriptblock]$CommandRunner,
        [scriptblock]$Prompt
    )
    $resolved = Resolve-TangentWipeTarget -AllowedRoot $AllowedRoot
    Write-Host "Wipe target: $($resolved.absolutePath)"
    Write-Host "Allowed root: $($resolved.allowedRoot)"
    $whatIf = $PSCmdlet.WhatIfPreference -or $WhatIfPreference
    if ($whatIf) {
        Write-Host "What if: Stop and remove only the Compose tangent service in $RepoRoot."
        Write-Host "What if: Delete $($resolved.absolutePath)."
        return [pscustomobject]@{ status = 'whatif'; target = $resolved.absolutePath }
    }
    if (-not (Confirm-TangentWipe -TargetPath $resolved.absolutePath -Force:$Force -Prompt $Prompt)) {
        Write-Host 'Cancelled. Nothing was stopped, removed or deleted.'
        return [pscustomobject]@{ status = 'cancelled'; target = $resolved.absolutePath }
    }
    $runner = Get-TangentDockerRunner $CommandRunner
    Stop-TangentComposeService -Runner $runner -RepoRoot $RepoRoot
    # Re-validate after the container stop in case anything changed underneath us.
    $resolved = Resolve-TangentWipeTarget -AllowedRoot $AllowedRoot
    if ($resolved.existed) {
        Remove-Item -LiteralPath $resolved.absolutePath -Recurse -Force
        if (Test-Path -LiteralPath $resolved.absolutePath) { throw "The wipe target still exists after deletion: $($resolved.absolutePath)" }
        Write-Host "Deleted: $($resolved.absolutePath)"
        return [pscustomobject]@{ status = 'wiped'; target = $resolved.absolutePath }
    }
    Write-Host "Nothing to delete; the target directory did not exist: $($resolved.absolutePath)"
    return [pscustomobject]@{ status = 'absent'; target = $resolved.absolutePath }
}

# Build the image: verify the pinned framework contribution, then build only the tangent
# service. This never stops containers.
function Invoke-TangentDockerBuild {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot, [scriptblock]$CommandRunner, [scriptblock]$FrameworkPreparer)
    $preparer = if ($FrameworkPreparer) { $FrameworkPreparer } else {
        { param([string]$Root) & (Join-Path $Root 'scripts/prepare-framework.ps1') }
    }
    & $preparer $RepoRoot
    $runner = Get-TangentDockerRunner $CommandRunner
    $previous = Get-Location
    Set-Location -LiteralPath $RepoRoot
    try { & $Runner @('docker', 'compose', 'build', 'tangent') }
    finally { Set-Location -LiteralPath $previous.Path }
    Write-Output 'Built the Tangent image. Nothing was stopped or removed.'
}

# Build the connector (src/connector): a release binary. Kept separate from the web image
# build so lifecycle tests can drive the web path with mocks.
function Invoke-ConnectorBuild {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot, [scriptblock]$CommandRunner)
    $runner = if ($CommandRunner) { $CommandRunner } else { $script:DefaultTangentCommandRunner }
    $connector = Join-Path $RepoRoot 'src/connector'
    if (-not (Test-Path -LiteralPath (Join-Path $connector 'Cargo.toml'))) {
        throw "The connector sources are missing: $connector"
    }
    $cargo = Get-Command cargo -ErrorAction SilentlyContinue
    if (-not $cargo) { throw 'cargo (Rust) is required to build the connector: install the Rust toolchain or add it to PATH.' }
    $previous = Get-Location
    Set-Location -LiteralPath $connector
    try { & $runner @('cargo', 'build', '--release') }
    finally { Set-Location -LiteralPath $previous.Path }
    $binary = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'tangent-connector.exe' } else { 'tangent-connector' }
    Write-Output "Built the connector: $(Join-Path $connector "target/release/$binary")"
}

# Entry point. Tests dot-source this file with $TangentLifecycleSkipMain set and call the
# functions above directly; the dispatcher below then never runs.
if (-not ($TangentLifecycleSkipMain -or $global:TangentLifecycleSkipMain)) {
    if (-not $Action) { throw 'Choose an action: -Action Wipe, -Action Build or -Action Launch.' }
    $repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    switch ($Action) {
        'Wipe' {
            $allowedRoot = Join-Path $repoRoot '.local/docker'
            $result = Invoke-TangentWipe -AllowedRoot $allowedRoot -RepoRoot $repoRoot -Force:$Force -CommandRunner $CommandRunner -Prompt $Prompt -WhatIf:$WhatIf
            if ($result.status -in @('wiped', 'absent')) {
                Write-Output 'A fresh site is configured on the next launch; use Launch.bat (or scripts/start-docker.ps1).'
                Write-Output 'The connector keeps no server-side state; operator-local connector state is untouched.'
            }
        }
        'Build' {
            Invoke-TangentDockerBuild -RepoRoot $repoRoot -CommandRunner $CommandRunner -FrameworkPreparer $FrameworkPreparer
            Invoke-ConnectorBuild -RepoRoot $repoRoot -CommandRunner $CommandRunner
        }
        'Launch' {
            $launchArguments = @{ Build = [bool]$Build }
            if ($CommandRunner) { $launchArguments.CommandRunner = $CommandRunner }
            & (Join-Path $repoRoot 'scripts/start-docker.ps1') @launchArguments
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            $binary = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'tangent-connector.exe' } else { 'tangent-connector' }
            Write-Output "The connector is host-run, not containerized: agent hosts start it via 'tangent-connector serve' (see src/connector/README.md). Expected binary after Build: $(Join-Path $repoRoot "src/connector/target/release/$binary")"
        }
    }
}
