@echo off
setlocal
where pwsh >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required. 1>&2
    exit /b 9009
)
pwsh -NoProfile -File "%~dp0scripts\docker-state.ps1" -Action Restore %*
exit /b %ERRORLEVEL%
