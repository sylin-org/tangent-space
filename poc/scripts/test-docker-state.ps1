# File-copy tests only: no second server, and every Docker command is replaced by this local function.
$ErrorActionPreference = 'Stop'
$testRepo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRoot = [IO.Path]::GetFullPath((Join-Path $testRepo ('.local/state-copy-test-' + [guid]::NewGuid().ToString('n'))))
$allowedTestRoot = [IO.Path]::GetFullPath((Join-Path $testRepo '.local')) + [IO.Path]::DirectorySeparatorChar
if (-not $testRoot.StartsWith($allowedTestRoot)) { throw 'Unexpected test root.' }
$global:TangentStateTestDockerCalls = [Collections.Generic.List[string]]::new()
function docker { $global:TangentStateTestDockerCalls.Add(($args -join ' ')); $global:LASTEXITCODE = 0 }
function Check([bool]$Condition, [string]$Label) { if (-not $Condition) { throw "FAIL: $Label" }; Write-Output "PASS: $Label" }
try {
    $testScripts = Join-Path $testRoot 'scripts'
    $testState = Join-Path $testRoot '.local/docker/site'
    New-Item -ItemType Directory -Path $testScripts,(Join-Path $testState 'data/keys') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docker-state.ps1') -Destination $testScripts
    $copyScript = Join-Path $testScripts 'docker-state.ps1'
    '{"test":true}' | Set-Content (Join-Path $testState 'appsettings.json')
    [IO.File]::WriteAllBytes((Join-Path $testState 'tangent.sqlite'), [byte[]](0,1,2,3,255))
    'original-key' | Set-Content (Join-Path $testState 'data/keys/test.xml')
    $snapshot = Join-Path $testRoot 'snapshot'
    & $copyScript -Action Backup -Path $snapshot
    Check (Test-Path (Join-Path $snapshot 'backup.json')) 'Backup includes an integrity manifest'
    Check ((Get-FileHash (Join-Path $snapshot 'state/tangent.sqlite')).Hash -eq (Get-FileHash (Join-Path $testState 'tangent.sqlite')).Hash) 'Database copy is byte-identical'
    'changed-key' | Set-Content (Join-Path $testState 'data/keys/test.xml')
    'stale' | Set-Content (Join-Path $testState 'stale.txt')
    & $copyScript -Action Restore -Path $snapshot -WhatIf
    Check (Test-Path (Join-Path $testState 'stale.txt')) 'Restore dry run preserves current files'
    & $copyScript -Action Restore -Path $snapshot -Force
    Check (-not (Test-Path (Join-Path $testState 'stale.txt'))) 'Restore replaces the whole state tree'
    Check ((Get-Content (Join-Path $testState 'data/keys/test.xml')) -eq 'original-key') 'Restore recovers keys'
    Check (@(Get-ChildItem (Join-Path $testRoot '.local/backups') -Directory).Count -eq 1) 'Restore backs up displaced current state'
    $before = $global:TangentStateTestDockerCalls.Count
    'corrupt' | Set-Content (Join-Path $snapshot 'state/tangent.sqlite')
    $rejected = $false
    try { & $copyScript -Action Restore -Path $snapshot -Force } catch { $rejected = $true }
    Check $rejected 'Corrupted snapshot is rejected'
    Check ($before -eq $global:TangentStateTestDockerCalls.Count) 'Integrity rejection happens before stopping Docker'
    Check (@($global:TangentStateTestDockerCalls | Where-Object { $_ -notin @('compose ps --status running --services', 'compose stop tangent', 'compose rm --force tangent') }).Count -eq 0) 'Only the Tangent service is addressed'
} finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTestRoot.StartsWith($allowedTestRoot) -or (Get-Item -LiteralPath $resolvedTestRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unsafe test cleanup target.' }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}

