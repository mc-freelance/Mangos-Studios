#!/usr/bin/env python3
"""Build self-contained tagged releases with a clean, verified payload.

The CI workflow runs this script on the native Windows, Linux, and macOS
runners. Only a valid Git tag pointing at HEAD is accepted for publishing.
"""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
APP_PROJECT = ROOT / "src/MangosDatabaseStudio.App/MangosDatabaseStudio.App.csproj"
LICENSE_URL = "https://www.gnu.org/licenses/agpl-3.0.txt"
RELEASE_RIDS = (
    "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"
)
TAG_PATTERN = re.compile(
    r"^v\.?(?P<major>0|[1-9]\d*)\."
    r"(?P<minor>0|[1-9]\d*)\."
    r"(?P<patch>0|[1-9]\d*)"
    r"(?P<suffix>-(?:0|[1-9A-Za-z][0-9A-Za-z-]*)"
    r"(?:\.(?:0|[1-9A-Za-z][0-9A-Za-z-]*))*)?$"
)
SENSITIVE_SUFFIXES = {".db", ".sqlite", ".sqlite3", ".key", ".pfx", ".pem", ".env"}


def git(*args: str) -> str:
    """Read source identity from Git without contacting remote services."""
    result = subprocess.run(
        ["git", "-C", str(ROOT), *args],
        check=True,
        text=True,
        capture_output=True,
    )
    return result.stdout.strip()


def verify_tag(tag: str) -> tuple[str, str, str]:
    """Require the pushed release tag to identify the exact build commit."""
    match = TAG_PATTERN.fullmatch(tag)
    if match is None:
        raise ValueError(f"Invalid release tag: {tag!r}. Use v1.2.3 or v.1.2.3-beta.")

    head = git("rev-parse", "HEAD")
    tagged_commit = git("rev-parse", f"refs/tags/{tag}^{{commit}}")
    if head != tagged_commit:
        raise ValueError("Release tag does not point at the checked-out HEAD commit.")

    version = tag[2:] if tag.startswith("v.") else tag[1:]
    numeric = ".".join(match.group(name) for name in ("major", "minor", "patch"))
    return version, numeric, head[:8]


def release_stage(version: str) -> str:
    """Derive the published stage from the semantic-version prerelease label."""
    if "-" not in version:
        return "Stable"

    label = version.split("-", 1)[1].split(".", 1)[0].lower()
    stages = {"a": "Alpha", "alpha": "Alpha", "b": "Beta", "beta": "Beta", "rc": "RC"}
    return stages.get(label, "Prerelease")


def ensure_license(destination: Path) -> None:
    """Use the tracked AGPL license or obtain GNU's official text if missing.

    The user's repository snapshot currently lacks LICENSE. Never distribute an
    AGPL release without the corresponding license text; fail if retrieval fails.
    """
    existing = ROOT / "LICENSE"
    if existing.is_file():
        content = existing.read_bytes()
    else:
        print("LICENSE is missing; retrieving official GNU AGPL v3 text.", flush=True)
        with urllib.request.urlopen(LICENSE_URL, timeout=30) as response:
            content = response.read(100_000)

    if b"GNU AFFERO GENERAL PUBLIC LICENSE" not in content.upper() or len(content) < 25_000:
        raise ValueError("LICENSE is missing or is not the full GNU AGPL v3 text.")

    (destination / "LICENSE").write_bytes(content)


def clean_payload(publish_dir: Path) -> None:
    """Strip all generated user data and reject leaked databases and secrets."""
    shutil.rmtree(publish_dir / "App_Data", ignore_errors=True)

    for file in publish_dir.rglob("*"):
        if not file.is_file():
            continue

        lower_name = file.name.lower()
        if file.suffix.lower() in SENSITIVE_SUFFIXES or lower_name in {
            "users.json", "connections.json", "role-permissions.json", ".env"
        }:
            raise ValueError(f"Refusing to package sensitive or local file: {file}")

    data_dir = publish_dir / "App_Data"
    for subdir in ("database", "keys", "avatars", "imports", "exports"):
        (data_dir / subdir).mkdir(parents=True, exist_ok=True)

    (data_dir / "README.txt").write_text(
        "User-specific encrypted databases, encryption keys, avatars, and SQL "
        "transfers are stored here. Never redistribute populated App_Data files.\n",
        encoding="utf-8",
    )


