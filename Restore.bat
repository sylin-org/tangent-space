@echo off
rem Tangent Space: puts a backup back with -From, and -Replace moves a non-empty data folder aside first.
rem Arguments pass through to scripts\tangent.ps1 restore; "pwsh scripts\tangent.ps1 help" lists every verb.
setlocal
where pwsh >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required but was not found on PATH. 1>&2
    exit /b 9009
)
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\tangent.ps1" restore %*
exit /b %ERRORLEVEL%
