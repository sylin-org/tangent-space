@echo off
rem Tangent Space Docker lifecycle: LAUNCH the Tangent app container.
rem Prepares a missing local configuration (retains an existing one byte-for-byte),
rem then starts only the tangent service. Add -Build to rebuild first.
setlocal
set "ROOT=%~dp0"
where pwsh >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required but was not found on PATH. 1>&2
    exit /b 9009
)
pwsh -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\server-lifecycle.ps1" -Action Launch %*
exit /b %ERRORLEVEL%
