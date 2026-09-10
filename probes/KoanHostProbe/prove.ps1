[CmdletBinding()]
param(
    [string]$Dotnet = 'dotnet',
    [int]$Port = 5210,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$probeRoot = $PSScriptRoot
$appDll = Join-Path $probeRoot "bin/$Configuration/net10.0/KoanHostProbe.dll"
if (-not (Test-Path -LiteralPath $appDll)) {
    throw 'Build the probe before running prove.ps1.'
}
$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$runDirectory = Join-Path $probeRoot ".work/runs/$runId"
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$databasePath = Join-Path $runDirectory 'probe.sqlite'
$origin = "http://127.0.0.1:$Port"
$hostProcess = $null
$observations = [System.Collections.Generic.List[object]]::new()

function Assert-Probe([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw $Description }
    $observations.Add([pscustomobject]@{ check = $Description; passed = $true })
}

function Read-Endpoint([string]$Path) {
    Invoke-RestMethod -Uri "$origin$Path" -TimeoutSec 5
}

function Start-ProbeHost([string]$Name, [string]$Adapter = 'sqlite') {
    $stdout = Join-Path $runDirectory "$Name.stdout.log"
    $stderr = Join-Path $runDirectory "$Name.stderr.log"
    $arguments = @(
        "`"$appDll`"",
        '--urls', $origin,
        '--environment', 'Development',
        '--Koan:Data:Sources:Default:Adapter', $Adapter,
        '--Koan:Data:Sources:Default:ConnectionString', "`"Data Source=$databasePath`""
    )
    Start-Process -FilePath $Dotnet -ArgumentList $arguments -WorkingDirectory $probeRoot `
        -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
}

function Stop-ProbeHost($Process) {
    if ($null -ne $Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id
        $Process.WaitForExit(10000) | Out-Null
    }
}

function Wait-ProbeHost($Process) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process.HasExited) { throw "Host exited with code $($Process.ExitCode). Inspect $runDirectory." }
        try {
            $response = Invoke-WebRequest -Uri "$origin/health/live" -TimeoutSec 2
            if ($response.StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw "Host did not become live. Inspect $runDirectory."
}

try {
    # Refuse an occupied port so this script never mistakes another host for its own.
    $portProbe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try { $portProbe.Start() } finally { $portProbe.Stop() }

    $hostProcess = Start-ProbeHost 'first-start'
    Wait-ProbeHost $hostProcess
    $live = Invoke-WebRequest -Uri "$origin/health/live" -TimeoutSec 5
    $ready = Invoke-WebRequest -Uri "$origin/health/ready" -TimeoutSec 5
    Assert-Probe ($live.StatusCode -eq 200) 'Initial liveness returns HTTP 200'
    Assert-Probe ($ready.StatusCode -eq 200) 'Initial readiness returns HTTP 200'
    $readyJson = if ($ready.Content -is [byte[]]) {
        [System.Text.Encoding]::UTF8.GetString($ready.Content)
    } else { [string]$ready.Content }
    $readyState = $readyJson | ConvertFrom-Json
    Assert-Probe ($readyState.status -eq 'healthy') 'Readiness reports a healthy composition'
    $readyJson | Set-Content -LiteralPath (Join-Path $runDirectory 'ready.json')

    $id = [guid]::NewGuid().ToString('N')
    $title = "SQLite survives host restart ($runId)"
    $body = @{ id = $id; title = $title; done = $false } | ConvertTo-Json -Compress
    $write = Invoke-WebRequest -Uri "$origin/api/todos" -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 15
    Assert-Probe ($write.StatusCode -ge 200 -and $write.StatusCode -lt 300) 'EntityController accepts one Todo write'
    $before = Read-Endpoint "/api/todos/$id"
    Assert-Probe ($before.id -eq $id -and $before.title -eq $title) 'Entity read returns the saved identity and title'
    $before | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runDirectory 'entity-before.json')

    $facts = Read-Endpoint '/.well-known/Koan/facts'
    $facts | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath (Join-Path $runDirectory 'facts.json')
    $selectedSqlite = @($facts.facts | Where-Object { $_.subject -eq 'data:default' -and $_.summary -match 'sqlite' })
    Assert-Probe ($selectedSqlite.Count -gt 0) 'Runtime facts identify SQLite as the default data provider'
    Assert-Probe (Test-Path -LiteralPath $databasePath) 'The configured SQLite database exists'
    $lockPath = Join-Path $probeRoot 'koan.lock.json'
    Assert-Probe (Test-Path -LiteralPath $lockPath) 'Build emits koan.lock.json'
    $composition = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    Assert-Probe (@($composition.modules | Where-Object { $_.id -match 'Koan.Data.Connector.Sqlite$' }).Count -eq 1) 'Composition lock includes one SQLite connector'
    Assert-Probe (@($composition.directReferences | Where-Object { $_.id -eq 'Sylin.Koan.App' }).Count -eq 1) 'Composition lock records the web bundle as direct intent'

    Stop-ProbeHost $hostProcess
    $hostProcess = Start-ProbeHost 'restart'
    Wait-ProbeHost $hostProcess
    $after = Read-Endpoint "/api/todos/$id"
    Assert-Probe ($after.id -eq $id -and $after.title -eq $title -and $after.done -eq $false) 'A new host process reads the unchanged SQLite Entity'
    $after | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runDirectory 'entity-after.json')
    Stop-ProbeHost $hostProcess

    # A required but unreferenced adapter must fail rather than silently select SQLite or JSON.
    $hostProcess = Start-ProbeHost 'invalid-adapter' 'not-referenced'
    $exited = $hostProcess.WaitForExit(30000)
    Assert-Probe ($exited -and $hostProcess.ExitCode -ne 0) 'Invalid explicit adapter intent fails host startup'
    $failure = (Get-Content -LiteralPath (Join-Path $runDirectory 'invalid-adapter.stdout.log') -Raw) +
        (Get-Content -LiteralPath (Join-Path $runDirectory 'invalid-adapter.stderr.log') -Raw)
    Assert-Probe ($failure -match 'not-referenced') 'Failure names the rejected adapter intent'

    $receipt = [ordered]@{
        status = 'passed'
        observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        runId = $runId
        frameworkReferenceCommit = 'e07a84cc3f71a0867f1122b03b723cc80727e772'
        templatePackage = 'Sylin.Koan.Templates/1.0.21'
        directPackages = @('Sylin.Koan.App/1.0.38', 'Sylin.Koan.Data.Connector.Sqlite/1.0.46')
        sdk = (& $Dotnet --version)
        port = $Port
        checks = $observations
        limits = @('Loopback Development probe; no product identity or authorization policy.', 'One host and local SQLite; no multi-node, federation, backup, or durable notification claim.')
    }
    $receipt | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runDirectory 'receipt.json')
    $receipt | ConvertTo-Json -Depth 10
    Write-Output "Evidence: $runDirectory"
} finally {
    Stop-ProbeHost $hostProcess
}
