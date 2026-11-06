<#
.SYNOPSIS
A page holds no markup of its own: the only Razor in the tree is the shell (src/server/Render/Shell.razor) and one
component per part, in src/server/Render/Parts. Any other .razor file is a violation.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'rule-scope.ps1')

$rule = 'parts'
$allowed = '^src/server/Render/(Parts/[^/]+\.razor|Shell\.razor)$'

$count = 0
foreach ($file in Get-RuleFiles) {
    if ($file.Relative -notmatch '\.razor$' -or $file.Relative -match $allowed) { continue }
    Write-Violation $file.Relative 1 $rule 'markup outside the parts and the shell (a page is a record of parts, each drawn by its one component in src/server/Render/Parts)'
    $count++
}
if ($count -gt 0) { exit 1 }
