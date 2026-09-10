[CmdletBinding()]
param([ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Instance = 'site')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixture = Get-Content -LiteralPath (Join-Path $repoRoot '.local/spaces-network/fixtures.json') -Raw | ConvertFrom-Json
$networkId = ([DateTimeOffset]$fixture.startedAt).ToUnixTimeMilliseconds()
$state = [IO.Path]::GetFullPath((Join-Path $repoRoot ".local/tangent/$networkId/$Instance"))
$pidFile = Join-Path $state 'host.pid'
if (-not (Test-Path -LiteralPath $pidFile)) { Write-Output 'This local instance is already stopped.'; return }
$instanceProcessId = [int](Get-Content -LiteralPath $pidFile -Raw)
$process = Get-CimInstance Win32_Process -Filter "ProcessId = $instanceProcessId"
if (-not $process) { Write-Output 'This local instance is already stopped.'; return }
if (-not $process.CommandLine.Contains($state) -or -not $process.CommandLine.Contains('TangentSpace.dll')) { throw 'The recorded process no longer belongs to this Tangent instance; it was left running.' }
Stop-Process -Id $instanceProcessId
Wait-Process -Id $instanceProcessId -ErrorAction SilentlyContinue
Write-Output "Stopped Tangent instance $Instance."
