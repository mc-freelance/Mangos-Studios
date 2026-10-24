#!/usr/bin/env bash
# Start the application with its project content root in a Linux graphical session.
set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
APP_PROJECT="$ROOT_DIR/src/MangosDatabaseStudio.App/MangosDatabaseStudio.App.csproj"

fail() {
    printf '\nError: %s\n' "$1" >&2
    exit 2
}

command -v dotnet >/dev/null 2>&1 || fail "The .NET 9 SDK is not installed or is not in PATH."
[[ "$(dotnet --version | cut -d. -f1)" == "9" ]] || fail "This project requires the .NET 9 SDK."
[[ -f "$APP_PROJECT" ]] || fail "Application project was not found: $APP_PROJECT"
[[ -n "${DISPLAY:-}" || -n "${WAYLAND_DISPLAY:-}" ]] || fail "A graphical Linux session is required."

export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export MDS_CONTENT_ROOT="$ROOT_DIR/src/MangosDatabaseStudio.App"

exec dotnet run --project "$APP_PROJECT" --configuration Debug -- "$@"
