<#
.SYNOPSIS
  The check a merge into dev owes: the build, the whole suite on that build, and the rule checks, in that order, each
  bounded, stopping at the first failure.

.DESCRIPTION
  A failed build stops the check, so no suite runs against the binary an earlier build left. A build that fails only on
  a file under .local/upstream/koan-framework that another checkout's build holds is a race with an executor sharing the
  pinned Koan checkout; it is built once more after a pause and reported as such. Exit code 0 means every step passed.

.EXAMPLE
  pwsh .claude/skills/work-split/merge-check.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [int]$BuildMinutes = 10,
    [int]$TestMinutes = 15,
    [int]$RuleMinutes = 5
)

$ErrorActionPreference = 'Stop'

function Invoke-Bounded([string]$Name, [string]$File, [string[]]$Arguments, [int]$Minutes) {
    $out = Join-Path ([IO.Path]::GetTempPath()) ("merge-check-{0}-{1}.log" -f $Name, [Guid]::NewGuid().ToString('N'))
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WorkingDirectory $Root -NoNewWindow -PassThru `
        -RedirectStandardOutput $out -RedirectStandardError "$out.err"
    if (-not $process.WaitForExit($Minutes * 60 * 1000)) {
        $process.Kill($true)
        throw "$Name did not finish within $Minutes minutes; it was ended. Its output is at $out."
    }
    $text = (Get-Content $out -Raw -ErrorAction SilentlyContinue) + (Get-Content "$out.err" -Raw -ErrorAction SilentlyContinue)
    Remove-Item $out, "$out.err" -ErrorAction SilentlyContinue
    [pscustomobject]@{ Code = $process.ExitCode; Text = $text }
}

function Build {
    Invoke-Bounded 'build' 'dotnet' @('build', 'Tangent.sln', '-nologo', '-v', 'q') $BuildMinutes
}

$build = Build
if ($build.Code -ne 0) {
    $errors = ($build.Text -split "`r?`n") | Where-Object { $_ -match ': error ' } | Sort-Object -Unique
    $raced = $errors.Count -gt 0 -and -not ($errors | Where-Object { $_ -notmatch 'koan-framework[\\/].*[\\/](obj|bin)[\\/]' })
    if (-not $raced) { $errors; "build: failed — the check stops here"; exit 1 }
    "build: failed on a held Koan obj or bin file (a race with another checkout's build); building once more"
    Start-Sleep -Seconds 60
    $build = Build
    if ($build.Code -ne 0) {
        ($build.Text -split "`r?`n") | Where-Object { $_ -match ': error ' } | Sort-Object -Unique
        "build: failed again — the check stops here"; exit 1
    }
}
"build: ok"

$test = Invoke-Bounded 'test' 'dotnet' @('test', 'tests/Tangent.Tests', '-nologo', '--no-build') $TestMinutes
($test.Text -split "`r?`n") | Where-Object { $_ -match '^\s*(Passed!|Failed!|Failed\s)' }
if ($test.Code -ne 0) { "suite: failed"; exit 1 }
"suite: ok"

$rules = Invoke-Bounded 'rules' 'pwsh' @('-NoProfile', '-File', 'scripts/check-rules.ps1') $RuleMinutes
($rules.Text -split "`r?`n") | Where-Object { $_ -match 'rules failed' }
if ($rules.Code -ne 0) { "rules: failed"; exit 1 }
"rules: ok"