def write_extras(package_dir: Path, version: str, hash_short: str, rid: str) -> None:
    """Add legal notices, release metadata, and a platform launcher."""
    ensure_license(package_dir)

    for document in ("README.md", "REQUIREMENTS.md", "DEVICE_IDENTITY.md", "PRIVACY.md"):
        if (ROOT / document).is_file():
            shutil.copy2(ROOT / document, package_dir / document)

    (package_dir / "BUILD-INFO.txt").write_text(
        "MaNGOS Database Studio\n"
        f"Version: {version}\nCommit: {hash_short}\nRuntime: {rid}\n"
        "Framework: .NET 10 (self-contained)\n"
        f"Release stage: {release_stage(version)}\n",
        encoding="utf-8",
    )

    if rid.startswith("win-"):
        (package_dir / "start.bat").write_bytes(
            b'@echo off\r\n"%~dp0MangosDatabaseStudio.exe" %*\r\nexit /b %ERRORLEVEL%\r\n'
        )
        command = "Double-click MangosDatabaseStudio.exe or start.bat."
    else:
        launcher = package_dir / "start.sh"
        launcher.write_text(
            "#!/usr/bin/env bash\n"
            "# Launch the application from its own extracted directory.\n"
            'set -Eeuo pipefail\ncd -- "$(dirname -- "${BASH_SOURCE[0]}")"\n'
            'exec ./MangosDatabaseStudio "$@"\n',
            encoding="utf-8",
        )
        launcher.chmod(0o755)
        command = "Run ./start.sh from the extracted directory."

    dependencies = (
        "Linux: GTK 3 and the WebKitGTK runtime compatible with Photino are required.\n"
        if rid.startswith("linux-") else
        "Windows: Microsoft Edge WebView2 Runtime may be required.\n"
        if rid.startswith("win-") else
        "macOS: This is an unsigned, unnotarized portable build; Gatekeeper may warn.\n"
    )
    (package_dir / "README-PACKAGE.txt").write_text(
        "MaNGOS Database Studio - Portable Release\n"
        f"Version: {version}\nRuntime: {rid}\n\n"
        f"{command}\n{dependencies}\n"
        "The .NET SDK/runtime are included. MySQL/MariaDB clients are optional for\n"
        "operations such as SQL import and export.\n"
        "Hardware Device / Device Identity remains experimental and OFF by default.\n"
        "No privileged hardware helper is distributed with this portable package.\n"
        "Keep App_Data/database and App_Data/keys together when backing up.\n",
        encoding="utf-8",
    )


