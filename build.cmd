@echo off
rem Runs build.ps1 without changing the machine's Execution Policy.
rem Usage: build [-Configuration Debug]
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)
