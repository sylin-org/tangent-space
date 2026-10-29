<#
.SYNOPSIS
Runs every standing rule's check over the tree. Prints each violation as
path:line: rule: detail, one summary line per rule and the total; exits with the number of
violating rules. Each rule is also its own script in this folder and runs alone.
#>
$ErrorActionPreference = 'Stop'
$base = [IO.Path]::GetDirectoryName($PSScriptRoot)
$checks = @(
    @{ Name = 'language';   Command = { & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-language.ps1') } }
    @{ Name = 'greenfield'; Command = { & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-greenfield.ps1') } }
    @{ Name = 'no-dialogs'; Command = { & node --test (Join-Path $base 'tests/rules/*.test.mjs') 2>&1 } }
    @{ Name = 'ports';      Command = { & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-ports.ps1') } }
)

$failed = 0
$summary = foreach ($check in $checks) {
    $output = @(& $check.Command)
    $code = $LASTEXITCODE
    $lines = @($output | ForEach-Object { "$_" })
    if ($code -ne 0) { $failed++; $lines | ForEach-Object { [Console]::WriteLine($_) } }
    elseif ($lines | Where-Object { $_ -like "manual:*" }) { $lines | Where-Object { $_ -like "manual:*" } | ForEach-Object { [Console]::WriteLine($_) } }
    $violations = @($lines | Where-Object { $_ -match '^\S+:\d+: \S+: ' }).Count
    '{0,-11} {1}  ({2} violations)' -f $check.Name, $(if ($code -eq 0) { 'ok' } else { 'FAILED' }), $violations
}
$summary
'rules failed: {0} of {1}' -f $failed, $checks.Count
exit $failed
