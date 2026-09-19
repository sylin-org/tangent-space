@echo off
rem Tangent Space Docker lifecycle: FULL cycle for serial deploy tests.
rem Wipes the site state, rebuilds the image and the connector, launches, and opens
rem the app. Runs start to finish without a single prompt.
rem
rem This DESTROYS .local/docker/site and takes NO backup, by design: it exists to
rem exercise a first run over and over, where the data is disposable. Use Backup.bat
rem first if this install holds anything you want to keep, or Wipe/Build/Launch
rem separately when you only mean one of them.
rem
rem The connector keeps its own state in your user profile and is untouched; after a
rem wipe its saved enrollment is dead, so run `companion-lobby forget` then connect
rem again before testing an agent.
setlocal
set "ROOT=%~dp0"
where pwsh >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required but was not found on PATH. 1>&2
    exit /b 9009
)

echo [1/3] Wiping the site state...
call "%ROOT%Wipe.bat" -Force
if errorlevel 1 (
    echo full: wipe failed; nothing was rebuilt or launched. 1>&2
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Building the image and the connector...
call "%ROOT%Build.bat"
if errorlevel 1 (
    echo full: build failed; the app was not launched and the state is already wiped. 1>&2
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Launching...
call "%ROOT%Launch.bat"
if errorlevel 1 (
    echo full: launch failed. 1>&2
    exit /b %ERRORLEVEL%
)

rem Launch waits for the container to report healthy, so the app answers by now.
rem A wiped install is unclaimed, so the root serves the arrival and the owner claim.
start "" "http://127.0.0.1:5220/"
echo.
echo Fresh install ready at http://127.0.0.1:5220/ - unclaimed, so it opens at the claim.
exit /b 0
