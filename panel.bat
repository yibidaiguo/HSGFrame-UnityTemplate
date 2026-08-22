@echo off
rem Double-click this file to start the dashboard and open it in a browser.
rem
rem This file is deliberately ASCII-only: cmd.exe reads .bat in the OEM codepage
rem (936 on a Chinese Windows), so Chinese text inside a .bat comes out as garbage
rem and can even break parsing. Every human-facing message lives in the PowerShell
rem script instead, where UTF-8 works. Real logic: Tools\panel-open.ps1
rem
rem This starts the dashboard AND the Feishu assistant (the chat bot). The bot only
rem receives messages while its long-connection sidecar is running: anything sent to it
rem while nothing is up is dropped by Feishu and cannot be recovered by restarting later.
rem A machine with no Feishu credentials configured skips the bot and says so.
rem
rem   panel.bat         build what it needs, start dashboard + bot, open browser
rem   panel.bat /skip   skip the build, run whatever was built last time
rem   panel.bat /nobot  dashboard only, do not start the Feishu assistant
rem   panel.bat 8790    use that port instead of the default 8766
rem   panel.bat 8790 /skip /nobot   any combination
setlocal
chcp 65001 >nul
cd /d "%~dp0"

set "PSARGS="
:parse
if "%~1"=="" goto run
if /i "%~1"=="/skip" (
    set "PSARGS=%PSARGS% -SkipBuild"
) else if /i "%~1"=="/nobot" (
    set "PSARGS=%PSARGS% -NoAssistant"
) else (
    rem Anything else is taken as the port number; panel-open.ps1 rejects non-numbers.
    set "PSARGS=%PSARGS% -Port %~1"
)
shift
goto parse

:run
where pwsh >nul 2>nul
if errorlevel 1 goto nopwsh

pwsh -NoProfile -ExecutionPolicy Bypass -File "Tools\panel-open.ps1"%PSARGS%
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
echo The dashboard did not start, or the port belongs to another repository.
echo The reason and the next step are printed above.
echo.
pause
endlocal
exit /b 1
