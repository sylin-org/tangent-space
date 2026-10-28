# Copy the complete Docker bind-mounted state; never operate on the source test network.
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][ValidateSet('Backup', 'Restore')][string]$Action,
    [Parameter(Position=0)][string]$Path,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$state = Join-Path $repoRoot '.local/docker/site'

function Assert-OrdinaryTree([string]$Directory) {
    $absolute = [IO.Path]::GetFullPath($Directory)
    if ($absolute.TrimEnd('\', '/') -eq [IO.Path]::GetPathRoot($absolute).TrimEnd('\', '/')) { throw 'A filesystem root is not a state directory.' }
    $ancestor = $absolute
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "State paths must not cross junctions or symbolic links: $ancestor"
            }
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    if (Test-Path -LiteralPath $absolute) {
        if (-not (Test-Path -LiteralPath $absolute -PathType Container)) { throw "Not a directory: $absolute" }
        if (Get-ChildItem -LiteralPath $absolute -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1) {
            throw "State trees must not contain junctions or symbolic links: $absolute"
        }
    }
    return $absolute
}
function Invoke-Compose([string[]]$Arguments) {
    & docker compose @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed: $($Arguments -join ' ')" }
}
function Copy-StateSnapshot([string]$Destination) {
    $destinationPath = Assert-OrdinaryTree $Destination
    if (Test-Path -LiteralPath $destinationPath) { throw "Backup destination already exists: $destinationPath" }
    if ($destinationPath.StartsWith($state + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A backup cannot be placed inside the live state directory.'
    }
    Assert-OrdinaryTree $state | Out-Null
    if (-not (Test-Path -LiteralPath (Join-Path $state 'appsettings.json'))) { throw 'No configured Docker state exists to back up.' }
    New-Item -ItemType Directory -Path $destinationPath | Out-Null
    Copy-Item -LiteralPath $state -Destination (Join-Path $destinationPath 'state') -Recurse -Force
    $files = @(Get-ChildItem -LiteralPath (Join-Path $destinationPath 'state') -File -Recurse -Force | ForEach-Object {
        @{ path = [IO.Path]::GetRelativePath((Join-Path $destinationPath 'state'), $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    @{ format = 'tangent-docker-state-v1'; createdAt = [DateTimeOffset]::UtcNow.ToString('O'); files = $files } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destinationPath 'backup.json') -Encoding utf8
    Write-Host "Backup saved: $destinationPath"
    return $destinationPath
}

Push-Location $repoRoot
try {
    Assert-OrdinaryTree $state | Out-Null
    if ($Action -eq 'Backup') {
        $destination = if ($Path) { [IO.Path]::GetFullPath($Path) } else {
            Join-Path $repoRoot ('.local/backups/docker-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
        }
        Assert-OrdinaryTree $destination | Out-Null
        if (Test-Path -LiteralPath $destination) { throw "Backup destination already exists: $destination" }
        if (-not $PSCmdlet.ShouldProcess($state, "Stop Tangent briefly and copy all state to $destination")) { return }
        $running = @(& docker compose ps --status running --services)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot determine whether Tangent is running.' }
        $wasRunning = $running -contains 'tangent'
        try {
            Invoke-Compose @('stop', 'tangent')
            Copy-StateSnapshot $destination | Out-Null
        } finally {
            if ($wasRunning) { Invoke-Compose @('start', '--wait', 'tangent') }
        }
    } else {
        if (-not $Path) { throw 'Usage: Restore.bat "path-to-backup". Choose a backup created by Backup.bat.' }
        $backup = Assert-OrdinaryTree ([IO.Path]::GetFullPath($Path))
        if ($backup -eq $state -or $backup.StartsWith($state + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Restore source must be outside the live state directory.'
        }
        $source = Join-Path $backup 'state'
        $manifest = Get-Content -LiteralPath (Join-Path $backup 'backup.json') -Raw | ConvertFrom-Json
        if ($manifest.format -ne 'tangent-docker-state-v1' -or -not $manifest.files.Count) { throw 'Not a complete Tangent Docker backup.' }
        if (-not (Test-Path -LiteralPath (Join-Path $source 'appsettings.json'))) { throw 'The backup has no configuration.' }
        $sourcePrefix = [IO.Path]::GetFullPath($source) + [IO.Path]::DirectorySeparatorChar
        foreach ($file in $manifest.files) {
            $candidate = [IO.Path]::GetFullPath((Join-Path $source $file.path))
            if (-not $candidate.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid path in backup manifest.' }
            if ((Get-FileHash -LiteralPath $candidate).Hash -ne $file.sha256) { throw "Backup integrity check failed: $($file.path)" }
        }
        if (@(Get-ChildItem -LiteralPath $source -File -Recurse -Force).Count -ne $manifest.files.Count) { throw 'Backup file inventory changed.' }
        Write-Host "Restore from: $backup"
        Write-Host "Replace app state: $state"
        if ($WhatIfPreference) { $null = $PSCmdlet.ShouldProcess($state, 'Replace with verified backup'); return }
        if (-not $Force -and (Read-Host 'Type RESTORE to replace the current app state') -cne 'RESTORE') { Write-Host 'Cancelled.'; return }
        if (-not $PSCmdlet.ShouldProcess($state, 'Replace with verified backup')) { return }
        Invoke-Compose @('stop', 'tangent')
        Invoke-Compose @('rm', '--force', 'tangent')
        if ((Test-Path -LiteralPath $state) -and @(Get-ChildItem -LiteralPath $state -Force).Count -gt 0) {
            Copy-StateSnapshot (Join-Path $repoRoot ('.local/backups/before-restore-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))) | Out-Null
        }
        # The deletion target is always this repository's fixed bind mount, checked again after stopping Docker.
        $resolvedState = Assert-OrdinaryTree $state
        $expected = [IO.Path]::GetFullPath((Join-Path $repoRoot '.local/docker/site'))
        if ($resolvedState -ne $expected) { throw 'Unexpected restore target.' }
        if (Test-Path -LiteralPath $resolvedState) { Remove-Item -LiteralPath $resolvedState -Recurse -Force }
        Copy-Item -LiteralPath $source -Destination $resolvedState -Recurse -Force
        Write-Host 'Restore complete. Run Launch.bat to start Tangent with this state.'
    }
} finally { Pop-Location }
