# Shared by the rule checks: lists the plain files of the tree.
# Each file comes back as @{ Path = <full path>; Relative = <path from the tree base, forward slashes> }.

$script:ExcludedTopFolders = '.git', '.local', '.dream-loop', 'poc'
$script:ExcludedDirectories = '.git', 'node_modules', 'bin', 'obj'
$script:ExcludedRelativePaths = 'docs/adr', 'docs/HARVEST.md', 'docs/LANGUAGE.md'
$script:BinaryExtensions = '.png', '.jpg', '.jpeg', '.gif', '.webp', '.ico', '.svg', '.woff', '.woff2', '.ttf', '.otf',
    '.zip', '.gz', '.tar', '.7z', '.pdf', '.dll', '.exe', '.pdb', '.sqlite', '.db', '.mp3', '.mp4', '.webm', '.wasm', '.map'

function Get-TreeBase {
    [IO.Path]::GetDirectoryName($PSScriptRoot)
}

function Test-BinaryFile([string]$Path) {
    if ($script:BinaryExtensions -contains [IO.Path]::GetExtension($Path).ToLowerInvariant()) { return $true }
    $stream = [IO.File]::OpenRead($Path)
    try {
        $buffer = New-Object byte[] 8192
        $count = $stream.Read($buffer, 0, $buffer.Length)
        return [Array]::IndexOf($buffer, [byte]0, 0, $count) -ge 0
    } finally { $stream.Dispose() }
}

function Get-RuleFiles {
    $base = Get-TreeBase
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($base)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($node in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
            $name = [IO.Path]::GetFileName($node)
            $relative = [IO.Path]::GetRelativePath($base, $node).Replace('\', '/')
            if ($script:ExcludedRelativePaths -contains $relative) { continue }
            if ($directory -eq $base -and $script:ExcludedTopFolders -contains $name) { continue }
            if ([IO.Directory]::Exists($node)) {
                if ($script:ExcludedDirectories -contains $name) { continue }
                $pending.Push($node)
            } elseif (-not (Test-BinaryFile $node)) {
                @{ Path = $node; Relative = $relative }
            }
        }
    }
}

# Reads a rule's approvals: one exact phrase per line; lines starting with # are notes.
function Get-Approvals([string]$Name) {
    $path = Join-Path $PSScriptRoot $Name
    if (-not (Test-Path $path)) { return @() }
    @([IO.File]::ReadAllLines($path) | Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith("#") } | ForEach-Object { $_.Trim() })
}

# Removes every approved phrase from a line before it is matched.
function Remove-Approved([string]$Line, [string[]]$Approved) {
    foreach ($phrase in $Approved) { $Line = $Line.Replace($phrase, ' ', [StringComparison]::OrdinalIgnoreCase) }
    $Line
}

function Write-Violation([string]$Relative, [int]$Line, [string]$Rule, [string]$Detail) {
    '{0}:{1}: {2}: {3}' -f $Relative, $Line, $Rule, $Detail
}

# A regex for a word or phrase as a whole word, with an optional plural s when asked.
function New-WordRegex([string]$Word, [switch]$Plural) {
    $body = [regex]::Escape($Word) -replace '\ ', '\s+'
    $tail = if ($Plural) { 's?' } else { '' }
    [regex]::new('(?<![\p{L}\p{N}_])' + $body + $tail + '(?![\p{L}\p{N}_])', 'IgnoreCase')
}
