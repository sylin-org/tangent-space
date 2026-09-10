[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CookieDirectory,
    [string]$Origin = 'http://127.0.0.1:5223',
    [ValidatePattern('^[a-z0-9-]{1,44}$')][string]$Run = ('outage-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()),
    [switch]$PrepareOnly,
    [string]$Evidence = 'docs/evidence/outage.json'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ($Origin -notmatch '^http://127\.0\.0\.1:\d+$') { throw 'This proof accepts only an explicit loopback application origin.' }
$privateDirectory = Join-Path $repo ".local/tangent/$Run"
New-Item -ItemType Directory -Path $privateDirectory -Force | Out-Null
$statePath = Join-Path $privateDirectory 'outage-state.json'
$evidencePath = Join-Path $repo $Evidence
$cookieRoot = if ([IO.Path]::IsPathRooted($CookieDirectory)) { $CookieDirectory } else { Join-Path $repo $CookieDirectory }
$saved = Get-Content -LiteralPath (Join-Path $cookieRoot 'owner.cookies.json') -Raw | ConvertFrom-Json
if ($saved.origin -ne $Origin) { throw 'The owner cookie belongs to a different origin.' }
$cookie = ($saved.cookies | ForEach-Object { $_[0] + '=' + $_[1] }) -join '; '
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(90)
$checks = [Collections.Generic.List[object]]::new()
$stage = 'prepare'
$completed = $false
$networkResumed = $false
$pauseSeconds = $null
$finalHealth = $null
$failure = $null
$state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else {
    [pscustomobject]@{ run = $Run; origin = $Origin; room = "a-$Run"; operationId = 'same-operation-through-outage';
        text = "An operation that survives the real PDS outage ($Run)."; seed = $null; prepared = $false; recovered = $null }
}
if ($state.origin -ne $Origin) { throw 'The private checkpoint belongs to another origin.' }

function Check([string]$Name, [bool]$Passed, $Observed) {
    $checks.Add([ordered]@{ name = $Name; passed = $Passed; observed = $Observed })
    if (-not $Passed) { throw "Outage proof check failed: $Name" }
}
function Save-State { $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8 }
function Begin-Request([string]$Method, [string]$Path, $Body = $null) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), $Origin + $Path)
    $request.Headers.Add('Cookie', $cookie)
    $request.Headers.Add('Origin', $Origin)
    $request.Headers.Add('Accept', 'application/json')
    if ($null -ne $Body) { $request.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 5 -Compress), [Text.Encoding]::UTF8, 'application/json') }
    [pscustomobject]@{ request = $request; task = $client.SendAsync($request) }
}
function Finish-Request($Pending) {
    $response = $null
    try {
        $response = $Pending.task.GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ([Text.Encoding]::UTF8.GetByteCount($text) -gt 256KB) { throw 'Bounded proof response exceeded 256 KiB.' }
        $json = $null
        try { if ($text) { $json = $text | ConvertFrom-Json } } catch { }
        [pscustomobject]@{ status = [int]$response.StatusCode; json = $json; noStore = $response.Headers.CacheControl.NoStore }
    } finally { if ($response) { $response.Dispose() }; $Pending.request.Dispose() }
}
function Send([string]$Method, [string]$Path, $Body = $null) { Finish-Request (Begin-Request $Method $Path $Body) }
function Resume-Network {
    $paused = docker inspect -f '{{.State.Paused}}' tangent-spaces-network
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the disposable network during cleanup.' }
    if ($paused.Trim() -eq 'true') {
        docker unpause tangent-spaces-network | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Disposable network unpause failed; manual unpause is required.' }
    }
    $verified = docker inspect -f '{{.State.Paused}}' tangent-spaces-network
    if ($LASTEXITCODE -ne 0 -or $verified.Trim() -ne 'false') { throw 'The disposable network did not confirm resumed state.' }
}

