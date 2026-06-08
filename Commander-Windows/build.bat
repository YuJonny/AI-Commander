@echo off
echo ================================
echo   Building Commander (接线员)
echo ================================
echo.

REM Check if dotnet is available
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET SDK not found. Please install .NET 8 SDK from:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

REM Check if icon files exist, create placeholder if not
if not exist "Resources\app.ico" (
    echo [INFO] No app.ico found, building without icon...
    echo [INFO] You can add your own app.ico to the Resources folder later.
)

REM Build
echo Building...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

if %ERRORLEVEL% equ 0 (
    echo.
    echo ================================
    echo   Build successful!
    echo   Output: publish\Commander.exe
    echo ================================
) else (
    echo.
    echo [ERROR] Build failed.
)
pause
