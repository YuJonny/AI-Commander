@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

set "SOURCE=%~dp0"
set "TARGET=%USERPROFILE%\Downloads\Commander-Windows"

echo.
if /I "%SOURCE%"=="%TARGET%\" goto build

echo   [1/3] Updating the local Windows source...
if not exist "%TARGET%" mkdir "%TARGET%"

robocopy "%SOURCE%" "%TARGET%" /E /XD bin obj publish /XF .DS_Store "在Windows双击这里-更新并编译.bat" /R:2 /W:1 /NFL /NDL /NJH /NJS /NP
if errorlevel 8 goto failed

:build
echo   [2/3] Building AI Commander v2.1...
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%TARGET%\setup_and_build.ps1"
if errorlevel 1 goto failed

if /I "%SOURCE%"=="%TARGET%\" goto done
set "RELEASE_DIR=%SOURCE%..\接线员\终版分发"
if not exist "!RELEASE_DIR!" goto release_folder_missing

echo.
echo   [3/3] Copying AI.Commander_Win_v2.1.exe to the final distribution folder...
copy /Y "%TARGET%\publish\Commander.exe" "!RELEASE_DIR!\AI.Commander_Win_v2.1.exe" >nul
if errorlevel 1 goto failed
echo   Done: !RELEASE_DIR!\AI.Commander_Win_v2.1.exe

:done
exit /b 0

:release_folder_missing
echo.
echo   Build succeeded. The final distribution folder was not found, so the EXE remains here:
echo   %TARGET%\publish\Commander.exe
goto done

:failed
echo.
echo   Update or build failed. Please send a screenshot of this window.
pause
exit /b 1
