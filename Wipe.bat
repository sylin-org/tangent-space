@echo off
rem Tangent Space Docker lifecycle: WIPE the local Docker state directory.
rem Stops/removes only the Compose tangent service, then deletes .local\docker\site.
rem Interactive use requires typing WIPE. Pass -WhatIf for a dry run.
setlocal
set "ROOT=%~dp0"
where pwsh >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required but was not found on PATH. 1>&2
    exit /b 9009
)
pwsh -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\docker-lifecycle.ps1" -Action Wipe %*
exit /b %ERRORLEVEL%
