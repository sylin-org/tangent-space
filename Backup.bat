@echo off
rem Tangent Space: backs up the data folder into .local\backups, or the folder given with -To.
rem Arguments pass through to scripts\tangent.ps1 backup; "pwsh scripts\tangent.ps1 help" lists every verb.
setlocal
where pwsh >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required but was not found on PATH. 1>&2
    exit /b 9009
)
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\tangent.ps1" backup %*
exit /b %ERRORLEVEL%
