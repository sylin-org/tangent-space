<#
.SYNOPSIS
Reports server code that does not read greenfield (ADR 0011): names of removed capabilities,
retired vocabulary and historical markers. Reports only; -Strict exits 1 when anything is found.
#>
param([switch]$Strict)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$targets = 'src/server/web', 'tests', 'scripts', 'compose.yaml', 'Dockerfile' | ForEach-Object { Join-Path $root $_ }
$rules = [ordered]@{
    'Inbound MCP transport' = '\bMcp[A-Z]\w*|\bTangentSpace\.Mcp\b'
    'Browser WebMCP'        = '(?i:\bwebmcp\b)'
    # Narrowed in R2.1 (N-038): every remaining finding is SourceDecision, and the broad
    # patterns would have reported the product's own new word - a Space is what an operator
    # runs - as the deleted atproto concept. R3.6 removes SourceDecision and this rule with it.
    'Spaces storage'        = '\bSpaceUri\b|\bWriteIntent\b|\bSourceNotification\w*|\bSourceDecision\b'
    'Change classification' = '\bChangeClass\w*|Koan\.AI\.Connector\.Onnx'
    # Narrowed in R2.4 (N-053): the product's channel vocabulary reached zero, and every remaining
    # match was .NET's System.Threading.Channels, which is not ours to rename. The clause still
    # catches a returning product channel; it no longer counts the BCL type as one.
    'Retired vocabulary'    = '(?i:\broom\w*|(?<!Threading\.)\bchannels?\w*(?!\s*(<|\.Create))|\bcompanion\w*)|\bTangentSite\b|\bTangentCommunity\b|\bEntity<Message>|\bMessage\.(Get|Query|Lifecycle|Project)\b'
    'Historical markers'    = '(?i:\blegacy\b|\bdeprecated\b|\bback-?compat\w*|\bformerly\b|\bsuperseded\b|\bcompatibility (path|shim)s?\b)'
    'Bootstrap in reads'    = '\bEnsureHome\b'
}

$files = Get-ChildItem -Path $targets -Recurse -File -Include *.cs, *.js, *.mjs, *.css, *.html, *.json, *.ps1, *.yaml, Dockerfile |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|TestResults)\\' -and $_.FullName -ne $PSCommandPath }

$total = 0
foreach ($rule in $rules.GetEnumerator()) {
    $hits = @($files | Select-String -Pattern $rule.Value -CaseSensitive)
    $total += $hits.Count
    $top = $hits | Group-Object Path | Sort-Object Count -Descending | Select-Object -First 4 |
        ForEach-Object { '{0} ({1})' -f [IO.Path]::GetRelativePath($root, $_.Name), $_.Count }
    '{0,-22} {1,6}  {2}' -f $rule.Key, $hits.Count, ($top -join ', ')
}
'{0,-22} {1,6}' -f 'Total (lines)', $total
if ($Strict -and $total -gt 0) { exit 1 }
