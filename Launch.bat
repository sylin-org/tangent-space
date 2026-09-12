@echo off
rem Tangent Space Docker lifecycle: LAUNCH the Tangent app container.
rem Prepares missing local configuration (retains an existing one byte-for-byte),
rem then starts only the tangent service. No fixture network is needed by default.
rem Add -Build to rebuild first. Experimental Spaces fixtures require -UseFixtureNetwork;
rem add -MigrateWindowsState only for their one-time legacy Windows migration.
setlocal
set "ROOT=%~dp0"
where pwsh >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required but was not found on PATH. 1>&2
    exit /b 9009
)
pwsh -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\server-lifecycle.ps1" -Action Launch %*
exit /b %ERRORLEVEL%
