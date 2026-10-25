@echo off
rem Launch the local desktop application from source.
setlocal EnableExtensions

for %%I in ("%~dp0..\..") do set "ROOT_DIR=%%~fI\"
set "APP_PROJECT=%ROOT_DIR%src\MangosDatabaseStudio.App\MangosDatabaseStudio.App.csproj"

where dotnet.exe >nul 2>&1 || (
    echo Error: The .NET 10 SDK is not installed or is not in PATH. 1>&2
    exit /b 2
)

for /f "tokens=1 delims=." %%V in ('dotnet --version 2^>nul') do set "DOTNET_MAJOR=%%V"
if not "%DOTNET_MAJOR%"=="10" (
    echo Error: This project requires the .NET 10 SDK. 1>&2
    exit /b 2
)

if not exist "%APP_PROJECT%" (
    echo Error: Application project was not found: %APP_PROJECT% 1>&2
    exit /b 2
)

set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "MDS_CONTENT_ROOT=%ROOT_DIR%src\MangosDatabaseStudio.App"

dotnet run --project "%APP_PROJECT%" --configuration Debug -- %*
exit /b %ERRORLEVEL%
