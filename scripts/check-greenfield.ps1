<#
.SYNOPSIS
Code and prose read greenfield: no historical markers, plan codes or names of capabilities the
product does not have. Exits 1 and names each violation.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'rule-scope.ps1')

$rule = 'greenfield'
$patterns = @(
    [pscustomobject]@{ Label = 'inbound MCP transport'; Pattern = '\bMcp[A-Z]\w*|\bTangentSpace\.Mcp\b' }
    [pscustomobject]@{ Label = 'browser WebMCP'; Pattern = '(?i:\bwebmcp\b)' }
    # A note states the rule, not the plan that produced it.
    [pscustomobject]@{ Label = 'plan code'; Pattern = '\((W\d-[A-D]\d?|C[1-9]|P\d)\)|\b(W\d-[A-D]\d?|C[1-9])\b' }
    [pscustomobject]@{ Label = 'bootstrap in reads'; Pattern = '\bEnsureHome\b' }
    [pscustomobject]@{ Label = 'historical marker'; Pattern = '(?i:\blegacy\b|\bdeprecated\b|\bback-?compat\w*|\bformerly\b|\bsuperseded\b|\bcompatibility (path|shim)s?\b)' }
)
# Documents only: wording that narrates a change.
$documentPatterns = @(
    [pscustomobject]@{ Label = 'narrated change'; Pattern = '(?i:\bno longer\b|\bused to\b|\bwas (removed|deleted|replaced|retired)\b)' }
)

$count = 0
foreach ($file in Get-RuleFiles) {
    if ($file.Path -eq $PSCommandPath) { continue }
    $active = if ($file.Relative -match '\.md$') { $patterns + $documentPatterns } else { $patterns }
    $number = 0
    foreach ($line in [IO.File]::ReadLines($file.Path)) {
        $number++
        foreach ($check in $active) {
            $match = [regex]::Match($line, $check.Pattern)
            if ($match.Success) {
                Write-Violation $file.Relative $number $rule "$($check.Label): '$($match.Value)'"
                $count++
            }
        }
    }
}
if ($count -gt 0) { exit 1 }
