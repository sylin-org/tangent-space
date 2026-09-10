[CmdletBinding()]
param(
    [int]$Port = 5223,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Instance = 'rooms-proof',
    [ValidatePattern('^[a-z0-9-]{1,40}$')][string]$RoomPrefix = ('poc-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$origin = "http://127.0.0.1:$Port"
$fixturePath = Join-Path $repoRoot '.local/spaces-network/fixtures.json'
$fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
$networkId = ([DateTimeOffset]$fixture.startedAt).ToUnixTimeMilliseconds()
$stateDirectory = Join-Path $repoRoot ".local/tangent/$networkId/$Instance"
$cookieDirectory = Join-Path $stateDirectory "room-proof-$RoomPrefix"
New-Item -ItemType Directory -Path $cookieDirectory -Force | Out-Null
$evidencePath = Join-Path $repoRoot 'docs/evidence/rooms.json'
$checks = [Collections.Generic.List[object]]::new()
$auditIds = [Collections.Generic.HashSet[string]]::new()
$accounts = @{}
foreach ($account in $fixture.accounts) { $accounts[$account.role] = $account }
$lounge = "$RoomPrefix-lounge"
$workshop = "$RoomPrefix-workshop"
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(45)
$completed = $false

function Check([string]$Name, [bool]$Passed, $Observed) {
    $checks.Add([ordered]@{ name = $Name; passed = $Passed; observed = $Observed })
    if (-not $Passed) { throw "Room proof failed: $Name. No credentials or response bodies were written to evidence." }
}
function Cookie-Path([string]$Role) { Join-Path $cookieDirectory "$Role.cookies.json" }
function Login([string]$Role, [string]$Connection = '') {
    $cookiePath = Cookie-Path $Role
    $arguments = @('probes/scripts/oauth-flow.mjs', '--accounts', $fixturePath, '--account', $Role,
        '--web', '--probe', $origin, '--use-handle', '--cookie-file', $cookiePath)
    if ($Connection) {
        $initial = if ($Connection -eq 'authority') { Cookie-Path 'owner' } else { $cookiePath }
        $arguments += @('--start-path', "/api/connections/$Connection", '--initial-cookies', $initial)
    }
    $output = & node @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $result = ($output -join "`n") | ConvertFrom-Json
    Check "Real OAuth: $Role $Connection" ($exitCode -eq 0 -and $result.passed) @{ account = $Role; connection = $Connection; phases = $result.phases }
}
function Send([string]$Role, [string]$Method, [string]$Path, $Body = $null,
    [string]$RequestOrigin = $origin, [string]$ContentType = 'application/json', [string]$Authorization = '') {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), $origin + $Path)
    $request.Headers.Add('Accept', 'application/json')
    if ($Role) {
        $saved = Get-Content -LiteralPath (Cookie-Path $Role) -Raw | ConvertFrom-Json
        if ($saved.origin -ne $origin) { throw 'The saved cookie belongs to a different loopback origin.' }
        $request.Headers.Add('Cookie', (($saved.cookies | ForEach-Object { $_[0] + '=' + $_[1] }) -join '; '))
    }
    if ($RequestOrigin) { $request.Headers.Add('Origin', $RequestOrigin) }
    if ($Authorization) { $request.Headers.Add('Authorization', $Authorization) }
    if ($null -ne $Body) { $request.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, $ContentType) }
    $response = $null
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $json = $null
        try { if ($text) { $json = $text | ConvertFrom-Json } } catch { }
        if ($json.auditId) { $auditIds.Add([string]$json.auditId) | Out-Null }
        return [pscustomobject]@{ status = [int]$response.StatusCode; json = $json }
    }
    finally { if ($response) { $response.Dispose() }; $request.Dispose() }
}
function Member-Path([string]$Room, [string]$Role) { "/api/rooms/$Room/members/" + [Uri]::EscapeDataString($accounts[$Role].did) }
function Suspension-Path([string]$Role) { '/api/site/participants/' + [Uri]::EscapeDataString($accounts[$Role].did) + '/suspension' }
function Accept([string]$Name, $Response) { Check $Name ($Response.status -eq 200 -and $Response.json.accepted) @{ status = $Response.status; policyRevision = $Response.json.policyRevision; audited = [bool]$Response.json.auditId } }
function Deny([string]$Name, $Response) { Check $Name ($Response.status -eq 403 -and -not $Response.json.accepted -and $Response.json.auditId) @{ status = $Response.status; denial = $Response.json.denial; audited = [bool]$Response.json.auditId } }

