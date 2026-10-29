<#
.SYNOPSIS
A word in a Never column of docs/LANGUAGE.md does not appear as a whole word, regardless of letter style,
in the tree. The dictionary is parsed at run time. Words with a parenthetical qualifier need
judgment and are printed once as manual words.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'rule-scope.ps1')

$rule = 'language'
$dictionary = Join-Path (Get-TreeBase) 'docs/LANGUAGE.md'
$never = [Collections.Generic.List[string]]::new()
$manual = [Collections.Generic.List[string]]::new()
$known = [Collections.Generic.List[string]]::new()

function Get-Words([string]$Column) {
    $depth = 0; $current = ''
    foreach ($character in $Column.ToCharArray()) {
        if ($character -eq '(') { $depth++ } elseif ($character -eq ')') { $depth-- }
        if ($character -eq ',' -and $depth -eq 0) { $current.Trim(); $current = '' } else { $current += $character }
    }
    if ($current.Trim()) { $current.Trim() }
}

$neverColumn = -1; $wordColumn = -1
foreach ($line in [IO.File]::ReadLines($dictionary)) {
    if (-not $line.StartsWith('|')) { $neverColumn = -1; continue }
    $columns = @([regex]::Matches($line.Trim().Trim('|'), '[^|]+') | ForEach-Object { $_.Value.Trim() })
    if ($columns[0] -eq 'Word') { $wordColumn = 0; $neverColumn = [Array]::IndexOf($columns, 'Never'); continue }
    if ($neverColumn -lt 0 -or $columns.Count -le $neverColumn -or $columns[0] -match '^-+$') { continue }
    $known.Add($columns[$wordColumn].Replace('*', ''))
    foreach ($node in Get-Words $columns[$neverColumn]) {
        if ($node.Contains('(')) { $manual.Add($node) } else { $never.Add($node) }
    }
}

if ($never.Count -eq 0) { Write-Violation 'docs/LANGUAGE.md' 1 $rule 'no Never column was found'; exit 1 }
if ($manual.Count -gt 0) { 'manual: {0}' -f (($manual | Sort-Object -Unique) -join '; ') }

# A dictionary word is removed from the line first, so a phrase such as "The door" does not report its parts.
$knownPhrases = @($known | Sort-Object Length -Descending | ForEach-Object { New-WordRegex $_ -Plural })
$approved = Get-Approvals 'check-language.approved'
$patterns = foreach ($word in ($never | Sort-Object -Unique)) {
    [pscustomobject]@{ Word = $word; Regex = New-WordRegex $word -Plural }
}

$count = 0
foreach ($file in Get-RuleFiles) {
    $number = 0
    foreach ($line in [IO.File]::ReadLines($file.Path)) {
        $number++
        $shown = Remove-Approved $line $approved
        foreach ($phrase in $knownPhrases) { $shown = $phrase.Replace($shown, " ") }
        foreach ($pattern in $patterns) {
            if ($pattern.Regex.IsMatch($shown)) {
                Write-Violation $file.Relative $number $rule "'$($pattern.Word)' is never used"
                $count++
            }
        }
    }
}
if ($count -gt 0) { exit 1 }