try {
    $roomPath = '/api/rooms/' + $state.room
    if (-not $state.prepared) {
        $created = Send 'POST' '/api/rooms' @{ key = $state.room; title = 'Bounded PDS outage proof'; admission = 'InvitationOnly' }
        Check 'Create an isolated outage room' ($created.status -eq 200 -and $created.json.accepted) @{ status = $created.status }
        $provisioned = Send 'POST' "$roomPath/provision" @{}
        Check 'Provision through the restored real authority session' ($provisioned.status -eq 200 -and $provisioned.json.accepted) @{ status = $provisioned.status }
        $seed = Send 'POST' "$roomPath/messages" @{ operationId = 'seed'; text = 'This accepted message remains readable during the PDS outage.' }
        Check 'Preload the owner session with a real source-backed message' ($seed.status -eq 200 -and $seed.json.state -eq 'accepted') @{ status = $seed.status; state = $seed.json.state }
        $state.seed = [pscustomobject]@{ uri = $seed.json.sourceUri; cid = $seed.json.sourceCid }
        $state.prepared = $true
        Save-State
    }
    $before = Send 'GET' "$roomPath/messages"
    Check 'The local projection contains the source-backed seed' ($before.status -eq 200 -and @($before.json.messages).Count -eq 1 -and $before.json.messages[0].sourceCid -eq $state.seed.cid) @{ status = $before.status; messages = @($before.json.messages).Count }
    if ($PrepareOnly) { $completed = $true }
    else {
        $stage = 'pause-real-pds-network'
        $running = docker inspect -f '{{.State.Running}} {{.State.Paused}}' tangent-spaces-network
        Check 'The disposable PDS network is running before the experiment' ($LASTEXITCODE -eq 0 -and $running.Trim() -eq 'true false') @{ running = $running.Trim() }
        # The caller must coordinate this window with other active UI/protocol work.
        docker pause tangent-spaces-network | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not pause the disposable PDS network.' }
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        try {
            $paused = docker inspect -f '{{.State.Paused}}' tangent-spaces-network
            Check 'The actual PDS network is paused' ($LASTEXITCODE -eq 0 -and $paused.Trim() -eq 'true') @{ paused = $true }
            $stage = 'cached-read-and-outage-requests'
            # Independent requests run concurrently so the network pause stays near 35 seconds.
            $pendingPost = Begin-Request 'POST' "$roomPath/messages" @{ operationId = $state.operationId; text = $state.text }
            $pendingSync = Begin-Request 'POST' "$roomPath/sync" @{}
            $cached = Send 'GET' "$roomPath/messages"
            Check 'Authorized cached history remains readable while the PDS is paused' ($cached.status -eq 200 -and $cached.noStore -and @($cached.json.messages).Count -eq 1 -and $cached.json.messages[0].sourceCid -eq $state.seed.cid) @{ status = $cached.status; messages = @($cached.json.messages).Count; noStore = $cached.noStore }
            $null = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($pendingPost.task, $pendingSync.task)).WaitAsync([TimeSpan]::FromSeconds(40)).GetAwaiter().GetResult()
            $postResult = Finish-Request $pendingPost
            $syncResult = Finish-Request $pendingSync
            Check 'A timed-out source write returns a durable pending operation' ($postResult.status -eq 202 -and $postResult.json.state -eq 'pending' -and $postResult.json.operationId -eq $state.operationId) @{ status = $postResult.status; state = $postResult.json.state; detail = $postResult.json.detail }
            Check 'Reconciliation reports unavailable freshness during the actual outage' ($syncResult.status -eq 200 -and $syncResult.json.freshness -eq 'unavailable') @{ status = $syncResult.status; freshness = $syncResult.json.freshness }
            if ($stopwatch.Elapsed.TotalSeconds -lt 35) { Start-Sleep -Milliseconds ([int](35000 - $stopwatch.Elapsed.TotalMilliseconds)) }
        } finally {
            Resume-Network
            $networkResumed = $true
            $stopwatch.Stop()
            $pauseSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        }
        $stage = 'background-recovery'
        Check 'The original PDS network is resumed without a restart' $networkResumed @{ resumed = $networkResumed; pausedSeconds = $pauseSeconds }
        # Observe only local GETs: no manual sync or post can cause this first recovery.
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $observed = $null
        do {
            $page = Send 'GET' "$roomPath/messages"
            $matches = @($page.json.messages | Where-Object { $_.content.text -eq $state.text })
            if ($page.status -eq 200 -and $matches.Count -eq 1) { $observed = $matches[0]; break }
            Start-Sleep -Seconds 3
        } while ($watch.Elapsed.TotalSeconds -lt 150)
        Check 'The periodic worker recovers the pending source without a manual write or sync' ($null -ne $observed) @{ observed = $null -ne $observed; waitSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2); observationRequests = 'local history GET only'; modelExecution = 'No model runtime is configured or invoked by this script or the periodic worker.' }
        $state.recovered = [pscustomobject]@{ uri = $observed.sourceUri; cid = $observed.sourceCid }
        Save-State
        $stage = 'same-operation-retries'
        $retry = Send 'POST' "$roomPath/messages" @{ operationId = $state.operationId; text = $state.text }
        $repeat = Send 'POST' "$roomPath/messages" @{ operationId = $state.operationId; text = $state.text }
        Check 'Retrying the identical operation returns the recovered real source' ($retry.status -eq 200 -and $retry.json.state -eq 'accepted' -and $retry.json.sourceUri -eq $state.recovered.uri -and $retry.json.sourceCid -eq $state.recovered.cid) @{ status = $retry.status; state = $retry.json.state }
        Check 'A subsequent retry preserves the same source URI and CID' ($repeat.status -eq 200 -and $repeat.json.sourceUri -eq $retry.json.sourceUri -and $repeat.json.sourceCid -eq $retry.json.sourceCid) @{ status = $repeat.status; sameSource = $repeat.json.sourceCid -eq $retry.json.sourceCid }
        $after = Send 'GET' "$roomPath/messages"
        $versions = @($after.json.messages | Where-Object sourceUri -eq $state.recovered.uri)
        Check 'Recovered history contains one source version and no duplicate message' ($after.status -eq 200 -and @($after.json.messages).Count -eq 2 -and $versions.Count -eq 1 -and $versions[0].sourceCid -eq $state.recovered.cid) @{ totalMessages = @($after.json.messages).Count; recoveredVersions = $versions.Count }
        $completed = $true
    }
} catch {
    $failure = if ($_.Exception.Message.StartsWith('Outage proof check failed:')) { $_.Exception.Message } else { $_.Exception.GetType().Name }
    $checks.Add([ordered]@{ name = 'Proof completion'; passed = $false; observed = @{ stage = $stage; error = $failure } })
} finally {
    # Covers failures before the inner cleanup is entered as well as assertion failures.
    if (-not $PrepareOnly) {
        Resume-Network; $networkResumed = $true
        $containerState = docker inspect -f '{{.State.Running}} {{.State.Paused}} {{.RestartCount}}' tangent-spaces-network
        try {
            $networkHealth = Invoke-RestMethod 'http://localhost:2585/health' -TimeoutSec 10
            $appHealth = Invoke-RestMethod "$Origin/health/ready" -TimeoutSec 10
            $finalHealth = @{ container = $containerState.Trim(); network = $networkHealth.status; application = $appHealth.status }
        } catch { $finalHealth = @{ container = $containerState.Trim(); healthRequestError = $_.Exception.GetType().Name } }
    }
    $client.Dispose()
    Save-State
    New-Item -ItemType Directory -Path (Split-Path -Parent $evidencePath) -Force | Out-Null
    [ordered]@{
        date = [DateTimeOffset]::UtcNow.ToString('o'); passed = $completed; origin = $Origin; run = $Run; room = $state.room
        mode = if ($PrepareOnly) { 'preparation-only' } else { 'real-pds-network-outage' }
        networkResumed = $networkResumed; pauseSeconds = $pauseSeconds; finalHealth = $finalHealth; secretsIncluded = $false; checks = $checks
        limits = @('Actual docker pause/unpause of the existing disposable PDS network; no fabricated credentials, firewall rules, source fault switches or network restart.',
            'This outage may occur before the PDS accepts the write. It does not by itself prove failure after a remote commit; the earlier S06 pending/read-back case covers that separate condition.',
            'Periodic recovery is observed through local history GETs before any manual retry or sync. No model execution is part of this path.')
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $evidencePath -Encoding utf8
}
[pscustomobject]@{ passed = $completed; checks = $checks.Count; evidence = $evidencePath; checkpoint = $statePath; networkResumed = $networkResumed } | ConvertTo-Json
if (-not $completed) { throw "Outage proof incomplete at $stage ($failure). The network cleanup ran." }
