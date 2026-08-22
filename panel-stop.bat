@echo off
rem Double-click this file to stop the dashboard (and the assistant, if running).
rem
rem ASCII-only on purpose, same reason as panel.bat: cmd.exe reads .bat in the OEM
rem codepage, so Chinese text inside a .bat is garbage. Real logic: Tools\stop.ps1
rem (the dashboard exits via its stop file; it is only killed if it overstays).
rem Stopping matters beyond tidiness: while services run they hold their DLLs,
rem and every dotnet build / gate run fails on locked files.
setlocal
chcp 65001 >nul
cd /d "%~dp0"

where pwsh >nul 2>nul
if errorlevel 1 goto nopwsh

pwsh -NoProfile -ExecutionPolicy Bypass -File "Tools\stop.ps1"
if errorlevel 1 goto failed

endlocal
exit /b 0

:nopwsh
echo.
echo PowerShell 7 (pwsh) not found. Install it:  winget install Microsoft.PowerShell
echo.
pause
endlocal
exit /b 1

:failed
echo.
echo Stopping reported an error; the reason is printed above.
echo.
pause
endlocal
exit /b 1
