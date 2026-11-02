@echo off
rem Windows release build: bin\win-x64\CPSystemMonitor.exe / bin\win-arm64\CPSystemMonitor.exe
rem Usage: build-release.bat                (both win-x64 and win-arm64)
rem        build-release.bat win-x64        (one RID only)
rem        build-release.bat -Zip           (also make bin\release\CPSystemMonitor-<rid>.zip)
rem        build-release.bat win-x64 -Zip
rem The build itself lives in src\build\publish-win.ps1.
setlocal
cd /d "%~dp0"

set "ARG1=%~1"
if "%ARG1%"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "src\build\publish-win.ps1"
) else if "%ARG1:~0,1%"=="-" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "src\build\publish-win.ps1" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "src\build\publish-win.ps1" -Rid %*
)
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" echo [build-release] FAILED (exit code %RC%)

rem Keep the window open when launched by double-click from Explorer
echo %CMDCMDLINE% | find /i "/c" >nul && pause

exit /b %RC%
