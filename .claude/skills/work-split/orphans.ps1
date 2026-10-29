<#
.SYNOPSIS
Lists the processes running from this repository whose parent is gone, and ends them with -Stop.

.DESCRIPTION
A process started by a command the harness reads holds that command open for as long as it lives;
when its parent has exited it is an orphan nothing will end. This lists every process whose command
line names this repository or Tangent.Server, with its age, the worktree it runs from, and whether
its parent is still alive. -Stop ends the orphans; -All also lists processes whose parent lives.
Windows only.
#>
[CmdletBinding()]
param([switch]$Stop, [switch]$All)

if (-not $IsWindows) { throw 'This script reads Win32_Process and runs on Windows only.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path.TrimEnd('\')
$rootPattern = [regex]::Escape($root) -replace '\\\\', '[\\/]'
$processes = Get-CimInstance Win32_Process
$byId = @{}
foreach ($p in $processes) { $byId[[int]$p.ProcessId] = $p }
$self = $PID
$ancestors = @{}
$cursor = $self
while ($byId.ContainsKey($cursor)) { $ancestors[$cursor] = $true; $cursor = [int]$byId[$cursor].ParentProcessId; if ($ancestors.ContainsKey($cursor)) { break } }

# A server the lifecycle script launched is detached on purpose; `scripts/tangent.ps1 stop` owns it.
$launched = @{}
foreach ($pidFile in (Get-ChildItem -Path (Join-Path $root '.local/launch/tangent.pid'), (Join-Path $root '.local/worktrees/*/.local/launch/tangent.pid') -ErrorAction SilentlyContinue)) {
    $id = ((Get-Content -LiteralPath $pidFile.FullName -Raw).Trim() -split ' ')[0]
    if ($id -match '^\d+$') { $launched[[int]$id] = $pidFile.FullName }
}

$rows = foreach ($p in $processes) {
    if ($ancestors.ContainsKey([int]$p.ProcessId)) { continue }
    $command = $p.CommandLine
    if (-not $command) { continue }
    if ($command -notmatch $rootPattern -and $command -notmatch 'Tangent\.Server') { continue }
    $parent = $byId[[int]$p.ParentProcessId]
    $parentAlive = $parent -and $parent.CreationDate -le $p.CreationDate
    $worktree = if ($command -match '\.local[\\/]worktrees[\\/]([^\\/\s"]+)') { $Matches[1] } else { '-' }
    $flat = $command -replace '\s+', ' '
    [pscustomobject]@{
        Pid      = $p.ProcessId
        Name     = $p.Name
        Orphan   = -not $parentAlive
        Launched = $launched.ContainsKey([int]$p.ProcessId)
        Minutes  = [int]((Get-Date) - $p.CreationDate).TotalMinutes
        Worktree = $worktree
        Command  = $flat.Substring(0, [Math]::Min(110, $flat.Length))
    }
}

$shown = if ($All) { $rows } else { $rows | Where-Object Orphan }
if (-not $shown) { Write-Output ($(if ($All) { 'No process runs from this repository.' } else { 'No orphan runs from this repository.' })); return }
$shown | Sort-Object Orphan -Descending | Format-Table -AutoSize | Out-String -Width 200 | Write-Output

if ($Stop) {
    foreach ($row in ($rows | Where-Object Orphan)) {
        if ($row.Launched) { Write-Output "Left $($row.Pid) $($row.Name): the lifecycle script launched it; end it with scripts/tangent.ps1 stop in its worktree."; continue }
        try { Stop-Process -Id $row.Pid -Force -ErrorAction Stop; Write-Output "Ended $($row.Pid) $($row.Name)." }
        catch { Write-Output "Could not end $($row.Pid) $($row.Name): $($_.Exception.Message)" }
    }
}
