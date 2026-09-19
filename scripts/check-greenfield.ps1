<#
.SYNOPSIS
Reports code that does not read greenfield (ADR 0011 and 0012): names of removed capabilities,
retired vocabulary, historical markers, plan-item codes and owner narration. Covers the server
and, since R2.5, the connector. Reports only; -Strict exits 1 when anything is found.
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
    'Retired vocabulary'    = '(?i:\broom\w*|(?<!Threading\.)\bchannels?\w*(?!\s*(<|\.Create)))|\bTangentSite\b|\bTangentCommunity\b|\bEntity<Message>|\bMessage\.(Get|Query|Lifecycle|Project)\b'
    # Retired on the server, where Participant replaces it. The connector — where Companion
    # is the product word — lives in its own repository with its own check.
    'Server vocabulary'     = '(?i:\bcompanion\w*)'
    # The connector glossary's Replaces column (C6), as identifiers rather than prose.
    'Connector vocabulary'  = '\bAtprotoSession\b|\bCompanionEntry\b|\bcompanion_id\b|\bcompanionId\b|\bLocalContext\b|\bPendingWrite\b'
    'Historical markers'    = '(?i:\blegacy\b|\bdeprecated\b|\bback-?compat\w*|\bformerly\b|\bsuperseded\b|\bcompatibility (path|shim)s?\b)'
    'Bootstrap in reads'    = '\bEnsureHome\b'
    # Added in R2.5 (C6). A comment should state the rule, not cite the plan item that produced it:
    # W2-A is a handoff brief and C1-C9 are connector-assessment items, and R6.6 deletes both, which
    # leaves the citation pointing at nothing. ADR NNNN is deliberately absent - decision records
    # survive, and the server already cites ADR 0007 and 0008 in living code.
    'Plan-item codes'       = '\((W\d-[A-D]\d?|C[1-9]|P\d)\)|\b(W\d-[A-D]\d?|C[1-9])\b'
    # Who decided is not a rule. "per the owner direction" tells a reader nothing they can check;
    # the rule it stands for does.
    'Owner narration'       = '(?i:owner (direction|correction|decision|call|note)|the owner (asked|wanted|said|directed|corrected|decided)|per the owner|by owner)'
}

# Rules can name paths they do not govern; with the connector in its own repository,
# none currently do.
$notGoverned = @{}
$files = Get-ChildItem -Path $targets -Recurse -File -Include *.cs, *.rs, *.js, *.mjs, *.css, *.html, *.json, *.ps1, *.yaml, *.md, Dockerfile |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|target|TestResults)\\' -and $_.FullName -ne $PSCommandPath }

$total = 0
foreach ($rule in $rules.GetEnumerator()) {
    $scanned = if ($notGoverned.ContainsKey($rule.Key)) {
        $files | Where-Object { $_.FullName -notmatch $notGoverned[$rule.Key] }
    } else { $files }
    $hits = @($scanned | Select-String -Pattern $rule.Value -CaseSensitive)
    $total += $hits.Count
    $top = $hits | Group-Object Path | Sort-Object Count -Descending | Select-Object -First 4 |
        ForEach-Object { '{0} ({1})' -f [IO.Path]::GetRelativePath($root, $_.Name), $_.Count }
    '{0,-22} {1,6}  {2}' -f $rule.Key, $hits.Count, ($top -join ', ')
}
'{0,-22} {1,6}' -f 'Total (lines)', $total
if ($Strict -and $total -gt 0) { exit 1 }
