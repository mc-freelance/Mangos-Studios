#!/usr/bin/env bash
# Install the published optional firmware-identity helper with restricted root permissions.

set -Eeuo pipefail

INSTALL_ROOT="/usr/lib/mangos-database-studio/hardware-identity"
POLICY_DEST="/usr/share/polkit-1/actions/org.mangosdatabasestudio.deviceidentity.policy"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ -d "$SCRIPT_DIR/../hardware-helper" ]]; then
    PACKAGE_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
else
    PACKAGE_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
fi
HELPER_SOURCE="${MDS_HARDWARE_HELPER_SOURCE:-$PACKAGE_ROOT/hardware-helper}"
POLICY_SOURCE="$SCRIPT_DIR/org.mangosdatabasestudio.deviceidentity.policy"

fail() {
    printf 'Error: %s\n' "$1" >&2
    exit 1
}

while (($# > 0)); do
    case "$1" in
        --source)
            (($# >= 2)) || fail "--source requires the published helper directory."
            HELPER_SOURCE="$2"
            shift 2
            ;;
        -h|--help)
            printf 'Usage: sudo %s [--source <published-helper-directory>]\n' "$0"
            exit 0
            ;;
        *)
            fail "Unknown option: $1"
            ;;
    esac
done

if [[ "${EUID:-$(id -u)}" -ne 0 ]]; then
    fail "Run this installer as root, for example: sudo ./install-hardware-helper.sh"
fi

HELPER_SOURCE="$(cd -- "$HELPER_SOURCE" 2>/dev/null && pwd)" \
    || fail "Published helper directory was not found: $HELPER_SOURCE"
[[ -f "$HELPER_SOURCE/mangos-hwid-helper" ]] || fail "Published helper executable was not found."
[[ -f "$POLICY_SOURCE" ]] || fail "Polkit policy file was not found: $POLICY_SOURCE"

TEMP_PARENT="/usr/lib/mangos-database-studio"
mkdir -p "$TEMP_PARENT" "$(dirname "$POLICY_DEST")"
TEMP_ROOT="$(mktemp -d "$TEMP_PARENT/.hardware-identity.XXXXXX")"
cleanup() {
    rm -rf "$TEMP_ROOT"
}
trap cleanup EXIT

cp -a "$HELPER_SOURCE/." "$TEMP_ROOT/"
chown -R root:root "$TEMP_ROOT"
find "$TEMP_ROOT" -type d -exec chmod 0755 {} +
find "$TEMP_ROOT" -type f -exec chmod 0644 {} +
chmod 0755 "$TEMP_ROOT/mangos-hwid-helper"

rm -rf "$INSTALL_ROOT"
mv "$TEMP_ROOT" "$INSTALL_ROOT"
trap - EXIT

install -o root -g root -m 0644 "$POLICY_SOURCE" "$POLICY_DEST"

printf 'Installed privileged hardware identity helper:\n'
printf '  Helper: %s\n' "$INSTALL_ROOT/mangos-hwid-helper"
printf '  Policy: %s\n' "$POLICY_DEST"
printf 'Restart Mangos Database Studio to request authorization.\n'
