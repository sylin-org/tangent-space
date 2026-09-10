@echo off
rem Tangent Space Docker lifecycle: BUILD the Tangent image.
rem Verifies the pinned framework contribution, then runs docker compose build tangent.
rem Nothing is stopped or removed and the protocol network is not touched.
setlocal
set "ROOT=%~dp0"
where pwsh >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required but was not found on PATH. 1>&2
    exit /b 9009
)
pwsh -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\docker-lifecycle.ps1" -Action Build %*
exit /b %ERRORLEVEL%
