# Tangent Space Docker lifecycle test suite. Plain PowerShell; no Pester required.
# Wipe behavior is exercised against disposable scratch trees under
# <repo>/.local/docker/lifecycle-test-* only. The production .local/docker/site directory,
# the running application container and the disposable Spaces network container are never
# touched: every docker invocation is captured by an injected command runner, and every
# launch scenario runs in a child process against a synthetic fixture file.
[CmdletBinding()]
param([switch]$KeepScratch)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$allowedRoot = Join-Path $repoRoot '.local/docker'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$scratch = Join-Path $allowedRoot "lifecycle-test-$stamp"
$syntheticNetworkId = [DateTimeOffset]::new(2020, 1, 1, 0, 0, 0, [TimeSpan]::Zero).ToUnixTimeMilliseconds()
$syntheticWindowsState = Join-Path $repoRoot ".local/tangent/$syntheticNetworkId/site"
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
    @{ networkId = $syntheticNetworkId } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Directory 'network.json')
    'lock' | Set-Content -LiteralPath (Join-Path $Directory 'koan.lock.json')
}
function Get-FileFingerprint {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

if (Test-Path -LiteralPath (Split-Path -Parent $syntheticWindowsState)) { throw 'The synthetic migration fixture path already exists; refusing to touch it.' }
New-Item -ItemType Directory -Path $scratch, $outsideRoot -Force | Out-Null
$script:RecordedRuns = [System.Collections.Generic.List[string]]::new()
$recordingRunner = { param([string[]]$CommandArguments) $script:RecordedRuns.Add($CommandArguments -join ' ') }
$junctionLinks = [System.Collections.Generic.List[string]]::new()
try {
    # --- 1. Parse and structural checks -------------------------------------------------
    foreach ($file in @('scripts/docker-lifecycle.ps1', 'scripts/start-docker.ps1', 'scripts/local-configuration.ps1', 'scripts/test-docker-lifecycle.ps1')) {
        $tokens = $null; $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repoRoot $file), [ref]$tokens, [ref]$errors) | Out-Null
        Assert-That ($errors.Count -eq 0) "Parses without errors: $file"
    }
    foreach ($bat in @('Wipe.bat', 'Build.bat', 'Launch.bat')) {
        $content = Get-Content -LiteralPath (Join-Path $repoRoot $bat) -Raw
        $action = $bat -replace '\.bat$', ''
        Assert-That ($content -match [regex]::Escape('"%ROOT%scripts\docker-lifecycle.ps1"') -and $content -match "-Action $action") "$bat invokes docker-lifecycle.ps1 -Action $action with a quoted absolute script path"
        Assert-That ($content -match [regex]::Escape('set "ROOT=%~dp0"')) "$bat resolves its own location with %~dp0"
        Assert-That ($content -match [regex]::Escape('exit /b %ERRORLEVEL%')) "$bat propagates the PowerShell exit code"
    }

    # --- 2. BAT wrapper behavior with a stubbed pwsh (cwd/space robustness, exit codes) --
    $stubDir = Join-Path $scratch 'stub'
    $spaceyCwd = Join-Path $scratch 'cwd with spaces'
    New-Item -ItemType Directory -Path $stubDir, $spaceyCwd -Force | Out-Null
    $recordPath = Join-Path $scratch 'stub-record.txt'
    $stub = Join-Path $stubDir 'pwsh.cmd'
    "@echo off`n>>`"%LIFECYCLE_RECORD%`" echo %*`nexit /b %LIFECYCLE_EXIT%" | Set-Content -LiteralPath $stub -Encoding Ascii
    $previousPath = $env:Path
    $env:Path = "$stubDir;$env:Path"
    $env:LIFECYCLE_RECORD = $recordPath
    $env:LIFECYCLE_EXIT = '42'
    try {
        foreach ($bat in @('Wipe.bat', 'Build.bat', 'Launch.bat')) {
            Remove-Item -LiteralPath $recordPath -Force -ErrorAction SilentlyContinue
            $action = $bat -replace '\.bat$', ''
            $process = Start-Process -FilePath $env:ComSpec -ArgumentList '/d', '/c', "`"$(Join-Path $repoRoot $bat)`" -WhatIf" -WorkingDirectory $spaceyCwd -Wait -PassThru -WindowStyle Hidden
            $record = if (Test-Path -LiteralPath $recordPath) { Get-Content -LiteralPath $recordPath -Raw } else { '' }
            Assert-That ($process.ExitCode -eq 42) "$bat propagates the child exit code from any working directory"
            Assert-That ($record -match "-Action $action -WhatIf") "$bat forwards arguments to -Action $action"
            Assert-That ($record -match [regex]::Escape((Join-Path $repoRoot 'scripts\docker-lifecycle.ps1'))) "$bat resolves the repository script path"
        }
    } finally {
        $env:Path = $previousPath
        Remove-Item Env:\LIFECYCLE_RECORD, Env:\LIFECYCLE_EXIT -ErrorAction SilentlyContinue
    }

    # --- 3. Target resolution safety -----------------------------------------------------
    $TangentLifecycleSkipMain = $true
    . (Join-Path $repoRoot 'scripts/docker-lifecycle.ps1')
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
    foreach ($gone in @('appsettings.json', 'tangent.sqlite', 'tangent.sqlite-wal', 'oauth', 'data', 'network.json', 'koan.lock.json')) {
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

    # --- 6. Local configuration helpers ---------------------------------------------------
    $syntheticFixture = [pscustomobject]@{
        status = 'ready'
        startedAt = '2020-01-01T00:00:00.0000000+00:00'
        plc = 'http://localhost:2582'
        pds1 = 'http://localhost:2583'
        pds2 = 'http://localhost:2584'
        accounts = @(
            [pscustomobject]@{ role = 'owner'; did = 'did:plc:ownerrrrrrrrrrrrrrrrrrrrr'; handle = 'owner.test' }
            [pscustomobject]@{ role = 'authority'; did = 'did:plc:authorityyyyyyyyyyyyyyyy'; handle = 'authority.test' }
        )
    }
    . (Join-Path $repoRoot 'scripts/local-configuration.ps1')
    $settings = New-TangentLocalConfiguration -Fixture $syntheticFixture -ManagingApp 'did:plc:mmmmmmmmmmmmmmmmmmmmmmmm#tangent' -Origin 'http://127.0.0.1:5220' -StateDirectory '/state'
    Assert-That ($settings.Tangent.Site.OwnerDid -eq 'did:plc:ownerrrrrrrrrrrrrrrrrrrrr') 'The native configuration helper retains its fixture-owner default'
    Assert-That ($settings.Koan.Data.Sources.Default.ConnectionString -eq 'Data Source=/state/tangent.sqlite') 'Generated configuration stores SQLite inside the container /state bind mount'
    Assert-Throws { New-TangentLocalConfiguration -Fixture $syntheticFixture -ManagingApp 'm' -Origin 'o' -StateDirectory '/state' -OwnerDid 'not-a-did' } 'An invalid owner DID override is rejected'

    $configState = Join-Path $scratch 'config-state'
    New-Item -ItemType Directory -Path $configState -Force | Out-Null
    $created = Save-TangentDockerConfiguration -Fixture $syntheticFixture -ManagingApp 'did:plc:mmmmmmmmmmmmmmmmmmmmmmmm#tangent' -Origin 'http://127.0.0.1:5220' -HostStateDirectory $configState
    Assert-That ($created.status -eq 'created') 'A missing configuration is generated'
    $createdSettings = Get-Content -LiteralPath (Join-Path $configState 'appsettings.json') -Raw | ConvertFrom-Json
    Assert-That ($createdSettings.Koan.Web.Auth.Atproto.DevelopmentConnectHost -eq 'host.docker.internal') 'The Docker configuration routes development origins through host.docker.internal'
    Assert-That ($createdSettings.Tangent.Spaces.ManagingApp -eq 'did:plc:mmmmmmmmmmmmmmmmmmmmmmmm#tangent') 'The Docker configuration records the registered managing app'
    $before = Get-FileFingerprint (Join-Path $configState 'appsettings.json')
    $retained = Save-TangentDockerConfiguration -Fixture $syntheticFixture -ManagingApp 'did:plc:mmmmmmmmmmmmmmmmmmmmmmmm#tangent' -Origin 'http://127.0.0.1:5220' -HostStateDirectory $configState
    Assert-That ($retained.status -eq 'retained') 'An existing configuration is retained on repeat launches'
    Assert-That ((Get-FileFingerprint (Join-Path $configState 'appsettings.json')) -eq $before) 'A retained configuration is byte-for-byte identical'
    New-Item -ItemType Directory -Path (Join-Path $scratch 'fresh-no-app') -Force | Out-Null
    Assert-Throws { Save-TangentDockerConfiguration -Fixture $syntheticFixture -ManagingApp '' -Origin 'http://127.0.0.1:5220' -HostStateDirectory (Join-Path $scratch 'fresh-no-app') } 'Generating a fresh configuration without a managing app fails'

    $markerState = Join-Path $scratch 'marker-state'
    New-Item -ItemType Directory -Path $markerState -Force | Out-Null
    Assert-That ((Set-TangentNetworkMarker -HostStateDirectory $markerState -NetworkId $syntheticNetworkId).status -eq 'created') 'A missing fixture network marker is created'
    Assert-That ((Set-TangentNetworkMarker -HostStateDirectory $markerState -NetworkId $syntheticNetworkId).status -eq 'matched') 'A matching fixture network marker is accepted'
    Assert-Throws { Set-TangentNetworkMarker -HostStateDirectory $markerState -NetworkId ($syntheticNetworkId + 1) } 'A foreign fixture network marker is rejected'

    # --- 7. Launch flow through start-docker.ps1 with injected seams -----------------------
    $harness = Join-Path $scratch 'launch-harness.ps1'
    $launchRecord = Join-Path $scratch 'launch-record.txt'
    @'
param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$StateRoot, [Parameter(Mandatory)][string]$FixtureFile,
    [Parameter(Mandatory)][string]$Record, [switch]$Build, [switch]$MigrateWindowsState)
$ErrorActionPreference = 'Stop'
try {
    $runner = { param([string[]]$CommandArguments) Add-Content -LiteralPath $Record -Value ('RUN ' + ($CommandArguments -join ' ')) }
    $probe = { param([int]$ProbePort) Add-Content -LiteralPath $Record -Value 'PROBE'; [pscustomobject]@{ status = 'passed' } }
    $registrar = { param([string]$Root, [int]$ServicePort) Add-Content -LiteralPath $Record -Value 'REGISTER'; [pscustomobject]@{ managingApp = 'did:plc:mmmmmmmmmmmmmmmmmmmmmmmm#tangent' } }
    $migrator = { param([string]$Root, [long]$NetworkId, [string]$StateDirectory) Add-Content -LiteralPath $Record -Value 'MIGRATE'; 'recorded' }
    & (Join-Path $RepoRoot 'scripts/start-docker.ps1') -StateRoot $StateRoot -FixtureFile $FixtureFile -Port 5220 `
        -HealthProbe $probe -Registrar $registrar -CommandRunner $runner -WindowsStateMigrator $migrator -Build:$Build -MigrateWindowsState:$MigrateWindowsState
    exit 0
} catch {
    Add-Content -LiteralPath $Record -Value ('ERROR ' + $_.Exception.Message)
    exit 5
}
'@ | Set-Content -LiteralPath $harness -Encoding utf8
    $launchStateRoot = Join-Path $scratch 'docker'
    $fixtureFile = Join-Path $scratch 'fixtures.json'
    $syntheticFixture | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fixtureFile -Encoding utf8
    # Legacy Windows state for the synthetic network id; used to prove migration is opt-in.
    New-Item -ItemType Directory -Path $syntheticWindowsState -Force | Out-Null
    'legacy' | Set-Content -LiteralPath (Join-Path $syntheticWindowsState 'tangent.sqlite')
    function Invoke-LaunchHarness {
        param([switch]$Build, [switch]$MigrateWindowsState)
        Remove-Item -LiteralPath $launchRecord -Force -ErrorAction SilentlyContinue
        $arguments = @('-NoProfile', '-File', $harness, '-RepoRoot', $repoRoot, '-StateRoot', $launchStateRoot, '-FixtureFile', $fixtureFile, '-Record', $launchRecord)
        if ($Build) { $arguments += '-Build' }
        if ($MigrateWindowsState) { $arguments += '-MigrateWindowsState' }
        $process = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        [pscustomobject]@{ exitCode = $process.ExitCode; record = @(if (Test-Path -LiteralPath $launchRecord) { Get-Content -LiteralPath $launchRecord }) }
    }
    $launchSite = Join-Path $launchStateRoot 'site'

    $first = Invoke-LaunchHarness
    $firstRuns = @($first.record | Where-Object { $_ -like 'RUN *' })
    Assert-That ($first.exitCode -eq 0) 'Fresh launch succeeds with all seams injected'
    Assert-That ($first.record -contains 'REGISTER') 'A fresh configuration registers the managing app once'
    Assert-That (@($first.record | Where-Object { $_ -eq 'REGISTER' }).Count -eq 1) 'Registration happens exactly once on a fresh launch'
    Assert-That ($firstRuns.Count -eq 1 -and $firstRuns[0] -eq 'RUN docker compose up -d --no-deps --wait tangent') 'A fresh launch starts only the tangent service'
    Assert-That ($first.record -notcontains 'MIGRATE') 'A normal launch never migrates legacy Windows state, even when present'
    Assert-That (-not (Test-Path -LiteralPath (Join-Path $launchSite 'migration.json'))) 'No migration record is written by a normal launch'
    $firstSettings = Get-Content -LiteralPath (Join-Path $launchSite 'appsettings.json') -Raw | ConvertFrom-Json
    Assert-That ($firstSettings.Tangent.Site.OwnerDid -eq '') 'The fresh Docker configuration leaves ownership for the first verified account'
    Assert-That ((Get-FileFingerprint (Join-Path $launchSite 'koan.lock.json')) -eq (Get-FileFingerprint (Join-Path $repoRoot 'src/TangentSpace/koan.lock.json'))) 'The current Koan composition lock is copied into the state directory'
    Assert-That ((Get-Content -LiteralPath (Join-Path $launchSite 'network.json') -Raw | ConvertFrom-Json).networkId -eq $syntheticNetworkId) 'The fixture network marker records the synthetic network id'

    $configBefore = Get-FileFingerprint (Join-Path $launchSite 'appsettings.json')
    $second = Invoke-LaunchHarness
    $secondRuns = @($second.record | Where-Object { $_ -like 'RUN *' })
    Assert-That ($second.exitCode -eq 0) 'Repeat launch succeeds'
    Assert-That ($second.record -notcontains 'REGISTER') 'A repeat launch does not contact the fixture network for registration'
    Assert-That ((Get-FileFingerprint (Join-Path $launchSite 'appsettings.json')) -eq $configBefore) 'A repeat launch retains the configuration byte-for-byte'
    Assert-That ($secondRuns.Count -eq 1 -and $secondRuns[0] -eq 'RUN docker compose up -d --no-deps --wait tangent') 'A repeat launch starts only the tangent service'
    Assert-That ($second.record -notcontains 'MIGRATE') 'A repeat launch never migrates legacy Windows state'

    @{ networkId = 12345 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $launchSite 'network.json') -Encoding utf8
    $mismatch = Invoke-LaunchHarness
    Assert-That ($mismatch.exitCode -eq 5) 'A foreign fixture network marker fails the launch'
    Assert-That (@($mismatch.record | Where-Object { $_ -like 'RUN *' }).Count -eq 0) 'A failed marker validation issues no docker commands'
    Assert-That ((Get-FileFingerprint (Join-Path $launchSite 'appsettings.json')) -eq $configBefore) 'A failed marker validation leaves the configuration untouched'
    @{ networkId = $syntheticNetworkId } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $launchSite 'network.json') -Encoding utf8

    $migrated = Invoke-LaunchHarness -MigrateWindowsState
    Assert-That ($migrated.exitCode -eq 0) 'An explicit -MigrateWindowsState launch succeeds'
    Assert-That ($migrated.record -contains 'MIGRATE') 'The explicit migration seam runs once'
    Assert-That ($migrated.record -notcontains 'REGISTER') 'An explicit migration launch still retains the existing configuration'
    Assert-That (@($migrated.record | Where-Object { $_ -like 'RUN *' })[-1] -eq 'RUN docker compose up -d --no-deps --wait tangent') 'The migration launch still starts only the tangent service'

    $built = Invoke-LaunchHarness -Build
    $builtRuns = @($built.record | Where-Object { $_ -like 'RUN *' })
    Assert-That ($built.exitCode -eq 0) 'A -Build launch succeeds'
    Assert-That ($builtRuns.Count -eq 2 -and $builtRuns[0] -eq 'RUN docker compose build tangent' -and $builtRuns[1] -eq 'RUN docker compose up -d --no-deps --wait tangent') 'A -Build launch builds the image and then starts the tangent service'

    $notReadyFixture = Join-Path $scratch 'fixtures-not-ready.json'
    $starting = $syntheticFixture | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    $starting.status = 'starting'
    $starting | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $notReadyFixture -Encoding utf8
    Remove-Item -LiteralPath $launchRecord -Force -ErrorAction SilentlyContinue
    $notReady = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile', '-File', $harness, '-RepoRoot', $repoRoot, '-StateRoot', $launchStateRoot, '-FixtureFile', $notReadyFixture, '-Record', $launchRecord) -Wait -PassThru -WindowStyle Hidden
    $notReadyRecord = @(if (Test-Path -LiteralPath $launchRecord) { Get-Content -LiteralPath $launchRecord })
    Assert-That ($notReady.ExitCode -eq 5) 'A not-ready fixture fails the launch'
    $notReadyError = @($notReadyRecord | Where-Object { $_ -like 'ERROR *' })
    Assert-That ($notReadyError.Count -eq 1 -and $notReadyError[0] -match 'probes/spaces-network/start.ps1') 'The failure explains how to initialize the disposable Spaces network'
    Assert-That (@($notReadyRecord | Where-Object { $_ -like 'RUN *' -or $_ -eq 'REGISTER' }).Count -eq 0) 'A not-ready fixture issues no docker commands and does not touch the source network'

    # --- 8. Build action dispatch (framework preparation + compose build) ------------------
    $script:BuildRuns = [System.Collections.Generic.List[string]]::new()
    $prepared = $false
    $buildRunner = { param([string[]]$CommandArguments) $script:BuildRuns.Add($CommandArguments -join ' ') }
    Invoke-TangentDockerBuild -RepoRoot $repoRoot -CommandRunner $buildRunner -FrameworkPreparer { param([string]$Root) $script:prepared = $true }
    Assert-That ($prepared) 'Build verifies the pinned framework contribution before building'
    Assert-That ($script:BuildRuns.Count -eq 1 -and $script:BuildRuns[0] -eq 'docker compose build tangent') 'Build builds only the tangent service image'

    Write-Output ''
    Write-Output "Lifecycle test summary: $($script:PassedCount) passed, $($script:FailedCount) failed."
    if ($script:FailedNames.Count -gt 0) { Write-Output ("Failed: " + ($script:FailedNames -join '; ')) }
} finally {
    foreach ($link in $junctionLinks) { Remove-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue }
    $legacyCleanup = [IO.Path]::GetFullPath((Split-Path -Parent $syntheticWindowsState))
    if ($legacyCleanup -ne [IO.Path]::GetFullPath((Join-Path $repoRoot ".local/tangent/$syntheticNetworkId"))) { throw 'Unexpected cleanup path.' }
    if (-not [IO.Path]::GetFullPath($outsideRoot).StartsWith([IO.Path]::GetFullPath($opencodeTemp) + [IO.Path]::DirectorySeparatorChar)) { throw 'Unexpected scratch cleanup path.' }
    if (Test-Path -LiteralPath $syntheticWindowsState) { Remove-Item -LiteralPath (Split-Path -Parent $syntheticWindowsState) -Recurse -Force -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $outsideRoot -Recurse -Force -ErrorAction SilentlyContinue
    if (-not $KeepScratch) {
        # Dogfood the tested wipe to remove the scratch tree itself.
        try { Invoke-TangentWipe -TargetPath $scratch -AllowedRoot $allowedRoot -RepoRoot $repoRoot -CommandRunner { param([string[]]$a) } -Force | Out-Null } catch { }
        if (Test-Path -LiteralPath $scratch) { $safeScratch = Resolve-TangentWipeTarget -TargetPath $scratch -AllowedRoot $allowedRoot; Remove-Item -LiteralPath $safeScratch.absolutePath -Recurse -Force }
    }
}
if ($script:FailedCount -gt 0) { exit 1 }
exit 0
