@echo off
rem Package Windows desktop releases after validation.
setlocal EnableExtensions

for %%I in ("%~dp0..\..") do set "ROOT_DIR=%%~fI\"
set "VERSION="
set "STAGE=Alpha"
set "RUNTIME=win-x64"
set "OUTPUT_ROOT=%ROOT_DIR%.artifacts\packages"
set "RUN_TESTS=1"
set "CREATE_ARCHIVE=1"

:ParseArguments
if "%~1"=="" goto ArgumentsParsed
if /I "%~1"=="--version" (
    if "%~2"=="" (call :Fail "--version requires a value." & exit /b 2)
    set "VERSION=%~2"
    shift
    shift
    goto ParseArguments
)
if /I "%~1"=="--stage" (
    if "%~2"=="" (call :Fail "--stage requires a value." & exit /b 2)
    set "STAGE=%~2"
    shift
    shift
    goto ParseArguments
)
if /I "%~1"=="--runtime" (
    if "%~2"=="" (call :Fail "--runtime requires a value." & exit /b 2)
    set "RUNTIME=%~2"
    shift
    shift
    goto ParseArguments
)
if /I "%~1"=="--output" (
    if "%~2"=="" (call :Fail "--output requires a directory." & exit /b 2)
    set "OUTPUT_ROOT=%~2"
    shift
    shift
    goto ParseArguments
)
if /I "%~1"=="--skip-tests" (
    set "RUN_TESTS=0"
    shift
    goto ParseArguments
)
if /I "%~1"=="--no-archive" (
    set "CREATE_ARCHIVE=0"
    shift
    goto ParseArguments
)
if /I "%~1"=="-h" goto ShowHelp
if /I "%~1"=="--help" goto ShowHelp
call :Fail "Unknown option: %~1"
exit /b 2

:ArgumentsParsed
rem Resolve the nearest reachable release tag when --version was not supplied.
if not defined VERSION (
    where git.exe >nul 2>&1 || (call :Fail "Git is required to detect a release tag; pass --version instead." & exit /b 2)
    for /f "delims=" %%T in ('git -C "%ROOT_DIR%" describe --tags --abbrev=0 --match=v[0-9]* --match=v.[0-9]* 2^>nul') do set "VERSION=%%T"
)
if not defined VERSION (call :Fail "No version tag was found; create v.0.2.5-beta or pass --version." & exit /b 2)
rem Support version tags beginning with either v or v., retaining the prerelease label.
if /I "%VERSION:~0,2%"=="v." (
    set "VERSION=%VERSION:~2%"
) else if /I "%VERSION:~0,1%"=="v" (
    set "VERSION=%VERSION:~1%"
)
rem Validate SemVer, including an optional prerelease suffix such as -beta.
powershell -NoProfile -Command "if ($env:VERSION -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+(\.[0-9A-Za-z-]+)*)?$') { exit 1 }"
if errorlevel 1 (call :Fail "Version must use major.minor.patch with optional prerelease, for example 0.2.5-beta." & exit /b 2)
set "COMMIT_HASH=unknown"
for /f "delims=" %%H in ('git -C "%ROOT_DIR%" rev-parse --short=8 HEAD 2^>nul') do set "COMMIT_HASH=%%H"
where dotnet.exe >nul 2>&1 || (call :Fail "The .NET 10 SDK is not installed or is not in PATH." & exit /b 2)
where powershell.exe >nul 2>&1 || (call :Fail "Windows PowerShell is required for checksums and ZIP creation." & exit /b 2)

set "PACKAGE_NAME=MangosDatabaseStudio-%VERSION%-%STAGE%-%RUNTIME%"
set "PACKAGE_DIR=%OUTPUT_ROOT%\%PACKAGE_NAME%"
set "ARCHIVE=%OUTPUT_ROOT%\%PACKAGE_NAME%.zip"

if not exist "%OUTPUT_ROOT%" mkdir "%OUTPUT_ROOT%" >nul 2>&1
if "%RUN_TESTS%"=="1" (
    echo Validating solution before packaging...
    call "%~dp0validate-windows.bat"
    if errorlevel 1 exit /b 1
)

