param()
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$experimentRoot = Join-Path $repoRoot ('.local/experiments/epic005/sqlite-baseline-' + [Guid]::NewGuid().ToString('N'))
$freeMiB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024
if ($freeMiB -lt 8192) { throw 'Need at least 8 GiB free host memory before this baseline.' }
$env:DOTNET_PROCESSOR_COUNT = '4'
dotnet build (Join-Path $PSScriptRoot 'ScaleProbe.csproj') -c Release --nologo -m:4 -v:q -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { throw 'ScaleProbe build failed.' }
dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0/ScaleProbe.dll') --self-test
if ($LASTEXITCODE -ne 0) { throw 'ScaleProbe validation self-test failed.' }
dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0/ScaleProbe.dll') --repo $repoRoot --root $experimentRoot
if ($LASTEXITCODE -ne 0) { throw "ScaleProbe failed; isolated state is retained in $experimentRoot" }