Push-Location $repoRoot
try {
    Invoke-RestMethod "$origin/health/ready" -TimeoutSec 5 | Out-Null
    foreach ($role in @('owner', 'manager', 'agent', 'outsider')) { Login $role }
    foreach ($role in @('owner', 'manager', 'agent', 'outsider')) { Login $role 'rooms' }
    Login 'authority' 'authority'
    $welcome = Send 'owner' 'GET' '/api/site'
    Check 'The configured verified owner is established' ($welcome.status -eq 200 -and $welcome.json.participant.isOwner) @{ established = $welcome.json.established; isOwner = $welcome.json.participant.isOwner }

    $unsigned = Send '' 'GET' '/xrpc/com.atproto.simplespace.checkUserAccess?space=unknown&user=unknown'
    Check 'Unsigned managing-app callback is rejected' ($unsigned.status -eq 401) @{ status = $unsigned.status }
    Deny 'A non-owner cannot create a room' (Send 'manager' 'POST' '/api/rooms' @{ key = "$RoomPrefix-denied"; title = 'Denied'; admission = 'SignedIn' })
    $createdLounge = Send 'owner' 'POST' '/api/rooms' @{ key = $lounge; title = 'PoC Lounge'; admission = 'SignedIn' }
    Accept 'The owner creates Lounge' $createdLounge
    $createdWorkshop = Send 'owner' 'POST' '/api/rooms' @{ key = $workshop; title = 'PoC Workshop'; admission = 'InvitationOnly' }
    Accept 'The owner creates Workshop' $createdWorkshop
    $pending = Send 'owner' 'GET' "/api/rooms/$workshop"
    Check 'A new room remains pending before real provisioning' ($pending.json.spaceState -eq 'Pending' -and -not $pending.json.canRead -and -not $pending.json.canWrite) @{ spaceState = $pending.json.spaceState; canRead = $pending.json.canRead; canWrite = $pending.json.canWrite }

    $noOrigin = Send 'owner' 'PUT' "/api/rooms/$workshop/topic" @{ topic = 'Rejected' } ''
    $crossOrigin = Send 'owner' 'PUT' "/api/rooms/$workshop/topic" @{ topic = 'Rejected' } 'https://other.example'
    $notJson = Send 'owner' 'PUT' "/api/rooms/$workshop/topic" @{ topic = 'Rejected' } $origin 'text/plain'
    $bearer = Send 'owner' 'PUT' "/api/rooms/$workshop/topic" @{ topic = 'Rejected' } $origin 'application/json' 'Bearer ts_invalid'
    Check 'Cookie mutations require same-origin JSON and cannot fall back from a bearer header' ($noOrigin.status -eq 403 -and $crossOrigin.status -eq 403 -and $notJson.status -eq 415 -and $bearer.status -in @(401, 403)) @{ missingOrigin = $noOrigin.status; foreignOrigin = $crossOrigin.status; nonJson = $notJson.status; bearerHeader = $bearer.status }

    Deny 'A manager cannot provision a room before delegation' (Send 'manager' 'POST' "/api/rooms/$workshop/provision" @{})
    $readyLounge = Send 'owner' 'POST' "/api/rooms/$lounge/provision" @{}
    Accept 'Real authority OAuth provisions Lounge' $readyLounge
    $readyWorkshop = Send 'owner' 'POST' "/api/rooms/$workshop/provision" @{}
    Accept 'Real authority OAuth provisions Workshop' $readyWorkshop
    Check 'Ready mappings identify two distinct verified Spaces' ($readyLounge.json.spaceUri -eq "at://$($accounts.authority.did)/space/local.tangent.room/$lounge" -and $readyWorkshop.json.spaceUri -eq "at://$($accounts.authority.did)/space/local.tangent.room/$workshop") @{ distinct = $readyLounge.json.spaceUri -ne $readyWorkshop.json.spaceUri }
    $retry = Send 'owner' 'POST' "/api/rooms/$workshop/provision" @{}
    Check 'Provisioning retry reconciles the same Space without changing room revision' ($retry.status -eq 200 -and $retry.json.spaceUri -eq $readyWorkshop.json.spaceUri -and $retry.json.policyRevision -eq $readyWorkshop.json.policyRevision) @{ status = $retry.status; policyRevision = $retry.json.policyRevision }

    Accept 'The owner delegates Workshop administration' (Send 'owner' 'PUT' (Member-Path $workshop 'manager') @{ role = 'Manager' })
    Accept 'The manager admits the agent' (Send 'manager' 'PUT' (Member-Path $workshop 'agent') @{ role = 'Member' })
    $agent = Send 'agent' 'GET' "/api/rooms/$workshop"
    Check 'An admitted agent can read and write' ($agent.json.canRead -and $agent.json.canWrite -and -not $agent.json.canManage) @{ canRead = $agent.json.canRead; canWrite = $agent.json.canWrite; canManage = $agent.json.canManage }
    $outsideWorkshop = Send 'outsider' 'GET' "/api/rooms/$workshop"
    $outsideLounge = Send 'outsider' 'GET' "/api/rooms/$lounge"
    Check 'The outsider sees both descriptions but only Lounge admits content' ($outsideWorkshop.status -eq 200 -and -not $outsideWorkshop.json.canRead -and $outsideLounge.json.canRead -and $outsideLounge.json.canWrite) @{ workshop = $outsideWorkshop.json.accessState; lounge = $outsideLounge.json.accessState }
    Accept 'The manager changes the topic' (Send 'manager' 'PUT' "/api/rooms/$workshop/topic" @{ topic = 'Persistent identity and conversation' })
    Deny 'A manager cannot appoint another manager' (Send 'manager' 'PUT' (Member-Path $workshop 'agent') @{ role = 'Manager' })
    Accept 'The owner can appoint a second manager' (Send 'owner' 'PUT' (Member-Path $workshop 'outsider') @{ role = 'Manager' })
    Deny 'A manager cannot demote another manager' (Send 'manager' 'PUT' (Member-Path $workshop 'outsider') @{ role = 'Removed' })
    Accept 'The owner restores the outsider to denied membership' (Send 'owner' 'PUT' (Member-Path $workshop 'outsider') @{ role = 'Removed' })
    Deny 'A manager cannot remove the owner' (Send 'manager' 'PUT' (Member-Path $workshop 'owner') @{ role = 'Removed' })
    Deny 'A manager cannot change admission policy' (Send 'manager' 'PUT' "/api/rooms/$workshop/admission" @{ admission = 'SignedIn' })

    Accept 'The manager restricts the agent to reader' (Send 'manager' 'PUT' (Member-Path $workshop 'agent') @{ role = 'Reader' })
    $reader = Send 'agent' 'GET' "/api/rooms/$workshop"
    Check 'Current reader policy allows reads and denies writes' ($reader.json.canRead -and -not $reader.json.canWrite) @{ canRead = $reader.json.canRead; canWrite = $reader.json.canWrite; revision = $reader.json.policyRevision }
    Accept 'The manager removes the agent' (Send 'manager' 'PUT' (Member-Path $workshop 'agent') @{ role = 'Removed' })
    $removed = Send 'agent' 'GET' "/api/rooms/$workshop"
    Check 'Removal changes the next request without replacing the cookie' (-not $removed.json.canRead -and -not $removed.json.canWrite -and $removed.json.policyRevision -gt $reader.json.policyRevision) @{ canRead = $removed.json.canRead; canWrite = $removed.json.canWrite; revision = $removed.json.policyRevision }
    Accept 'The owner explicitly removes the agent from signed-in Lounge' (Send 'owner' 'PUT' (Member-Path $lounge 'agent') @{ role = 'Removed' })
    $removedPublic = Send 'agent' 'GET' "/api/rooms/$lounge"
    Check 'Removal overrides signed-in admission' (-not $removedPublic.json.canRead -and $removedPublic.json.accessState -eq 'removed') @{ admission = $removedPublic.json.admission; accessState = $removedPublic.json.accessState }

    Accept 'The manager restores Workshop membership' (Send 'manager' 'PUT' (Member-Path $workshop 'agent') @{ role = 'Member' })
    Accept 'The owner suspends the agent site-wide' (Send 'owner' 'PUT' (Suspension-Path 'agent') @{ suspended = $true })
    $suspended = Send 'agent' 'GET' "/api/rooms/$workshop"
    Check 'Suspension overrides retained membership on the next request' (-not $suspended.json.canRead -and $suspended.json.accessState -eq 'suspended') @{ canRead = $suspended.json.canRead; sitePolicyRevision = $suspended.json.sitePolicyRevision }
    Accept 'The owner restores the agent' (Send 'owner' 'PUT' (Suspension-Path 'agent') @{ suspended = $false })
    $restored = Send 'agent' 'GET' "/api/rooms/$workshop"
    Check 'Restoration preserves the existing room grant and advances site revision' ($restored.json.canRead -and $restored.json.canWrite -and $restored.json.policyRevision -eq $suspended.json.policyRevision -and $restored.json.sitePolicyRevision -gt $suspended.json.sitePolicyRevision) @{ canRead = $restored.json.canRead; canWrite = $restored.json.canWrite; sitePolicyRevision = $restored.json.sitePolicyRevision }
    Accept 'The owner suspends the manager' (Send 'owner' 'PUT' (Suspension-Path 'manager') @{ suspended = $true })
    Deny 'A suspended manager cannot administer rooms' (Send 'manager' 'PUT' "/api/rooms/$workshop/topic" @{ topic = 'Rejected suspended authority' })
    Accept 'The owner restores the manager' (Send 'owner' 'PUT' (Suspension-Path 'manager') @{ suspended = $false })
    Accept 'The owner demotes the manager' (Send 'owner' 'PUT' (Member-Path $workshop 'manager') @{ role = 'Member' })
    Deny 'A demoted manager cannot reuse cookie authority' (Send 'manager' 'PUT' "/api/rooms/$workshop/topic" @{ topic = 'Rejected stale authority' })
    Deny 'A non-owner cannot change site suspension' (Send 'manager' 'PUT' (Suspension-Path 'agent') @{ suspended = $true })
    Accept 'The owner can change room admission' (Send 'owner' 'PUT' "/api/rooms/$lounge/admission" @{ admission = 'SignedIn' })

    $database = Join-Path $stateDirectory 'tangent.sqlite'
    $auditProbe = @'
import json, sqlite3, sys
ids = json.loads(sys.argv[2])
db = sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True)
rows = db.execute('select "Json" from "TangentSpace.Rooms.RoomAudit" where "Id" in (' + ','.join('?' for _ in ids) + ')', ids).fetchall()
audits = [json.loads(row[0]) for row in rows]
def field(item, name): return item.get(name, item.get(name[0].lower()+name[1:]))
print(json.dumps({'requested': len(ids), 'persisted': len(audits),
 'accepted': sum(field(a, 'Accepted') is True for a in audits),
 'denied': sum(field(a, 'Accepted') is False for a in audits),
 'attributed': all(field(a, 'ActorDid') and field(a, 'Operation') is not None and field(a, 'OccurredAt') for a in audits),
 'hasSiteRevision': all(field(a, 'SitePolicyRevision') >= 1 for a in audits)}))
