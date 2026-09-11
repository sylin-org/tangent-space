[CmdletBinding()]
param([int]$Port = 5222)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$instance = 'arrival-proof-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$checks = [System.Collections.Generic.List[object]]::new()
$hostInfo = $null
$failureProcess = $null
$origin = "http://127.0.0.1:$Port"
$evidencePath = Join-Path $repoRoot 'docs/evidence/arrival.json'

function Check([string]$Name, [bool]$Passed, $Observed) {
    $checks.Add([ordered]@{ name = $Name; passed = $Passed; observed = $Observed })
    if (-not $Passed) { throw "Arrival proof failed: $Name." }
}
function Wait-Host($Info) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (-not (Get-Process -Id $Info.pid -ErrorAction SilentlyContinue)) { throw 'The proof host exited before becoming ready.' }
        try { Invoke-RestMethod "$origin/health/ready" -TimeoutSec 2 | Out-Null; return } catch { Start-Sleep -Milliseconds 250 }
    }
    throw 'The proof host did not become ready.'
}
function Stop-Host($Info) {
    if ($null -eq $Info) { return }
    $running = Get-CimInstance Win32_Process -Filter "ProcessId = $($Info.pid)"
    if ($running -and $running.CommandLine.Contains($Info.stateDirectory) -and $running.CommandLine.Contains('TangentSpace.dll')) {
        Stop-Process -Id $Info.pid
        Wait-Process -Id $Info.pid -ErrorAction SilentlyContinue
    }
}
function Cookie-Headers([string]$Role) {
    $cookie = Get-Content -LiteralPath (Join-Path $hostInfo.stateDirectory "$Role.cookies.json") -Raw | ConvertFrom-Json
    @{ Cookie = (($cookie.cookies | ForEach-Object { $_[0] + '=' + $_[1] }) -join '; ') }
}
function Welcome([string]$Role = '') {
    if ($Role) { return Invoke-RestMethod "$origin/api/site" -Headers (Cookie-Headers $Role) -TimeoutSec 10 }
    Invoke-RestMethod "$origin/api/site" -TimeoutSec 10
}
function Login([string]$Role, [string]$Negative = '') {
    $arguments = @('probes/scripts/oauth-flow.mjs', '--accounts', '.local/spaces-network/fixtures.json', '--account', $Role,
        '--web', '--probe', $origin, '--use-handle')
    if ($Negative) { $arguments += @('--negative', $Negative) }
    else { $arguments += @('--verify-replay', '--cookie-file', (Join-Path $hostInfo.stateDirectory "$Role.cookies.json")) }
    $output = & node @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $result = ($output -join "`n") | ConvertFrom-Json
    Check "Real Koan sign-in: $Role $Negative" ($exitCode -eq 0 -and $result.passed) $result
}

