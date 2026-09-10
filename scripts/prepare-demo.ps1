[CmdletBinding()]
param([switch]$HumanOnly, [switch]$Reconnect)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$origin = 'http://127.0.0.1:5220'
Invoke-RestMethod "$origin/health/ready" -TimeoutSec 5 | Out-Null
$fixturePath = Join-Path $repoRoot '.local/spaces-network/fixtures.json'
$fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
if ($fixture.status -ne 'ready') { throw 'Start the real disposable Spaces network first.' }
$networkId = ([DateTimeOffset]$fixture.startedAt).ToUnixTimeMilliseconds()
$demoDirectory = Join-Path $repoRoot ".local/demo/$networkId"
New-Item -ItemType Directory -Path $demoDirectory -Force | Out-Null
$arguments = @('scripts/prepare-demo.mjs', '--origin', $origin, '--fixtures', $fixturePath,
    '--directory', $demoDirectory, '--powershell', (Get-Process -Id $PID).Path)
if ($HumanOnly) { $arguments += @('--human-only', 'true') }
if ($Reconnect) { $arguments += @('--reconnect', 'true') }
Push-Location $repoRoot
try {
    & node @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Demo preparation stopped; its ignored context retains any pending operation.' }
} finally { Pop-Location }
