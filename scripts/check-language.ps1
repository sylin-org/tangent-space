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
        if ($node -match '^\*.+\*$') { $manual.Add($node.Trim('*')) }
        elseif ($node.Contains('(')) { $manual.Add($node) } else { $never.Add($node) }
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


# Phrases (a space or hyphen inside) are matched on the line as written; single words are matched
# after the dictionary's own phrases are taken out of it.
$phrasePatterns = @($patterns | Where-Object { $_.Word -match '[\s-]' })
$wordPatterns = @($patterns | Where-Object { $_.Word -notmatch '[\s-]' })

# An identifier read as words: PascalCase boundaries and underscores separate them.
function Format-Words([string]$Name) {
    $spaced = [regex]::Replace($Name, '([a-z0-9])([A-Z])', '$1 $2')
    $spaced = [regex]::Replace($spaced, '([A-Z]+)([A-Z][a-z])', '$1 $2')
    $spaced.Replace('_', ' ')
}

function Get-Captures([string]$Line, [string]$Pattern) {
    foreach ($found in [regex]::Matches($Line, $Pattern)) { $found.Result('$1') }
}

$markupPattern = '>([^<>@{}]*[A-Za-z][^<>@{}]*)<'
$pathPattern = '["''`](/[A-Za-z0-9_{}:.\-/?=&]*)["''`]'
$declarationPattern = '\b(?:class|record|struct|interface|enum)\s+(?:class\s+|struct\s+)?([A-Za-z_]\w*)'
$memberPattern = '([A-Za-z_]\w*)\s*(?:<[^<>]*>\s*)?(?:\{|\(|=>|=|;|,)'
$routeLine = '\b(?:Route|Http(?:Get|Post|Put|Delete|Patch)|Map(?:Get|Post|Put|Delete|Patch|Group|Methods|ControllerRoute)|GetSection|GetValue)\b|Configuration\['
$exportPattern = '^\s*export\s+(?:default\s+)?(?:declare\s+)?(?:async\s+)?(?:abstract\s+)?(?:const|let|var|function\*?|class|interface|type|enum)\s+([A-Za-z_$][\w$]*)'
$componentPattern = '(?:defineComponent\(\s*\{|defineOptions\(\s*\{)\s*name\s*:\s*["'']([^"'']+)'

# Source files: declarations and literals are read, never usages, so framework members do not fire.
function Get-CodeNames([string]$Line, [string]$Extension, [string]$Relative) {
    $names = [Collections.Generic.List[string]]::new()
    if ($Extension -in '.cs', '.razor') {
        $names.AddRange([string[]]@(Get-Captures $Line $declarationPattern))
        $names.AddRange([string[]]@(Get-Captures $Line '\bnamespace\s+([\w.]+)'))
        if ($Line -match '^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|protected|private)\b') {
            $names.AddRange([string[]]@(Get-Captures $Line $memberPattern | Select-Object -First 1))
        }
        if ($Line -match $routeLine) { $names.AddRange([string[]]@(Get-Captures $Line '"([^"]*)"')) }
        $names.AddRange([string[]]@(Get-Captures $Line $pathPattern))
        if ($Extension -eq '.razor') { $names.AddRange([string[]]@(Get-Captures $Line $markupPattern)) }
    } elseif ($Extension -in '.ts', '.js', '.mjs', '.vue') {
        $names.AddRange([string[]]@(Get-Captures $Line $exportPattern))
        $names.AddRange([string[]]@(Get-Captures $Line $componentPattern))
        $names.AddRange([string[]]@(Get-Captures $Line '\.component\(\s*["'']([^"'']+)'))
        $names.AddRange([string[]]@(Get-Captures $Line 'data-island["'']?\s*[=:]\s*["'']([^"'']+)'))
        $names.AddRange([string[]]@(Get-Captures $Line $pathPattern))
        if ($Extension -eq '.vue') { $names.AddRange([string[]]@(Get-Captures $Line $markupPattern)) }
    } elseif ($Extension -eq '.json' -and $Relative -match '(^|/)appsettings[^/]*\.json$') {
        $names.AddRange([string[]]@(Get-Captures $Line '"([^"]+)"\s*:'))
    } else {
        $names.Add($Line)
    }
    $names
}

# Markdown: code spans, fences and indented code are quotations of identifiers, so they are not read.
function Get-ProseLines([string]$Path) {
    $number = 0; $fence = ''; $previousBlank = $true; $previousCode = $false
    foreach ($line in [IO.File]::ReadLines($Path)) {
        $number++
        $opening = [regex]::Match($line, '^ {0,3}(`{3,}|~{3,})')
        if ($fence) {
            $mark = $opening.Value.Trim()
            if ($opening.Success -and $mark[0] -eq $fence[0] -and $mark.Length -ge $fence.Length) { $fence = '' }
            continue
        }
        if ($opening.Success) { $fence = $opening.Value.Trim(); continue }
        $blank = $line.Trim() -eq ''
        $indented = ($line -match '^( {4}|\t)') -and ($previousBlank -or $previousCode)
        $previousBlank = $blank; $previousCode = $indented
        if ($indented -or $blank) { continue }
        @{ Number = $number; Text = [regex]::Replace($line, '(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)', ' ') }
    }
}

function Get-Segments($File) {
    $extension = [IO.Path]::GetExtension($File.Path).ToLowerInvariant()
    if ($extension -eq '.md') { Get-ProseLines $File.Path; return }
    $number = 0
    $isSource = $extension -in '.cs', '.razor', '.ts', '.js', '.mjs', '.vue'
    foreach ($line in [IO.File]::ReadLines($File.Path)) {
        $number++
        foreach ($name in Get-CodeNames $line $extension $File.Relative) {
            @{ Number = $number; Text = if ($isSource) { Format-Words $name } else { $name } }
        }
    }
}

$count = 0
function Test-Segment([string]$Relative, [int]$Number, [string]$Text, [string]$Where, [hashtable]$Reported) {
    $shown = Remove-Approved $Text $approved
    foreach ($pattern in $phrasePatterns) {
        if (-not $Reported.ContainsKey($pattern.Word) -and $pattern.Regex.IsMatch($shown)) {
            $Reported[$pattern.Word] = $true
            Write-Violation $Relative $Number $rule "'$($pattern.Word)' is never used$Where"
            $script:count++
        }
    }
    foreach ($phrase in $knownPhrases) { $shown = $phrase.Replace($shown, ' ') }
    foreach ($pattern in $wordPatterns) {
        if (-not $Reported.ContainsKey($pattern.Word) -and $pattern.Regex.IsMatch($shown)) {
            $Reported[$pattern.Word] = $true
            Write-Violation $Relative $Number $rule "'$($pattern.Word)' is never used$Where"
            $script:count++
        }
    }
}

foreach ($file in Get-RuleFiles) {
    $byLine = @{}
    foreach ($segment in Get-Segments $file) {
        if (-not $byLine.ContainsKey($segment.Number)) { $byLine[$segment.Number] = @{} }
        Test-Segment $file.Relative $segment.Number $segment.Text '' $byLine[$segment.Number]
    }
    if ($file.Relative.StartsWith('src/')) {
        $named = @{}
        foreach ($part in $file.Relative.Substring(4).Replace('\', '/').Split('/')) {
            Test-Segment $file.Relative 1 (Format-Words ([IO.Path]::GetFileNameWithoutExtension($part))) ' (in a file or folder name)' $named
        }
    }
}
if ($count -gt 0) { exit 1 }
