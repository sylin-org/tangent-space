# Shared by the rule checks: lists the prose and source files of the tree.
# Each file comes back as @{ Path = <full path>; Relative = <path from the tree base, forward slashes> }.

$script:ExcludedTopFolders = '.git', '.local', '.dream-loop', 'poc'
$script:ExcludedDirectories = '.git', 'node_modules', 'bin', 'obj'
$script:ExcludedRelativePaths = 'docs/adr', 'docs/HARVEST.md', 'docs/LANGUAGE.md'
$script:TextExtensions = '.md', '.cs', '.razor', '.ts', '.vue', '.js', '.mjs', '.json', '.ps1', '.yaml', '.yml', '.css', '.html',
    '.csproj', '.props', '.targets', '.sln', '.sh', '.cmd', '.bat', '.xml'
$script:TextNames = 'Dockerfile'

function Get-TreeBase {
    [IO.Path]::GetDirectoryName($PSScriptRoot)
}

# Only prose and source files are read: not a licence text, not git configuration, not a root dotfile.
function Test-TextFile([string]$Path, [bool]$AtBase) {
    $name = [IO.Path]::GetFileName($Path)
    if ($AtBase -and $name.StartsWith('.')) { return $false }
    ($script:TextExtensions -contains [IO.Path]::GetExtension($Path).ToLowerInvariant()) -or ($script:TextNames -contains $name)
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
            } elseif (Test-TextFile $node ($directory -eq $base)) {
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
    $body = [regex]::Escape($Word).Replace('-', '[\s_-]+').Replace('\ ', '[\s_-]+')
    $tail = if ($Plural) { 's?' } else { '' }
    [regex]::new('(?<![\p{L}\p{N}_])' + $body + $tail + '(?![\p{L}\p{N}_])', 'IgnoreCase')
}
