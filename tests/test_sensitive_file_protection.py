"""Regression tests for source-control protection of private runtime files."""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))

import check_sensitive_files  # noqa: E402


class SensitiveFileTests(unittest.TestCase):
    def test_runtime_files_are_sensitive_even_inside_nested_applications(self) -> None:
        for path in (
            "src/MangosDatabaseStudio.App/App_Data/users.json",
            "src/MangosDatabaseStudio.App/App_Data/database/mangos-studio.db",
            "src/MangosDatabaseStudio.App/App_Data/keys/application-database.key",
            "src/MangosDatabaseStudio.App/App_Data/connections.json.migrated-20261030.bak",
            "publish/App_Data/imports/input.sql",
            "App_Data/avatars/person.jpg",
        ):
            with self.subTest(path=path):
                self.assertTrue(check_sensitive_files.is_sensitive_path(path))

    def test_safe_repository_sources_and_placeholders_are_allowed(self) -> None:
        for path in (
            "src/MangosDatabaseStudio.App/App_Data/.gitkeep",
            "src/MangosDatabaseStudio.App/App_Data/keys/.gitkeep",
            ".env.example",
            "src/MangosDatabaseStudio.App/appsettings.json",
            "src/MangosDatabaseStudio.App/wwwroot/css/site.css",
            "src/MangosDatabaseStudio.Schema/mangos-tables.json",
        ):
            with self.subTest(path=path):
                self.assertFalse(check_sensitive_files.is_sensitive_path(path))

    def test_gitignore_excludes_secrets_but_allows_directory_placeholders(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".gitignore").write_text((ROOT / ".gitignore").read_text(encoding="utf-8"), encoding="utf-8")
            subprocess.run(["git", "init", "-q", str(root)], check=True)

            cases = {
                "src/MangosDatabaseStudio.App/App_Data/users.json": True,
                "src/MangosDatabaseStudio.App/App_Data/connections.json": True,
                "src/MangosDatabaseStudio.App/App_Data/keys/private.xml": True,
                "src/MangosDatabaseStudio.App/App_Data/keys/.gitkeep": False,
                "src/MangosDatabaseStudio.App/App_Data/.gitkeep": False,
                "src/MangosDatabaseStudio.App/App_Data/database/main.db": True,
                "build/App_Data/imports/update.sql": True,
                "migration.migrated-20261030.bak": True,
                "avatars/photo.jpg": True,
                "src/MangosDatabaseStudio.App/wwwroot/css/site.css": False,
                "README.md": False,
            }
            for path, expected_ignored in cases.items():
                with self.subTest(path=path):
                    result = subprocess.run(
                        ["git", "-C", str(root), "check-ignore", "--no-index", "-q", path],
                        check=False,
                    )
                    self.assertEqual(expected_ignored, result.returncode == 0)

    def test_tracked_private_file_is_rejected_even_when_gitignored(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(["git", "init", "-q", str(root)], check=True)
            data = root / "src" / "MangosDatabaseStudio.App" / "App_Data" / "users.json"
            data.parent.mkdir(parents=True)
            data.write_text("{}", encoding="utf-8")
            subprocess.run(["git", "-C", str(root), "add", "--force", "src/MangosDatabaseStudio.App/App_Data/users.json"], check=True)
            tracked = check_sensitive_files.get_tracked_files(root)
            self.assertEqual(1, len(tracked))
            self.assertTrue(check_sensitive_files.is_sensitive_path(tracked[0]))


if __name__ == "__main__":
    unittest.main()