echo.
echo Publishing self-contained Windows application...
call "%~dp0publish-windows.bat" --output "%PACKAGE_DIR%" --runtime "%RUNTIME%" --self-contained --version "%VERSION%" --stage "%STAGE%"
if errorlevel 1 exit /b %ERRORLEVEL%

for %%F in (LICENSE README.md REQUIREMENTS.md DEVICE_IDENTITY.md PRIVACY.md) do if exist "%ROOT_DIR%%%F" copy /y "%ROOT_DIR%%%F" "%PACKAGE_DIR%\%%F" >nul

>"%PACKAGE_DIR%\README-PACKAGE.txt" echo Mangos Database Studio - %STAGE%
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo =====================================
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo.
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo Version: %VERSION%
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo Commit: %COMMIT_HASH%
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo Runtime: %RUNTIME%
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo.
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo STARTUP
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo -------
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo MangosDatabaseStudio.exe  Run the desktop application directly
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo start.bat              Optional launcher wrapper
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo.
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo Device Identity is disabled by default. Enable it with:
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo   MangosDatabaseStudio.exe --device-identity
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo.
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo SQL imports require mysql.exe or mariadb.exe in PATH.
>>"%PACKAGE_DIR%\README-PACKAGE.txt" echo SQL exports require mysqldump.exe or mariadb-dump.exe in PATH.

powershell -NoProfile -Command "$utc=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'); @('Mangos Database Studio','Application version: %VERSION%','Source commit: %COMMIT_HASH%','Release stage: %STAGE%','Runtime identifier: %RUNTIME%','Build time (UTC): '+$utc,'Framework: .NET 10 self-contained','Configuration: Release') | Set-Content -Encoding UTF8 -Path '%PACKAGE_DIR%\BUILD-INFO.txt'"
if errorlevel 1 exit /b %ERRORLEVEL%

powershell -NoProfile -Command "$root=(Resolve-Path '%PACKAGE_DIR%').Path; Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName | ForEach-Object { $h=(Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(); $r=$_.FullName.Substring($root.Length+1).Replace('\','/'); $h+'  '+$r } | Set-Content -Encoding ASCII -Path (Join-Path $root 'SHA256SUMS.txt')"
if errorlevel 1 exit /b %ERRORLEVEL%

if "%CREATE_ARCHIVE%"=="1" (
    if exist "%ARCHIVE%" del /q "%ARCHIVE%"
    echo.
    echo Creating ZIP archive...
    powershell -NoProfile -Command "Compress-Archive -Path '%PACKAGE_DIR%\*' -DestinationPath '%ARCHIVE%' -CompressionLevel Optimal"
    if errorlevel 1 exit /b 1
    powershell -NoProfile -Command "$h=(Get-FileHash -Algorithm SHA256 -LiteralPath '%ARCHIVE%').Hash.ToLowerInvariant(); Set-Content -Encoding ASCII -Path '%ARCHIVE%.sha256' -Value ($h+'  %PACKAGE_NAME%.zip')"
    if errorlevel 1 exit /b 1
)

echo.
echo Windows package completed.
echo Package directory: %PACKAGE_DIR%
if "%CREATE_ARCHIVE%"=="1" echo Archive          : %ARCHIVE%
exit /b 0

:Fail
echo. 1>&2
echo Error: %~1 1>&2
exit /b 2

:ShowHelp
echo Package Mangos Database Studio for Windows
echo.
echo Usage:
echo   scripts\windows\package-app.bat [options]
echo.
echo Options:
echo   --version ^<version^>      Override the latest Git tag
 echo   --stage ^<name^>          Release stage. Default: Alpha
 echo   --runtime ^<rid^>         Windows runtime. Default: win-x64
 echo   --output ^<directory^>    Package output root
 echo   --skip-tests             Skip validate-windows.bat
 echo   --no-archive             Do not create a ZIP archive
 echo   -h, --help               Show this help
 echo.
 echo Example:
 echo   scripts\windows\package-app.bat --version 0.2.5-beta --stage Beta
exit /b 0
