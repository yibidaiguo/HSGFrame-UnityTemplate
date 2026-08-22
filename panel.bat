@echo off
rem Double-click this file to start the dashboard and open it in a browser.
rem
rem This file is deliberately ASCII-only: cmd.exe reads .bat in the OEM codepage
rem (936 on a Chinese Windows), so Chinese text inside a .bat comes out as garbage
rem and can even break parsing. Every human-facing message lives in the PowerShell
rem script instead, where UTF-8 works. Real logic: Tools\panel-open.ps1
rem
rem   panel.bat        build the two projects it needs, start, open browser
rem   panel.bat /skip  skip the build, run whatever was built last time
setlocal
chcp 65001 >nul
cd /d "%~dp0"

set "PSARGS="
if /i "%~1"=="/skip" set "PSARGS=-SkipBuild"

where pwsh >nul 2>nul
if errorlevel 1 goto nopwsh

pwsh -NoProfile -ExecutionPolicy Bypass -File "Tools\panel-open.ps1" %PSARGS%
if errorlevel 1 goto failed

rem Success: the browser is open, this console window has no further use.
endlocal
exit /b 0

:nopwsh
echo.
echo PowerShell 7 (pwsh) not found. These scripts need it; Windows PowerShell 5.1 will not do.
echo Install it:  winget install Microsoft.PowerShell
echo Then open a new console window and run this file again.
echo.
pause
endlocal
exit /b 1

:failed
echo.
echo The dashboard did not start. The reason and the next step are printed above.
echo.
pause
endlocal
exit /b 1
