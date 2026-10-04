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

rem Windows' own tools by full path: with Git's Unix tools on PATH, a bare "find" can be Git's
rem find.exe, which made the "is it still running?" check below always answer "no".
set "SYS32=%SystemRoot%\System32"

set "PROJECT=src\Eve-F-Preview\Eve-F-Preview.csproj"
set "PUBLISH_EXE=bin\net8.0-windows8.0\win-x64\publish\EVE-F-Preview.exe"
set "PUBLISH_ARGS=-c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true"

rem The old exe goes first, so a publish that silently produced nothing can't deploy it again.
if exist "%PUBLISH_EXE%" del /q "%PUBLISH_EXE%"

echo Building EVE-F-Preview...
rem A full rebuild of exactly what gets published (same runtime and options); publish then only
rem packages it. Only the app is built - the solution also holds Eve-F-Mock, a fake EVE window.
dotnet build "%PROJECT%" %PUBLISH_ARGS% --no-incremental
if errorlevel 1 goto :failed

echo Publishing single-file executable...
dotnet publish "%PROJECT%" %PUBLISH_ARGS% --no-build
if errorlevel 1 goto :failed

if not exist "%PUBLISH_EXE%" (
    echo ERROR: Published exe not found: %REPO_ROOT%%PUBLISH_EXE%
    goto :failed
)

if not exist "%DEPLOY_DIR%\" mkdir "%DEPLOY_DIR%"

set "DEPLOY_EXE=%DEPLOY_DIR%\EVE-F-Preview.exe"

echo Stopping EVE-F-Preview (graceful, so settings can save)...
rem First try a clean close (no /F) so the app can flush EVE-F-Preview.json
"%SYS32%\taskkill.exe" /IM EVE-F-Preview.exe >nul 2>&1
"%SYS32%\taskkill.exe" /IM EVE-O-Preview.exe >nul 2>&1

rem Wait up to ~5s for the process to exit
set /a "_wait=0"
:wait_exit
"%SYS32%\tasklist.exe" /FI "IMAGENAME eq EVE-F-Preview.exe" 2>nul | "%SYS32%\find.exe" /I "EVE-F-Preview.exe" >nul
if errorlevel 1 goto :stopped
set /a "_wait+=1"
if %_wait% GEQ 5 goto :force_stop
"%SYS32%\timeout.exe" /t 1 /nobreak >nul
goto :wait_exit

:force_stop
echo Process still running — force stopping...
"%SYS32%\taskkill.exe" /IM EVE-F-Preview.exe /F >nul 2>&1
"%SYS32%\taskkill.exe" /IM EVE-O-Preview.exe /F >nul 2>&1
"%SYS32%\timeout.exe" /t 1 /nobreak >nul

:stopped
echo Copying exe only to %DEPLOY_DIR% (config JSON is left untouched)...
copy /Y "%PUBLISH_EXE%" "%DEPLOY_EXE%"
if errorlevel 1 goto :failed

rem Don't report success unless the exe in place really is the one just built.
"%SYS32%\fc.exe" /b "%PUBLISH_EXE%" "%DEPLOY_EXE%" >nul
if errorlevel 1 (
    echo ERROR: %DEPLOY_EXE% is not the exe that was just built - is EVE-F-Preview still running?
    goto :failed
)

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