def file_sha256(path: Path) -> str:
    """Hash large runtime binaries without loading a whole file into memory."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_checksums(package_dir: Path) -> None:
    """Write reproducible SHA-256 checksums of every packaged regular file."""
    lines = []
    for file in sorted(package_dir.rglob("*")):
        if file.is_file() and file.name != "SHA256SUMS.txt":
            relative = file.relative_to(package_dir).as_posix()
            lines.append(f"{file_sha256(file)}  {relative}")

    (package_dir / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="ascii")


def make_archive(package_dir: Path, output: Path, rid: str) -> Path:
    """Archive with directory structure and POSIX executable permissions intact."""
    archive = output / f"{package_dir.name}{'.zip' if rid.startswith('win-') else '.tar.gz'}"
    if rid.startswith("win-"):
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
            for item in sorted(package_dir.rglob("*")):
                if item.is_file():
                    zf.write(item, arcname=f"{package_dir.name}/{item.relative_to(package_dir).as_posix()}")
    else:
        with tarfile.open(archive, "w:gz") as tf:
            tf.add(package_dir, arcname=package_dir.name, recursive=True)

    digest = file_sha256(archive)
    (output / f"{archive.name}.sha256").write_text(f"{digest}  {archive.name}\n", encoding="ascii")
    return archive


def package(tag: str, rid: str, output: Path) -> Path:
    """Publish the native app, purge runtime data, and construct the release asset."""
    if rid not in RELEASE_RIDS:
        raise ValueError(f"Unsupported runtime identifier: {rid}")

    version, numeric, short_hash = verify_tag(tag)
    output.mkdir(parents=True, exist_ok=True)
    package_name = f"MangosDatabaseStudio-{version}-{rid}"
    destination = output / package_name
    shutil.rmtree(destination, ignore_errors=True)

    sdk = subprocess.run(["dotnet", "--version"], check=True, text=True, capture_output=True).stdout.strip()
    if sdk.split(".")[0] != "10":
        raise ValueError(f"A .NET 10 SDK is required, found {sdk}.")

    arguments = [
        "dotnet", "publish", str(APP_PROJECT), "--configuration", "Release",
        "--runtime", rid, "--self-contained", "true", "--output", str(destination),
        "-p:UseAppHost=true", "-p:PublishTrimmed=false",
        "-p:PublishSingleFile=false", "-p:DebugSymbols=false", "-p:DebugType=None",
        f"-p:Version={version}", f"-p:AssemblyVersion={numeric}.0",
        f"-p:FileVersion={numeric}.0", f"-p:StudioVersionOverride={tag}",
        f"-p:StudioReleaseStage={release_stage(version)}",
        "-p:ContinuousIntegrationBuild=true",
    ]
    print(f"Publishing {tag} ({short_hash}) for {rid}", flush=True)
    subprocess.run(arguments, cwd=ROOT, check=True)

    executable = destination / ("MangosDatabaseStudio.exe" if rid.startswith("win-") else "MangosDatabaseStudio")
    if not executable.is_file():
        raise FileNotFoundError(f"Expected native executable is missing: {executable}")

    if not rid.startswith("win-"):
        executable.chmod(executable.stat().st_mode | 0o111)

    clean_payload(destination)
    write_extras(destination, version, short_hash, rid)
    write_checksums(destination)
    archive = make_archive(destination, output, rid)
    print(f"Release asset created: {archive}", flush=True)
    return archive


def verify_assets(directory: Path) -> None:
    """Block publication unless all six runtime assets and hashes exist."""
    archives = sorted(
        file for file in directory.iterdir()
        if file.is_file() and (file.name.endswith(".tar.gz") or file.name.endswith(".zip"))
    )
    if len(archives) != len(RELEASE_RIDS):
        raise ValueError(f"Expected {len(RELEASE_RIDS)} package archives, found {len(archives)}.")

    for rid in RELEASE_RIDS:
        matches = [file for file in archives if file.name.endswith(f"-{rid}.zip") or file.name.endswith(f"-{rid}.tar.gz")]
        if len(matches) != 1:
            raise ValueError(f"Expected exactly one release asset for {rid}, found {len(matches)}.")

    for archive in archives:
        checksum_file = directory / f"{archive.name}.sha256"
        if not checksum_file.is_file():
            raise ValueError(f"Missing checksum: {checksum_file.name}")

        expected = checksum_file.read_text(encoding="ascii").strip().split("  ", 1)
        if len(expected) != 2 or expected[1] != archive.name:
            raise ValueError(f"Invalid checksum line in {checksum_file.name}")

        actual = file_sha256(archive)
        if expected[0] != actual:
            raise ValueError(f"SHA-256 mismatch for {archive.name}")

    print("All six release packages and their SHA-256 checksums are valid.")


def main() -> int:
    """Dispatch validation, packaging, and aggregate release verification."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate = subparsers.add_parser("validate-tag")
    validate.add_argument("--tag", required=True)

    build = subparsers.add_parser("package")
    build.add_argument("--tag", required=True)
    build.add_argument("--rid", required=True, choices=RELEASE_RIDS)
    build.add_argument("--output", type=Path, default=ROOT / ".artifacts" / "release")

    checks = subparsers.add_parser("verify-assets")
    checks.add_argument("--directory", required=True, type=Path)

    args = parser.parse_args()
    try:
        if args.command == "validate-tag":
            print("Release tag accepted:", verify_tag(args.tag))
        elif args.command == "package":
            package(args.tag, args.rid, args.output)
        elif args.command == "verify-assets":
            verify_assets(args.directory)
    except (ValueError, FileNotFoundError, OSError, subprocess.CalledProcessError) as error:
        print(f"Release packaging failed: {error}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
