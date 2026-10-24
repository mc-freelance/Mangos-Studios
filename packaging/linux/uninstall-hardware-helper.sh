#!/usr/bin/env bash
# Remove the optional firmware-identity helper and its Polkit policy.

set -Eeuo pipefail

INSTALL_ROOT="/usr/lib/mangos-database-studio/hardware-identity"
POLICY_DEST="/usr/share/polkit-1/actions/org.mangosdatabasestudio.deviceidentity.policy"

if [[ "${EUID:-$(id -u)}" -ne 0 ]]; then
    printf 'Error: run this uninstaller as root, for example: sudo ./uninstall-hardware-helper.sh\n' >&2
    exit 1
fi

rm -rf "$INSTALL_ROOT"
rm -f "$POLICY_DEST"

printf 'Removed the Mangos Database Studio hardware identity helper and Polkit policy.\n'
