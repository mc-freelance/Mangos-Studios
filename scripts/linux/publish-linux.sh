#!/usr/bin/env bash
# Publish the Linux desktop binary and optional hardware-helper integration.
set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
APP_PROJECT="$ROOT_DIR/src/MangosDatabaseStudio.App/MangosDatabaseStudio.App.csproj"
HELPER_PROJECT="$ROOT_DIR/src/MangosDatabaseStudio.HardwareIdentityHelper/MangosDatabaseStudio.HardwareIdentityHelper.csproj"
LINUX_INTEGRATION="$ROOT_DIR/packaging/linux"

OUTPUT_DIR="${MDS_PUBLISH_DIR:-$ROOT_DIR/.artifacts/linux-x64}"
RUNTIME="${MDS_RUNTIME:-linux-x64}"
SELF_CONTAINED=false

usage() {
    cat <<'USAGE'
Publish Mangos Database Studio for Linux.

Usage:
  ./scripts/publish-linux.sh [options]

Options:
  --output <directory>   Publish destination
  --runtime <rid>        Runtime identifier. Default: linux-x64
  --self-contained       Include the .NET runtime
  -h, --help             Show this help

The published package is desktop-only. Run the MangosDatabaseStudio executable directly.
USAGE
}

fail()
{
    printf '\nError: %s\n' "$1" >&2
    exit 2
}

while (($# > 0)); do
    case "$1" in
        --output) (($# >= 2)) || fail "--output requires a directory."; OUTPUT_DIR="$2"; shift 2 ;;
        --runtime) (($# >= 2)) || fail "--runtime requires a runtime identifier."; RUNTIME="$2"; shift 2 ;;
        --self-contained) SELF_CONTAINED=true; shift ;;
        -h|--help) usage; exit 0 ;;
        *) fail "Unknown option: $1" ;;
    esac
done

command -v dotnet >/dev/null 2>&1 || fail "The .NET 10 SDK is not installed or is not in PATH."
[[ "$(dotnet --version | cut -d. -f1)" == "10" ]] || fail "This project requires the .NET 10 SDK."
[[ -f "$APP_PROJECT" ]] || fail "Application project was not found: $APP_PROJECT"
[[ -f "$HELPER_PROJECT" ]] || fail "Hardware identity helper project was not found: $HELPER_PROJECT"

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR" "$OUTPUT_DIR/hardware-helper" "$OUTPUT_DIR/linux-integration"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

printf 'Publishing single-process MaNGOS Database Studio for %s...\n' "$RUNTIME"
dotnet publish "$APP_PROJECT" \
    --configuration Release \
    --runtime "$RUNTIME" \
    --self-contained "$SELF_CONTAINED" \
    --output "$OUTPUT_DIR" \
    -p:UseAppHost=true \
    -p:PublishTrimmed=false

rm -rf "$OUTPUT_DIR/App_Data"
mkdir -p "$OUTPUT_DIR/App_Data/imports" \
    "$OUTPUT_DIR/App_Data/exports" \
    "$OUTPUT_DIR/App_Data/database" \
    "$OUTPUT_DIR/App_Data/keys" \
    "$OUTPUT_DIR/App_Data/avatars"
cat > "$OUTPUT_DIR/App_Data/README.txt" <<'APPDATA'
Runtime application data is created here. This directory may contain the encrypted Studio database, keys, avatars, imports, and exports. Do not distribute populated App_Data contents.
APPDATA

printf 'Publishing Linux hardware identity helper for %s...\n' "$RUNTIME"
dotnet publish "$HELPER_PROJECT" \
    --configuration Release \
    --runtime "$RUNTIME" \
    --self-contained "$SELF_CONTAINED" \
    --output "$OUTPUT_DIR/hardware-helper" \
    -p:UseAppHost=true \
    -p:PublishTrimmed=false

cp "$LINUX_INTEGRATION/org.mangosdatabasestudio.deviceidentity.policy" "$OUTPUT_DIR/linux-integration/"
cp "$LINUX_INTEGRATION/install-hardware-helper.sh" "$OUTPUT_DIR/linux-integration/"
cp "$LINUX_INTEGRATION/uninstall-hardware-helper.sh" "$OUTPUT_DIR/linux-integration/"
chmod +x "$OUTPUT_DIR/linux-integration/install-hardware-helper.sh" "$OUTPUT_DIR/linux-integration/uninstall-hardware-helper.sh"

cat > "$OUTPUT_DIR/start.sh" <<'START'
#!/usr/bin/env bash
set -Eeuo pipefail
APP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec "$APP_DIR/MangosDatabaseStudio"
START
chmod +x "$OUTPUT_DIR/start.sh"

for file in LICENSE README.md REQUIREMENTS.md DEVICE_IDENTITY.md PRIVACY.md; do
    [[ -f "$ROOT_DIR/$file" ]] && cp "$ROOT_DIR/$file" "$OUTPUT_DIR/$file"
done

printf '\nPublish completed: %s\n' "$OUTPUT_DIR"
printf 'Run directly     : %s/MangosDatabaseStudio\n' "$OUTPUT_DIR"
printf 'Optional wrapper: %s/start.sh\n' "$OUTPUT_DIR"
printf 'Hardware helper : sudo %s/linux-integration/install-hardware-helper.sh\n' "$OUTPUT_DIR"
