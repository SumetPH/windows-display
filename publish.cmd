@echo off
rem Runs publish.ps1 without changing the machine's Execution Policy.
rem Usage: publish [-Run] [-OutputPath <folder>]
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*
if errorlevel 1 (
    echo.
    echo Publish failed.
    pause
    exit /b 1
)
