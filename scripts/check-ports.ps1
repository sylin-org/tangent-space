<#
.SYNOPSIS
The listening port is 5220 or the one an operator configures. Any other port literal in src,
scripts, compose.yaml, Dockerfile and appsettings*.json is a violation; documented allowances
go in check-ports.approved.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'rule-scope.ps1')

$rule = 'ports'
$fixed = '5220'
$governed = '^(src/|scripts/|compose\.yaml$|Dockerfile$|appsettings[^/]*\.json$)'
$approved = Get-Approvals 'check-ports.approved'
# Each pattern captures the port as port (and other for a mapping); 'urls value' scans a whole value.
$patterns = @(
    [pscustomobject]@{ Label = 'address and port'; Pattern = '(?i)(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1?\]|\+|\*):(?<port>\d{2,5})\b' }
    [pscustomobject]@{ Label = 'port in a URL'; Pattern = '(?i)https?://[^\s/"''`]+:(?<port>\d{2,5})\b' }
    [pscustomobject]@{ Label = 'urls value'; Pattern = '(?i)(?:--urls|ASPNETCORE_URLS)\b\S*\s*[=:]?\s*(?<value>\S+)' }
    [pscustomobject]@{ Label = 'port setting'; Pattern = '(?i)(?<![a-z])ports?["'']?\s*[:=]\s*["'']?(?<list>\d{2,5}(?:[;,]\d{2,5})*)\b' }
    [pscustomobject]@{ Label = 'published port'; Pattern = '^\s*-\s*["'']?(?:[\d.]+:)?(?<port>\d{2,5}):(?<other>\d{2,5})\b' }
    [pscustomobject]@{ Label = 'exposed port'; Pattern = '^\s*EXPOSE\s+(?<port>\d{2,5})\b' }
)

# A capture the pattern does not define stays literal in Result; report it as empty.
function Get-Capture($Match, [string]$Name) {
    $reference = '${' + $Name + '}'
    $captured = $Match.Result($reference)
    if ($captured -eq $reference) { '' } else { $captured }
}

$count = 0
foreach ($file in Get-RuleFiles) {
    if ($file.Relative -notmatch $governed -or $file.Path -eq $PSCommandPath) { continue }
    $number = 0
    foreach ($line in [IO.File]::ReadLines($file.Path)) {
        $number++
        $remaining = Remove-Approved $line $approved
        $reported = @{}
        foreach ($check in $patterns) {
            foreach ($match in [regex]::Matches($remaining, $check.Pattern)) {
                $value = Get-Capture $match 'value'
                $list = Get-Capture $match 'list'
                $ports = if ($list) {
                    [regex]::Matches($list, '\d+') | ForEach-Object { $_.Result('$0') }
                } elseif ($value) {
                    [regex]::Matches($value, ':(\d{2,5})\b') | ForEach-Object { $_.Result('$1') }
                } else {
                    @((Get-Capture $match 'port'), (Get-Capture $match 'other')) | Where-Object { $_ }
                }
                foreach ($port in $ports) {
                    if ($port -ne $fixed -and -not $reported.ContainsKey($port)) {
                        $reported[$port] = $true
                        Write-Violation $file.Relative $number $rule "$($check.Label): port $port (the listening port is $fixed or an operator's)"
                        $count++
                    }
                }
            }
        }
    }
}
if ($count -gt 0) { exit 1 }