'@
    $auditSummary = (& python -c $auditProbe $database (ConvertTo-Json -InputObject @($auditIds) -Compress)) | ConvertFrom-Json
    Check 'Accepted and denied administrative receipts are durably attributed in SQLite' ($LASTEXITCODE -eq 0 -and $auditSummary.requested -eq $auditSummary.persisted -and $auditSummary.accepted -gt 0 -and $auditSummary.denied -ge 7 -and $auditSummary.attributed -and $auditSummary.hasSiteRevision) $auditSummary
    $completed = $true
}
finally {
    Pop-Location
    $client.Dispose()
    New-Item -ItemType Directory -Path (Split-Path -Parent $evidencePath) -Force | Out-Null
    [ordered]@{
        observedAt = [DateTimeOffset]::UtcNow.ToString('o'); passed = $completed
        roomKeys = @($lounge, $workshop); checks = $checks
        limits = @('Disposable loopback accounts and real OAuth/Spaces provisioning',
            'HTTP permissions and durable audit verified; this script does not fetch conversation records or signed Space credentials',
            'Existing running host retained; process restart is covered by separate integration evidence',
            'Cookies and credentials remain under ignored .local and are absent from this receipt')
    } | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $evidencePath -Encoding utf8
}
Write-Output "Passed $($checks.Count) room checks. Redacted receipt: $evidencePath"
