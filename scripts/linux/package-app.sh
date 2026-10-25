#!/usr/bin/env bash
# Create reproducible desktop release packages for both target platforms.
set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
SOLUTION="$ROOT_DIR/MangosDatabaseStudio.sln"
APP_PROJECT="$ROOT_DIR/src/MangosDatabaseStudio.App/MangosDatabaseStudio.App.csproj"
HELPER_PROJECT="$ROOT_DIR/src/MangosDatabaseStudio.HardwareIdentityHelper/MangosDatabaseStudio.HardwareIdentityHelper.csproj"
LINUX_INTEGRATION="$ROOT_DIR/packaging/linux"

OUTPUT_ROOT="${MDS_APP_OUTPUT:-$ROOT_DIR/.artifacts/packages}"
APP_VERSION="${MDS_APP_VERSION:-}"
RELEASE_STAGE="${MDS_APP_STAGE:-Alpha}"
PLATFORM="all"
LINUX_RID="${MDS_LINUX_RID:-linux-x64}"
WINDOWS_RID="${MDS_WINDOWS_RID:-win-x64}"
RUN_TESTS=true
CREATE_ARCHIVES=true

usage() {
    cat <<'USAGE'
Package Mangos Database Studio desktop binaries for Linux and Windows.

Usage:
  ./scripts/linux/package-app.sh [options]

Options:
  --output <directory>       Output root. Default: .artifacts/packages
  --version <version>        Override the latest Git tag, for example 0.2.5-beta
  --stage <name>             Release stage. Default: Alpha
  --platform <value>         all, linux, or windows. Default: all
  --linux-only               Same as --platform linux
  --windows-only             Same as --platform windows
  --linux-rid <rid>          Default: linux-x64
  --windows-rid <rid>        Default: win-x64
  --skip-tests               Skip dotnet test before publishing
  --no-archive               Leave package directories without archives
  -h, --help                 Show this help

Packages are self-contained and desktop-only. Testers launch MangosDatabaseStudio
(or MangosDatabaseStudio.exe) directly; no alternate runtime mode is included.
USAGE
}

fail()
{
    printf '\nError: %s\n' "$1" >&2
    exit 1
}
info()
{
    printf '\n==> %s\n' "$1"
}
sanitize()
{
    printf '%s' "$1" | tr -cs 'A-Za-z0-9._-' '-'
}

