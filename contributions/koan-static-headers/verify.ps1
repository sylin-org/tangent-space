[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination,
    [string]$SourceRepository = 'https://github.com/sylin-org/koan-framework.git',
    [switch]$RunTests)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Destination) { throw 'Verification creates a new checkout and never resets or deletes an existing destination.' }
$authVerifier = Join-Path (Split-Path -Parent $PSScriptRoot) 'koan-atproto-auth/verify.ps1'
& $authVerifier -Destination $Destination -SourceRepository $SourceRepository | Out-Null
& (Join-Path $PSScriptRoot 'apply.ps1') -Checkout $Destination -RunTests:$RunTests