try {
    $portProbe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try { $portProbe.Start() } finally { $portProbe.Stop() }
    $hostInfo = & "$PSScriptRoot/run-local.ps1" -Port $Port -NoBuild -Background -Instance $instance
    Wait-Host $hostInfo
    $anonymous = Welcome
    Check 'A fresh site has no established owner or participant' (-not $anonymous.established -and -not $anonymous.participant) $anonymous

    Login 'manager'
    $visitor = Welcome 'manager'
    Check 'First visitor is a participant and cannot bootstrap ownership' (-not $visitor.established -and $visitor.participant.did -and -not $visitor.participant.isOwner) $visitor

    Login 'owner'
    $before = Welcome 'owner'
    Check 'Only the configured verified account establishes ownership' ($before.established -and $before.participant.isOwner) $before
    Login 'agent'
    $agent = Welcome 'agent'
    Check 'An account on the second PDS arrives without owner privileges' ($agent.established -and $agent.participant.did -and -not $agent.participant.isOwner) $agent
    Login 'owner' 'issuer'
    Login 'owner' 'state'
    Login 'owner' 'correlation'
    Login 'owner'
    $returned = Welcome 'owner'
    Check 'Repeated sign-in retains participant DID and original join time' ($returned.participant.did -eq $before.participant.did -and $returned.participant.joinedAt -eq $before.participant.joinedAt) $returned

    $beforeProcess = $hostInfo.pid
    Stop-Host $hostInfo
    $hostInfo = & "$PSScriptRoot/run-local.ps1" -Port $Port -NoBuild -Background -Instance $instance
    Wait-Host $hostInfo
    $restored = Welcome 'owner'
    Check 'A new process accepts the protected cookie and persisted owner/participant' ($hostInfo.pid -ne $beforeProcess -and $restored.participant.did -eq $before.participant.did -and $restored.participant.joinedAt -eq $before.participant.joinedAt -and $restored.participant.isOwner) $restored

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$origin/auth/logout?return=/api/site")
    $request.Headers.Add('Cookie', (Cookie-Headers 'owner').Cookie)
    try {
        $logout = $client.SendAsync($request).GetAwaiter().GetResult()
        $expired = ($logout.Headers.GetValues('Set-Cookie') -join ';') -match '\.AspNetCore.Koan.cookie=;'
        Check 'Local logout expires the application cookie' ([int]$logout.StatusCode -eq 302 -and $expired) @{ status = [int]$logout.StatusCode; cookieExpired = $expired }
    } finally { if ($logout) { $logout.Dispose() }; $request.Dispose(); $client.Dispose() }

    Stop-Host $hostInfo
    $configPath = Join-Path $hostInfo.stateDirectory 'appsettings.json'
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $config.Tangent.Site.OwnerDid = $agent.participant.did
    $config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configPath -Encoding utf8
    $runtime = Get-ChildItem -LiteralPath $hostInfo.stateDirectory -Directory -Filter 'runtime-*' | Sort-Object Name -Descending | Select-Object -First 1
    $failureLog = Join-Path $hostInfo.stateDirectory 'wrong-owner.stdout.log'
    $failureError = Join-Path $hostInfo.stateDirectory 'wrong-owner.stderr.log'
    $arguments = @((Join-Path $runtime.FullName 'TangentSpace.dll'), '--urls', $origin, '--environment', 'Development', '--contentRoot', $hostInfo.stateDirectory,
        '--webroot', (Join-Path $repoRoot 'src/server/web/wwwroot'))
    $quoted = @($arguments | ForEach-Object { if ($_ -match '[\s]') { '"' + $_ + '"' } else { $_ } })
    $failureProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList $quoted -WorkingDirectory $hostInfo.stateDirectory `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $failureLog -RedirectStandardError $failureError
    $exited = $failureProcess.WaitForExit(20000)
    $failureText = (Get-Content -LiteralPath $failureLog -Raw) + (Get-Content -LiteralPath $failureError -Raw)
    Check 'A changed owner configuration fails startup rather than transferring ownership' ($exited -and $failureProcess.ExitCode -ne 0 -and $failureText.Contains('differs from the persisted owner')) @{ exited = $exited; correctionObserved = $failureText.Contains('differs from the persisted owner') }
    $config.Tangent.Site.OwnerDid = $before.participant.did
    $config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configPath -Encoding utf8
}
finally {
    Stop-Host $hostInfo
    if ($failureProcess -and -not $failureProcess.HasExited) {
        Stop-Host ([pscustomobject]@{ pid = $failureProcess.Id; stateDirectory = $hostInfo.stateDirectory })
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $evidencePath) -Force | Out-Null
    [ordered]@{
        observedAt = [DateTimeOffset]::UtcNow.ToString('o')
        passed = $checks.Count -eq 15 -and @($checks | Where-Object { -not $_.passed }).Count -eq 0
        checks = $checks
        limits = @('Disposable loopback identity network', 'Real HTTP cookie/issuer flow; UI rendered separately', 'Arrival slice only; room governance and conversation follow')
    } | ConvertTo-Json -Depth 22 | Set-Content -LiteralPath $evidencePath -Encoding utf8
}
Write-Output "Passed $($checks.Count) arrival checks. Redacted receipt: $evidencePath"
