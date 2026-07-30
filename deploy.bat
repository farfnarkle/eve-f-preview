@echo off
setlocal EnableExtensions

rem ============================================================================
rem  Configuration — edit DEPLOY_DIR to change where EVE-F-Preview.exe is copied
rem ============================================================================
set "DEPLOY_DIR=C:\Eve"
rem Deployed exe name: %DEPLOY_DIR%\EVE-F-Preview.exe
rem Settings live in %DEPLOY_DIR%\EVE-F-Preview.json and are NEVER overwritten by this script.

rem ============================================================================
rem  Build and deploy (paths are relative to this repo root)
rem ============================================================================
set "REPO_ROOT=%~dp0"
cd /d "%REPO_ROOT%"

set "PUBLISH_EXE=bin\net8.0-windows8.0\win-x64\publish\EVE-F-Preview.exe"

echo Building solution...
dotnet build "src\EVE-F-Preview.sln" -c Release --no-incremental
if errorlevel 1 goto :failed

echo Publishing single-file executable...
dotnet publish "src\Eve-F-Preview\Eve-F-Preview.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto :failed

if not exist "%PUBLISH_EXE%" (
    echo ERROR: Published exe not found: %REPO_ROOT%%PUBLISH_EXE%
    goto :failed
)

if not exist "%DEPLOY_DIR%\" mkdir "%DEPLOY_DIR%"

set "DEPLOY_EXE=%DEPLOY_DIR%\EVE-F-Preview.exe"

echo Stopping EVE-F-Preview (graceful, so settings can save)...
rem First try a clean close (no /F) so the app can flush EVE-F-Preview.json
taskkill /IM EVE-F-Preview.exe >nul 2>&1
taskkill /IM EVE-O-Preview.exe >nul 2>&1

rem Wait up to ~5s for the process to exit
set /a "_wait=0"
:wait_exit
tasklist /FI "IMAGENAME eq EVE-F-Preview.exe" 2>nul | find /I "EVE-F-Preview.exe" >nul
if errorlevel 1 goto :stopped
set /a "_wait+=1"
if %_wait% GEQ 5 goto :force_stop
timeout /t 1 /nobreak >nul
goto :wait_exit

:force_stop
echo Process still running — force stopping...
taskkill /IM EVE-F-Preview.exe /F >nul 2>&1
taskkill /IM EVE-O-Preview.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

:stopped
echo Copying exe only to %DEPLOY_DIR% (config JSON is left untouched)...
copy /Y "%PUBLISH_EXE%" "%DEPLOY_EXE%"
if errorlevel 1 goto :failed

echo Starting EVE-F-Preview...
rem /D sets working directory; config is resolved next to the exe either way
start "" /D "%DEPLOY_DIR%" "%DEPLOY_EXE%"

echo.
echo Deploy complete: %DEPLOY_EXE%
echo Settings file preserved: %DEPLOY_DIR%\EVE-F-Preview.json
exit /b 0

:failed
echo.
echo Deploy failed.
exit /b 1
