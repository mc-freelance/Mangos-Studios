"""Regression checks for secure cross-platform release packaging."""

from __future__ import annotations

import hashlib
from pathlib import Path
import sys
import tarfile
import tempfile
from types import SimpleNamespace
import unittest
import zipfile
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts" / "release"))
import package_release as release


class ReleasePackagingTests(unittest.TestCase):
    """Verify tags, native launchers, secret exclusions, and asset integrity."""

    def test_tag_formats(self) -> None:
        for tag in ("v1.0.0", "v.0.2.5-beta", "v3.4.5-rc.1"):
            with self.subTest(tag=tag):
                self.assertIsNotNone(release.TAG_PATTERN.fullmatch(tag))

    def test_invalid_tags(self) -> None:
        for tag in ("v.1.0", "version1.2.3", "v1.2.3/evil", "v1.2.3+hi", "v01.2.3", "v1.0.0-"):
            with self.subTest(tag=tag):
                self.assertIsNone(release.TAG_PATTERN.fullmatch(tag))

    def test_verify_tag_rejects_wrong_commit(self) -> None:
        with patch.object(release, "git", side_effect=["a" * 40, "b" * 40]):
            with self.assertRaisesRegex(ValueError, "HEAD"):
                release.verify_tag("v.0.2.5-beta")

    def test_version_and_stage(self) -> None:
        with patch.object(release, "git", side_effect=["a" * 40, "a" * 40]):
            version, numeric, short = release.verify_tag("v.0.2.5-beta")
        self.assertEqual((version, numeric, short), ("0.2.5-beta", "0.2.5", "aaaaaaaa"))
        self.assertEqual(release.release_stage(version), "Beta")
        self.assertEqual(release.release_stage("1.2.3"), "Stable")

    def test_sensitive_local_data_removed(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            data = root / "App_Data"
            (data / "keys").mkdir(parents=True)
            (data / "keys" / "database.key").write_text("secret")
            (data / "database.db").write_text("private")
            release.clean_payload(root)
            self.assertFalse((data / "keys" / "database.key").exists())
            self.assertFalse((data / "database.db").exists())
            self.assertTrue((data / "README.txt").is_file())

    def test_other_sensitive_files_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "credentials.pfx").write_text("secret")
            with self.assertRaisesRegex(ValueError, "credentials.pfx"):
                release.clean_payload(root)

    def test_archives_preserve_layout_and_scripts(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for rid in ("win-x64", "linux-x64", "osx-arm64"):
                with self.subTest(rid=rid):
                    app = root / f"MangosDatabaseStudio-0.2.5-beta-{rid}"
                    app.mkdir()
                    binary = app / ("MangosDatabaseStudio.exe" if rid.startswith("win") else "MangosDatabaseStudio")
                    binary.write_bytes(b"fake-executable")
                    if not rid.startswith("win"):
                        binary.chmod(0o755)
                    release.write_checksums(app)
                    archive = release.make_archive(app, root, rid)
                    self.assertTrue(archive.is_file())
                    if rid.startswith("win"):
                        with zipfile.ZipFile(archive) as zf:
                            self.assertTrue(any(path.endswith("MangosDatabaseStudio.exe") for path in zf.namelist()))
                    else:
                        with tarfile.open(archive, "r:gz") as tf:
                            member = next(m for m in tf.getmembers() if m.name.endswith("/MangosDatabaseStudio"))
                            self.assertTrue(member.mode & 0o111)

    def test_package_sanitizes_fake_dotnet_publish(self) -> None:
        # Simulate publish output to exercise the full packager without the SDK.
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp)
            commands = []

            def fake_run(command, **kwargs):
                commands.append(command)
                if command[:2] == ["dotnet", "--version"]:
                    return SimpleNamespace(stdout="10.0.113\n")

                self.assertEqual(command[:2], ["dotnet", "publish"])
                destination = Path(command[command.index("--output") + 1])
                destination.mkdir(parents=True)
                (destination / "MangosDatabaseStudio").write_bytes(b"binary")
                key_folder = destination / "App_Data" / "keys"
                key_folder.mkdir(parents=True)
                (key_folder / "application-database.key").write_text("must not ship")
                return SimpleNamespace(stdout="")

            def fake_license(destination):
                (destination / "LICENSE").write_text("Test-only AGPL license marker")

            with patch.object(release, "verify_tag", return_value=("0.2.5-beta", "0.2.5", "deadbeef")):
                with patch.object(release.subprocess, "run", side_effect=fake_run):
                    with patch.object(release, "ensure_license", side_effect=fake_license):
                        archive = release.package("v.0.2.5-beta", "osx-arm64", output)

            self.assertTrue(archive.exists())
            self.assertIn("--self-contained", commands[1])
            with tarfile.open(archive, "r:gz") as tf:
                paths = [item.name for item in tf.getmembers()]
                self.assertFalse(any(path.endswith("application-database.key") for path in paths))
                self.assertTrue(any(path.endswith("README-PACKAGE.txt") for path in paths))
                self.assertTrue(any(path.endswith("start.sh") for path in paths))

    def test_all_six_assets_required_and_verified(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with self.assertRaisesRegex(ValueError, "Expected 6"):
                release.verify_assets(root)

            for rid in release.RELEASE_RIDS:
                ext = ".zip" if rid.startswith("win-") else ".tar.gz"
                name = f"MangosDatabaseStudio-0.2.5-beta-{rid}{ext}"
                file = root / name
                file.write_bytes(name.encode())
                digest = hashlib.sha256(file.read_bytes()).hexdigest()
                (root / f"{name}.sha256").write_text(f"{digest}  {name}\n")
            release.verify_assets(root)
            (root / "MangosDatabaseStudio-0.2.5-beta-win-x64.zip").write_bytes(b"tampered")
            with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                release.verify_assets(root)


if __name__ == "__main__":
    unittest.main()
