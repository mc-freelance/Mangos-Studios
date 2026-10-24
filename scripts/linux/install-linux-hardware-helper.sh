#!/usr/bin/env bash
# Build and install the optional privileged Device Identity helper.

set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT_DIR/src/MangosDatabaseStudio.HardwareIdentityHelper/MangosDatabaseStudio.HardwareIdentityHelper.csproj"
INSTALLER="$ROOT_DIR/packaging/linux/install-hardware-helper.sh"
POLICY="$ROOT_DIR/packaging/linux/org.mangosdatabasestudio.deviceidentity.policy"
OUTPUT_DIR="${MDS_HARDWARE_HELPER_BUILD_DIR:-$ROOT_DIR/.artifacts/hardware-identity-helper}"
RUNTIME="${MDS_RUNTIME:-linux-x64}"
SELF_CONTAINED="${MDS_HELPER_SELF_CONTAINED:-true}"

fail() {
    printf 'Error: %s\n' "$1" >&2
    exit 1
}

[[ "$(uname -s)" == "Linux" ]] || fail "The privileged DMI helper is supported only on Linux."
command -v dotnet >/dev/null 2>&1 || fail "The .NET 9 SDK is required to build the helper."
command -v sudo >/dev/null 2>&1 || fail "sudo is required for the one-time system installation."
[[ "$(dotnet --version | cut -d. -f1)" == "9" ]] || fail "The .NET 9 SDK is required."
[[ -f "$PROJECT" ]] || fail "Helper project was not found: $PROJECT"
[[ -f "$INSTALLER" ]] || fail "Helper installer was not found: $INSTALLER"
[[ -f "$POLICY" ]] || fail "Polkit policy was not found: $POLICY"

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

printf 'Publishing the privileged hardware identity helper...\n'
dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime "$RUNTIME" \
    --self-contained "$SELF_CONTAINED" \
    --output "$OUTPUT_DIR" \
    -p:UseAppHost=true \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false

printf '\nInstalling the root-owned helper and Polkit policy...\n'
sudo "$INSTALLER" --source "$OUTPUT_DIR"
