@echo off
rem Restore, build, test, and check JavaScript before publishing.
setlocal EnableExtensions

for %%I in ("%~dp0..") do set "ROOT_DIR=%%~fI\"
set "SOLUTION=%ROOT_DIR%MangosDatabaseStudio.sln"
set "JS_DIR=%ROOT_DIR%src\MangosDatabaseStudio.App\wwwroot\js"
set "CONFIGURATION=Release"

if /I "%~1"=="--debug" set "CONFIGURATION=Debug"
if /I "%~1"=="-h" goto ShowHelp
if /I "%~1"=="--help" goto ShowHelp
if not "%~1"=="" if /I not "%~1"=="--debug" (
    echo Error: Unknown option: %~1 1>&2
    exit /b 2
)

where dotnet.exe >nul 2>&1 || (
    echo Error: The .NET 9 SDK is not installed or is not in PATH. 1>&2
    exit /b 2
)
for /f "tokens=1 delims=." %%V in ('dotnet --version 2^>nul') do set "DOTNET_MAJOR=%%V"
if not "%DOTNET_MAJOR%"=="9" (
    echo Error: This project requires the .NET 9 SDK. 1>&2
    exit /b 2
)
if not exist "%SOLUTION%" (
    echo Error: Solution was not found: %SOLUTION% 1>&2
    exit /b 2
)

set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
pushd "%ROOT_DIR%" >nul

echo.
echo [1/4] Restoring solution...
dotnet restore "%SOLUTION%"
if errorlevel 1 goto Failed

echo.
echo [2/4] Building solution ^(%CONFIGURATION%^)...
dotnet build "%SOLUTION%" --configuration "%CONFIGURATION%" --no-restore
if errorlevel 1 goto Failed

echo.
echo [3/4] Running .NET tests...
dotnet test "%SOLUTION%" --configuration "%CONFIGURATION%" --no-build --no-restore
if errorlevel 1 goto Failed

echo.
echo [4/4] Checking JavaScript syntax...
where node.exe >nul 2>&1
if errorlevel 1 (
    echo Node.js is not installed. JavaScript syntax checks were skipped.
) else if exist "%JS_DIR%" (
    for /r "%JS_DIR%" %%F in (*.js) do (
        node --check "%%F"
        if errorlevel 1 goto Failed
    )
) else (
    echo JavaScript directory was not found. Nothing to check.
)

echo.
echo Validation completed successfully.
popd >nul
exit /b 0

:Failed
set "EXIT_CODE=%ERRORLEVEL%"
echo.
echo Validation failed with exit code %EXIT_CODE%. 1>&2
popd >nul
exit /b %EXIT_CODE%

:ShowHelp
echo Mangos Database Studio Windows validation
echo.
echo Usage:
echo   scripts\validate-windows.bat [--debug]
echo.
echo The script restores, builds, runs .NET tests, and checks JavaScript syntax when Node.js is installed.
exit /b 0
