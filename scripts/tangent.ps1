<#
.SYNOPSIS
Builds, publishes, launches, stops, backs up and restores a Tangent Space.

.DESCRIPTION
pwsh scripts/tangent.ps1 <verb> [options], from any directory.

  build                      Verifies the pinned Koan checkout and builds the solution.
  publish                    Publishes publish/<platform>/: one self-contained executable for this
                             platform with its appsettings.json, Koan's koan.lock.json and the
                             static web assets in wwwroot/. Its data folder is created beside it on
                             the first start.
  launch [-Port <port>]      Starts the published executable, or `dotnet run` when nothing is
                             published, on port 5220 or the one given, and waits until
                             /health/live answers. Output goes to .local/launch/.
  stop                       Ends the Tangent Space this script launched.
  backup [-To <folder>]      Backs up the data folder into a new timestamped folder under <folder>
                             (default .local/backups): the database through SQLite's VACUUM INTO,
                             every other file copied, and backup.json naming each file and the
                             versions. Safe while the Tangent Space runs.
  restore -From <backup> [-Replace]
                             Puts a backup back into an empty or absent data folder. A data folder
                             that is not empty is refused unless -Replace is given, which first
                             moves it aside with a timestamp. Run it while the Tangent Space is
                             stopped.
  restore -Replace           A wipe is a restore of nothing: the data folder is moved aside with a
                             timestamp and the next launch begins a new Tangent Space.

Nothing here deletes a data folder. backup and restore run the executable's own verbs
(`Tangent.Server backup <folder>`, `Tangent.Server restore <backup> [--replace]`), so they work
wherever the executable runs, including inside the container compose.yaml starts.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'publish', 'launch', 'stop', 'backup', 'restore', 'help')]
    [string]$Verb = 'help',
    [ValidateRange(1, 65535)]
    [int]$Port = 5220,
    [string]$To,
    [string]$From,
    [switch]$Replace
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $root 'src/server/Tangent.Server.csproj'
$launchFolder = Join-Path $root '.local/launch'
$pidFile = Join-Path $launchFolder 'tangent.pid'

function Get-Platform {
    $os = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    "$os-$architecture"
}

function Get-PublishFolder { Join-Path $root "publish/$(Get-Platform)" }

function Get-PublishedExecutable {
    $name = if ($IsWindows) { 'Tangent.Server.exe' } else { 'Tangent.Server' }
    $path = Join-Path (Get-PublishFolder) $name
    if (Test-Path -LiteralPath $path -PathType Leaf) { $path }
}

function Resolve-OperatorPath([string]$Path) { $PSCmdlet.GetUnresolvedProviderPathFromPSPath($Path) }

function Invoke-Checked([string]$Description, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

# The process this script launched, or nothing when it has ended or another process took its id.
function Get-LaunchedProcess {
    if (-not (Test-Path -LiteralPath $pidFile)) { return }
    $id, $started = (Get-Content -LiteralPath $pidFile -Raw).Trim() -split ' '
    $process = Get-Process -Id ([int]$id) -ErrorAction SilentlyContinue
    if ($process -and $process.StartTime.ToUniversalTime().Ticks -eq [long]$started) { $process }
}

# Runs one of the executable's own verbs: the published executable, or the built project.
function Invoke-ServerVerb([string[]]$Arguments) {
    $executable = Get-PublishedExecutable
    if ($executable) {
        & $executable @Arguments
    } else {
        & dotnet run --no-build --no-launch-profile --project $serverProject -- @Arguments
    }
}

function Test-Live([int]$Port) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:$Port/health/live" -TimeoutSec 2 -SkipHttpErrorCheck
        $response.StatusCode -eq 200
    } catch { $false }
}

