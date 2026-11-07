<#
.SYNOPSIS
A page holds no markup of its own: the only Razor in the tree is the shell (src/server/Render/Shell.razor) and one
component per part, in src/server/Render/Parts. Any other .razor file is a violation. Every form is drawn by the Form
part: a <form>, <input>, <select> or <textarea> anywhere else in the server's markup, or built by hand in its C#, is a
violation, except at an island's root, which the island takes over: the article editor's mount and the reply box, the
composer's.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'rule-scope.ps1')

$rule = 'parts'
$allowed = '^src/server/Render/(Parts/[^/]+\.razor|Shell\.razor)$'
$forms = '^src/server/Render/Parts/(Form|ArticleEditor|ReplyBox)\.razor$'
$markup = '<(form|input|select|textarea)\b'
$built = 'OpenElement\(\s*[^,]+,\s*"(form|input|select|textarea)"'

$count = 0
foreach ($file in Get-RuleFiles) {
    if ($file.Relative -notmatch '^src/server/') { continue }
    if ($file.Relative -match '\.razor$' -and $file.Relative -notmatch $allowed) {
        Write-Violation $file.Relative 1 $rule 'markup outside the parts and the shell (a page is a record of parts, each drawn by its one component in src/server/Render/Parts)'
        $count++
        continue
    }
    $pattern = if ($file.Relative -match '\.razor$') { $markup } elseif ($file.Relative -match '\.cs$') { $built } else { $null }
    if ($null -eq $pattern -or $file.Relative -match $forms) { continue }
    $number = 0
    foreach ($line in [IO.File]::ReadAllLines($file.Path)) {
        $number++
        if ($line -match $pattern) {
            Write-Violation $file.Relative $number $rule ('<{0}> outside the Form part (a form is a Render.Form record, drawn by src/server/Render/Parts/Form.razor)' -f $Matches[1].ToLowerInvariant())
            $count++
        }
    }
}
if ($count -gt 0) { exit 1 }