while (($# > 0)); do
    case "$1" in
        --output) (($# >= 2)) || fail "--output requires a directory."; OUTPUT_ROOT="$2"; shift 2 ;;
        --version) (($# >= 2)) || fail "--version requires a value."; APP_VERSION="$2"; shift 2 ;;
        --stage) (($# >= 2)) || fail "--stage requires a value."; RELEASE_STAGE="$2"; shift 2 ;;
        --platform) (($# >= 2)) || fail "--platform requires all, linux, or windows."; PLATFORM="$2"; shift 2 ;;
        --linux-only) PLATFORM="linux"; shift ;;
        --windows-only) PLATFORM="windows"; shift ;;
        --linux-rid) (($# >= 2)) || fail "--linux-rid requires a value."; LINUX_RID="$2"; shift 2 ;;
        --windows-rid) (($# >= 2)) || fail "--windows-rid requires a value."; WINDOWS_RID="$2"; shift 2 ;;
        --skip-tests) RUN_TESTS=false; shift ;;
        --no-archive) CREATE_ARCHIVES=false; shift ;;
        -h|--help) usage; exit 0 ;;
        *) fail "Unknown option: $1" ;;
    esac
done

# Use the nearest reachable version tag; accept both v1.2.3 and v.1.2.3-beta.
# An explicit --version remains available for untagged release preparation.
if [[ -z "$APP_VERSION" ]]; then
    command -v git >/dev/null 2>&1 || fail "Git is required to detect a release tag; pass --version instead."
    RELEASE_TAG="$(git -C "$ROOT_DIR" describe --tags --abbrev=0 --match='v[0-9]*' --match='v.[0-9]*' 2>/dev/null || true)"
    [[ -n "$RELEASE_TAG" ]] || fail "No version tag was found; create v.0.2.5-beta or pass --version."
    APP_VERSION="$RELEASE_TAG"
fi
# Strip the optional Git tag prefix without discarding prerelease labels.
APP_VERSION="${APP_VERSION#v}"
APP_VERSION="${APP_VERSION#.}"
[[ "$APP_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+(\.[0-9A-Za-z-]+)*)?$ ]] || fail "Version must use major.minor.patch with an optional prerelease label, for example 0.2.5-beta."
VERSION_CORE="${APP_VERSION%%-*}"
BUILD_COMMIT="$(git -C "$ROOT_DIR" rev-parse --short=8 HEAD 2>/dev/null || printf 'unknown')"
[[ "$PLATFORM" == "all" || "$PLATFORM" == "linux" || "$PLATFORM" == "windows" ]] || fail "Platform must be all, linux, or windows."
command -v dotnet >/dev/null 2>&1 || fail "The .NET 10 SDK is not installed or is not in PATH."
[[ "$(dotnet --version | cut -d. -f1)" == "10" ]] || fail "This project requires the .NET 10 SDK."
for file in "$SOLUTION" "$APP_PROJECT" "$HELPER_PROJECT"; do [[ -f "$file" ]] || fail "Required file was not found: $file"; done
mkdir -p "$OUTPUT_ROOT"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

VERSION_PROPERTIES=(
    "-p:Version=$APP_VERSION"
    "-p:StudioVersionOverride=$APP_VERSION"
    "-p:StudioReleaseStage=$RELEASE_STAGE"
    "-p:AssemblyVersion=$VERSION_CORE.0"
    "-p:FileVersion=$VERSION_CORE.0"
    "-p:InformationalVersion=$APP_VERSION"
)

if $RUN_TESTS; then
    info "Validating solution"
    dotnet restore "$SOLUTION"
    dotnet build "$SOLUTION" --configuration Release --no-restore
    dotnet test "$SOLUTION" --configuration Release --no-build --no-restore
fi

prepare_app_data() {
    local app_dir="$1"
    rm -rf "$app_dir/App_Data"
    mkdir -p "$app_dir/App_Data/imports" "$app_dir/App_Data/exports" "$app_dir/App_Data/database" "$app_dir/App_Data/keys" "$app_dir/App_Data/avatars"
    cat > "$app_dir/App_Data/README.txt" <<'APPDATA'
Runtime application data is created here.
This directory can contain the encrypted Studio database, encryption keys, avatars,
SQL imports, and SQL exports. Do not distribute populated App_Data contents.
APPDATA
}

write_metadata() {
    local dir="$1" runtime="$2" os_name="$3"
    cp "$ROOT_DIR/LICENSE" "$dir/LICENSE"
    [[ -f "$ROOT_DIR/README.md" ]] && cp "$ROOT_DIR/README.md" "$dir/README.md"
    [[ -f "$ROOT_DIR/DEVICE_IDENTITY.md" ]] && cp "$ROOT_DIR/DEVICE_IDENTITY.md" "$dir/DEVICE_IDENTITY.md"
    [[ -f "$ROOT_DIR/PRIVACY.md" ]] && cp "$ROOT_DIR/PRIVACY.md" "$dir/PRIVACY.md"
    [[ -f "$ROOT_DIR/REQUIREMENTS.md" ]] && cp "$ROOT_DIR/REQUIREMENTS.md" "$dir/REQUIREMENTS.md"
    cat > "$dir/BUILD-INFO.txt" <<EOF_INFO
Mangos Database Studio
Application version: $APP_VERSION
Source commit: $BUILD_COMMIT
Release stage: $RELEASE_STAGE
Runtime identifier: $runtime
Platform: $os_name
Framework: .NET 10 self-contained
Configuration: Release
Build time (UTC): $(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF_INFO
    cat > "$dir/README-PACKAGE.txt" <<EOF_README
Mangos Database Studio - $RELEASE_STAGE
=====================================

Version: $APP_VERSION
Commit: $BUILD_COMMIT
Runtime: $runtime

STARTUP
-------
This package is desktop-only and runs as a single MangosDatabaseStudio process.

$([[ "$os_name" == "Windows" ]] && printf 'Run MangosDatabaseStudio.exe directly.\nOptional: start.bat\n' || printf 'Run ./MangosDatabaseStudio directly.\nOptional: ./start.sh\n')

Device Identity is disabled by default. Enable it by passing --device-identity.
SQL imports require mysql/mariadb in PATH.
SQL exports require mysqldump/mariadb-dump in PATH.
EOF_README
}

write_checksums() {
    local dir="$1"
    (
        cd "$dir"
        if command -v sha256sum >/dev/null 2>&1; then
            find . -type f ! -name SHA256SUMS.txt -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS.txt
        else
            find . -type f ! -name SHA256SUMS.txt -print | LC_ALL=C sort | while IFS= read -r f; do shasum -a 256 "$f"; done > SHA256SUMS.txt
        fi
    )
}

publish_one() {
    local runtime="$1" os_name="$2"
    local label="MangosDatabaseStudio-$(sanitize "$APP_VERSION")-$(sanitize "$RELEASE_STAGE")-$runtime"
    local dir="$OUTPUT_ROOT/$label"
    rm -rf "$dir"
    mkdir -p "$dir"

    info "Publishing single-process desktop application for $runtime"
    dotnet publish "$APP_PROJECT" --configuration Release --runtime "$runtime" --self-contained true --output "$dir" \
        -p:UseAppHost=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false "${VERSION_PROPERTIES[@]}"
    prepare_app_data "$dir"

    if [[ "$os_name" == "Linux" ]]; then
        mkdir -p "$dir/hardware-helper" "$dir/linux-integration"
        info "Publishing Linux hardware identity helper"
        dotnet publish "$HELPER_PROJECT" --configuration Release --runtime "$runtime" --self-contained true --output "$dir/hardware-helper" \
            -p:UseAppHost=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false "${VERSION_PROPERTIES[@]}"
        cp "$LINUX_INTEGRATION/org.mangosdatabasestudio.deviceidentity.policy" "$dir/linux-integration/"
        cp "$LINUX_INTEGRATION/install-hardware-helper.sh" "$dir/linux-integration/"
        cp "$LINUX_INTEGRATION/uninstall-hardware-helper.sh" "$dir/linux-integration/"
        chmod +x "$dir/linux-integration/"*.sh
        cat > "$dir/start.sh" <<'START'
#!/usr/bin/env bash
set -Eeuo pipefail
APP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec "$APP_DIR/MangosDatabaseStudio"
START
        chmod +x "$dir/start.sh"
    else
        cat > "$dir/start.bat" <<'STARTBAT'
@echo off
"%~dp0MangosDatabaseStudio.exe"
exit /b %ERRORLEVEL%
STARTBAT
        if command -v python3 >/dev/null 2>&1; then
            python3 - "$dir/start.bat" <<'PY'
from pathlib import Path
import sys
p=Path(sys.argv[1]); s=p.read_text().replace('\r\n','\n'); p.write_bytes(s.replace('\n','\r\n').encode())
PY
        fi
    fi

    write_metadata "$dir" "$runtime" "$os_name"
    write_checksums "$dir"

    if $CREATE_ARCHIVES; then
        if [[ "$os_name" == "Linux" ]]; then
            local archive="$OUTPUT_ROOT/$label.tar.gz"
            rm -f "$archive"
            tar -C "$OUTPUT_ROOT" -czf "$archive" "$label"
            info "Created $archive"
        else
            local archive="$OUTPUT_ROOT/$label.zip"
            rm -f "$archive"
            if command -v zip >/dev/null 2>&1; then
                (cd "$OUTPUT_ROOT" && zip -q -r "$archive" "$label")
            elif command -v python3 >/dev/null 2>&1; then
                python3 - "$dir" "$archive" <<'PY'
from pathlib import Path
import sys, zipfile
root=Path(sys.argv[1]); archive=Path(sys.argv[2])
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    for p in sorted(root.rglob('*')):
        if p.is_file(): z.write(p, Path(root.name)/p.relative_to(root))
PY
            else
                fail "zip or python3 is required to create the Windows archive."
            fi
            info "Created $archive"
        fi
    fi
}

[[ "$PLATFORM" == "all" || "$PLATFORM" == "linux" ]] && publish_one "$LINUX_RID" Linux
[[ "$PLATFORM" == "all" || "$PLATFORM" == "windows" ]] && publish_one "$WINDOWS_RID" Windows

printf '\nPackaging completed under: %s\n' "$OUTPUT_ROOT"
