@echo off
setlocal
set "installDir=%LOCALAPPDATA%\RDP Sessions"
set "launcher=%installDir%\RDP Sessions.exe"
if not exist "%~dp0RDP Sessions.exe" goto missing
if not exist "%installDir%" mkdir "%installDir%" || goto failed
if exist "%launcher%" (
    copy /y "%launcher%" "%installDir%\RDP Sessions.previous.exe" >nul || goto failed
)
copy /y "%~dp0RDP Sessions.exe" "%launcher%" >nul || goto failed
start "" /wait "%launcher%" --register
if errorlevel 1 goto failed
if /i not "%~1"=="--quiet" start "" "%launcher%"
exit /b 0

:missing
echo Extract the ZIP first, then run Install.cmd from the extracted folder.
goto stop

:failed
echo Setup could not finish. Close RDP Sessions if it is open, then try again.

:stop
if /i not "%~1"=="--quiet" pause
exit /b 1