switch ($Verb) {
    'help' {
        Get-Help $PSCommandPath -Detailed | Out-String | Write-Output
    }
    'build' {
        & (Join-Path $PSScriptRoot 'prepare-framework.ps1')
        Invoke-Checked 'dotnet build' { dotnet build (Join-Path $root 'Tangent.sln') -nologo }
    }
    'publish' {
        if (Get-LaunchedProcess) { throw 'The Tangent Space is running; stop it before publishing over its executable.' }
        & (Join-Path $PSScriptRoot 'prepare-framework.ps1')
        $folder = Get-PublishFolder
        Invoke-Checked 'dotnet publish' {
            dotnet publish $serverProject -nologo -c Release -r (Get-Platform) --self-contained true -o $folder `
                -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
                -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false `
                -p:IsTransformWebConfigDisabled=true
        }
        Write-Output "Published $folder"
        Get-ChildItem -LiteralPath $folder | ForEach-Object { Write-Output "  $($_.Name)" }
    }
    'launch' {
        $running = Get-LaunchedProcess
        if ($running) { throw "The Tangent Space is already running (process $($running.Id)); stop it first." }
        if (Test-Live $Port) { throw "Something already answers on port $Port; choose another with -Port." }
        New-Item -ItemType Directory -Force -Path $launchFolder | Out-Null
        $urls = "--urls=http://localhost:$Port"
        $executable = Get-PublishedExecutable
        $start = @{
            RedirectStandardOutput = Join-Path $launchFolder 'tangent.log'
            RedirectStandardError = Join-Path $launchFolder 'tangent.error.log'
            PassThru = $true
        }
        if ($IsWindows) { $start.WindowStyle = 'Hidden' }
        $process = if ($executable) {
            Start-Process -FilePath $executable -ArgumentList $urls -WorkingDirectory (Split-Path -Parent $executable) @start
        } else {
            Start-Process -FilePath 'dotnet' -ArgumentList 'run', '--no-launch-profile', '--project', "`"$serverProject`"", '--', $urls -WorkingDirectory $root @start
        }
        "$($process.Id) $($process.StartTime.ToUniversalTime().Ticks)" | Set-Content -LiteralPath $pidFile
        $deadline = [DateTime]::UtcNow.AddSeconds(180)
        while (-not (Test-Live $Port)) {
            if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) {
                Get-Content -LiteralPath $start.RedirectStandardOutput, $start.RedirectStandardError -Tail 20 -ErrorAction SilentlyContinue
                throw "The Tangent Space did not answer /health/live on port $Port. Its output is in $launchFolder."
            }
            Start-Sleep -Milliseconds 500
        }
        $what = if ($executable) { $executable } else { 'dotnet run' }
        Write-Output "Tangent Space at http://localhost:$Port/ (process $($process.Id), $what). Output in $launchFolder."
    }
    'stop' {
        $process = Get-LaunchedProcess
        if (-not $process) {
            Remove-Item -LiteralPath $pidFile -ErrorAction SilentlyContinue
            Write-Output 'No Tangent Space launched by this script is running.'
            return
        }
        if (-not $IsWindows) {
            & (Get-Command kill -CommandType Application | Select-Object -First 1) -s TERM $process.Id
            $process.WaitForExit(15000) | Out-Null
        }
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit(15000) | Out-Null
        }
        Remove-Item -LiteralPath $pidFile
        Write-Output "Stopped the Tangent Space (process $($process.Id))."
    }
    'backup' {
        $destination = if ($To) { Resolve-OperatorPath $To } else { Join-Path $root '.local/backups' }
        Invoke-ServerVerb @('backup', $destination)
        exit $LASTEXITCODE
    }
    'restore' {
        if (-not $From -and -not $Replace) { throw 'restore needs -From <backup-folder>, -Replace, or both. Run tangent.ps1 help.' }
        if (Get-LaunchedProcess) { throw 'The Tangent Space is running; stop it before restoring.' }
        $arguments = @('restore')
        if ($From) { $arguments += Resolve-OperatorPath $From }
        if ($Replace) { $arguments += '--replace' }
        Invoke-ServerVerb $arguments
        exit $LASTEXITCODE
    }
}
