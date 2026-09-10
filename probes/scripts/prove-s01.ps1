param(
    [string]$Fixture = '.local/spaces-network/fixtures.json',
    [string]$Probe = 'http://127.0.0.1:5180',
    [string]$Evidence = 'probes/evidence/s01-native.json'
)
$ErrorActionPreference = 'Stop'
$fixtureData = Get-Content -LiteralPath $Fixture -Raw | ConvertFrom-Json
$accounts = @{}
foreach ($account in $fixtureData.accounts) { $accounts[$account.role] = $account }
$checks = [System.Collections.Generic.List[object]]::new()
$runKey = 'native-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()

function Check([string]$Name, [bool]$Passed, $Observed) {
    $checks.Add([ordered]@{ name = $Name; passed = $Passed; observed = $Observed })
    if (-not $Passed) { throw "S01 failed: $Name. See redacted receipt." }
}
function Rpc($Body, [string]$Path = '/xrpc') {
    Invoke-RestMethod -Uri ($Probe + $Path) -Method Post -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 20 -Compress)
}
function Login([string]$Role, [string]$Scope = 'atproto', [string]$Negative = '') {
    $arguments = @('probes/scripts/oauth-flow.mjs', '--accounts', $Fixture, '--account', $Role, '--scope', $Scope)
    if ($Negative) { $arguments += @('--negative', $Negative) } else { $arguments += '--verify-replay' }
    $output = & node @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $result = ($output -join "`n") | ConvertFrom-Json
    Check "OAuth $Role $Negative" ($exitCode -eq 0 -and $result.passed) $result
}

try {
    Login 'owner'
    Login 'owner' 'atproto' 'issuer'
    Login 'owner' 'atproto' 'state'
    $type = $fixtureData.spaceType
    $collection = $fixtureData.collection
    $authority = $accounts.authority.did
    $room = $fixtureData.rooms.workshop
    $readScope = "atproto space:${type}?authority=$authority&action=read"
    Login 'authority' "atproto space:${type}?authority=self&action=read_self&manage=create"
    Login 'agent' ($readScope + "&action=create&collection=$collection")
    Login 'manager' $readScope
    Login 'outsider' $readScope

    $created = Rpc @{
        did = $authority; nsid = 'com.atproto.simplespace.createSpace'; method = 'POST'
        body = @{ type = $type; skey = $runKey
            policy = @{ '$type' = 'com.atproto.simplespace.defs#publicPolicy' }
            appAccess = @{ '$type' = 'com.atproto.simplespace.defs#open' } }
    }
    Check 'Native OAuth creates a Space under authority DID' ($created.status -eq 200 -and $created.body.uri -eq "at://$authority/space/$type/$runKey") $created

    $record = @{ '$type' = $collection; text = 'A native OAuth participant wrote this message.'; createdAt = [DateTimeOffset]::UtcNow.ToString('o') }
    $write = Rpc @{
        did = $accounts.agent.did; nsid = 'com.atproto.space.createRecord'; method = 'POST'
        body = @{ space = $room; repo = $accounts.agent.did; collection = $collection; rkey = $runKey; record = $record }
    }
    Check 'Native OAuth creates participant-owned source record on PDS2' ($write.status -eq 200 -and $write.body.cid) $write
    $readInput = @{ did = $accounts.manager.did; space = $room; repo = $accounts.agent.did; parameters = @{ collection = $collection; rkey = $runKey } }
    $read = Rpc $readInput '/spaces/read'
    Check 'PDS1 participant reads PDS2 source through OAuth delegation and SpaceCredential' ($read.response.status -eq 200 -and $read.response.body.cid -eq $write.body.cid -and $read.response.body.value.text -eq $record.text) $read

    $readInput.did = $accounts.outsider.did
    $denial = Rpc $readInput '/spaces/read'
    Check 'Outsider denied SpaceCredential despite OAuth read grant' ($denial.stage -eq 'credential' -and $denial.response.status -ge 400 -and $denial.response.body.error -eq 'UserNotAuthorized') $denial
    $readInput.did = $accounts.owner.did
    $leastScope = Rpc $readInput '/spaces/read'
    Check 'Plain sign-in scope cannot request room delegation' ($leastScope.stage -eq 'delegation' -and $leastScope.response.status -eq 403) $leastScope

    $refresh = Rpc @{ did = $accounts.agent.did; expireForProbe = $true } '/sessions/refresh'
    Check 'Real refresh token rotates the access token' ($refresh.refreshed -eq $true) $refresh
    $readInput.did = $accounts.agent.did
    $afterRefresh = Rpc $readInput '/spaces/read'
    Check 'Refreshed session retains native Spaces access' ($afterRefresh.response.status -eq 200 -and $afterRefresh.response.body.cid -eq $write.body.cid) $afterRefresh

    $repoRead = Rpc @{ did = $accounts.manager.did; space = $room; repo = $accounts.agent.did; nsid = 'com.atproto.space.getRepo' } '/spaces/read'
    Check 'Spaces CAR transport works while native parser gap remains explicit' ($repoRead.response.status -eq 200 -and $repoRead.response.carRoots -eq 2 -and $repoRead.response.verified -eq $false) $repoRead
}
finally {
    $parentDirectory = Split-Path -Parent $Evidence
    if ($parentDirectory) { New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null }
    [ordered]@{
        observedAt = [DateTimeOffset]::UtcNow.ToString('o')
        upstreamRevision = $fixtureData.revision
        sdk = 'CarpaNet.OAuth/1.1.0-alpha.5'
        passed = $checks.Count -eq 15 -and @($checks | Where-Object { -not $_.passed }).Count -eq 0
        checks = $checks
        limits = @('Disposable loopback PDS network', 'HTTP issuer API driver, not browser rendering', 'PDS custom record validation may be unknown', 'Spaces CAR integrity verification is not implemented by the selected SDK', 'Application cookie, governance and unattended credentials are later stories')
    } | ConvertTo-Json -Depth 24 | Set-Content -LiteralPath $Evidence -Encoding utf8
}
Write-Output "Passed $($checks.Count) S01 native checks. Redacted receipt: $Evidence"
