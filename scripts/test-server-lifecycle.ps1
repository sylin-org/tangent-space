# Tangent Space Docker lifecycle test suite. Plain PowerShell; no Pester required.
# Wipe behavior is exercised against disposable scratch trees under
# <repo>/.local/docker/lifecycle-test-* only. The production .local/docker/site directory and
# the running application container are never touched: every docker invocation is captured by
# an injected command runner, and every launch scenario runs in a child process.
[CmdletBinding()]
param([switch]$KeepScratch)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$allowedRoot = Join-Path $repoRoot '.local/docker'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$scratch = Join-Path $allowedRoot "lifecycle-test-$stamp"
$opencodeTemp = Join-Path $env:LOCALAPPDATA 'Temp/opencode'
$outsideRoot = Join-Path $opencodeTemp "lifecycle-outside-$stamp"

$script:PassedCount = 0
$script:FailedCount = 0
$script:FailedNames = [System.Collections.Generic.List[string]]::new()
function Assert-That {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Name)
    if ($Condition) { $script:PassedCount++; Write-Output "PASS $Name" }
    else { $script:FailedCount++; $script:FailedNames.Add($Name); Write-Output "FAIL $Name" }
}
function Assert-Throws {
    param([Parameter(Mandatory)][scriptblock]$Block, [Parameter(Mandatory)][string]$Name)
    $threw = $false
    try { & $Block | Out-Null } catch { $threw = $true }
    Assert-That $threw $Name
}
function New-ScratchSiteState {
    param([Parameter(Mandatory)][string]$Directory)
    New-Item -ItemType Directory -Path (Join-Path $Directory 'oauth'), (Join-Path $Directory 'data/keys') -Force | Out-Null
    'settings' | Set-Content -LiteralPath (Join-Path $Directory 'appsettings.json')
    'database' | Set-Content -LiteralPath (Join-Path $Directory 'tangent.sqlite')
    'journal' | Set-Content -LiteralPath (Join-Path $Directory 'tangent.sqlite-wal')
    'session' | Set-Content -LiteralPath (Join-Path $Directory 'oauth/session.json')
    'key' | Set-Content -LiteralPath (Join-Path $Directory 'data/keys/key.xml')
    'lock' | Set-Content -LiteralPath (Join-Path $Directory 'koan.lock.json')
}
function Get-FileFingerprint {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

New-Item -ItemType Directory -Path $scratch, $outsideRoot -Force | Out-Null
$script:RecordedRuns = [System.Collections.Generic.List[string]]::new()
$recordingRunner = { param([string[]]$CommandArguments) $script:RecordedRuns.Add($CommandArguments -join ' ') }
$junctionLinks = [System.Collections.Generic.List[string]]::new()
try {
    # --- 3. Target resolution safety -----------------------------------------------------
    $TangentLifecycleSkipMain = $true
    . (Join-Path $repoRoot 'scripts/server-lifecycle.ps1')
    Assert-That ((Resolve-TangentWipeTarget -TargetPath 'site' -AllowedRoot $allowedRoot).absolutePath -eq (Join-Path $allowedRoot 'site')) 'Resolver accepts the default state directory inside the allowed root'
    Assert-That ((Resolve-TangentWipeTarget -TargetPath "lifecycle-test-$stamp/nested" -AllowedRoot $allowedRoot).existed -eq $false) 'Resolver accepts a non-existent nested target and reports it absent'
    Assert-Throws { Resolve-TangentWipeTarget -TargetPath $allowedRoot -AllowedRoot $allowedRoot } 'Resolver rejects the allowed root itself'
    Assert-Throws { Resolve-TangentWipeTarget -TargetPath $repoRoot -AllowedRoot $allowedRoot } 'Resolver rejects the repository root'
    Assert-Throws { Resolve-TangentWipeTarget -TargetPath ([IO.Path]::GetPathRoot($allowedRoot)) -AllowedRoot $allowedRoot } 'Resolver rejects a filesystem root'
    Assert-Throws { Resolve-TangentWipeTarget -TargetPath (Join-Path $outsideRoot 'site') -AllowedRoot $allowedRoot } 'Resolver rejects a target outside the repository'
    Assert-Throws { Resolve-TangentWipeTarget -TargetPath 'site\..\..\..\outside' -AllowedRoot $allowedRoot } 'Resolver rejects traversal out of the allowed root'
    'plain' | Set-Content -LiteralPath (Join-Path $scratch 'plain-file.txt')
    Assert-Throws { Resolve-TangentWipeTarget -TargetPath "lifecycle-test-$stamp/plain-file.txt" -AllowedRoot $allowedRoot } 'Resolver rejects a plain file target'
    New-Item -ItemType Directory -Path (Join-Path $scratch 'junction-target') -Force | Out-Null
    'target-content' | Set-Content -LiteralPath (Join-Path $scratch 'junction-target/keep.txt')
    $junctionLink = Join-Path $scratch 'junction-site'
    New-Item -ItemType Junction -Path $junctionLink -Target (Join-Path $scratch 'junction-target') | Out-Null
    $junctionLinks.Add($junctionLink)
    Assert-Throws { Resolve-TangentWipeTarget -TargetPath "lifecycle-test-$stamp/junction-site" -AllowedRoot $allowedRoot } 'Resolver rejects a junction as the wipe target'
    $parentLink = Join-Path $scratch 'junction-parent'
    New-Item -ItemType Junction -Path $parentLink -Target (Join-Path $scratch 'junction-target') | Out-Null
    $junctionLinks.Add($parentLink)
    Assert-Throws { Resolve-TangentWipeTarget -TargetPath "lifecycle-test-$stamp/junction-parent/inner" -AllowedRoot $allowedRoot } 'Resolver rejects a path crossing a junction ancestor'

    # --- 4. Confirmation rules ------------------------------------------------------------
    Assert-That (Confirm-TangentWipe -TargetPath 'x' -Force) '-Force confirms without prompting'
    Assert-That (Confirm-TangentWipe -TargetPath 'x' -Prompt { param($message) 'WIPE' }) 'Typing WIPE exactly confirms'
    Assert-That (-not (Confirm-TangentWipe -TargetPath 'x' -Prompt { param($message) 'wipe' })) 'A lowercase answer does not confirm'
    Assert-That (-not (Confirm-TangentWipe -TargetPath 'x' -Prompt { param($message) 'no' })) 'A declined prompt cancels'

    # --- 5. Actual wipe against a disposable scratch tree ---------------------------------
    $site = Join-Path $scratch 'site'
    New-ScratchSiteState -Directory $site
    New-Item -ItemType Directory -Path (Join-Path $scratch 'sentinel') -Force | Out-Null
    'sentinel' | Set-Content -LiteralPath (Join-Path $scratch 'sentinel/keep.txt')

    $whatIfResult = Invoke-TangentWipe -TargetPath $site -AllowedRoot $allowedRoot -RepoRoot $repoRoot -CommandRunner $recordingRunner -WhatIf
    Assert-That ($whatIfResult.status -eq 'whatif') 'WhatIf dry run reports the whatif status'
    Assert-That ((Test-Path -LiteralPath (Join-Path $site 'appsettings.json')) -and (Test-Path -LiteralPath (Join-Path $site 'tangent.sqlite'))) 'WhatIf preserves the state tree'
    Assert-That ($script:RecordedRuns.Count -eq 0) 'WhatIf issues no docker commands'

    $cancelledResult = Invoke-TangentWipe -TargetPath $site -AllowedRoot $allowedRoot -RepoRoot $repoRoot -CommandRunner $recordingRunner -Prompt { param($message) 'abort' }
    Assert-That ($cancelledResult.status -eq 'cancelled') 'A declined confirmation cancels the wipe'
    Assert-That ((Test-Path -LiteralPath (Join-Path $site 'tangent.sqlite'))) 'A cancelled wipe preserves the state tree'
    Assert-That ($script:RecordedRuns.Count -eq 0) 'A cancelled wipe issues no docker commands'

    $wipedResult = Invoke-TangentWipe -TargetPath $site -AllowedRoot $allowedRoot -RepoRoot $repoRoot -CommandRunner $recordingRunner -Force
    Assert-That ($wipedResult.status -eq 'wiped') 'A forced wipe reports the wiped status'
    foreach ($gone in @('appsettings.json', 'tangent.sqlite', 'tangent.sqlite-wal', 'oauth', 'data', 'koan.lock.json')) {
        Assert-That (-not (Test-Path -LiteralPath (Join-Path $site $gone))) "Wipe removed $gone"
    }
    Assert-That ((Test-Path -LiteralPath (Join-Path $scratch 'sentinel/keep.txt'))) 'Wipe preserves the sibling sentinel'
    Assert-That ($script:RecordedRuns.Count -eq 2) 'Wipe issued exactly two docker commands'
    Assert-That ($script:RecordedRuns[0] -eq 'docker compose stop tangent') 'Wipe stops only the Compose tangent service'
    Assert-That ($script:RecordedRuns[1] -eq 'docker compose rm --force tangent') 'Wipe removes only the Compose tangent service'

    Assert-Throws { Invoke-TangentWipe -TargetPath (Join-Path $outsideRoot 'site') -AllowedRoot $allowedRoot -RepoRoot $repoRoot -CommandRunner $recordingRunner -Force } 'Wipe rejects a target outside the repository before any docker command'
    Assert-Throws { Invoke-TangentWipe -TargetPath "lifecycle-test-$stamp/junction-site" -AllowedRoot $allowedRoot -RepoRoot $repoRoot -CommandRunner $recordingRunner -Force } 'Wipe rejects a junction target before any docker command'
    Assert-That ($script:RecordedRuns.Count -eq 2) 'Rejected wipes issued no additional docker commands'

    $absentResult = Invoke-TangentWipe -TargetPath (Join-Path $scratch 'never-existed') -AllowedRoot $allowedRoot -RepoRoot $repoRoot -CommandRunner $recordingRunner -Force
    Assert-That ($absentResult.status -eq 'absent') 'Wiping an absent target reports absent without failing'
    Assert-That ($script:RecordedRuns.Count -eq 4) 'An absent-target wipe still stopped and removed the tangent service'

    # Sections 7 and 8 (launch and build dispatch against a mocked Docker) were deleted in
    # R1.15: they asserted that a development script calls docker with the strings it calls
    # docker with. What remains guards the one thing that can go irreversibly wrong here —
    # a wipe resolving outside its allowed root, through `..`, a junction, or the repository
    # itself. When Wipe.bat stops taking a target at all, even these become unnecessary.

    Write-Output ''
    Write-Output "Lifecycle test summary: $($script:PassedCount) passed, $($script:FailedCount) failed."
    if ($script:FailedNames.Count -gt 0) { Write-Output ("Failed: " + ($script:FailedNames -join '; ')) }
} finally {
    foreach ($link in $junctionLinks) { Remove-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue }
    if (-not [IO.Path]::GetFullPath($outsideRoot).StartsWith([IO.Path]::GetFullPath($opencodeTemp) + [IO.Path]::DirectorySeparatorChar)) { throw 'Unexpected scratch cleanup path.' }
    Remove-Item -LiteralPath $outsideRoot -Recurse -Force -ErrorAction SilentlyContinue
    if (-not $KeepScratch) {
        # Dogfood the tested wipe to remove the scratch tree itself.
        try { Invoke-TangentWipe -TargetPath $scratch -AllowedRoot $allowedRoot -RepoRoot $repoRoot -CommandRunner { param([string[]]$a) } -Force | Out-Null } catch { }
        if (Test-Path -LiteralPath $scratch) { $safeScratch = Resolve-TangentWipeTarget -TargetPath $scratch -AllowedRoot $allowedRoot; Remove-Item -LiteralPath $safeScratch.absolutePath -Recurse -Force }
    }
}
if ($script:FailedCount -gt 0) { exit 1 }
exit 0
