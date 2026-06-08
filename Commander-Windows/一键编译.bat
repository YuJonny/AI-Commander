@echo off
chcp 65001 >nul
echo.
echo   Launching build script...
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0setup_and_build.ps1"
