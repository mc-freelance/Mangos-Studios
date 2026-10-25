@echo off
rem Publish the Windows desktop application for release.
setlocal EnableExtensions

for %%I in ("%~dp0..\..") do set "ROOT_DIR=%%~fI\"
set "APP_PROJECT=%ROOT_DIR%src\MangosDatabaseStudio.App\MangosDatabaseStudio.App.csproj"
if defined MDS_PUBLISH_DIR (set "OUTPUT_DIR=%MDS_PUBLISH_DIR%") else set "OUTPUT_DIR=%ROOT_DIR%.artifacts\windows-x64"
if defined MDS_RUNTIME (set "RUNTIME=%MDS_RUNTIME%") else set "RUNTIME=win-x64"
set "SELF_CONTAINED=false"
set "PUBLISH_VERSION="
set "PUBLISH_STAGE="

:ParseArguments
if /I "%~1"=="--version" (
    if "%~2"=="" (call :Fail "--version requires a value." & exit /b 2)
    set "PUBLISH_VERSION=%~2"
    shift
    shift
    goto ParseArguments
)
if /I "%~1"=="--stage" (
    if "%~2"=="" (call :Fail "--stage requires a value." & exit /b 2)
    set "PUBLISH_STAGE=%~2"
    shift
    shift
    goto ParseArguments
)
if "%~1"=="" goto ArgumentsParsed
if /I "%~1"=="--output" (
    if "%~2"=="" (call :Fail "--output requires a directory." & exit /b 2)
    set "OUTPUT_DIR=%~2"
    shift
    shift
    goto ParseArguments
)
if /I "%~1"=="--runtime" (
    if "%~2"=="" (call :Fail "--runtime requires a runtime identifier." & exit /b 2)
    set "RUNTIME=%~2"
    shift
    shift
    goto ParseArguments
)
if /I "%~1"=="--self-contained" (
    set "SELF_CONTAINED=true"
    shift
    goto ParseArguments
)
if /I "%~1"=="-h" goto ShowHelp
if /I "%~1"=="--help" goto ShowHelp
call :Fail "Unknown option: %~1"
exit /b 2

:ArgumentsParsed
where dotnet.exe >nul 2>&1 || (call :Fail "The .NET 10 SDK is not installed or is not in PATH." & exit /b 2)
for /f "tokens=1 delims=." %%V in ('dotnet --version 2^>nul') do set "DOTNET_MAJOR=%%V"
if not "%DOTNET_MAJOR%"=="10" (call :Fail "This project requires the .NET 10 SDK." & exit /b 2)
if not exist "%APP_PROJECT%" (call :Fail "Application project was not found: %APP_PROJECT%" & exit /b 2)

if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
mkdir "%OUTPUT_DIR%" >nul 2>&1
set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

echo Publishing single-process MaNGOS Database Studio for %RUNTIME%...
set "VERSION_PROPERTIES="
if defined PUBLISH_VERSION set "VERSION_PROPERTIES=-p:Version=%PUBLISH_VERSION% -p:StudioVersionOverride=%PUBLISH_VERSION%"
if defined PUBLISH_STAGE set "VERSION_PROPERTIES=%VERSION_PROPERTIES% -p:StudioReleaseStage=%PUBLISH_STAGE%"
dotnet publish "%APP_PROJECT%" ^
    --configuration Release ^
    --runtime "%RUNTIME%" ^
    --self-contained %SELF_CONTAINED% ^
    --output "%OUTPUT_DIR%" ^
    -p:UseAppHost=true ^
    -p:PublishTrimmed=false %VERSION_PROPERTIES%
if errorlevel 1 exit /b %ERRORLEVEL%

if exist "%OUTPUT_DIR%\App_Data" rmdir /s /q "%OUTPUT_DIR%\App_Data"
for %%D in (imports exports database keys avatars) do mkdir "%OUTPUT_DIR%\App_Data\%%D" >nul 2>&1
>"%OUTPUT_DIR%\App_Data\README.txt" echo Runtime application data is created here. Do not distribute populated App_Data contents.

>"%OUTPUT_DIR%\start.bat" echo @echo off
>>"%OUTPUT_DIR%\start.bat" echo "%%~dp0MangosDatabaseStudio.exe"
>>"%OUTPUT_DIR%\start.bat" echo exit /b %%ERRORLEVEL%%

for %%F in (LICENSE README.md REQUIREMENTS.md DEVICE_IDENTITY.md PRIVACY.md) do if exist "%ROOT_DIR%%%F" copy /y "%ROOT_DIR%%%F" "%OUTPUT_DIR%\%%F" >nul

echo.
echo Publish completed: %OUTPUT_DIR%
echo Run directly     : %OUTPUT_DIR%\MangosDatabaseStudio.exe
echo Optional wrapper: %OUTPUT_DIR%\start.bat
exit /b 0

:Fail
echo. 1>&2
echo Error: %~1 1>&2
exit /b 2

:ShowHelp
echo Publish Mangos Database Studio for Windows
echo.
echo Usage:
echo   scripts\publish-windows.bat [options]
echo.
echo Options:
echo   --output ^<directory^>   Publish destination
echo   --runtime ^<rid^>        Runtime identifier. Default: win-x64
echo   --self-contained       Include the .NET runtime
echo   --version ^<version^>    Override the Git tag for the embedded version
echo   --stage ^<name^>         Set the release stage for package builds
echo   -h, --help             Show this help
echo.
echo The published package is desktop-only. Run MangosDatabaseStudio.exe directly.
exit /b 0
